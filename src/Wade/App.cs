using System.Diagnostics;
using System.Text;
using Wade.FileSystem;
using Wade.Highlighting;
using Wade.Terminal;
using Wade.UI;

namespace Wade;

internal sealed class App
{
    private readonly WadeConfig _config;
    private readonly DirectoryContents _directoryContents = new();
    private readonly Layout _layout = new();
    private readonly StringBuilder _flushBuffer = new(4096);

    private string _currentPath = "";
    private int _selectedIndex;
    private int _scrollOffset;
    // Modal input state
    private InputMode _inputMode = InputMode.Normal;

    // Confirm dialog state
    private string? _confirmTitle;
    private string? _confirmMessage;
    private Action? _confirmYesAction;

    // Text input dialog state
    private TextInput? _activeTextInput;
    private string? _textInputTitle;
    private Action<string>? _textInputCompleteAction;

    // Notification state
    private Notification? _notification;

    // Expanded preview state
    private int _expandedPreviewScrollOffset;

    // Multi-select state
    private readonly HashSet<string> _markedPaths = new(StringComparer.OrdinalIgnoreCase);

    // Clipboard state
    private readonly List<string> _clipboardPaths = [];
    private bool _clipboardIsCut;

    // Search/filter state
    private TextInput? _searchInput;
    private string _searchFilter = "";
    private List<FileSystemEntry>? _filteredEntries;

    // Go-to-path state
    private TextInput? _goToPathInput;
    private string? _goToPathSuggestion;

    // Action palette state
    private int _actionPaletteSelectedIndex;
    private TextInput? _actionPaletteInput;
    private (string Label, string Shortcut, AppAction Action)[]? _actionPaletteItems;
    private int _actionPaletteScrollOffset;

    // Bookmark state
    private readonly BookmarkStore _bookmarkStore = new();
    private int _bookmarkSelectedIndex;
    private TextInput? _bookmarkInput;
    private int _bookmarkScrollOffset;

    // File finder state
    private int _fileFinderSelectedIndex;
    private TextInput? _fileFinderInput;
    private int _fileFinderScrollOffset;
    private List<FileSystemEntry>? _fileFinderAllEntries;
    private bool _fileFinderScanning;
    private CancellationTokenSource? _fileFinderCts;

    // Config dialog state
    private int _configSelectedIndex;
    private bool _configShowIcons;
    private bool _configImagePreviews;
    private bool _configShowHidden;
    private bool _configShowSystem;
    private SortMode _configSortMode;
    private bool _configSortAscending;
    private bool _configConfirmDelete;
    private bool _configPreviewPane;
    private bool _configSizeColumn;
    private bool _configDateColumn;
    private bool _configGlowMarkdownPreview;
    private bool _configZipPreview;
    private bool _configPdfPreview;
    private bool _configCopySymlinksAsLinks;
    private bool _configTerminalTitle;
    private bool _configGitStatus;

    // Git status state
    private GitStatusLoader? _gitStatusLoader;
    private GitActionRunner? _gitActionRunner;
    private string? _currentRepoRoot;
    private string? _currentBranchName;
    private Dictionary<string, GitFileStatus>? _gitStatuses;

    // Directory size calculation state
    private DirectorySizeLoader? _dirSizeLoader;
    private string? _propertiesDirSizePath;
    private string? _propertiesDirSizeText;

    private string? _cachedPreviewPath;
    private StyledLine[]? _cachedStyledLines;
    private string? _cachedPreviewFileTypeLabel;
    private string? _cachedPreviewEncoding;
    private string? _cachedPreviewLineEnding;
    private bool _previewLoading;
    private string? _pendingPreviewPath;
    private PreviewLoader? _previewLoader;

    // Image preview state
    private string? _cachedSixelData;
    private string? _cachedImagePath;
    private int _cachedImagePixelWidth;
    private int _cachedImagePixelHeight;
    private bool _isImagePreview;
    private bool _isRenderedPreview;
    private bool _diffPreviewActive;
    private bool _hexPreviewActive;
    private bool _sixelPending;

    // Terminal capability state
    private bool _imagePreviewsEffective;
    private bool _sixelSupported;
    private int _cellPixelWidth = 8;
    private int _cellPixelHeight = 16;

    // Track selected index per directory so we restore position when navigating back
    private readonly Dictionary<string, int> _selectedIndexPerDir = new(StringComparer.OrdinalIgnoreCase);

    // Left pane state cached during Render for mouse hit-testing
    private List<FileSystemEntry>? _leftPaneEntries;
    private int _leftPaneScroll;
    private int _leftPaneSelected;

#pragma warning disable CSLINT221 // Consider using a primary constructor
    public App(WadeConfig config)
#pragma warning restore CSLINT221
    {
        _config = config;
    }

    private void UpdateTerminalTitle()
    {
        if (_config.TerminalTitleEnabled)
        {
            Console.Write(AnsiCodes.SetTitle($"wade - {_currentPath}"));
        }
        else
        {
            Console.Write(AnsiCodes.ClearTitle);
        }
    }

    public string? Run()
    {
        _currentPath = PathCompletion.CapitalizeDriveLetter(Path.GetFullPath(_config.StartPath));
        _directoryContents.ShowHiddenFiles = _config.ShowHiddenFiles;
        _directoryContents.ShowSystemFiles = _config.ShowSystemFiles;
        _directoryContents.SortMode = _config.SortMode;
        _directoryContents.SortAscending = _config.SortAscending;
        _bookmarkStore.Load();

        using var terminal = new TerminalSetup();
        using var inputSource = InputPipeline.CreatePlatformSource();
        using var pipeline = new InputPipeline(inputSource);
        _previewLoader = new PreviewLoader(pipeline);
        _dirSizeLoader = new DirectorySizeLoader(pipeline);
        _gitStatusLoader = new GitStatusLoader(pipeline);
        _gitActionRunner = new GitActionRunner(pipeline);
        var previewLoader = _previewLoader;

        var caps = terminal.Capabilities;
        _sixelSupported = caps.SixelSupported;
        _imagePreviewsEffective = _config.ImagePreviewsEnabled && _sixelSupported;
        _cellPixelWidth = caps.CellPixelWidth;
        _cellPixelHeight = caps.CellPixelHeight;

        int lastWidth = Console.WindowWidth;
        int lastHeight = Console.WindowHeight;

        var buffer = new ScreenBuffer(lastWidth, lastHeight);
        _layout.Calculate(lastWidth, lastHeight, _config.PreviewPaneEnabled);
        previewLoader.Configure(_imagePreviewsEffective, _layout.RightPane.Width, _layout.RightPane.Height,
            _cellPixelWidth, _cellPixelHeight, glowEnabled: _config.GlowMarkdownPreviewEnabled,
            zipPreviewEnabled: _config.ZipPreviewEnabled,
            pdfPreviewEnabled: _config.PdfPreviewEnabled);

        UpdateTerminalTitle();
        RefreshGitStatus();

        bool quit = false;
        bool writeCwd = true;

        while (!quit)
        {
            // Auto-clear expired notifications
            if (_notification is { } notif && notif.IsExpired(Environment.TickCount64))
            {
                _notification = null;
            }

            // Render
            buffer.Clear();
            Render(buffer);
            buffer.Flush(_flushBuffer);

            // Write Sixel data after flush (bypasses cell grid)
            if (_sixelPending && _cachedSixelData is not null && _config.PreviewPaneEnabled
                && _inputMode is InputMode.Normal or InputMode.Search or InputMode.ExpandedPreview)
            {
                _sixelPending = false;
                var sixelPane = _inputMode == InputMode.ExpandedPreview ? _layout.ExpandedPane : _layout.RightPane;
                int cursorRow = sixelPane.Top;
                int cursorCol = sixelPane.Left;

                if (_inputMode == InputMode.ExpandedPreview && _cachedImagePixelWidth > 0 && _cachedImagePixelHeight > 0)
                {
                    (cursorRow, cursorCol) = sixelPane.CenterContent(_cachedImagePixelWidth / _cellPixelWidth, _cachedImagePixelHeight / _cellPixelHeight);
                }

                var moveCursor = AnsiCodes.MoveCursor(cursorRow, cursorCol);
                buffer.WriteRaw(moveCursor + _cachedSixelData);
            }

            // Wait for next input event
            var inputEvent = pipeline.Take();

            // Drain any additional queued events (e.g. rapid key repeats)
            while (pipeline.TryTake(out var extra))
            {
                if (extra is PreviewReadyEvent previewReady)
                {
                    bool wasImage = _isImagePreview;
                    HandlePreviewReady(previewReady);
                    if (wasImage)
                    {
                        buffer.ForceFullRedraw();
                    }
                }
                else if (extra is ImagePreviewReadyEvent imagePreviewReady)
                {
                    bool wasImage = _isImagePreview;
                    HandleImagePreviewReady(imagePreviewReady);
                    if (wasImage)
                    {
                        buffer.ForceFullRedraw();
                    }
                }
                else if (extra is DirectorySizeReadyEvent dirSizeExtra)
                {
                    HandleDirectorySizeReady(dirSizeExtra);
                }
                else if (extra is GitStatusReadyEvent gitExtra)
                {
                    HandleGitStatusReady(gitExtra);
                }
                else if (extra is GitActionCompleteEvent gitActionExtra)
                {
                    HandleGitActionComplete(gitActionExtra);
                }
                else if (extra is FileFinderScanCompleteEvent scanExtra)
                {
                    if (_inputMode == InputMode.FileFinder && scanExtra.BasePath == _currentPath)
                    {
                        _fileFinderAllEntries = scanExtra.Entries;
                        _fileFinderScanning = false;
                    }
                }
                else if (extra is ResizeEvent)
                {
                    inputEvent = extra;
                }
                else if (extra is KeyEvent or MouseEvent)
                {
                    inputEvent = extra;
                }
            }

            // Handle resize events
            if (inputEvent is ResizeEvent resize)
            {
                if (resize.Width != lastWidth || resize.Height != lastHeight)
                {
                    lastWidth = resize.Width;
                    lastHeight = resize.Height;
                    buffer.Resize(lastWidth, lastHeight);
                    _layout.Calculate(lastWidth, lastHeight, _config.PreviewPaneEnabled);
                    var resizePane = _inputMode == InputMode.ExpandedPreview ? _layout.ExpandedPane : _layout.RightPane;
                    previewLoader.Configure(_imagePreviewsEffective, resizePane.Width, resizePane.Height,
                        _cellPixelWidth, _cellPixelHeight, glowEnabled: _config.GlowMarkdownPreviewEnabled,
            zipPreviewEnabled: _config.ZipPreviewEnabled,
            pdfPreviewEnabled: _config.PdfPreviewEnabled);
                    Console.Write(AnsiCodes.ClearScreen);

                    // Re-render image at new size
                    if (_isImagePreview && _cachedImagePath is not null)
                    {
                        _cachedSixelData = null;
                        _sixelPending = false;
                        _pendingPreviewPath = _cachedImagePath;
                        _previewLoading = true;
                        previewLoader.BeginLoad(_cachedImagePath);
                    }
                    else if (_isRenderedPreview && _cachedPreviewPath is not null)
                    {
                        _cachedStyledLines = null;
                        _pendingPreviewPath = _cachedPreviewPath;
                        _previewLoading = true;
                        previewLoader.BeginLoad(_cachedPreviewPath);
                    }
                }

                continue;
            }

            // Handle preview ready events
            if (inputEvent is PreviewReadyEvent previewEvt)
            {
                bool wasImage = _isImagePreview;
                HandlePreviewReady(previewEvt);
                if (wasImage)
                {
                    buffer.ForceFullRedraw();
                }

                continue;
            }

            if (inputEvent is ImagePreviewReadyEvent imagePreviewEvt)
            {
                bool wasImage = _isImagePreview;
                HandleImagePreviewReady(imagePreviewEvt);
                if (wasImage)
                {
                    buffer.ForceFullRedraw();
                }

                continue;
            }

            if (inputEvent is DirectorySizeReadyEvent dirSizeEvt)
            {
                HandleDirectorySizeReady(dirSizeEvt);
                continue;
            }

            if (inputEvent is GitStatusReadyEvent gitEvt)
            {
                HandleGitStatusReady(gitEvt);
                continue;
            }

            if (inputEvent is GitActionCompleteEvent gitActionEvt)
            {
                HandleGitActionComplete(gitActionEvt);
                continue;
            }

            if (inputEvent is FileFinderScanCompleteEvent scanEvt)
            {
                if (_inputMode == InputMode.FileFinder && scanEvt.BasePath == _currentPath)
                {
                    _fileFinderAllEntries = scanEvt.Entries;
                    _fileFinderScanning = false;
                }

                continue;
            }

            // Handle mouse events
            if (inputEvent is MouseEvent mouseEvent)
            {
                if (_inputMode == InputMode.ExpandedPreview)
                {
                    HandleExpandedPreviewMouse(mouseEvent);
                    continue;
                }

                // Discard mouse events while a modal dialog is open
                if (_inputMode is InputMode.Help or InputMode.GoToPath or InputMode.TextInput or InputMode.Confirm or InputMode.Config or InputMode.Properties or InputMode.ActionPalette or InputMode.Bookmarks or InputMode.FileFinder)
                {
                    continue;
                }

                var mouseEntries = GetVisibleEntries();
                HandleMouseEvent(mouseEvent, mouseEntries, previewLoader, buffer);

                // Clamp and adjust scroll after mouse handling
                var currentAfterMouse = GetVisibleEntries();
                if (currentAfterMouse.Count > 0)
                {
                    _selectedIndex = Math.Clamp(_selectedIndex, 0, currentAfterMouse.Count - 1);
                }
                else
                {
                    _selectedIndex = 0;
                }

                AdjustScroll(VisibleFileListHeight);
                continue;
            }

            // Handle key events
            if (inputEvent is not KeyEvent keyEvent)
            {
                continue;
            }

            // Modal input dispatch — consume all keys when in a modal mode
            switch (_inputMode)
            {
                case InputMode.Help:
                    if (!keyEvent.IsModifierOnly)
                    {
                        _inputMode = InputMode.Normal;
                    }
                    continue;
                case InputMode.Properties:
                    if (!keyEvent.IsModifierOnly)
                    {
                        _inputMode = InputMode.Normal;
                        _dirSizeLoader?.Cancel();
                        _propertiesDirSizePath = null;
                        _propertiesDirSizeText = null;
                    }
                    continue;
                case InputMode.Search:
                    HandleSearchKey(keyEvent);
                    var searchEntries = GetVisibleEntries();
                    if (searchEntries.Count > 0)
                    {
                        _selectedIndex = Math.Clamp(_selectedIndex, 0, searchEntries.Count - 1);
                    }
                    else
                    {
                        _selectedIndex = 0;
                    }

                    AdjustScroll(VisibleFileListHeight);
                    continue;

                case InputMode.TextInput:
                    HandleTextInputKey(keyEvent);
                    continue;

                case InputMode.ExpandedPreview:
                    HandleExpandedPreviewKey(keyEvent, previewLoader, buffer);
                    continue;

                case InputMode.Confirm:
                    HandleConfirmKey(keyEvent);
                    continue;

                case InputMode.GoToPath:
                    HandleGoToPathKey(keyEvent, previewLoader, buffer);
                    continue;

                case InputMode.Config:
                    HandleConfigKey(keyEvent, previewLoader, buffer);
                    continue;

                case InputMode.ActionPalette:
                    HandleActionPaletteKey(keyEvent, previewLoader, buffer, pipeline);
                    continue;

                case InputMode.Bookmarks:
                    HandleBookmarkKey(keyEvent, previewLoader, buffer);
                    continue;

                case InputMode.FileFinder:
                    HandleFileFinderKey(keyEvent, previewLoader, buffer);
                    continue;

                case InputMode.Normal:
                default:
                    break; // fall through to normal AppAction dispatch
            }

            var action = InputReader.MapKey(keyEvent);
            var entries = GetVisibleEntries();

            switch (action)
            {
                case AppAction.NavigateUp:
                    if (_selectedIndex > 0)
                    {
                        _selectedIndex--;
                    }

                    break;

                case AppAction.NavigateDown:
                    if (_selectedIndex < entries.Count - 1)
                    {
                        _selectedIndex++;
                    }

                    break;

                case AppAction.Open:
                    if (entries.Count > 0 && entries[_selectedIndex].IsDirectory)
                    {
                        if (entries[_selectedIndex].IsBrokenSymlink)
                        {
                            ShowNotification("Cannot open: broken symlink", NotificationKind.Error);
                            break;
                        }

                        _selectedIndexPerDir[_currentPath] = _selectedIndex;
                        _currentPath = PathCompletion.CapitalizeDriveLetter(entries[_selectedIndex].FullPath);
                        _selectedIndex = _selectedIndexPerDir.GetValueOrDefault(_currentPath, 0);
                        _scrollOffset = 0;
                        _notification = null;
                        _markedPaths.Clear();
                        ClearSearchFilter();
                        ClearPreviewCache(previewLoader, buffer);
                        UpdateTerminalTitle();
                        RefreshGitStatus();
                    }
                    else if (entries.Count > 0 && !entries[_selectedIndex].IsDirectory)
                    {
                        EnterExpandedPreview(previewLoader, buffer);
                    }
                    break;

                case AppAction.Back:
                {
                    if (_currentPath == DirectoryContents.DrivesPath)
                    {
                        break; // Already at the top level
                    }

                    _notification = null;
                    _markedPaths.Clear();
                    ClearSearchFilter();
                    _selectedIndexPerDir[_currentPath] = _selectedIndex;
                    string oldPath = _currentPath;

                    if (DirectoryContents.IsDriveRoot(_currentPath))
                    {
                        // Go up to the drives list
                        _currentPath = DirectoryContents.DrivesPath;
                        UpdateTerminalTitle();
                        RefreshGitStatus();
                        var driveEntries = _directoryContents.GetEntries(_currentPath);
                        string root = Path.GetPathRoot(oldPath)!.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                        int idx = driveEntries.FindIndex(e => e.Name.Equals(root, StringComparison.OrdinalIgnoreCase));
                        _selectedIndex = idx >= 0 ? idx : 0;
                    }
                    else
                    {
                        var parent = Directory.GetParent(_currentPath);
                        if (parent is not null)
                        {
                            _currentPath = PathCompletion.CapitalizeDriveLetter(parent.FullName);
                            UpdateTerminalTitle();
                            RefreshGitStatus();
                            var parentEntries = _directoryContents.GetEntries(_currentPath);
                            string oldName = Path.GetFileName(oldPath);
                            int idx = parentEntries.FindIndex(e => e.Name.Equals(oldName, StringComparison.OrdinalIgnoreCase));
                            _selectedIndex = idx >= 0 ? idx : _selectedIndexPerDir.GetValueOrDefault(_currentPath, 0);
                        }
                    }

                    _scrollOffset = 0;
                    ClearPreviewCache(previewLoader, buffer);
                    break;
                }

                case AppAction.PageUp:
                    _selectedIndex = Math.Max(0, _selectedIndex - _layout.CenterPane.Height);
                    break;

                case AppAction.PageDown:
                    _selectedIndex = Math.Min(entries.Count - 1, _selectedIndex + _layout.CenterPane.Height);
                    break;

                case AppAction.Home:
                    _selectedIndex = 0;
                    break;

                case AppAction.End:
                    _selectedIndex = Math.Max(0, entries.Count - 1);
                    break;

                case AppAction.Quit:
                    if (!string.IsNullOrEmpty(_searchFilter))
                    {
                        ClearSearchFilter();
                        _selectedIndex = 0;
                        _scrollOffset = 0;
                    }
                    else
                    {
                        quit = true;
                    }
                    break;

                case AppAction.QuitNoCd:
                    quit = true;
                    writeCwd = false;
                    break;

                case AppAction.Search:
                    _inputMode = InputMode.Search;
                    _searchInput = new TextInput(_searchFilter);
                    break;

                case AppAction.ShowHelp:
                    _inputMode = InputMode.Help;
                    break;

                case AppAction.ShowProperties:
                    if (entries.Count > 0 && _selectedIndex < entries.Count)
                    {
                        _inputMode = InputMode.Properties;
                        var propsEntry = entries[_selectedIndex];
                        if (propsEntry.IsDirectory && !propsEntry.IsDrive)
                        {
                            _propertiesDirSizePath = propsEntry.FullPath;
                            _propertiesDirSizeText = "Calculating\u2026";
                            _dirSizeLoader!.BeginCalculation(propsEntry.FullPath);
                        }
                        else
                        {
                            _propertiesDirSizePath = null;
                            _propertiesDirSizeText = null;
                        }
                    }
                    break;

                case AppAction.ShowActionPalette:
                    ShowActionPalette();
                    break;

                case AppAction.ShowBookmarks:
                    ShowBookmarks();
                    break;

                case AppAction.ShowFileFinder:
                    ShowFileFinder(pipeline);
                    break;

                case AppAction.ToggleBookmark:
                    _bookmarkStore.Toggle(_currentPath);
                    ShowNotification(
                        _bookmarkStore.Contains(_currentPath) ? "Bookmarked" : "Bookmark removed",
                        NotificationKind.Success);
                    break;

                case AppAction.ShowConfig:
                    ShowConfigDialog();
                    break;

                case AppAction.Refresh:
                    _notification = null;
                    _markedPaths.Clear();
                    ClearSearchFilter();
                    _directoryContents.InvalidateAll();
                    ClearPreviewCache(previewLoader, buffer);
                    RefreshGitStatus();
                    buffer.ForceFullRedraw();
                    break;

                case AppAction.ToggleMark:
                    if (entries.Count > 0 && _selectedIndex < entries.Count)
                    {
                        string path = entries[_selectedIndex].FullPath;
                        if (!_markedPaths.Remove(path))
                        {
                            _markedPaths.Add(path);
                        }

                        if (_selectedIndex < entries.Count - 1)
                        {
                            _selectedIndex++;
                        }
                    }
                    break;

                case AppAction.ToggleHiddenFiles:
                    _directoryContents.ShowHiddenFiles = !_directoryContents.ShowHiddenFiles;
                    _directoryContents.InvalidateAll();
                    ClearPreviewCache(previewLoader, buffer);
                    break;

                case AppAction.CycleSortMode:
                    _directoryContents.SortMode = _directoryContents.SortMode switch
                    {
                        SortMode.Name => SortMode.Modified,
                        SortMode.Modified => SortMode.Size,
                        SortMode.Size => SortMode.Extension,
                        _ => SortMode.Name,
                    };
                    _directoryContents.InvalidateAll();
                    ClearPreviewCache(previewLoader, buffer);
                    break;

                case AppAction.ToggleSortDirection:
                    _directoryContents.SortAscending = !_directoryContents.SortAscending;
                    _directoryContents.InvalidateAll();
                    ClearPreviewCache(previewLoader, buffer);
                    break;

                case AppAction.GoToPath:
                    _inputMode = InputMode.GoToPath;
                    _goToPathInput = new TextInput();
                    _goToPathSuggestion = null;
                    break;

                case AppAction.OpenExternal:
                case AppAction.Rename:
                case AppAction.Delete:
                case AppAction.DeletePermanently:
                case AppAction.Copy:
                case AppAction.Cut:
                case AppAction.Paste:
                case AppAction.CopyAbsolutePath:
                case AppAction.CopyGitRelativePath:
                case AppAction.NewFile:
                case AppAction.NewDirectory:
                case AppAction.CreateSymlink:
                    DispatchFileAction(action);
                    break;

                case AppAction.OpenTerminal:
                    try
                    {
                        OpenTerminalHere(_currentPath);
                        ShowNotification("Opened terminal", NotificationKind.Success);
                    }
                    catch (Exception ex)
                    {
                        ShowNotification($"Error: {ex.Message}", NotificationKind.Error);
                    }
                    break;

            }

            // Clamp selection
            var currentEntries = GetVisibleEntries();
            if (currentEntries.Count > 0)
            {
                _selectedIndex = Math.Clamp(_selectedIndex, 0, currentEntries.Count - 1);
            }
            else
            {
                _selectedIndex = 0;
            }

            // Adjust scroll offset to keep selection visible
            AdjustScroll(VisibleFileListHeight);
        }

        return writeCwd ? _currentPath : null;
    }

    private static void OpenTerminalHere(string workingDirectory)
    {
        if (OperatingSystem.IsWindows())
        {
            try
            {
                Process.Start(new ProcessStartInfo("wt.exe", $"-w 0 new-tab -d \"{workingDirectory}\"")
                {
                    UseShellExecute = false,
                    CreateNoWindow = true,
                });
            }
            catch
            {
                // wt.exe not available — fall back to COMSPEC (cmd.exe)
                var comspec = Environment.GetEnvironmentVariable("COMSPEC") ?? "cmd.exe";
                Process.Start(new ProcessStartInfo(comspec)
                {
                    WorkingDirectory = workingDirectory,
                    UseShellExecute = false,
                });
            }
        }
        else
        {
            var shell = Environment.GetEnvironmentVariable("SHELL") ?? "/bin/sh";
            Process.Start(new ProcessStartInfo(shell)
            {
                WorkingDirectory = workingDirectory,
                UseShellExecute = false,
            });
        }
    }

    private void ExecutePaste(bool overwrite)
    {
        int errors = 0;
        int success = 0;
        var sourceParents = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (string sourcePath in _clipboardPaths)
        {
            string destName = Path.GetFileName(sourcePath);
            string destPath = Path.Combine(_currentPath, destName);

            if (Path.Exists(destPath))
            {
                if (!overwrite)
                {
                    errors++;
                    continue;
                }

                try
                {
                    // Check symlink FIRST — Directory.Exists follows the link on Windows
                    var destInfo = new FileInfo(destPath);
                    if (destInfo.LinkTarget != null)
                    {
                        if (Directory.Exists(destPath))
                        {
                            Directory.Delete(destPath, false);
                        }
                        else
                        {
                            File.Delete(destPath);
                        }
                    }
                    else if (Directory.Exists(destPath))
                    {
                        Directory.Delete(destPath, true);
                    }
                    else
                    {
                        File.Delete(destPath);
                    }
                }
                catch
                {
                    errors++;
                    continue;
                }
            }

            try
            {
                if (_clipboardIsCut)
                {
                    if (Directory.Exists(sourcePath))
                    {
                        Directory.Move(sourcePath, destPath);
                    }
                    else
                    {
                        File.Move(sourcePath, destPath);
                    }

                    sourceParents.Add(Path.GetDirectoryName(sourcePath)!);
                }
                else
                {
                    var sourceInfo = new FileInfo(sourcePath);
                    if (_config.CopySymlinksAsLinksEnabled && sourceInfo.LinkTarget != null)
                    {
                        try
                        {
                            if (Directory.Exists(sourcePath))
                            {
                                Directory.CreateSymbolicLink(destPath, sourceInfo.LinkTarget);
                            }
                            else
                            {
                                File.CreateSymbolicLink(destPath, sourceInfo.LinkTarget);
                            }

                            success++;
                            continue;
                        }
                        catch (UnauthorizedAccessException) { }
                    }

                    if (Directory.Exists(sourcePath))
                    {
                        FileOperations.CopyDirectory(sourcePath, destPath, _config.CopySymlinksAsLinksEnabled);
                    }
                    else
                    {
                        File.Copy(sourcePath, destPath);
                    }
                }

                success++;
            }
            catch
            {
                errors++;
            }
        }

        _directoryContents.Invalidate(_currentPath);
        InvalidateFilteredEntries();
        foreach (string parent in sourceParents)
        {
            _directoryContents.Invalidate(parent);
        }

        if (_clipboardIsCut && errors == 0)
        {
            _clipboardPaths.Clear();
        }

        RefreshGitStatus();

        if (errors > 0)
        {
            ShowNotification($"Pasted {success}, {errors} failed", NotificationKind.Error);
        }
        else
        {
            ShowNotification($"Pasted {success} item(s)", NotificationKind.Success);
        }
    }

    private void Render(ScreenBuffer buffer)
    {
        int width = buffer.Width;
        int height = buffer.Height;

        if (_inputMode == InputMode.ExpandedPreview)
        {
            RenderExpandedPreview(buffer, width, height);
            return;
        }

        // Current directory entries (filtered if search is active)
        var entries = GetVisibleEntries();
        bool showSearchBar = _inputMode == InputMode.Search || !string.IsNullOrEmpty(_searchFilter);
        var fileListPane = showSearchBar
            ? _layout.CenterPane with { Height = _layout.CenterPane.Height - 1 }
            : _layout.CenterPane;

        // Center pane: current directory
        PaneRenderer.RenderFileList(
            buffer, fileListPane, entries, _selectedIndex, _scrollOffset,
            isActive: true, showIcons: _config.ShowIconsEnabled,
            showSize: _config.SizeColumnEnabled, showDate: _config.DateColumnEnabled,
            markedPaths: _markedPaths, gitStatuses: _gitStatuses);

        // Search bar at bottom of center pane
        if (showSearchBar)
        {
            RenderSearchBar(buffer);
        }

        // Left pane: parent directory (or drives list)
        if (_currentPath == DirectoryContents.DrivesPath)
        {
            // At the drives list — no parent to show
            _leftPaneEntries = null;
        }
        else
        {
            string parentKey;
            string currentName;

            if (DirectoryContents.IsDriveRoot(_currentPath))
            {
                parentKey = DirectoryContents.DrivesPath;
                currentName = Path.GetPathRoot(_currentPath)!.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            }
            else
            {
                var parentDir = Directory.GetParent(_currentPath);
                parentKey = parentDir?.FullName ?? DirectoryContents.DrivesPath;
                currentName = Path.GetFileName(_currentPath);
            }

            var parentEntries = _directoryContents.GetEntries(parentKey);
            int parentSelected = -1;
            for (int i = 0; i < parentEntries.Count; i++)
            {
                if (parentEntries[i].Name.Equals(currentName, StringComparison.OrdinalIgnoreCase))
                {
                    parentSelected = i;
                    break;
                }
            }

            if (parentSelected < 0)
            {
                parentSelected = 0;
            }

            int parentScroll = CalculateScroll(parentSelected, _layout.LeftPane.Height, parentEntries.Count);
            PaneRenderer.RenderFileList(buffer, _layout.LeftPane, parentEntries, parentSelected, parentScroll, isActive: false, showIcons: _config.ShowIconsEnabled);

            // Cache for mouse hit-testing
            _leftPaneEntries = parentEntries;
            _leftPaneScroll = parentScroll;
            _leftPaneSelected = parentSelected;
        }

        // Right pane: preview
        if (_config.PreviewPaneEnabled && entries.Count > 0 && _selectedIndex < entries.Count)
        {
            var selected = entries[_selectedIndex];
            if (selected.IsDirectory)
            {
                var previewEntries = _directoryContents.GetEntries(selected.FullPath);
                if (previewEntries.Count > 0)
                {
                    PaneRenderer.RenderFileList(buffer, _layout.RightPane, previewEntries, -1, 0, isActive: false, showIcons: _config.ShowIconsEnabled);
                }
                else
                {
                    PaneRenderer.RenderMessage(buffer, _layout.RightPane, "[empty directory]");
                }
            }
            else
            {
                if (selected.FullPath != _cachedPreviewPath && selected.FullPath != _pendingPreviewPath)
                {
                    _diffPreviewActive = false;
                    _hexPreviewActive = false;
                    _pendingPreviewPath = selected.FullPath;
                    _previewLoading = true;
                    _previewLoader!.BeginLoad(selected.FullPath, selected.IsCloudPlaceholder);
                }

                if (_previewLoading)
                {
                    PaneRenderer.RenderMessage(buffer, _layout.RightPane, "[loading\u2026]");
                }
                else if (_isImagePreview && _cachedSixelData is not null)
                {
                    // Fill right pane with spaces so ScreenBuffer claims the area
                    for (int row = _layout.RightPane.Top; row < _layout.RightPane.Bottom; row++)
                    {
                        buffer.FillRow(row, _layout.RightPane.Left, _layout.RightPane.Width, ' ', CellStyle.Default);
                    }

                    _sixelPending = true;
                }
                else if (_cachedStyledLines is not null)
                {
                    PaneRenderer.RenderPreview(buffer, _layout.RightPane, _cachedStyledLines, showLineNumbers: !_isRenderedPreview);
                }
            }
        }

        // Borders
        PaneRenderer.RenderBorders(buffer, _layout, height, _config.PreviewPaneEnabled);

        // Status bar
        FileSystemEntry? selectedEntry = entries.Count > 0 && _selectedIndex < entries.Count
            ? entries[_selectedIndex]
            : null;
        string displayPath = _currentPath == DirectoryContents.DrivesPath ? "Drives" : _currentPath;
        StatusBar.Render(buffer, _layout.StatusBar, displayPath, entries.Count, _selectedIndex, selectedEntry, _cachedPreviewFileTypeLabel, _cachedPreviewEncoding, _cachedPreviewLineEnding, _notification, _markedPaths.Count, _directoryContents.SortMode, _directoryContents.SortAscending, _clipboardPaths.Count, _clipboardIsCut, _currentBranchName);

        // Help overlay
        if (_inputMode == InputMode.Help)
        {
            HelpOverlay.Render(buffer, width, height);
        }

        // Modal overlays (render last, on top)
        switch (_inputMode)
        {
            case InputMode.Confirm:
                RenderConfirmDialog(buffer, width, height);
                break;
            case InputMode.TextInput:
                RenderTextInputDialog(buffer, width, height);
                break;
            case InputMode.GoToPath:
                RenderGoToPathDialog(buffer, width, height);
                break;
            case InputMode.Config:
                RenderConfigDialog(buffer, width, height);
                break;
            case InputMode.Properties:
                if (selectedEntry is not null)
                {
                    PropertiesOverlay.Render(buffer, width, height, selectedEntry, _propertiesDirSizeText);
                }
                break;
            case InputMode.ActionPalette:
                RenderActionPalette(buffer, width, height);
                break;
            case InputMode.Bookmarks:
                RenderBookmarks(buffer, width, height);
                break;
            case InputMode.FileFinder:
                RenderFileFinder(buffer, width, height);
                break;
        }
    }

    private void AdjustScroll(int visibleHeight)
    {
        if (_selectedIndex < _scrollOffset)
        {
            _scrollOffset = _selectedIndex;
        }
        else if (_selectedIndex >= _scrollOffset + visibleHeight)
        {
            _scrollOffset = _selectedIndex - visibleHeight + 1;
        }
    }

    private void HandlePreviewReady(PreviewReadyEvent evt)
    {
        if (evt.Path != _pendingPreviewPath)
        {
            return;
        }

        _cachedPreviewPath = evt.Path;
        _cachedStyledLines = evt.StyledLines;
        _cachedPreviewFileTypeLabel = evt.FileTypeLabel;
        _cachedPreviewEncoding = evt.Encoding;
        _cachedPreviewLineEnding = evt.LineEnding;
        _previewLoading = false;
        _isImagePreview = false;
        _isRenderedPreview = evt.IsRendered;
        _cachedSixelData = null;
        _cachedImagePath = null;
    }

    private void HandleImagePreviewReady(ImagePreviewReadyEvent evt)
    {
        if (evt.Path != _pendingPreviewPath)
        {
            return;
        }

        _cachedImagePath = evt.Path;
        _cachedPreviewPath = evt.Path;
        _cachedSixelData = evt.SixelData;
        _cachedImagePixelWidth = evt.PixelWidth;
        _cachedImagePixelHeight = evt.PixelHeight;
        _cachedPreviewFileTypeLabel = evt.FileTypeLabel;
        _cachedPreviewEncoding = null;
        _cachedPreviewLineEnding = null;
        _cachedStyledLines = null;
        _isImagePreview = true;
        _previewLoading = false;
    }

    private void HandleDirectorySizeReady(DirectorySizeReadyEvent evt)
    {
        if (evt.Path != _propertiesDirSizePath)
        {
            return;
        }

        Span<char> sizeBuf = stackalloc char[32];
        int n = UI.FormatHelpers.FormatSize(sizeBuf, evt.TotalBytes);
        string formatted = sizeBuf[..n].ToString();
        _propertiesDirSizeText = $"{formatted} ({evt.TotalBytes:N0} bytes)";
    }

    private void HandleGitStatusReady(GitStatusReadyEvent evt)
    {
        if (evt.RepoRoot != _currentRepoRoot)
        {
            return;
        }

        _currentBranchName = evt.BranchName;
        _gitStatuses = evt.Statuses;
    }

    private bool HasStatusInSelection(GitFileStatus statusMask)
    {
        if (_gitStatuses is null)
        {
            return false;
        }

        if (_markedPaths.Count > 0)
        {
            foreach (string path in _markedPaths)
            {
                if (_gitStatuses.TryGetValue(path, out var s) && (s & statusMask) != 0)
                {
                    return true;
                }
            }

            return false;
        }

        var entries = GetVisibleEntries();
        if (_selectedIndex < entries.Count)
        {
            string path = entries[_selectedIndex].FullPath;
            return _gitStatuses.TryGetValue(path, out var s) && (s & statusMask) != 0;
        }

        return false;
    }

    private void HandleGitActionComplete(GitActionCompleteEvent evt)
    {
        if (evt.Success)
        {
            ShowNotification("Git action completed", NotificationKind.Success);
        }
        else
        {
            ShowNotification($"Git error: {evt.ErrorMessage}", NotificationKind.Error);
        }

        RefreshGitStatus();
    }

    private void HandleToggleDiffPreview(List<FileSystemEntry> entries, PreviewLoader previewLoader)
    {
        if (entries.Count == 0 || _selectedIndex >= entries.Count)
        {
            return;
        }

        var selected = entries[_selectedIndex];
        if (selected.IsDirectory)
        {
            return;
        }

        // Toggle off: reload normal preview
        if (_diffPreviewActive)
        {
            _diffPreviewActive = false;
            _pendingPreviewPath = selected.FullPath;
            _previewLoading = true;
            previewLoader.BeginLoad(selected.FullPath, selected.IsCloudPlaceholder);
            return;
        }

        // Check git status for this file
        GitFileStatus status = GitFileStatus.None;
        if (_gitStatuses is not null)
        {
            _gitStatuses.TryGetValue(selected.FullPath, out status);
        }

        bool hasModified = status.HasFlag(GitFileStatus.Modified);
        bool hasStaged = status.HasFlag(GitFileStatus.Staged);

        if (!hasModified && !hasStaged)
        {
            ShowNotification("No changes to diff", NotificationKind.Info);
            return;
        }

        if (_currentRepoRoot is null)
        {
            ShowNotification("No changes to diff", NotificationKind.Info);
            return;
        }

        _diffPreviewActive = true;
        _hexPreviewActive = false;
        _pendingPreviewPath = selected.FullPath;
        _previewLoading = true;
        // Prefer unstaged diff when both modified and staged; show staged if only staged
        previewLoader.BeginLoadDiff(selected.FullPath, _currentRepoRoot, staged: !hasModified && hasStaged);
    }

    private void HandleToggleHexPreview(List<FileSystemEntry> entries, PreviewLoader previewLoader)
    {
        if (entries.Count == 0 || _selectedIndex >= entries.Count)
        {
            return;
        }

        var selected = entries[_selectedIndex];
        if (selected.IsDirectory)
        {
            return;
        }

        // Toggle off: reload normal preview
        if (_hexPreviewActive)
        {
            _hexPreviewActive = false;
            _pendingPreviewPath = selected.FullPath;
            _previewLoading = true;
            previewLoader.BeginLoad(selected.FullPath, selected.IsCloudPlaceholder);
            return;
        }

        if (!FilePreview.IsBinary(selected.FullPath))
        {
            ShowNotification("Not a binary file", NotificationKind.Info);
            return;
        }

        _hexPreviewActive = true;
        _diffPreviewActive = false;
        _pendingPreviewPath = selected.FullPath;
        _previewLoading = true;
        previewLoader.BeginLoadHex(selected.FullPath);
    }

    private void ClearPreviewCache(PreviewLoader loader, ScreenBuffer? buffer = null)
    {
        bool wasImage = _isImagePreview;
        if (_inputMode == InputMode.ExpandedPreview)
        {
            _inputMode = InputMode.Normal;
            _expandedPreviewScrollOffset = 0;
        }

        loader.Cancel();
        _cachedPreviewPath = null;
        _cachedStyledLines = null;
        _cachedPreviewFileTypeLabel = null;
        _cachedPreviewEncoding = null;
        _cachedPreviewLineEnding = null;
        _previewLoading = false;
        _pendingPreviewPath = null;
        _cachedSixelData = null;
        _cachedImagePath = null;
        _cachedImagePixelWidth = 0;
        _cachedImagePixelHeight = 0;
        _isImagePreview = false;
        _isRenderedPreview = false;
        _diffPreviewActive = false;
        _hexPreviewActive = false;
        _sixelPending = false;

        if (wasImage)
        {
            buffer?.ForceFullRedraw();
        }
    }

    private void HandleMouseEvent(MouseEvent mouse, List<FileSystemEntry> entries, PreviewLoader previewLoader, ScreenBuffer buffer)
    {
        // Scroll wheel → move selection up/down in center pane
        if (mouse.Button == MouseButton.ScrollUp)
        {
            if (_selectedIndex > 0)
            {
                _selectedIndex--;
            }

            return;
        }

        if (mouse.Button == MouseButton.ScrollDown)
        {
            if (_selectedIndex < entries.Count - 1)
            {
                _selectedIndex++;
            }

            return;
        }

        // Ignore releases and non-left-click
        if (mouse.IsRelease || mouse.Button != MouseButton.Left)
        {
            return;
        }

        // Hit-test: which pane was clicked?
        int row = mouse.Row;
        int col = mouse.Col;

        if (HitTestPane(_layout.CenterPane, row, col))
        {
            // Center pane click — select the entry (same as arrow keys)
            int entryIndex = _scrollOffset + (row - _layout.CenterPane.Top);
            if (entryIndex >= 0 && entryIndex < entries.Count)
            {
                _selectedIndex = entryIndex;
            }
        }
        else if (HitTestPane(_layout.LeftPane, row, col) && _leftPaneEntries is not null)
        {
            // Left pane click
            int entryIndex = _leftPaneScroll + (row - _layout.LeftPane.Top);
            if (entryIndex >= 0 && entryIndex < _leftPaneEntries.Count)
            {
                var clicked = _leftPaneEntries[entryIndex];
                if (clicked.IsDirectory)
                {
                    _selectedIndexPerDir[_currentPath] = _selectedIndex;
                    _currentPath = PathCompletion.CapitalizeDriveLetter(clicked.FullPath);
                    _selectedIndex = _selectedIndexPerDir.GetValueOrDefault(_currentPath, 0);
                    _scrollOffset = 0;
                    _markedPaths.Clear();
                    ClearSearchFilter();
                    ClearPreviewCache(previewLoader, buffer);
                    UpdateTerminalTitle();
                    RefreshGitStatus();
                }
                else
                {
                    // File in parent dir — navigate to parent, select file
                    _selectedIndexPerDir[_currentPath] = _selectedIndex;
                    var parentDir = Directory.GetParent(_currentPath);
                    if (parentDir is not null)
                    {
                        _currentPath = PathCompletion.CapitalizeDriveLetter(parentDir.FullName);
                        UpdateTerminalTitle();
                        RefreshGitStatus();
                        var parentEntries = _directoryContents.GetEntries(_currentPath);
                        int idx = parentEntries.FindIndex(e => e.Name.Equals(clicked.Name, StringComparison.OrdinalIgnoreCase));
                        _selectedIndex = idx >= 0 ? idx : 0;
                        _scrollOffset = 0;
                        _markedPaths.Clear();
                        ClearSearchFilter();
                        ClearPreviewCache(previewLoader, buffer);
                    }
                }
            }
        }
        else if (HitTestPane(_layout.RightPane, row, col))
        {
            // Right pane click — only meaningful if selected entry is a directory
            if (entries.Count > 0 && _selectedIndex < entries.Count)
            {
                var selected = entries[_selectedIndex];
                if (selected.IsDirectory)
                {
                    var previewEntries = _directoryContents.GetEntries(selected.FullPath);
                    int entryIndex = row - _layout.RightPane.Top; // scroll is always 0 for preview
                    if (entryIndex >= 0 && entryIndex < previewEntries.Count)
                    {
                        var clicked = previewEntries[entryIndex];
                        if (clicked.IsDirectory)
                        {
                            _selectedIndexPerDir[_currentPath] = _selectedIndex;
                            _currentPath = PathCompletion.CapitalizeDriveLetter(clicked.FullPath);
                            _selectedIndex = _selectedIndexPerDir.GetValueOrDefault(_currentPath, 0);
                            _scrollOffset = 0;
                            _markedPaths.Clear();
                            ClearSearchFilter();
                            ClearPreviewCache(previewLoader, buffer);
                            UpdateTerminalTitle();
                            RefreshGitStatus();
                        }
                        else
                        {
                            // File in previewed directory — navigate there, select the file
                            _selectedIndexPerDir[_currentPath] = _selectedIndex;
                            _currentPath = PathCompletion.CapitalizeDriveLetter(selected.FullPath);
                            UpdateTerminalTitle();
                            RefreshGitStatus();
                            var dirEntries = _directoryContents.GetEntries(_currentPath);
                            int idx = dirEntries.FindIndex(e => e.Name.Equals(clicked.Name, StringComparison.OrdinalIgnoreCase));
                            _selectedIndex = idx >= 0 ? idx : 0;
                            _scrollOffset = 0;
                            _markedPaths.Clear();
                            ClearSearchFilter();
                            ClearPreviewCache(previewLoader, buffer);
                        }
                    }
                }
            }
        }
    }

    private static bool HitTestPane(Rect pane, int row, int col)
    {
        return row >= pane.Top && row < pane.Bottom
            && col >= pane.Left && col < pane.Right;
    }

    private static int CalculateScroll(int selectedIndex, int visibleHeight, int totalCount)
    {
        if (totalCount <= visibleHeight)
        {
            return 0;
        }

        int scroll = selectedIndex - visibleHeight / 2;
        return Math.Clamp(scroll, 0, totalCount - visibleHeight);
    }

    // ── Expanded preview ──────────────────────────────────────────────────

    private void EnterExpandedPreview(PreviewLoader previewLoader, ScreenBuffer buffer)
    {
        _inputMode = InputMode.ExpandedPreview;
        _expandedPreviewScrollOffset = 0;

        if (_isImagePreview && _cachedImagePath is not null)
        {
            previewLoader.Configure(_imagePreviewsEffective, _layout.ExpandedPane.Width, _layout.ExpandedPane.Height,
                _cellPixelWidth, _cellPixelHeight, glowEnabled: _config.GlowMarkdownPreviewEnabled,
            zipPreviewEnabled: _config.ZipPreviewEnabled,
            pdfPreviewEnabled: _config.PdfPreviewEnabled);
            _cachedSixelData = null;
            _sixelPending = false;
            _pendingPreviewPath = _cachedImagePath;
            _previewLoading = true;
            previewLoader.BeginLoad(_cachedImagePath);
        }
        else if (_diffPreviewActive && _cachedPreviewPath is not null && _currentRepoRoot is not null)
        {
            _cachedStyledLines = null;
            _pendingPreviewPath = _cachedPreviewPath;
            _previewLoading = true;

            GitFileStatus status = GitFileStatus.None;
            _gitStatuses?.TryGetValue(_cachedPreviewPath, out status);
            bool staged = !status.HasFlag(GitFileStatus.Modified) && status.HasFlag(GitFileStatus.Staged);
            previewLoader.BeginLoadDiff(_cachedPreviewPath, _currentRepoRoot, staged);
        }
        else if (_hexPreviewActive && _cachedPreviewPath is not null)
        {
            _cachedStyledLines = null;
            _pendingPreviewPath = _cachedPreviewPath;
            _previewLoading = true;
            previewLoader.BeginLoadHex(_cachedPreviewPath);
        }
        else if (_isRenderedPreview && _cachedPreviewPath is not null)
        {
            previewLoader.Configure(_imagePreviewsEffective, _layout.ExpandedPane.Width, _layout.ExpandedPane.Height,
                _cellPixelWidth, _cellPixelHeight, glowEnabled: _config.GlowMarkdownPreviewEnabled,
            zipPreviewEnabled: _config.ZipPreviewEnabled,
            pdfPreviewEnabled: _config.PdfPreviewEnabled);
            _cachedStyledLines = null;
            _pendingPreviewPath = _cachedPreviewPath;
            _previewLoading = true;
            previewLoader.BeginLoad(_cachedPreviewPath);
        }

        buffer.ForceFullRedraw();
    }

    private void LeaveExpandedPreview(PreviewLoader previewLoader, ScreenBuffer buffer)
    {
        _inputMode = InputMode.Normal;
        _expandedPreviewScrollOffset = 0;

        previewLoader.Configure(_imagePreviewsEffective, _layout.RightPane.Width, _layout.RightPane.Height,
            _cellPixelWidth, _cellPixelHeight, glowEnabled: _config.GlowMarkdownPreviewEnabled,
            zipPreviewEnabled: _config.ZipPreviewEnabled,
            pdfPreviewEnabled: _config.PdfPreviewEnabled);

        if (_isImagePreview && _cachedImagePath is not null)
        {
            _cachedSixelData = null;
            _sixelPending = false;
            _pendingPreviewPath = _cachedImagePath;
            _previewLoading = true;
            previewLoader.BeginLoad(_cachedImagePath);
        }
        else if (_isRenderedPreview && _cachedPreviewPath is not null)
        {
            _cachedStyledLines = null;
            _pendingPreviewPath = _cachedPreviewPath;
            _previewLoading = true;
            previewLoader.BeginLoad(_cachedPreviewPath);
        }

        Console.Write(AnsiCodes.ClearScreen);
        buffer.ForceFullRedraw();
    }

    private void HandleExpandedPreviewKey(KeyEvent key, PreviewLoader previewLoader, ScreenBuffer buffer)
    {
        switch (key.Key)
        {
            case ConsoleKey.LeftArrow:
            case ConsoleKey.H:
            case ConsoleKey.Backspace:
            case ConsoleKey.Escape:
            case ConsoleKey.Q:
                LeaveExpandedPreview(previewLoader, buffer);
                break;

            case ConsoleKey.UpArrow:
            case ConsoleKey.K:
                if (_expandedPreviewScrollOffset > 0)
                {
                    _expandedPreviewScrollOffset--;
                }

                break;

            case ConsoleKey.DownArrow:
            case ConsoleKey.J:
                if (_cachedStyledLines is not null)
                {
                    int maxScroll = Math.Max(0, _cachedStyledLines.Length - _layout.ExpandedPane.Height);
                    if (_expandedPreviewScrollOffset < maxScroll)
                    {
                        _expandedPreviewScrollOffset++;
                    }
                }
                break;

            case ConsoleKey.PageUp:
                _expandedPreviewScrollOffset = Math.Max(0, _expandedPreviewScrollOffset - _layout.ExpandedPane.Height);
                break;

            case ConsoleKey.PageDown:
                if (_cachedStyledLines is not null)
                {
                    int maxScroll = Math.Max(0, _cachedStyledLines.Length - _layout.ExpandedPane.Height);
                    _expandedPreviewScrollOffset = Math.Min(maxScroll, _expandedPreviewScrollOffset + _layout.ExpandedPane.Height);
                }
                break;

            case ConsoleKey.Home:
                _expandedPreviewScrollOffset = 0;
                break;

            case ConsoleKey.End:
                if (_cachedStyledLines is not null)
                {
                    _expandedPreviewScrollOffset = Math.Max(0, _cachedStyledLines.Length - _layout.ExpandedPane.Height);
                }

                break;

            default:
                if (key.KeyChar == 'y')
                {
                    string? previewPath = _cachedPreviewPath ?? _cachedImagePath;

                    if (previewPath is not null)
                    {
                        if (SystemClipboard.SetText(previewPath))
                        {
                            ShowNotification("Copied path to clipboard", NotificationKind.Success);
                        }
                        else
                        {
                            ShowNotification("Clipboard not available", NotificationKind.Error);
                        }
                    }
                }
                else if (key.KeyChar == 'Y')
                {
                    string? previewPath = _cachedPreviewPath ?? _cachedImagePath;

                    if (previewPath is not null)
                    {
                        string? repoRoot = GitUtils.FindRepoRoot(_currentPath);

                        if (repoRoot is null)
                        {
                            ShowNotification("Not inside a git repository", NotificationKind.Error);
                        }
                        else
                        {
                            string relativePath = Path.GetRelativePath(repoRoot, previewPath)
                                                      .Replace('\\', '/');

                            if (SystemClipboard.SetText(relativePath))
                            {
                                ShowNotification("Copied git-relative path to clipboard", NotificationKind.Success);
                            }
                            else
                            {
                                ShowNotification("Clipboard not available", NotificationKind.Error);
                            }
                        }
                    }
                }

                break;
        }
    }

    private void HandleExpandedPreviewMouse(MouseEvent mouse)
    {
        if (mouse.Button == MouseButton.ScrollUp)
        {
            if (_expandedPreviewScrollOffset > 0)
            {
                _expandedPreviewScrollOffset--;
            }
        }
        else if (mouse.Button == MouseButton.ScrollDown)
        {
            if (_cachedStyledLines is not null)
            {
                int maxScroll = Math.Max(0, _cachedStyledLines.Length - _layout.ExpandedPane.Height);
                if (_expandedPreviewScrollOffset < maxScroll)
                {
                    _expandedPreviewScrollOffset++;
                }
            }
        }
    }

    private void RenderExpandedPreview(ScreenBuffer buffer, int width, int height)
    {
        var pane = _layout.ExpandedPane;

        if (_previewLoading)
        {
            PaneRenderer.RenderMessage(buffer, pane, "[loading\u2026]");
        }
        else if (_isImagePreview && _cachedSixelData is not null)
        {
            for (int row = pane.Top; row < pane.Bottom; row++)
            {
                buffer.FillRow(row, pane.Left, pane.Width, ' ', CellStyle.Default);
            }

            _sixelPending = true;
        }
        else if (_cachedStyledLines is not null)
        {
            PaneRenderer.RenderPreview(buffer, pane, _cachedStyledLines, _expandedPreviewScrollOffset, showLineNumbers: !_isRenderedPreview);
        }

        // Status bar
        var entries = GetVisibleEntries();
        FileSystemEntry? selectedEntry = entries.Count > 0 && _selectedIndex < entries.Count
            ? entries[_selectedIndex]
            : null;
        string displayPath = _cachedPreviewPath ?? _cachedImagePath ?? _currentPath;
        if (displayPath == DirectoryContents.DrivesPath)
        {
            displayPath = "Drives";
        }

        StatusBar.Render(buffer, _layout.StatusBar, displayPath, entries.Count, _selectedIndex, selectedEntry, _cachedPreviewFileTypeLabel, _cachedPreviewEncoding, _cachedPreviewLineEnding, _notification, _markedPaths.Count, _directoryContents.SortMode, _directoryContents.SortAscending, _clipboardPaths.Count, _clipboardIsCut, _currentBranchName);
    }

    // ── Modal input handlers ────────────────────────────────────────────────

    private void HandleTextInputKey(KeyEvent key)
    {
        switch (key.Key)
        {
            case ConsoleKey.Escape:
                _inputMode = InputMode.Normal;
                _activeTextInput = null;
                _textInputTitle = null;
                _textInputCompleteAction = null;
                break;

            case ConsoleKey.Enter:
                string value = _activeTextInput!.Value;
                Action<string>? completeAction = _textInputCompleteAction;
                _inputMode = InputMode.Normal;
                _activeTextInput = null;
                _textInputTitle = null;
                _textInputCompleteAction = null;
                completeAction?.Invoke(value);
                break;

            case ConsoleKey.Backspace:
                _activeTextInput!.DeleteBackward();
                break;

            case ConsoleKey.Delete:
                _activeTextInput!.DeleteForward();
                break;

            case ConsoleKey.LeftArrow:
                _activeTextInput!.MoveCursorLeft();
                break;

            case ConsoleKey.RightArrow:
                _activeTextInput!.MoveCursorRight();
                break;

            case ConsoleKey.Home:
                _activeTextInput!.MoveCursorHome();
                break;

            case ConsoleKey.End:
                _activeTextInput!.MoveCursorEnd();
                break;

            default:
                if (key.KeyChar >= ' ')
                {
                    _activeTextInput!.InsertChar(key.KeyChar);
                }

                break;
        }
    }

    private void HandleConfirmKey(KeyEvent key)
    {
        switch (key.Key)
        {
            case ConsoleKey.Y:
            case ConsoleKey.Enter:
                Action? yesAction = _confirmYesAction;
                _inputMode = InputMode.Normal;
                _confirmTitle = null;
                _confirmMessage = null;
                _confirmYesAction = null;
                yesAction?.Invoke();
                break;

            case ConsoleKey.N:
            case ConsoleKey.Escape:
                _inputMode = InputMode.Normal;
                _confirmTitle = null;
                _confirmMessage = null;
                _confirmYesAction = null;
                break;
        }
        // All other keys are consumed but ignored
    }

    // ── Go-to-path handlers ────────────────────────────────────────────────

    private void HandleGoToPathKey(KeyEvent key, PreviewLoader previewLoader, ScreenBuffer buffer)
    {
        switch (key.Key)
        {
            case ConsoleKey.Escape:
                if (_goToPathInput!.Value.Length > 0)
                {
                    _goToPathInput.Clear();
                    _goToPathSuggestion = null;
                }
                else
                {
                    _inputMode = InputMode.Normal;
                    _goToPathInput = null;
                    _goToPathSuggestion = null;
                }
                break;

            case ConsoleKey.Enter:
                string path = _goToPathInput!.Value.TrimEnd('/', '\\');
                _inputMode = InputMode.Normal;
                _goToPathInput = null;
                _goToPathSuggestion = null;
                NavigateToPath(path, previewLoader, buffer);
                break;

            case ConsoleKey.Tab:
                if (_goToPathSuggestion is not null)
                {
                    string accepted = _goToPathSuggestion;
                    if (Directory.Exists(accepted))
                    {
                        accepted += Path.DirectorySeparatorChar;
                    }

                    _goToPathInput = new TextInput(accepted);
                    _goToPathSuggestion = GetPathSuggestion(accepted);
                }
                break;

            case ConsoleKey.Backspace:
                _goToPathInput!.DeleteBackward();
                _goToPathSuggestion = GetPathSuggestion(_goToPathInput.Value);
                break;

            case ConsoleKey.Delete:
                _goToPathInput!.DeleteForward();
                _goToPathSuggestion = GetPathSuggestion(_goToPathInput.Value);
                break;

            case ConsoleKey.LeftArrow:
                _goToPathInput!.MoveCursorLeft();
                break;

            case ConsoleKey.RightArrow:
                if (_goToPathSuggestion is not null && _goToPathInput!.CursorPosition == _goToPathInput.Value.Length)
                {
                    string accepted = _goToPathSuggestion;
                    if (Directory.Exists(accepted))
                    {
                        accepted += Path.DirectorySeparatorChar;
                    }

                    _goToPathInput = new TextInput(accepted);
                    _goToPathSuggestion = GetPathSuggestion(accepted);
                }
                else
                {
                    _goToPathInput!.MoveCursorRight();
                }
                break;

            case ConsoleKey.Home:
                _goToPathInput!.MoveCursorHome();
                break;

            case ConsoleKey.End:
                _goToPathInput!.MoveCursorEnd();
                break;

            case ConsoleKey.UpArrow:
            {
                string val = _goToPathInput!.Value;
                if (val.Length > 0)
                {
                    string trimmed = val.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                    int lastSep = trimmed.LastIndexOfAny([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar]);
                    if (lastSep >= 0)
                    {
                        string parent = trimmed[..(lastSep + 1)];
                        _goToPathInput = new TextInput(parent);
                        _goToPathSuggestion = GetPathSuggestion(parent);
                    }
                }

                break;
            }

            default:
                if (key.KeyChar >= ' ')
                {
                    _goToPathInput!.InsertChar(key.KeyChar);
                    _goToPathSuggestion = GetPathSuggestion(_goToPathInput.Value);
                }
                break;
        }
    }

    private void NavigateToPath(string path, PreviewLoader previewLoader, ScreenBuffer buffer)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        try
        {
            path = Path.GetFullPath(PathCompletion.NormalizeSeparators(PathCompletion.ExpandTilde(path)));
        }
        catch
        {
            ShowNotification("Invalid path", NotificationKind.Error);
            return;
        }

        if (Directory.Exists(path))
        {
            _selectedIndexPerDir[_currentPath] = _selectedIndex;
            _currentPath = PathCompletion.CapitalizeDriveLetter(path);
            _selectedIndex = 0;
            _scrollOffset = 0;
            _markedPaths.Clear();
            ClearSearchFilter();
            ClearPreviewCache(previewLoader, buffer);
            UpdateTerminalTitle();
            RefreshGitStatus();
        }
        else if (File.Exists(path))
        {
            string? parent = Path.GetDirectoryName(path);
            if (parent is not null)
            {
                _selectedIndexPerDir[_currentPath] = _selectedIndex;
                _currentPath = PathCompletion.CapitalizeDriveLetter(parent);
                UpdateTerminalTitle();
                RefreshGitStatus();
                var entries = _directoryContents.GetEntries(_currentPath);
                string fileName = Path.GetFileName(path);
                int idx = entries.FindIndex(e => e.Name.Equals(fileName, StringComparison.OrdinalIgnoreCase));
                _selectedIndex = idx >= 0 ? idx : 0;
                _scrollOffset = 0;
                _markedPaths.Clear();
                ClearSearchFilter();
                ClearPreviewCache(previewLoader, buffer);
            }
        }
        else
        {
            ShowNotification("Path not found", NotificationKind.Error);
        }
    }

    private void RefreshGitStatus()
    {
        if (!_config.GitStatusEnabled)
        {
            _currentRepoRoot = null;
            _currentBranchName = null;
            _gitStatuses = null;
            _gitStatusLoader?.Cancel();
            return;
        }

        var repoRoot = GitUtils.FindRepoRoot(_currentPath);
        _currentRepoRoot = repoRoot;

        if (repoRoot is not null)
        {
            _gitStatusLoader?.BeginLoad(repoRoot);
        }
        else
        {
            _currentBranchName = null;
            _gitStatuses = null;
        }
    }

    private string? GetPathSuggestion(string input) =>
        PathCompletion.GetSuggestion(input, _directoryContents.ShowHiddenFiles, _directoryContents.ShowSystemFiles);

    private void RenderGoToPathDialog(ScreenBuffer buffer, int width, int height)
    {
        int contentWidth = Math.Min(60, width - 8);
        int contentHeight = 1;
        string footer = "[Tab] Complete  [\u2191] Up dir  [Esc] Clear/Close  [Enter] Go";

        Rect content = DialogBox.Render(buffer, width, height, contentWidth, contentHeight,
            title: "Go to path", footer: footer);

        var inputStyle = new CellStyle(new Color(200, 200, 200), DialogBox.BgColor);
        _goToPathInput?.Render(buffer, content.Top, content.Left, content.Width, inputStyle);

        // Inline ghost suffix — show the untyped remainder of the suggestion after the cursor
        if (_goToPathSuggestion is not null && _goToPathInput is not null)
        {
            string inputValue = _goToPathInput.Value;
            string expandedInput = PathCompletion.NormalizeSeparators(PathCompletion.ExpandTilde(inputValue));

            // Only show ghost when cursor is at end and suggestion extends beyond expanded input
            if (_goToPathInput.CursorPosition == inputValue.Length
                && _goToPathSuggestion.Length > expandedInput.Length
                && _goToPathSuggestion.StartsWith(expandedInput, StringComparison.OrdinalIgnoreCase))
            {
                int scrollOffset = _goToPathInput.ScrollOffset;
                int visualTextEnd = inputValue.Length - scrollOffset + 1; // +1 for cursor space
                int ghostCol = content.Left + visualTextEnd;
                int ghostMaxWidth = content.Width - visualTextEnd;

                if (ghostMaxWidth > 0)
                {
                    string ghost = _goToPathSuggestion[expandedInput.Length..];
                    var ghostStyle = new CellStyle(new Color(90, 90, 110), DialogBox.BgColor);
                    buffer.WriteString(content.Top, ghostCol, ghost, ghostStyle, ghostMaxWidth);
                }
            }
        }
    }

    // ── Modal entry points ──────────────────────────────────────────────────

    private void ExecuteDelete(List<string> targets, bool permanent)
    {
        int errors = FileOperations.Delete(targets, permanent);

        _directoryContents.Invalidate(_currentPath);
        InvalidateFilteredEntries();
        _markedPaths.Clear();
        RefreshGitStatus();

        if (errors > 0)
        {
            ShowNotification($"Deleted with {errors} error(s)", NotificationKind.Error);
        }
        else
        {
            ShowNotification($"Deleted {targets.Count} item(s)", NotificationKind.Success);
        }
    }

    private void ShowConfirmDialog(string title, string message, Action onYes)
    {
        _inputMode = InputMode.Confirm;
        _confirmTitle = title;
        _confirmMessage = message;
        _confirmYesAction = onYes;
    }

    private void ShowTextInputDialog(string title, string initialValue, Action<string> onComplete)
    {
        _inputMode = InputMode.TextInput;
        _textInputTitle = title;
        _activeTextInput = new TextInput(initialValue);
        _textInputCompleteAction = onComplete;
    }

    // ── Notification helpers ──────────────────────────────────────────────────

    private void ShowNotification(string message, NotificationKind kind = NotificationKind.Info)
    {
        _notification = new Notification(message, kind, Environment.TickCount64);
    }

    // ── Search/filter helpers ────────────────────────────────────────────────

    private List<FileSystemEntry> GetVisibleEntries()
    {
        var all = _directoryContents.GetEntries(_currentPath);
        if (string.IsNullOrEmpty(_searchFilter))
        {
            _filteredEntries = null;
            return all;
        }

        _filteredEntries ??= all
            .Where(e => e.Name.Contains(_searchFilter, StringComparison.OrdinalIgnoreCase))
            .ToList();

        return _filteredEntries;
    }

    private void ClearSearchFilter()
    {
        _searchFilter = "";
        _filteredEntries = null;
        _searchInput = null;
        if (_inputMode == InputMode.Search)
        {
            _inputMode = InputMode.Normal;
        }
    }

    private void InvalidateFilteredEntries()
    {
        _filteredEntries = null;
    }

    private int VisibleFileListHeight =>
        (_inputMode == InputMode.Search || !string.IsNullOrEmpty(_searchFilter))
            ? _layout.CenterPane.Height - 1
            : _layout.CenterPane.Height;

    private void HandleSearchKey(KeyEvent key)
    {
        switch (key.Key)
        {
            case ConsoleKey.Escape:
                ClearSearchFilter();
                _selectedIndex = 0;
                _scrollOffset = 0;
                break;

            case ConsoleKey.Enter:
                _inputMode = InputMode.Normal;
                _searchInput = null;
                break;

            case ConsoleKey.UpArrow:
                if (_selectedIndex > 0)
                {
                    _selectedIndex--;
                }

                break;

            case ConsoleKey.DownArrow:
            {
                var entries = GetVisibleEntries();
                if (_selectedIndex < entries.Count - 1)
                {
                    _selectedIndex++;
                }

                break;
            }

            case ConsoleKey.LeftArrow:
                _searchInput!.MoveCursorLeft();
                break;

            case ConsoleKey.RightArrow:
                _searchInput!.MoveCursorRight();
                break;

            case ConsoleKey.Home:
                _searchInput!.MoveCursorHome();
                break;

            case ConsoleKey.End:
                _searchInput!.MoveCursorEnd();
                break;

            case ConsoleKey.Backspace:
                _searchInput!.DeleteBackward();
                _searchFilter = _searchInput.Value;
                InvalidateFilteredEntries();
                _selectedIndex = 0;
                _scrollOffset = 0;
                break;

            case ConsoleKey.Delete:
                _searchInput!.DeleteForward();
                _searchFilter = _searchInput.Value;
                InvalidateFilteredEntries();
                _selectedIndex = 0;
                _scrollOffset = 0;
                break;

            default:
                if (key.KeyChar >= ' ')
                {
                    _searchInput!.InsertChar(key.KeyChar);
                    _searchFilter = _searchInput.Value;
                    InvalidateFilteredEntries();
                    _selectedIndex = 0;
                    _scrollOffset = 0;
                }
                break;
        }
    }

    // ── Modal rendering ─────────────────────────────────────────────────────

    private void RenderConfirmDialog(ScreenBuffer buffer, int width, int height)
    {
        string message = _confirmMessage ?? "";
        string[] lines = message.Split('\n');
        string footer = "[Y/Enter] Yes  [N/Esc] No";
        int maxLineLen = 0;
        foreach (string line in lines)
        {
            if (line.Length > maxLineLen)
            {
                maxLineLen = line.Length;
            }
        }

        int contentWidth = Math.Max(maxLineLen, footer.Length) + 2;
        int contentHeight = lines.Length;

        Rect content = DialogBox.Render(buffer, width, height, contentWidth, contentHeight, title: _confirmTitle, footer: footer);

        var textStyle = new CellStyle(new Color(200, 200, 200), DialogBox.BgColor);
        var warnStyle = new CellStyle(new Color(255, 100, 100), DialogBox.BgColor);

        for (int i = 0; i < lines.Length; i++)
        {
            string line = lines[i];
            int msgCol = content.Left + (content.Width - line.Length) / 2;
            var style = i > 0 ? warnStyle : textStyle;
            buffer.WriteString(content.Top + i, msgCol, line, style);
        }
    }

    private void RenderTextInputDialog(ScreenBuffer buffer, int width, int height)
    {
        int contentWidth = Math.Min(40, width - 8);
        int contentHeight = 1; // single row for text input
        string footer = "[Enter] Confirm  [Esc] Cancel";

        Rect content = DialogBox.Render(buffer, width, height, contentWidth, contentHeight, title: _textInputTitle, footer: footer);

        var inputStyle = new CellStyle(new Color(200, 200, 200), DialogBox.BgColor);
        _activeTextInput?.Render(buffer, content.Top, content.Left, content.Width, inputStyle);
    }

    // ── Action palette ─────────────────────────────────────────────────────

    private void ShowActionPalette()
    {
        _inputMode = InputMode.ActionPalette;
        _actionPaletteSelectedIndex = 0;
        _actionPaletteScrollOffset = 0;
        _actionPaletteInput = new TextInput();
        _actionPaletteItems = BuildActionPaletteItems();
    }

    private (string Label, string Shortcut, AppAction Action)[] BuildActionPaletteItems()
    {
        var items = new List<(string Label, string Shortcut, AppAction Action)>
        {
            ("Open with default app", "o", AppAction.OpenExternal),
            ("Rename", "F2", AppAction.Rename),
            ("Delete", "Del", AppAction.Delete),
            ("Copy", "c", AppAction.Copy),
            ("Cut", "x", AppAction.Cut),
        };

        if (_clipboardPaths.Count > 0)
        {
            items.Add(("Paste", "p", AppAction.Paste));
        }

        items.Add(("Copy absolute path", "y", AppAction.CopyAbsolutePath));
        items.Add(("New file", "n", AppAction.NewFile));
        items.Add(("New directory", "Shift+N", AppAction.NewDirectory));
        items.Add(("Create symlink", "Ctrl+L", AppAction.CreateSymlink));
        items.Add(("Properties", "i", AppAction.ShowProperties));
        items.Add(("Toggle hex preview", "", AppAction.ToggleHexPreview));
        items.Add(("Toggle hidden files", ".", AppAction.ToggleHiddenFiles));
        items.Add(("Cycle sort mode", "s", AppAction.CycleSortMode));
        items.Add(("Reverse sort direction", "S", AppAction.ToggleSortDirection));
        items.Add(("Bookmarks", "b", AppAction.ShowBookmarks));
        items.Add(("Toggle bookmark", "B", AppAction.ToggleBookmark));
        items.Add(("Go to path", "g", AppAction.GoToPath));
        items.Add(("Find file", "Ctrl+F", AppAction.ShowFileFinder));
        items.Add(("Search / filter", "/", AppAction.Search));
        items.Add(("Open terminal here", "Ctrl+T", AppAction.OpenTerminal));
        items.Add(("Configuration", ",", AppAction.ShowConfig));
        items.Add(("Help", "?", AppAction.ShowHelp));
        items.Add(("Refresh", "Ctrl+R", AppAction.Refresh));

        // Git actions — only shown when actionable
        if (_currentRepoRoot is not null)
        {
            items.Add(("Git: Copy relative path", "Y", AppAction.CopyGitRelativePath));

            if (_gitStatuses is not null)
            {
                var entries = GetVisibleEntries();
                if (_selectedIndex < entries.Count)
                {
                    var selected = entries[_selectedIndex];
                    _gitStatuses.TryGetValue(selected.FullPath, out var selectedStatus);
                    if (selectedStatus.HasFlag(GitFileStatus.Modified) || selectedStatus.HasFlag(GitFileStatus.Staged))
                    {
                        items.Add(("Git: Toggle diff preview", "", AppAction.ToggleDiffPreview));
                    }

                    // Stage: visible when focused/marked files have Modified or Untracked status
                    bool hasStageableStatus = HasStatusInSelection(GitFileStatus.Modified | GitFileStatus.Untracked);
                    if (hasStageableStatus)
                    {
                        items.Add(("Git: Stage", "", AppAction.StageFile));
                    }

                    // Unstage: visible when focused/marked files have Staged status
                    bool hasUnstageableStatus = HasStatusInSelection(GitFileStatus.Staged);
                    if (hasUnstageableStatus)
                    {
                        items.Add(("Git: Unstage", "", AppAction.UnstageFile));
                    }
                }

                // Stage all: visible when any file has Modified or Untracked status
                bool hasAnyChanges = false;
                foreach (var kvp in _gitStatuses)
                {
                    if ((kvp.Value & (GitFileStatus.Modified | GitFileStatus.Untracked)) != 0)
                    {
                        hasAnyChanges = true;
                        break;
                    }
                }

                if (hasAnyChanges)
                {
                    items.Add(("Git: Stage all changes", "", AppAction.StageAll));
                }

                bool hasAnyStagedChanges = false;
                foreach (var kvp in _gitStatuses)
                {
                    if ((kvp.Value & GitFileStatus.Staged) != 0)
                    {
                        hasAnyStagedChanges = true;
                        break;
                    }
                }

                if (hasAnyStagedChanges)
                {
                    items.Add(("Git: Unstage all", "", AppAction.UnstageAll));
                }
            }
        }

        return items.ToArray();
    }

    private List<(string Label, string Shortcut, AppAction Action)> GetFilteredActionPaletteItems()
    {
        if (_actionPaletteItems is null)
        {
            return [];
        }

        string filter = _actionPaletteInput?.Value ?? "";

        if (string.IsNullOrEmpty(filter))
        {
            return [.. _actionPaletteItems];
        }

        var result = new List<(string Label, string Shortcut, AppAction Action)>();

        foreach (var item in _actionPaletteItems)
        {
            if (item.Label.Contains(filter, StringComparison.OrdinalIgnoreCase))
            {
                result.Add(item);
            }
        }

        return result;
    }

    private void HandleActionPaletteKey(KeyEvent key, PreviewLoader previewLoader, ScreenBuffer buffer, InputPipeline pipeline)
    {
        var filtered = GetFilteredActionPaletteItems();

        switch (key.Key)
        {
            case ConsoleKey.Escape:
                _inputMode = InputMode.Normal;
                _actionPaletteInput = null;
                _actionPaletteItems = null;
                break;

            case ConsoleKey.Enter:
                if (filtered.Count > 0 && _actionPaletteSelectedIndex < filtered.Count)
                {
                    var selectedAction = filtered[_actionPaletteSelectedIndex].Action;
                    _inputMode = InputMode.Normal;
                    _actionPaletteInput = null;
                    _actionPaletteItems = null;
                    DispatchActionPaletteAction(selectedAction, previewLoader, buffer, pipeline);
                }

                break;

            case ConsoleKey.UpArrow:
                if (_actionPaletteSelectedIndex > 0)
                {
                    _actionPaletteSelectedIndex--;
                }

                break;

            case ConsoleKey.DownArrow:
                if (_actionPaletteSelectedIndex < filtered.Count - 1)
                {
                    _actionPaletteSelectedIndex++;
                }

                break;

            case ConsoleKey.PageUp:
            {
                int visibleCount = Math.Min(18, filtered.Count);
                _actionPaletteSelectedIndex = Math.Max(0, _actionPaletteSelectedIndex - visibleCount);
                break;
            }

            case ConsoleKey.PageDown:
            {
                int visibleCount = Math.Min(18, filtered.Count);
                _actionPaletteSelectedIndex = Math.Min(filtered.Count - 1, _actionPaletteSelectedIndex + visibleCount);
                break;
            }

            case ConsoleKey.Home:
                _actionPaletteSelectedIndex = 0;
                break;

            case ConsoleKey.End:
                _actionPaletteSelectedIndex = Math.Max(0, filtered.Count - 1);
                break;

            case ConsoleKey.Backspace:
                _actionPaletteInput!.DeleteBackward();
                _actionPaletteSelectedIndex = 0;
                _actionPaletteScrollOffset = 0;
                break;

            case ConsoleKey.Delete:
                _actionPaletteInput!.DeleteForward();
                _actionPaletteSelectedIndex = 0;
                _actionPaletteScrollOffset = 0;
                break;

            case ConsoleKey.LeftArrow:
                _actionPaletteInput!.MoveCursorLeft();
                break;

            case ConsoleKey.RightArrow:
                _actionPaletteInput!.MoveCursorRight();
                break;

            default:
                if (key.Key == ConsoleKey.K && key.Control)
                {
                    if (_actionPaletteSelectedIndex > 0)
                    {
                        _actionPaletteSelectedIndex--;
                    }
                }
                else if (key.Key == ConsoleKey.J && key.Control)
                {
                    if (_actionPaletteSelectedIndex < filtered.Count - 1)
                    {
                        _actionPaletteSelectedIndex++;
                    }
                }
                else if (key.KeyChar >= ' ')
                {
                    _actionPaletteInput!.InsertChar(key.KeyChar);
                    _actionPaletteSelectedIndex = 0;
                    _actionPaletteScrollOffset = 0;
                }

                break;
        }

        // Adjust scroll offset to keep selection visible
        filtered = GetFilteredActionPaletteItems();

        if (filtered.Count > 0)
        {
            _actionPaletteSelectedIndex = Math.Clamp(_actionPaletteSelectedIndex, 0, filtered.Count - 1);
        }
        else
        {
            _actionPaletteSelectedIndex = 0;
        }

        int maxVisible = 18;

        if (_actionPaletteSelectedIndex < _actionPaletteScrollOffset)
        {
            _actionPaletteScrollOffset = _actionPaletteSelectedIndex;
        }
        else if (_actionPaletteSelectedIndex >= _actionPaletteScrollOffset + maxVisible)
        {
            _actionPaletteScrollOffset = _actionPaletteSelectedIndex - maxVisible + 1;
        }
    }

    /// <summary>
    /// Dispatches file-operation actions shared between the main key handler and the action palette.
    /// Returns true if the action was handled.
    /// </summary>
    private bool DispatchFileAction(AppAction action)
    {
        var entries = GetVisibleEntries();

        switch (action)
        {
            case AppAction.OpenExternal:
                if (entries.Count > 0 && _selectedIndex < entries.Count)
                {
                    var entry = entries[_selectedIndex];
                    try
                    {
                        Process.Start(new ProcessStartInfo(entry.FullPath) { UseShellExecute = true });
                        ShowNotification($"Opened '{entry.Name}'", NotificationKind.Success);
                    }
                    catch (Exception ex)
                    {
                        ShowNotification($"Error: {ex.Message}", NotificationKind.Error);
                    }
                }

                return true;

            case AppAction.Rename:
                if (entries.Count > 0 && _selectedIndex < entries.Count)
                {
                    var entry = entries[_selectedIndex];
                    ShowTextInputDialog("Rename", entry.Name, newName =>
                    {
                        if (string.IsNullOrWhiteSpace(newName) || newName == entry.Name)
                        {
                            return;
                        }

                        string parentDir = Path.GetDirectoryName(entry.FullPath)!;
                        string newPath = Path.Combine(parentDir, newName);

                        if (Path.Exists(newPath))
                        {
                            ShowNotification($"'{newName}' already exists", NotificationKind.Error);
                            return;
                        }

                        try
                        {
                            if (entry.IsDirectory)
                            {
                                Directory.Move(entry.FullPath, newPath);
                            }
                            else
                            {
                                File.Move(entry.FullPath, newPath);
                            }

                            _directoryContents.Invalidate(_currentPath);
                            InvalidateFilteredEntries();
                            RefreshGitStatus();
                            ShowNotification($"Renamed to '{newName}'", NotificationKind.Success);

                            var updatedEntries = GetVisibleEntries();
                            for (int i = 0; i < updatedEntries.Count; i++)
                            {
                                if (updatedEntries[i].Name.Equals(newName, StringComparison.OrdinalIgnoreCase))
                                {
                                    _selectedIndex = i;
                                    break;
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            ShowNotification($"Rename failed: {ex.Message}", NotificationKind.Error);
                        }
                    });
                }

                return true;

            case AppAction.Delete:
            case AppAction.DeletePermanently:
                if (entries.Count > 0)
                {
                    List<string> targets;
                    string prompt;

                    if (_markedPaths.Count > 0)
                    {
                        targets = [.. _markedPaths];
                        prompt = $"Delete {targets.Count} item(s)?";
                    }
                    else if (_selectedIndex < entries.Count)
                    {
                        targets = [entries[_selectedIndex].FullPath];
                        prompt = $"Delete '{entries[_selectedIndex].Name}'?";
                    }
                    else
                    {
                        return true;
                    }

                    bool permanent = action == AppAction.DeletePermanently;
                    bool isPermanent = permanent || !OperatingSystem.IsWindows();

                    if (_config.ConfirmDeleteEnabled)
                    {
                        string title = isPermanent ? "Permanently Delete" : "Delete";
                        string warning = isPermanent ? "\nThis cannot be undone!" : "";
                        ShowConfirmDialog(title, prompt + warning, () => ExecuteDelete(targets, permanent));
                    }
                    else
                    {
                        ExecuteDelete(targets, permanent);
                    }
                }

                return true;

            case AppAction.Copy:
                if (_markedPaths.Count > 0)
                {
                    _clipboardPaths.Clear();
                    _clipboardPaths.AddRange(_markedPaths);
                    _clipboardIsCut = false;
                    ShowNotification($"Copied {_markedPaths.Count} item(s)", NotificationKind.Success);
                }
                else if (entries.Count > 0 && _selectedIndex < entries.Count)
                {
                    _clipboardPaths.Clear();
                    _clipboardPaths.Add(entries[_selectedIndex].FullPath);
                    _clipboardIsCut = false;
                    ShowNotification($"Copied '{entries[_selectedIndex].Name}'", NotificationKind.Success);
                }

                if (OperatingSystem.IsWindows())
                {
                    SystemClipboard.SetFiles(_clipboardPaths, _clipboardIsCut);
                }

                return true;

            case AppAction.Cut:
                if (_markedPaths.Count > 0)
                {
                    _clipboardPaths.Clear();
                    _clipboardPaths.AddRange(_markedPaths);
                    _clipboardIsCut = true;
                    ShowNotification($"Cut {_markedPaths.Count} item(s)", NotificationKind.Success);
                }
                else if (entries.Count > 0 && _selectedIndex < entries.Count)
                {
                    _clipboardPaths.Clear();
                    _clipboardPaths.Add(entries[_selectedIndex].FullPath);
                    _clipboardIsCut = true;
                    ShowNotification($"Cut '{entries[_selectedIndex].Name}'", NotificationKind.Success);
                }

                if (OperatingSystem.IsWindows())
                {
                    SystemClipboard.SetFiles(_clipboardPaths, _clipboardIsCut);
                }

                return true;

            case AppAction.Paste:
                if (OperatingSystem.IsWindows())
                {
                    var osFiles = SystemClipboard.GetFiles();

                    if (osFiles is not null && osFiles.Value.Paths.Count > 0)
                    {
                        _clipboardPaths.Clear();
                        _clipboardPaths.AddRange(osFiles.Value.Paths);
                        _clipboardIsCut = osFiles.Value.IsCut;
                    }
                }

                if (_clipboardPaths.Count == 0)
                {
                    ShowNotification("Clipboard is empty", NotificationKind.Error);
                }
                else
                {
                    int conflicts = _clipboardPaths.Count(p => Path.Exists(Path.Combine(_currentPath, Path.GetFileName(p))));

                    if (conflicts > 0)
                    {
                        ShowConfirmDialog("Overwrite", $"{conflicts} item(s) already exist. Overwrite?", () => ExecutePaste(overwrite: true));
                    }
                    else
                    {
                        ExecutePaste(overwrite: false);
                    }
                }

                return true;

            case AppAction.CopyAbsolutePath:
                if (entries.Count > 0 && _selectedIndex < entries.Count)
                {
                    string pathToCopy = entries[_selectedIndex].FullPath;

                    if (SystemClipboard.SetText(pathToCopy))
                    {
                        ShowNotification("Copied path to clipboard", NotificationKind.Success);
                    }
                    else
                    {
                        ShowNotification("Clipboard not available", NotificationKind.Error);
                    }
                }

                return true;

            case AppAction.CopyGitRelativePath:
                if (entries.Count > 0 && _selectedIndex < entries.Count)
                {
                    string? repoRoot = GitUtils.FindRepoRoot(_currentPath);

                    if (repoRoot is null)
                    {
                        ShowNotification("Not inside a git repository", NotificationKind.Error);
                    }
                    else
                    {
                        string relativePath = Path.GetRelativePath(repoRoot, entries[_selectedIndex].FullPath)
                                                  .Replace('\\', '/');

                        if (SystemClipboard.SetText(relativePath))
                        {
                            ShowNotification("Copied git-relative path to clipboard", NotificationKind.Success);
                        }
                        else
                        {
                            ShowNotification("Clipboard not available", NotificationKind.Error);
                        }
                    }
                }

                return true;

            case AppAction.NewFile:
                ShowTextInputDialog("New File", "", name =>
                {
                    if (string.IsNullOrWhiteSpace(name))
                    {
                        return;
                    }

                    if (name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
                    {
                        ShowNotification("Invalid file name", NotificationKind.Error);
                        return;
                    }

                    string destPath = Path.Combine(_currentPath, name);

                    if (Path.Exists(destPath))
                    {
                        ShowNotification($"'{name}' already exists", NotificationKind.Error);
                        return;
                    }

                    try
                    {
                        File.Create(destPath).Dispose();
                        _directoryContents.Invalidate(_currentPath);
                        InvalidateFilteredEntries();
                        RefreshGitStatus();
                        ShowNotification($"Created '{name}'", NotificationKind.Success);

                        var updatedEntries = GetVisibleEntries();
                        for (int i = 0; i < updatedEntries.Count; i++)
                        {
                            if (updatedEntries[i].Name.Equals(name, StringComparison.OrdinalIgnoreCase))
                            {
                                _selectedIndex = i;
                                break;
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        ShowNotification($"Create failed: {ex.Message}", NotificationKind.Error);
                    }
                });
                return true;

            case AppAction.NewDirectory:
                ShowTextInputDialog("New Directory", "", name =>
                {
                    if (string.IsNullOrWhiteSpace(name))
                    {
                        return;
                    }

                    if (name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
                    {
                        ShowNotification("Invalid directory name", NotificationKind.Error);
                        return;
                    }

                    string destPath = Path.Combine(_currentPath, name);

                    if (Path.Exists(destPath))
                    {
                        ShowNotification($"'{name}' already exists", NotificationKind.Error);
                        return;
                    }

                    try
                    {
                        Directory.CreateDirectory(destPath);
                        _directoryContents.Invalidate(_currentPath);
                        InvalidateFilteredEntries();
                        RefreshGitStatus();
                        ShowNotification($"Created '{name}'", NotificationKind.Success);

                        var updatedEntries = GetVisibleEntries();
                        for (int i = 0; i < updatedEntries.Count; i++)
                        {
                            if (updatedEntries[i].Name.Equals(name, StringComparison.OrdinalIgnoreCase))
                            {
                                _selectedIndex = i;
                                break;
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        ShowNotification($"Create failed: {ex.Message}", NotificationKind.Error);
                    }
                });
                return true;

            case AppAction.CreateSymlink:
            {
                if (entries.Count == 0 || _selectedIndex >= entries.Count)
                {
                    return true;
                }

                var selectedEntry = entries[_selectedIndex];
                string target = selectedEntry.FullPath;

                ShowTextInputDialog("Create Symlink", selectedEntry.Name + "_link", linkName =>
                {
                    if (string.IsNullOrWhiteSpace(linkName))
                    {
                        return;
                    }

                    if (linkName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
                    {
                        ShowNotification("Invalid link name", NotificationKind.Error);
                        return;
                    }

                    string linkPath = Path.Combine(_currentPath, linkName);

                    if (Path.Exists(linkPath))
                    {
                        ShowNotification($"'{linkName}' already exists", NotificationKind.Error);
                        return;
                    }

                    try
                    {
                        if (selectedEntry.IsDirectory)
                        {
                            Directory.CreateSymbolicLink(linkPath, target);
                        }
                        else
                        {
                            File.CreateSymbolicLink(linkPath, target);
                        }

                        _directoryContents.Invalidate(_currentPath);
                        InvalidateFilteredEntries();
                        RefreshGitStatus();
                        ShowNotification($"Created symlink '{linkName}'", NotificationKind.Success);

                        var updatedEntries = GetVisibleEntries();
                        for (int i = 0; i < updatedEntries.Count; i++)
                        {
                            if (updatedEntries[i].Name.Equals(linkName, StringComparison.OrdinalIgnoreCase))
                            {
                                _selectedIndex = i;
                                break;
                            }
                        }
                    }
                    catch (UnauthorizedAccessException)
                    {
                        ShowNotification("Insufficient privileges to create symlink", NotificationKind.Error);
                    }
                    catch (Exception ex)
                    {
                        ShowNotification($"Create symlink failed: {ex.Message}", NotificationKind.Error);
                    }
                });
                return true;
            }

            default:
                return false;
        }
    }

    private void DispatchActionPaletteAction(AppAction action, PreviewLoader previewLoader, ScreenBuffer buffer, InputPipeline pipeline)
    {
        if (DispatchFileAction(action))
        {
            return;
        }

        var entries = GetVisibleEntries();

        switch (action)
        {
            case AppAction.ShowProperties:
                if (entries.Count > 0 && _selectedIndex < entries.Count)
                {
                    _inputMode = InputMode.Properties;
                    var propsEntry2 = entries[_selectedIndex];
                    if (propsEntry2.IsDirectory && !propsEntry2.IsDrive)
                    {
                        _propertiesDirSizePath = propsEntry2.FullPath;
                        _propertiesDirSizeText = "Calculating\u2026";
                        _dirSizeLoader!.BeginCalculation(propsEntry2.FullPath);
                    }
                    else
                    {
                        _propertiesDirSizePath = null;
                        _propertiesDirSizeText = null;
                    }
                }

                break;

            case AppAction.ToggleDiffPreview:
                HandleToggleDiffPreview(GetVisibleEntries(), previewLoader);
                break;

            case AppAction.ToggleHexPreview:
                HandleToggleHexPreview(GetVisibleEntries(), previewLoader);
                break;

            case AppAction.ToggleHiddenFiles:
                _directoryContents.ShowHiddenFiles = !_directoryContents.ShowHiddenFiles;
                _directoryContents.InvalidateAll();
                ClearPreviewCache(previewLoader, buffer);
                break;

            case AppAction.CycleSortMode:
                _directoryContents.SortMode = _directoryContents.SortMode switch
                {
                    SortMode.Name => SortMode.Modified,
                    SortMode.Modified => SortMode.Size,
                    SortMode.Size => SortMode.Extension,
                    _ => SortMode.Name,
                };
                _directoryContents.InvalidateAll();
                ClearPreviewCache(previewLoader, buffer);
                break;

            case AppAction.ToggleSortDirection:
                _directoryContents.SortAscending = !_directoryContents.SortAscending;
                _directoryContents.InvalidateAll();
                ClearPreviewCache(previewLoader, buffer);
                break;

            case AppAction.GoToPath:
                _inputMode = InputMode.GoToPath;
                _goToPathInput = new TextInput();
                _goToPathSuggestion = null;
                break;

            case AppAction.Search:
                _inputMode = InputMode.Search;
                _searchInput = new TextInput(_searchFilter);
                break;

            case AppAction.OpenTerminal:
                try
                {
                    OpenTerminalHere(_currentPath);
                    ShowNotification("Opened terminal", NotificationKind.Success);
                }
                catch (Exception ex)
                {
                    ShowNotification($"Error: {ex.Message}", NotificationKind.Error);
                }

                break;

            case AppAction.ShowConfig:
                ShowConfigDialog();
                break;

            case AppAction.ShowHelp:
                _inputMode = InputMode.Help;
                break;

            case AppAction.ShowBookmarks:
                ShowBookmarks();
                break;

            case AppAction.ToggleBookmark:
                _bookmarkStore.Toggle(_currentPath);
                ShowNotification(
                    _bookmarkStore.Contains(_currentPath) ? "Bookmarked" : "Bookmark removed",
                    NotificationKind.Success);
                break;

            case AppAction.ShowFileFinder:
                ShowFileFinder(pipeline);
                break;

            case AppAction.Refresh:
                _notification = null;
                _markedPaths.Clear();
                ClearSearchFilter();
                _directoryContents.InvalidateAll();
                ClearPreviewCache(previewLoader, buffer);
                RefreshGitStatus();
                buffer.ForceFullRedraw();
                break;

            case AppAction.StageFile:
                if (_currentRepoRoot is not null)
                {
                    var stagePaths = GetSelectedOrMarkedPaths(entries);
                    if (stagePaths.Count > 0)
                    {
                        _gitActionRunner?.RunStage(_currentRepoRoot, stagePaths);
                    }
                }

                break;

            case AppAction.UnstageFile:
                if (_currentRepoRoot is not null)
                {
                    var unstagePaths = GetSelectedOrMarkedPaths(entries);
                    if (unstagePaths.Count > 0)
                    {
                        _gitActionRunner?.RunUnstage(_currentRepoRoot, unstagePaths);
                    }
                }

                break;

            case AppAction.StageAll:
                if (_currentRepoRoot is not null)
                {
                    _gitActionRunner?.RunStageAll(_currentRepoRoot);
                }

                break;

            case AppAction.UnstageAll:
                if (_currentRepoRoot is not null)
                {
                    _gitActionRunner?.RunUnstageAll(_currentRepoRoot);
                }

                break;

        }
    }

    private List<string> GetSelectedOrMarkedPaths(List<FileSystemEntry> entries)
    {
        if (_markedPaths.Count > 0)
        {
            return [.. _markedPaths];
        }

        if (_selectedIndex < entries.Count)
        {
            return [entries[_selectedIndex].FullPath];
        }

        return [];
    }

    private void RenderActionPalette(ScreenBuffer buffer, int width, int height)
    {
        var filtered = GetFilteredActionPaletteItems();
        int contentWidth = Math.Min(60, width - 8);
        int itemRows = Math.Min(filtered.Count, 18);
        int contentHeight = itemRows + 2; // 1 row for text input + 1 separator + item rows
        const string Footer = "[↑↓] Navigate  [Enter] Select  [Esc] Cancel";

        var content = DialogBox.Render(
            buffer, width, height,
            Math.Max(contentWidth, Footer.Length),
            contentHeight,
            title: "Action Palette",
            footer: Footer);

        // Row 0: text input with "> " prefix
        var prefixStyle = new CellStyle(new Color(220, 220, 100), DialogBox.BgColor);
        var inputStyle = new CellStyle(new Color(200, 200, 200), DialogBox.BgColor);
        buffer.WriteString(content.Top, content.Left, "> ", prefixStyle);
        _actionPaletteInput?.Render(buffer, content.Top, content.Left + 2, content.Width - 2, inputStyle);

        // Row 1: separator
        var separatorStyle = new CellStyle(DialogBox.BorderColor, DialogBox.BgColor, Dim: true);
        for (int c = 0; c < content.Width; c++)
        {
            buffer.Put(content.Top + 1, content.Left + c, '─', separatorStyle);
        }

        // Rows 2+: filtered items
        var normalStyle = new CellStyle(new Color(200, 200, 200), DialogBox.BgColor);
        var selectedStyle = new CellStyle(new Color(20, 20, 35), new Color(200, 200, 200));
        var shortcutStyle = new CellStyle(new Color(120, 120, 140), DialogBox.BgColor);
        var shortcutSelectedStyle = new CellStyle(new Color(20, 20, 35), new Color(200, 200, 200));

        int visibleCount = content.Height - 2;

        for (int i = 0; i < visibleCount; i++)
        {
            int itemIndex = _actionPaletteScrollOffset + i;

            if (itemIndex >= filtered.Count)
            {
                break;
            }

            var (label, shortcut, _) = filtered[itemIndex];
            bool selected = itemIndex == _actionPaletteSelectedIndex;
            int row = content.Top + 2 + i;

            CellStyle labelStyle = selected ? selectedStyle : normalStyle;
            CellStyle scStyle = selected ? shortcutSelectedStyle : shortcutStyle;

            // Fill entire row with selected background if selected
            if (selected)
            {
                buffer.FillRow(row, content.Left, content.Width, ' ', selectedStyle);
            }

            buffer.WriteString(row, content.Left + 1, label, labelStyle, content.Width - shortcut.Length - 3);

            int shortcutCol = content.Left + content.Width - shortcut.Length - 1;
            buffer.WriteString(row, shortcutCol, shortcut, scStyle);
        }
    }

    // ── Bookmarks ──────────────────────────────────────────────────────────────

    private void ShowBookmarks()
    {
        _inputMode = InputMode.Bookmarks;
        _bookmarkSelectedIndex = 0;
        _bookmarkScrollOffset = 0;
        _bookmarkInput = new TextInput();
    }

    private List<string> GetFilteredBookmarks()
    {
        string filter = _bookmarkInput?.Value ?? "";

        if (string.IsNullOrEmpty(filter))
        {
            return [.. _bookmarkStore.Bookmarks];
        }

        var result = new List<string>();

        foreach (string bookmark in _bookmarkStore.Bookmarks)
        {
            if (bookmark.Contains(filter, StringComparison.OrdinalIgnoreCase))
            {
                result.Add(bookmark);
            }
        }

        return result;
    }

    private void HandleBookmarkKey(KeyEvent key, PreviewLoader previewLoader, ScreenBuffer buffer)
    {
        var filtered = GetFilteredBookmarks();

        switch (key.Key)
        {
            case ConsoleKey.Escape:
                _inputMode = InputMode.Normal;
                _bookmarkInput = null;
                break;

            case ConsoleKey.Enter:
                if (filtered.Count > 0 && _bookmarkSelectedIndex < filtered.Count)
                {
                    string path = filtered[_bookmarkSelectedIndex];
                    _inputMode = InputMode.Normal;
                    _bookmarkInput = null;
                    NavigateToPath(path, previewLoader, buffer);
                }

                break;

            case ConsoleKey.UpArrow:
                if (_bookmarkSelectedIndex > 0)
                {
                    _bookmarkSelectedIndex--;
                }

                break;

            case ConsoleKey.DownArrow:
                if (_bookmarkSelectedIndex < filtered.Count - 1)
                {
                    _bookmarkSelectedIndex++;
                }

                break;

            case ConsoleKey.PageUp:
            {
                int visibleCount = Math.Min(18, filtered.Count);
                _bookmarkSelectedIndex = Math.Max(0, _bookmarkSelectedIndex - visibleCount);
                break;
            }

            case ConsoleKey.PageDown:
            {
                int visibleCount = Math.Min(18, filtered.Count);
                _bookmarkSelectedIndex = Math.Min(filtered.Count - 1, _bookmarkSelectedIndex + visibleCount);
                break;
            }

            case ConsoleKey.Home:
                _bookmarkSelectedIndex = 0;
                break;

            case ConsoleKey.End:
                _bookmarkSelectedIndex = Math.Max(0, filtered.Count - 1);
                break;

            case ConsoleKey.Backspace:
                _bookmarkInput!.DeleteBackward();
                _bookmarkSelectedIndex = 0;
                _bookmarkScrollOffset = 0;
                break;

            case ConsoleKey.Delete:
                if (filtered.Count > 0 && _bookmarkSelectedIndex < filtered.Count)
                {
                    _bookmarkStore.Remove(filtered[_bookmarkSelectedIndex]);
                }

                break;

            case ConsoleKey.LeftArrow:
                _bookmarkInput!.MoveCursorLeft();
                break;

            case ConsoleKey.RightArrow:
                _bookmarkInput!.MoveCursorRight();
                break;

            default:
                if (key.Key == ConsoleKey.K && key.Control)
                {
                    if (_bookmarkSelectedIndex > 0)
                    {
                        _bookmarkSelectedIndex--;
                    }
                }
                else if (key.Key == ConsoleKey.J && key.Control)
                {
                    if (_bookmarkSelectedIndex < filtered.Count - 1)
                    {
                        _bookmarkSelectedIndex++;
                    }
                }
                else if (key.KeyChar == 'd')
                {
                    // 'd' also removes bookmark
                    if (filtered.Count > 0 && _bookmarkSelectedIndex < filtered.Count)
                    {
                        _bookmarkStore.Remove(filtered[_bookmarkSelectedIndex]);
                    }
                }
                else if (key.KeyChar == 'B')
                {
                    // Toggle current directory as bookmark from within dialog
                    _bookmarkStore.Toggle(_currentPath);
                }
                else if (key.KeyChar is >= '1' and <= '9')
                {
                    int index = key.KeyChar - '1';

                    if (index < filtered.Count)
                    {
                        string path = filtered[index];
                        _inputMode = InputMode.Normal;
                        _bookmarkInput = null;
                        NavigateToPath(path, previewLoader, buffer);
                        return;
                    }
                }
                else if (key.KeyChar >= ' ')
                {
                    _bookmarkInput!.InsertChar(key.KeyChar);
                    _bookmarkSelectedIndex = 0;
                    _bookmarkScrollOffset = 0;
                }

                break;
        }

        // Adjust scroll offset to keep selection visible
        filtered = GetFilteredBookmarks();

        if (filtered.Count > 0)
        {
            _bookmarkSelectedIndex = Math.Clamp(_bookmarkSelectedIndex, 0, filtered.Count - 1);
        }
        else
        {
            _bookmarkSelectedIndex = 0;
        }

        int maxVisible = 18;

        if (_bookmarkSelectedIndex < _bookmarkScrollOffset)
        {
            _bookmarkScrollOffset = _bookmarkSelectedIndex;
        }
        else if (_bookmarkSelectedIndex >= _bookmarkScrollOffset + maxVisible)
        {
            _bookmarkScrollOffset = _bookmarkSelectedIndex - maxVisible + 1;
        }
    }

    private void RenderBookmarks(ScreenBuffer buffer, int width, int height)
    {
        var filtered = GetFilteredBookmarks();
        int contentWidth = Math.Min(70, width - 8);
        int itemRows = Math.Min(filtered.Count, 18);
        int contentHeight = itemRows + 2; // 1 row for text input + 1 separator + item rows
        const string Footer = "[↑↓] Navigate [Enter] Open [d] Remove [1-9] Jump  [B] Add/Remove  [Esc] Close";

        var content = DialogBox.Render(
            buffer, width, height,
            Math.Max(contentWidth, Footer.Length),
            Math.Max(contentHeight, 3),
            title: "Bookmarks",
            footer: Footer);

        // Row 0: text input with "> " prefix
        var prefixStyle = new CellStyle(new Color(220, 220, 100), DialogBox.BgColor);
        var inputStyle = new CellStyle(new Color(200, 200, 200), DialogBox.BgColor);
        buffer.WriteString(content.Top, content.Left, "> ", prefixStyle);
        _bookmarkInput?.Render(buffer, content.Top, content.Left + 2, content.Width - 2, inputStyle);

        // Row 1: separator
        var separatorStyle = new CellStyle(DialogBox.BorderColor, DialogBox.BgColor, Dim: true);
        for (int c = 0; c < content.Width; c++)
        {
            buffer.Put(content.Top + 1, content.Left + c, '─', separatorStyle);
        }

        if (filtered.Count == 0)
        {
            var emptyStyle = new CellStyle(new Color(120, 120, 140), DialogBox.BgColor);
            buffer.WriteString(content.Top + 2, content.Left + 1, "No bookmarks", emptyStyle);
            return;
        }

        // Rows 2+: bookmark items
        var normalStyle = new CellStyle(new Color(200, 200, 200), DialogBox.BgColor);
        var selectedStyle = new CellStyle(new Color(20, 20, 35), new Color(200, 200, 200));
        var numberStyle = new CellStyle(new Color(220, 220, 100), DialogBox.BgColor);
        var numberSelectedStyle = new CellStyle(new Color(20, 20, 35), new Color(200, 200, 200));
        var dimStyle = new CellStyle(new Color(120, 120, 140), DialogBox.BgColor);
        var dimSelectedStyle = new CellStyle(new Color(80, 80, 100), new Color(200, 200, 200));

        int visibleCount = content.Height - 2;

        for (int i = 0; i < visibleCount; i++)
        {
            int itemIndex = _bookmarkScrollOffset + i;

            if (itemIndex >= filtered.Count)
            {
                break;
            }

            string path = filtered[itemIndex];
            bool isSelected = itemIndex == _bookmarkSelectedIndex;
            bool exists = Directory.Exists(path) || File.Exists(path);
            int row = content.Top + 2 + i;

            CellStyle labelStyle = isSelected
                ? (exists ? selectedStyle : dimSelectedStyle)
                : (exists ? normalStyle : dimStyle);

            CellStyle numStyle = isSelected ? numberSelectedStyle : numberStyle;

            if (isSelected)
            {
                buffer.FillRow(row, content.Left, content.Width, ' ', selectedStyle);
            }

            // Number prefix [1]-[9] for first 9 items
            int col = content.Left + 1;

            if (itemIndex < 9)
            {
                string num = $"[{itemIndex + 1}] ";
                buffer.WriteString(row, col, num, numStyle);
                col += num.Length;
            }
            else
            {
                col += 4; // align with numbered items
            }

            buffer.WriteString(row, col, path, labelStyle, content.Width - (col - content.Left) - 1);
        }
    }

    private void ShowConfigDialog()
    {
        _inputMode = InputMode.Config;
        _configSelectedIndex = 0;
        _configShowIcons = _config.ShowIconsEnabled;
        _configImagePreviews = _config.ImagePreviewsEnabled;
        _configShowHidden = _config.ShowHiddenFiles;
        _configShowSystem = _config.ShowSystemFiles;
        _configSortMode = _config.SortMode;
        _configSortAscending = _config.SortAscending;
        _configConfirmDelete = _config.ConfirmDeleteEnabled;
        _configPreviewPane = _config.PreviewPaneEnabled;
        _configSizeColumn = _config.SizeColumnEnabled;
        _configDateColumn = _config.DateColumnEnabled;
        _configGlowMarkdownPreview = _config.GlowMarkdownPreviewEnabled;
        _configZipPreview = _config.ZipPreviewEnabled;
        _configPdfPreview = _config.PdfPreviewEnabled;
        _configCopySymlinksAsLinks = _config.CopySymlinksAsLinksEnabled;
        _configTerminalTitle = _config.TerminalTitleEnabled;
        _configGitStatus = _config.GitStatusEnabled;
    }

    private void HandleConfigKey(KeyEvent key, PreviewLoader previewLoader, ScreenBuffer buffer)
    {
        switch (key.Key)
        {
            case ConsoleKey.UpArrow or ConsoleKey.K:
                if (_configSelectedIndex > 0)
                {
                    _configSelectedIndex--;
                }

                break;

            case ConsoleKey.DownArrow or ConsoleKey.J:
                int maxIndex = OperatingSystem.IsWindows() ? 14 : 13;
                if (_configSelectedIndex < maxIndex)
                {
                    _configSelectedIndex++;
                }

                break;

            case ConsoleKey.Spacebar:
                ToggleConfigOption();
                break;

            case ConsoleKey.Enter:
                ApplyConfigChanges(previewLoader, buffer);
                break;

            case ConsoleKey.Escape:
                _inputMode = InputMode.Normal;
                break;

            case ConsoleKey.LeftArrow or ConsoleKey.H:
            {
                int sortModeIndex = OperatingSystem.IsWindows() ? 3 : 2;
                if (_configSelectedIndex == sortModeIndex)
                {
                    _configSortMode = CycleSortModePrev(_configSortMode);
                }

                break;
            }

            case ConsoleKey.RightArrow or ConsoleKey.L:
            {
                int sortModeIndex = OperatingSystem.IsWindows() ? 3 : 2;
                if (_configSelectedIndex == sortModeIndex)
                {
                    _configSortMode = CycleSortModeNext(_configSortMode);
                }

                break;
            }
        }
    }

    private void ToggleConfigOption()
    {
        // On Windows, "Show System Files" is inserted at index 2, shifting everything after by 1
        int idx = _configSelectedIndex;

        switch (idx)
        {
            case 0: _configShowIcons = !_configShowIcons; return;
            case 1: _configShowHidden = !_configShowHidden; return;
            case 2 when OperatingSystem.IsWindows():
                if (_configShowHidden)
                {
                    _configShowSystem = !_configShowSystem;
                }

                return;
        }

        // Items after the Windows-only row: on Windows the extra row shifts indices up by 1
        int adjusted = idx + (OperatingSystem.IsWindows() ? -1 : 0);
        switch (adjusted)
        {
            case 2: _configSortMode = CycleSortModeNext(_configSortMode); break;
            case 3: _configSortAscending = !_configSortAscending; break;
            case 4: _configConfirmDelete = !_configConfirmDelete; break;
            case 5: _configPreviewPane = !_configPreviewPane; break;
            case 6:
                if (_configPreviewPane)
                {
                    _configImagePreviews = !_configImagePreviews;
                }

                break;
            case 7:
                if (_configPreviewPane && _configImagePreviews)
                {
                    _configPdfPreview = !_configPdfPreview;
                }

                break;
            case 8:
                if (_configPreviewPane)
                {
                    _configGlowMarkdownPreview = !_configGlowMarkdownPreview;
                }

                break;
            case 9:
                if (_configPreviewPane)
                {
                    _configZipPreview = !_configZipPreview;
                }

                break;
            case 10: _configSizeColumn = !_configSizeColumn; break;
            case 11: _configDateColumn = !_configDateColumn; break;
            case 12: _configCopySymlinksAsLinks = !_configCopySymlinksAsLinks; break;
            case 13: _configTerminalTitle = !_configTerminalTitle; break;
            case 14: _configGitStatus = !_configGitStatus; break;
        }
    }

    private void ApplyConfigChanges(PreviewLoader previewLoader, ScreenBuffer buffer)
    {
        _config.ShowIconsEnabled = _configShowIcons;
        _config.ImagePreviewsEnabled = _configImagePreviews;
        _config.ShowHiddenFiles = _configShowHidden;
        _config.ShowSystemFiles = _configShowHidden && _configShowSystem;
        _configShowSystem = _config.ShowSystemFiles;
        _config.SortMode = _configSortMode;
        _config.SortAscending = _configSortAscending;
        _config.ConfirmDeleteEnabled = _configConfirmDelete;
        _config.PreviewPaneEnabled = _configPreviewPane;
        _config.SizeColumnEnabled = _configSizeColumn;
        _config.DateColumnEnabled = _configDateColumn;
        _config.GlowMarkdownPreviewEnabled = _configGlowMarkdownPreview;
        _config.ZipPreviewEnabled = _configZipPreview;
        _config.PdfPreviewEnabled = _configPdfPreview;
        _config.CopySymlinksAsLinksEnabled = _configCopySymlinksAsLinks;
        _config.TerminalTitleEnabled = _configTerminalTitle;
        _config.GitStatusEnabled = _configGitStatus;

        _directoryContents.ShowHiddenFiles = _config.ShowHiddenFiles;
        _directoryContents.ShowSystemFiles = _config.ShowSystemFiles;
        _directoryContents.SortMode = _config.SortMode;
        _directoryContents.SortAscending = _config.SortAscending;
        _imagePreviewsEffective = _config.ImagePreviewsEnabled && _sixelSupported;

        _directoryContents.InvalidateAll();
        ClearPreviewCache(previewLoader, buffer);
        _layout.Calculate(Console.WindowWidth, Console.WindowHeight, _config.PreviewPaneEnabled);
        previewLoader.Configure(_imagePreviewsEffective, _layout.RightPane.Width, _layout.RightPane.Height,
            _cellPixelWidth, _cellPixelHeight, glowEnabled: _config.GlowMarkdownPreviewEnabled,
            zipPreviewEnabled: _config.ZipPreviewEnabled,
            pdfPreviewEnabled: _config.PdfPreviewEnabled);
        UpdateTerminalTitle();
        RefreshGitStatus();

        try
        {
            _config.Save();
            ShowNotification("Configuration saved", NotificationKind.Success);
        }
        catch (Exception ex)
        {
            ShowNotification($"Save failed: {ex.Message}", NotificationKind.Error);
        }

        _inputMode = InputMode.Normal;
    }

    private static SortMode CycleSortModeNext(SortMode current) =>
        current switch
        {
            SortMode.Name => SortMode.Modified,
            SortMode.Modified => SortMode.Size,
            SortMode.Size => SortMode.Extension,
            _ => SortMode.Name,
        };

    private static SortMode CycleSortModePrev(SortMode current) =>
        current switch
        {
            SortMode.Name => SortMode.Extension,
            SortMode.Modified => SortMode.Name,
            SortMode.Size => SortMode.Modified,
            _ => SortMode.Size,
        };

    private void RenderConfigDialog(ScreenBuffer buffer, int width, int height)
    {
        const int ContentWidth = 40;
        int contentHeight = OperatingSystem.IsWindows() ? 17 : 16;
        const string Footer = "[Space] Toggle [◄►] Cycle [Enter] Save [Esc] Cancel";

        var content = DialogBox.Render(buffer, width, height, Math.Max(ContentWidth, Footer.Length), contentHeight, title: "Configuration", footer: Footer);

        var normalStyle = new CellStyle(new Color(200, 200, 200), DialogBox.BgColor);
        var selectedStyle = new CellStyle(new Color(20, 20, 35), new Color(200, 200, 200));
        var valueStyle = new CellStyle(new Color(100, 200, 255), DialogBox.BgColor);
        var valueSelectedStyle = new CellStyle(new Color(20, 20, 35), new Color(200, 200, 200));
        var disabledStyle = new CellStyle(new Color(80, 80, 80), DialogBox.BgColor);

        var itemList = new List<(string label, string value, bool enabled)>
        {
            ("Show Icons", FormatBool(_configShowIcons), true),
            ("Show Hidden Files", FormatBool(_configShowHidden), true),
        };

        if (OperatingSystem.IsWindows())
        {
            itemList.Add(("  Show System Files", FormatBool(_configShowSystem), _configShowHidden));
        }

        itemList.Add(("Sort Mode", $"\u25c4 {_configSortMode.ToString().ToLowerInvariant()} \u25ba", true));
        itemList.Add(("Sort Ascending", FormatBool(_configSortAscending), true));
        itemList.Add(("Confirm Delete", FormatBool(_configConfirmDelete), true));
        itemList.Add(("Show Preview Pane", FormatBool(_configPreviewPane), true));
        itemList.Add(("  Image Previews", FormatBool(_configImagePreviews), _configPreviewPane));
        itemList.Add(("    PDF Preview", FormatBool(_configPdfPreview), _configPreviewPane && _configImagePreviews));
        itemList.Add(("  Glow (Markdown)", FormatBool(_configGlowMarkdownPreview), _configPreviewPane && GlowRenderer.IsAvailable));
        itemList.Add(("  Zip Preview", FormatBool(_configZipPreview), _configPreviewPane));
        itemList.Add(("Show Size Column", FormatBool(_configSizeColumn), true));
        itemList.Add(("Show Date Column", FormatBool(_configDateColumn), true));
        itemList.Add(("Copy Symlinks as Link", FormatBool(_configCopySymlinksAsLinks), true));
        itemList.Add(("Change Terminal Title", FormatBool(_configTerminalTitle), true));
        itemList.Add(("Show Git Status", FormatBool(_configGitStatus), true));

        for (int i = 0; i < itemList.Count; i++)
        {
            bool selected = i == _configSelectedIndex;
            var (label, value, enabled) = itemList[i];
            CellStyle style, vStyle;

            if (!enabled)
            {
                style = disabledStyle;
                vStyle = disabledStyle;
            }
            else
            {
                style = selected ? selectedStyle : normalStyle;
                vStyle = selected ? valueSelectedStyle : valueStyle;
            }

            int row = content.Top + i;

            buffer.WriteString(row, content.Left, label, style, 22);
            buffer.WriteString(row, content.Left + 22, value, vStyle, content.Width - 22);
        }
    }

    private static string FormatBool(bool value) => value ? "[X]" : "[ ]";

    private void RenderSearchBar(ScreenBuffer buffer)
    {
        int row = _layout.CenterPane.Bottom - 1;
        int col = _layout.CenterPane.Left;
        int width = _layout.CenterPane.Width;

        var labelStyle = new CellStyle(new Color(220, 220, 100), null);
        buffer.Put(row, col, '/', labelStyle);

        int inputCol = col + 1;
        int inputWidth = width - 1;

        if (_inputMode == InputMode.Search && _searchInput is not null)
        {
            var inputStyle = new CellStyle(new Color(200, 200, 200), null);
            _searchInput.Render(buffer, row, inputCol, inputWidth, inputStyle);
        }
        else
        {
            var textStyle = new CellStyle(new Color(200, 200, 200), null);
            buffer.WriteString(row, inputCol, _searchFilter, textStyle, inputWidth);
        }
    }

    // ── File finder ───────────────────────────────────────────────────────────

    private void ShowFileFinder(InputPipeline pipeline)
    {
        _inputMode = InputMode.FileFinder;
        _fileFinderSelectedIndex = 0;
        _fileFinderScrollOffset = 0;
        _fileFinderInput = new TextInput();
        _fileFinderAllEntries = null;
        _fileFinderScanning = true;
        _fileFinderCts?.Cancel();
        _fileFinderCts = new CancellationTokenSource();

        string basePath = _currentPath;
        bool showHidden = _directoryContents.ShowHiddenFiles;
        bool showSystem = _directoryContents.ShowSystemFiles;
        var ct = _fileFinderCts.Token;

        Task.Run(() => ScanFilesForFinder(basePath, showHidden, showSystem, pipeline, ct), ct);
    }

    private void CloseFileFinder()
    {
        _fileFinderCts?.Cancel();
        _fileFinderCts = null;
        _inputMode = InputMode.Normal;
        _fileFinderInput = null;
        _fileFinderAllEntries = null;
        _fileFinderScanning = false;
    }

    internal static void ScanFilesForFinder(
        string basePath,
        bool showHidden,
        bool showSystem,
        InputPipeline pipeline,
        CancellationToken ct)
    {
        const int maxEntries = 10_000;
        var entries = new List<FileSystemEntry>();

        try
        {
            var options = new EnumerationOptions
            {
                RecurseSubdirectories = true,
                MaxRecursionDepth = 8,
                IgnoreInaccessible = true,
                AttributesToSkip = FileAttributes.Device,
            };

            foreach (string filePath in Directory.EnumerateFiles(basePath, "*", options))
            {
                if (ct.IsCancellationRequested)
                {
                    return;
                }

                try
                {
                    var fileInfo = new FileInfo(filePath);

                    if (!showSystem && OperatingSystem.IsWindows() &&
                        (fileInfo.Attributes & FileAttributes.System) != 0)
                    {
                        continue;
                    }

                    if (!showHidden &&
                        ((fileInfo.Attributes & FileAttributes.Hidden) != 0 || fileInfo.Name.StartsWith('.')))
                    {
                        continue;
                    }

                    entries.Add(new FileSystemEntry(
                        fileInfo.Name,
                        fileInfo.FullName,
                        IsDirectory: false,
                        Size: fileInfo.Length,
                        LastModified: fileInfo.LastWriteTime,
                        LinkTarget: fileInfo.LinkTarget,
                        IsBrokenSymlink: false,
                        IsDrive: false));

                    if (entries.Count >= maxEntries)
                    {
                        break;
                    }
                }
                catch
                {
                    // Skip files we can't access
                }
            }
        }
        catch
        {
            // Skip if enumeration fails entirely
        }

        if (!ct.IsCancellationRequested)
        {
            pipeline.Inject(new FileFinderScanCompleteEvent(basePath, entries));
        }
    }

    internal static List<FileSystemEntry> GetFilteredFileFinderEntries(
        List<FileSystemEntry>? allEntries, string filter, string basePath)
    {
        if (allEntries is null)
        {
            return [];
        }

        if (string.IsNullOrEmpty(filter))
        {
            return allEntries;
        }

        var result = new List<FileSystemEntry>();

        foreach (FileSystemEntry entry in allEntries)
        {
            string relativePath = Path.GetRelativePath(basePath, entry.FullPath);

            if (relativePath.Contains(filter, StringComparison.OrdinalIgnoreCase))
            {
                result.Add(entry);
            }
        }

        return result;
    }

    private void HandleFileFinderKey(KeyEvent key, PreviewLoader previewLoader, ScreenBuffer buffer)
    {
        var filtered = GetFilteredFileFinderEntries(
            _fileFinderAllEntries, _fileFinderInput?.Value ?? "", _currentPath);

        switch (key.Key)
        {
            case ConsoleKey.Escape:
                CloseFileFinder();
                break;

            case ConsoleKey.Enter:
                if (filtered.Count > 0 && _fileFinderSelectedIndex < filtered.Count)
                {
                    string filePath = filtered[_fileFinderSelectedIndex].FullPath;
                    CloseFileFinder();
                    NavigateToPath(filePath, previewLoader, buffer);
                }

                break;

            case ConsoleKey.UpArrow:
                if (_fileFinderSelectedIndex > 0)
                {
                    _fileFinderSelectedIndex--;
                }

                break;

            case ConsoleKey.DownArrow:
                if (_fileFinderSelectedIndex < filtered.Count - 1)
                {
                    _fileFinderSelectedIndex++;
                }

                break;

            case ConsoleKey.PageUp:
            {
                int visibleCount = Math.Min(18, filtered.Count);
                _fileFinderSelectedIndex = Math.Max(0, _fileFinderSelectedIndex - visibleCount);
                break;
            }

            case ConsoleKey.PageDown:
            {
                int visibleCount = Math.Min(18, filtered.Count);
                _fileFinderSelectedIndex = Math.Min(filtered.Count - 1, _fileFinderSelectedIndex + visibleCount);
                break;
            }

            case ConsoleKey.Home:
                _fileFinderSelectedIndex = 0;
                break;

            case ConsoleKey.End:
                _fileFinderSelectedIndex = Math.Max(0, filtered.Count - 1);
                break;

            case ConsoleKey.Backspace:
                _fileFinderInput!.DeleteBackward();
                _fileFinderSelectedIndex = 0;
                _fileFinderScrollOffset = 0;
                break;

            case ConsoleKey.Delete:
                _fileFinderInput!.DeleteForward();
                _fileFinderSelectedIndex = 0;
                _fileFinderScrollOffset = 0;
                break;

            case ConsoleKey.LeftArrow:
                _fileFinderInput!.MoveCursorLeft();
                break;

            case ConsoleKey.RightArrow:
                _fileFinderInput!.MoveCursorRight();
                break;

            default:
                if (key.Key == ConsoleKey.K && key.Control)
                {
                    if (_fileFinderSelectedIndex > 0)
                    {
                        _fileFinderSelectedIndex--;
                    }
                }
                else if (key.Key == ConsoleKey.J && key.Control)
                {
                    if (_fileFinderSelectedIndex < filtered.Count - 1)
                    {
                        _fileFinderSelectedIndex++;
                    }
                }
                else if (key.KeyChar >= ' ')
                {
                    _fileFinderInput!.InsertChar(key.KeyChar);
                    _fileFinderSelectedIndex = 0;
                    _fileFinderScrollOffset = 0;
                }

                break;
        }

        // Adjust scroll offset to keep selection visible
        filtered = GetFilteredFileFinderEntries(
            _fileFinderAllEntries, _fileFinderInput?.Value ?? "", _currentPath);

        if (filtered.Count > 0)
        {
            _fileFinderSelectedIndex = Math.Clamp(_fileFinderSelectedIndex, 0, filtered.Count - 1);
        }
        else
        {
            _fileFinderSelectedIndex = 0;
        }

        int maxVisible = 18;

        if (_fileFinderSelectedIndex < _fileFinderScrollOffset)
        {
            _fileFinderScrollOffset = _fileFinderSelectedIndex;
        }
        else if (_fileFinderSelectedIndex >= _fileFinderScrollOffset + maxVisible)
        {
            _fileFinderScrollOffset = _fileFinderSelectedIndex - maxVisible + 1;
        }
    }

    private void RenderFileFinder(ScreenBuffer buffer, int width, int height)
    {
        var filtered = GetFilteredFileFinderEntries(
            _fileFinderAllEntries, _fileFinderInput?.Value ?? "", _currentPath);
        int contentWidth = Math.Min(70, width - 8);
        int itemRows = Math.Min(filtered.Count, 18);
        int contentHeight = itemRows + 2; // 1 row for text input + 1 separator + item rows
        const string Footer = "[↑↓] Navigate  [Enter] Open  [Esc] Cancel";
        string title = _fileFinderScanning ? "Find File [scanning...]" : "Find File";

        var content = DialogBox.Render(
            buffer, width, height,
            Math.Max(contentWidth, Footer.Length),
            Math.Max(contentHeight, 3),
            title: title,
            footer: Footer);

        // Row 0: text input with "> " prefix
        var prefixStyle = new CellStyle(new Color(220, 220, 100), DialogBox.BgColor);
        var inputStyle = new CellStyle(new Color(200, 200, 200), DialogBox.BgColor);
        buffer.WriteString(content.Top, content.Left, "> ", prefixStyle);
        _fileFinderInput?.Render(buffer, content.Top, content.Left + 2, content.Width - 2, inputStyle);

        // Row 1: separator
        var separatorStyle = new CellStyle(DialogBox.BorderColor, DialogBox.BgColor, Dim: true);
        for (int c = 0; c < content.Width; c++)
        {
            buffer.Put(content.Top + 1, content.Left + c, '─', separatorStyle);
        }

        if (filtered.Count == 0)
        {
            var emptyStyle = new CellStyle(new Color(120, 120, 140), DialogBox.BgColor);
            string emptyText = _fileFinderScanning ? "Scanning..." : "No matching files";
            buffer.WriteString(content.Top + 2, content.Left + 1, emptyText, emptyStyle);
            return;
        }

        // Rows 2+: filtered items
        var normalStyle = new CellStyle(new Color(200, 200, 200), DialogBox.BgColor);
        var selectedStyle = new CellStyle(new Color(20, 20, 35), new Color(200, 200, 200));
        var dimStyle = new CellStyle(new Color(120, 120, 140), DialogBox.BgColor);
        var dimSelectedStyle = new CellStyle(new Color(80, 80, 100), new Color(200, 200, 200));

        int visibleCount = content.Height - 2;

        for (int i = 0; i < visibleCount; i++)
        {
            int itemIndex = _fileFinderScrollOffset + i;

            if (itemIndex >= filtered.Count)
            {
                break;
            }

            FileSystemEntry entry = filtered[itemIndex];
            bool selected = itemIndex == _fileFinderSelectedIndex;
            int row = content.Top + 2 + i;

            CellStyle nameStyle = selected ? selectedStyle : normalStyle;
            CellStyle pathStyle = selected ? dimSelectedStyle : dimStyle;

            if (selected)
            {
                buffer.FillRow(row, content.Left, content.Width, ' ', selectedStyle);
            }

            // Left: filename
            buffer.WriteString(row, content.Left + 1, entry.Name, nameStyle, content.Width / 2 - 1);

            // Right: relative parent path (dim hint)
            string? parentDir = Path.GetDirectoryName(Path.GetRelativePath(_currentPath, entry.FullPath));
            string parentHint = string.IsNullOrEmpty(parentDir) ? "." : parentDir + Path.DirectorySeparatorChar;
            int hintMaxLen = content.Width / 2 - 1;
            int hintCol = content.Left + content.Width - Math.Min(parentHint.Length, hintMaxLen) - 1;
            buffer.WriteString(row, hintCol, parentHint, pathStyle, hintMaxLen);
        }
    }
}
