using System.Diagnostics;
using System.Text;
using Wade.FileSystem;
using Wade.Highlighting;
using Wade.Preview;
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
    private readonly Stack<ActionMenuLevel> _actionMenuStack = new();

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
    private string? _aheadBehindText;
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
    private int _activeProviderIndex;
    private List<IPreviewProvider>? _applicableProviders;
    private PreviewContext? _activePreviewContext;
    private bool _isCombinedPreview;
    private int _sixelImageTop;
    private bool _sixelPending;

    // Metadata provider state
    private MetadataSection[]? _cachedMetadataSections;
    private string? _cachedMetadataFileTypeLabel;
    private IMetadataProvider? _activeMetadataProvider;

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
                int cursorRow = _isCombinedPreview ? _sixelImageTop : sixelPane.Top;
                int cursorCol = sixelPane.Left;

                if (!_isCombinedPreview && _inputMode == InputMode.ExpandedPreview && _cachedImagePixelWidth > 0 && _cachedImagePixelHeight > 0)
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
                else if (extra is CombinedPreviewReadyEvent combinedPreviewReady)
                {
                    bool wasImage = _isImagePreview;
                    HandleCombinedPreviewReady(combinedPreviewReady);
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
                else if (extra is MetadataReadyEvent metadataExtra)
                {
                    HandleMetadataReady(metadataExtra);
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
                    Console.Write(AnsiCodes.ClearScreen);

                    // Re-render preview at new size
                    if (_cachedPreviewPath is not null || _cachedImagePath is not null)
                    {
                        string path = (_isImagePreview ? _cachedImagePath : _cachedPreviewPath)!;
                        _cachedSixelData = null;
                        _sixelPending = false;
                        _cachedStyledLines = null;
                        _activePreviewContext = BuildPreviewContext(resizePane.Width, resizePane.Height);
                        ReloadActiveProvider(path, previewLoader);
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

            if (inputEvent is CombinedPreviewReadyEvent combinedEvt)
            {
                bool wasImage = _isImagePreview;
                HandleCombinedPreviewReady(combinedEvt);
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

            if (inputEvent is MetadataReadyEvent metadataEvt)
            {
                HandleMetadataReady(metadataEvt);
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

                case AppAction.ShowPreviewMenu:
                    ShowPreviewMenu();
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
                    _activeProviderIndex = 0;
                    _activePreviewContext = BuildPreviewContext(_layout.RightPane.Width, _layout.RightPane.Height);
                    _activeMetadataProvider = MetadataProviderRegistry.GetProvider(selected.FullPath, _activePreviewContext);
                    _applicableProviders = PreviewProviderRegistry.GetApplicableProviders(selected.FullPath, _activePreviewContext);

                    if (_applicableProviders.Count == 0 && _activeMetadataProvider is null)
                    {
                        ClearPreviewCache(_previewLoader!);
                        _applicableProviders = [];
                        _cachedPreviewPath = selected.FullPath;
                    }
                    else
                    {
                        ReloadActiveProvider(selected.FullPath, _previewLoader!);
                    }
                }

                if (_applicableProviders is { Count: 0 } && _activeMetadataProvider is null)
                {
                    string message = _activePreviewContext is { IsBrokenSymlink: true } ? "[broken symlink]"
                        : _activePreviewContext is { IsCloudPlaceholder: true } ? "[cloud file \u2013 not downloaded]"
                        : "[no preview available]";
                    PaneRenderer.RenderMessage(buffer, _layout.RightPane, message);
                }
                else if (_previewLoading && _cachedMetadataSections is null)
                {
                    PaneRenderer.RenderMessage(buffer, _layout.RightPane, "[loading\u2026]");
                }
                else if (_cachedMetadataSections is not null && !_previewLoading && _cachedStyledLines is null && !_isImagePreview && !_isCombinedPreview)
                {
                    // Metadata only (no preview provider)
                    StyledLine[] metadataLines = MetadataRenderer.Render(_cachedMetadataSections, _layout.RightPane.Width);
                    PaneRenderer.RenderPreview(buffer, _layout.RightPane, metadataLines, showLineNumbers: false);
                }
                else if (_cachedMetadataSections is not null && _isImagePreview && _cachedSixelData is not null)
                {
                    // Metadata + image: render metadata at top, image below
                    RenderMetadataWithImage(buffer, _layout.RightPane);
                }
                else if (_cachedMetadataSections is not null && _cachedStyledLines is not null)
                {
                    // Metadata + text preview: render metadata at top, text below
                    RenderMetadataWithText(buffer, _layout.RightPane);
                }
                else if (_isCombinedPreview && _cachedStyledLines is not null && _cachedSixelData is not null)
                {
                    RenderCombinedPreview(buffer, _layout.RightPane);
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
        StatusBar.Render(buffer, _layout.StatusBar, displayPath, entries.Count, _selectedIndex, selectedEntry, _cachedPreviewFileTypeLabel, _cachedPreviewEncoding, _cachedPreviewLineEnding, _notification, _markedPaths.Count, _directoryContents.SortMode, _directoryContents.SortAscending, _clipboardPaths.Count, _clipboardIsCut, _currentBranchName, _aheadBehindText);

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
                    GitFileStatus? propGitStatus = _gitStatuses?.TryGetValue(selectedEntry.FullPath, out var gs) == true ? gs : null;
                    PropertiesOverlay.Render(buffer, width, height, selectedEntry, _propertiesDirSizeText, propGitStatus, _cachedMetadataSections);
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
        _isCombinedPreview = false;
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
        _isCombinedPreview = false;
        _previewLoading = false;
    }

    private void HandleCombinedPreviewReady(CombinedPreviewReadyEvent evt)
    {
        if (evt.Path != _pendingPreviewPath)
        {
            return;
        }

        _cachedPreviewPath = evt.Path;
        _cachedImagePath = evt.Path;
        _cachedStyledLines = evt.StyledLines;
        _cachedSixelData = evt.SixelData;
        _cachedImagePixelWidth = evt.PixelWidth;
        _cachedImagePixelHeight = evt.PixelHeight;
        _cachedPreviewFileTypeLabel = evt.FileTypeLabel;
        _cachedPreviewEncoding = evt.Encoding;
        _cachedPreviewLineEnding = evt.LineEnding;
        _isImagePreview = false;
        _isCombinedPreview = true;
        _isRenderedPreview = evt.IsRendered;
        _previewLoading = false;
    }

    private void HandleMetadataReady(MetadataReadyEvent evt)
    {
        if (evt.Path != _pendingPreviewPath && evt.Path != _cachedPreviewPath)
        {
            return;
        }

        _cachedMetadataSections = evt.Sections;
        _cachedMetadataFileTypeLabel = evt.FileTypeLabel;

        // Use metadata file type label if preview hasn't provided one
        if (_cachedPreviewFileTypeLabel is null && evt.FileTypeLabel is not null)
        {
            _cachedPreviewFileTypeLabel = evt.FileTypeLabel;
        }
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
        _aheadBehindText = FormatAheadBehind(evt.AheadCount, evt.BehindCount);
    }

    private static string? FormatAheadBehind(int ahead, int behind)
    {
        if (ahead == 0 && behind == 0)
        {
            return null;
        }

        if (ahead > 0 && behind > 0)
        {
            return $" \u2191{ahead} \u2193{behind}";
        }

        if (ahead > 0)
        {
            return $" \u2191{ahead}";
        }

        return $" \u2193{behind}";
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

    private void HandleSelectPreviewProvider(int index, PreviewLoader previewLoader)
    {
        if (_applicableProviders is null || index < 0 || index >= _applicableProviders.Count)
        {
            return;
        }

        var entries = GetVisibleEntries();
        if (_selectedIndex >= entries.Count)
        {
            return;
        }

        var selected = entries[_selectedIndex];
        if (selected.IsDirectory)
        {
            return;
        }

        _activeProviderIndex = index;
        ReloadActiveProvider(selected.FullPath, previewLoader);
    }

    private PreviewContext BuildPreviewContext(int paneWidth, int paneHeight)
    {
        GitFileStatus? gitStatus = null;
        var entries = GetVisibleEntries();
        if (_selectedIndex < entries.Count && _gitStatuses is not null)
        {
            _gitStatuses.TryGetValue(entries[_selectedIndex].FullPath, out var status);
            gitStatus = status;
        }

        bool isCloudPlaceholder = false;
        bool isBrokenSymlink = false;
        if (_selectedIndex < entries.Count)
        {
            isCloudPlaceholder = entries[_selectedIndex].IsCloudPlaceholder;
            isBrokenSymlink = entries[_selectedIndex].IsBrokenSymlink;
        }

        return new PreviewContext(
            PaneWidthCells: paneWidth,
            PaneHeightCells: paneHeight,
            CellPixelWidth: _cellPixelWidth,
            CellPixelHeight: _cellPixelHeight,
            IsCloudPlaceholder: isCloudPlaceholder,
            IsBrokenSymlink: isBrokenSymlink,
            GitStatus: gitStatus,
            RepoRoot: _currentRepoRoot,
            GlowEnabled: _config.GlowMarkdownPreviewEnabled,
            ZipPreviewEnabled: _config.ZipPreviewEnabled,
            PdfPreviewEnabled: _config.PdfPreviewEnabled,
            ImagePreviewsEnabled: _imagePreviewsEffective);
    }

    private void ReloadActiveProvider(string path, PreviewLoader loader)
    {
        if (_activePreviewContext is null)
        {
            return;
        }

        IPreviewProvider? previewProvider = null;
        if (_applicableProviders is { Count: > 0 })
        {
            int index = Math.Min(_activeProviderIndex, _applicableProviders.Count - 1);
            previewProvider = _applicableProviders[index];
        }

        if (previewProvider is null && _activeMetadataProvider is null)
        {
            return;
        }

        _pendingPreviewPath = path;
        _previewLoading = true;
        _cachedMetadataSections = null;
        _cachedMetadataFileTypeLabel = null;
        loader.BeginLoad(path, _activeMetadataProvider, previewProvider, _activePreviewContext);
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
        _isCombinedPreview = false;
        _isRenderedPreview = false;
        _activeProviderIndex = 0;
        _applicableProviders = null;
        _activePreviewContext = null;
        _sixelPending = false;
        _cachedMetadataSections = null;
        _cachedMetadataFileTypeLabel = null;
        _activeMetadataProvider = null;

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

        if (_cachedPreviewPath is not null || _cachedImagePath is not null)
        {
            string path = (_isImagePreview ? _cachedImagePath : _cachedPreviewPath)!;
            _cachedSixelData = null;
            _sixelPending = false;
            _cachedStyledLines = null;
            _activePreviewContext = BuildPreviewContext(_layout.ExpandedPane.Width, _layout.ExpandedPane.Height);
            ReloadActiveProvider(path, previewLoader);
        }

        buffer.ForceFullRedraw();
    }

    private void LeaveExpandedPreview(PreviewLoader previewLoader, ScreenBuffer buffer)
    {
        _inputMode = InputMode.Normal;
        _expandedPreviewScrollOffset = 0;

        if (_cachedPreviewPath is not null || _cachedImagePath is not null)
        {
            string path = (_isImagePreview ? _cachedImagePath : _cachedPreviewPath)!;
            _cachedSixelData = null;
            _sixelPending = false;
            _cachedStyledLines = null;
            _activePreviewContext = BuildPreviewContext(_layout.RightPane.Width, _layout.RightPane.Height);
            ReloadActiveProvider(path, previewLoader);
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

    private void RenderCombinedPreview(ScreenBuffer buffer, Rect pane)
    {
        // Split pane: text at top, image below
        int textRows = Math.Min(_cachedStyledLines!.Length, pane.Height / 2);
        textRows = Math.Max(textRows, 1);
        int imageRows = pane.Height - textRows;

        var textRect = new Rect(pane.Left, pane.Top, pane.Width, textRows);
        PaneRenderer.RenderPreview(buffer, textRect, _cachedStyledLines, showLineNumbers: !_isRenderedPreview);

        // Fill image area with spaces for Sixel rendering
        int imageTop = pane.Top + textRows;
        for (int row = imageTop; row < pane.Bottom; row++)
        {
            buffer.FillRow(row, pane.Left, pane.Width, ' ', CellStyle.Default);
        }

        _sixelImageTop = imageTop;
        _sixelPending = true;
    }

    private void RenderMetadataWithText(ScreenBuffer buffer, Rect pane)
    {
        StyledLine[] metadataLines = MetadataRenderer.Render(_cachedMetadataSections!, pane.Width);

        // Metadata at top, preview text below
        int metadataRows = Math.Min(metadataLines.Length + 1, pane.Height / 2); // +1 for blank separator
        int previewRows = pane.Height - metadataRows;

        var metadataRect = new Rect(pane.Left, pane.Top, pane.Width, metadataRows);
        PaneRenderer.RenderPreview(buffer, metadataRect, metadataLines, showLineNumbers: false);

        if (previewRows > 0)
        {
            var previewRect = new Rect(pane.Left, pane.Top + metadataRows, pane.Width, previewRows);
            PaneRenderer.RenderPreview(buffer, previewRect, _cachedStyledLines!, showLineNumbers: !_isRenderedPreview);
        }
    }

    private void RenderMetadataWithImage(ScreenBuffer buffer, Rect pane)
    {
        StyledLine[] metadataLines = MetadataRenderer.Render(_cachedMetadataSections!, pane.Width);

        int metadataRows = Math.Min(metadataLines.Length + 1, pane.Height / 2);
        int imageRows = pane.Height - metadataRows;

        var metadataRect = new Rect(pane.Left, pane.Top, pane.Width, metadataRows);
        PaneRenderer.RenderPreview(buffer, metadataRect, metadataLines, showLineNumbers: false);

        // Fill image area with spaces for Sixel rendering
        int imageTop = pane.Top + metadataRows;
        for (int row = imageTop; row < pane.Bottom; row++)
        {
            buffer.FillRow(row, pane.Left, pane.Width, ' ', CellStyle.Default);
        }

        _sixelImageTop = imageTop;
        _sixelPending = true;
    }

    private void RenderExpandedPreview(ScreenBuffer buffer, int width, int height)
    {
        var pane = _layout.ExpandedPane;

        if (_previewLoading)
        {
            PaneRenderer.RenderMessage(buffer, pane, "[loading\u2026]");
        }
        else if (_isCombinedPreview && _cachedStyledLines is not null && _cachedSixelData is not null)
        {
            RenderCombinedPreview(buffer, pane);
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

        // Status bar (expanded preview)
        var entries = GetVisibleEntries();
        FileSystemEntry? selectedEntry = entries.Count > 0 && _selectedIndex < entries.Count
            ? entries[_selectedIndex]
            : null;
        string displayPath = _cachedPreviewPath ?? _cachedImagePath ?? _currentPath;
        if (displayPath == DirectoryContents.DrivesPath)
        {
            displayPath = "Drives";
        }

        StatusBar.Render(buffer, _layout.StatusBar, displayPath, entries.Count, _selectedIndex, selectedEntry, _cachedPreviewFileTypeLabel, _cachedPreviewEncoding, _cachedPreviewLineEnding, _notification, _markedPaths.Count, _directoryContents.SortMode, _directoryContents.SortAscending, _clipboardPaths.Count, _clipboardIsCut, _currentBranchName, _aheadBehindText);
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
        _actionMenuStack.Clear();
        _actionMenuStack.Push(new ActionMenuLevel("Action Palette", BuildActionPaletteItems()));
    }

    private void ShowPreviewMenu()
    {
        ActionMenuItem[]? previewItems = BuildPreviewMenuItems();
        if (previewItems is null)
        {
            return;
        }

        _inputMode = InputMode.ActionPalette;
        _actionMenuStack.Clear();
        _actionMenuStack.Push(new ActionMenuLevel("Change preview", previewItems));
    }

    private ActionMenuItem[]? BuildPreviewMenuItems()
    {
        if (_applicableProviders is null || _applicableProviders.Count <= 1)
        {
            return null;
        }

        var items = new ActionMenuItem[_applicableProviders.Count];
        for (int i = 0; i < _applicableProviders.Count; i++)
        {
            string prefix = i == _activeProviderIndex ? "\u25cf " : "  ";
            items[i] = new() { Label = prefix + _applicableProviders[i].Label, Action = AppAction.SelectPreviewProvider, Data = i };
        }

        return items;
    }

    private ActionMenuItem[] BuildActionPaletteItems()
    {
        var items = new List<ActionMenuItem>
        {
            new() { Label = "Open with default app", Shortcut = "o", Action = AppAction.OpenExternal },
            new() { Label = "Rename", Shortcut = "F2", Action = AppAction.Rename },
            new() { Label = "Delete", Shortcut = "Del", Action = AppAction.Delete },
            new() { Label = "Copy", Shortcut = "c", Action = AppAction.Copy },
            new() { Label = "Cut", Shortcut = "x", Action = AppAction.Cut },
        };

        if (_clipboardPaths.Count > 0)
        {
            items.Add(new() { Label = "Paste", Shortcut = "v", Action = AppAction.Paste });
        }

        items.Add(new() { Label = "Copy absolute path", Shortcut = "y", Action = AppAction.CopyAbsolutePath });
        items.Add(new() { Label = "New file", Shortcut = "n", Action = AppAction.NewFile });
        items.Add(new() { Label = "New directory", Shortcut = "Shift+N", Action = AppAction.NewDirectory });
        items.Add(new() { Label = "Create symlink", Shortcut = "Ctrl+L", Action = AppAction.CreateSymlink });
        items.Add(new() { Label = "Properties", Shortcut = "i", Action = AppAction.ShowProperties });

        // Preview provider submenu — shown when multiple providers are available
        ActionMenuItem[]? previewSubItems = BuildPreviewMenuItems();
        if (previewSubItems is not null)
        {
            items.Add(new() { Label = "Change preview", Shortcut = "p", SubItems = previewSubItems });
        }

        items.Add(new() { Label = "Toggle hidden files", Shortcut = ".", Action = AppAction.ToggleHiddenFiles });
        items.Add(new() { Label = "Cycle sort mode", Shortcut = "s", Action = AppAction.CycleSortMode });
        items.Add(new() { Label = "Reverse sort direction", Shortcut = "S", Action = AppAction.ToggleSortDirection });
        items.Add(new() { Label = "Bookmarks", Shortcut = "b", Action = AppAction.ShowBookmarks });
        items.Add(new() { Label = "Toggle bookmark", Shortcut = "B", Action = AppAction.ToggleBookmark });
        items.Add(new() { Label = "Go to path", Shortcut = "g", Action = AppAction.GoToPath });
        items.Add(new() { Label = "Search / Find file", Shortcut = "Ctrl+F", Action = AppAction.ShowFileFinder });
        items.Add(new() { Label = "Filter", Shortcut = "/", Action = AppAction.Search });
        items.Add(new() { Label = "Open terminal here", Shortcut = "Ctrl+T", Action = AppAction.OpenTerminal });
        items.Add(new() { Label = "Configuration", Shortcut = ",", Action = AppAction.ShowConfig });
        items.Add(new() { Label = "Help", Shortcut = "?", Action = AppAction.ShowHelp });
        items.Add(new() { Label = "Refresh", Shortcut = "Ctrl+R", Action = AppAction.Refresh });

        // Git actions — only shown when actionable
        if (_currentRepoRoot is not null)
        {
            items.Add(new() { Label = "Git: Copy relative path", Shortcut = "Y", Action = AppAction.CopyGitRelativePath });

            if (_gitStatuses is not null)
            {
                var entries = GetVisibleEntries();
                if (_selectedIndex < entries.Count)
                {
                    bool hasStageableStatus = HasStatusInSelection(GitFileStatus.Modified | GitFileStatus.Untracked);
                    if (hasStageableStatus)
                    {
                        items.Add(new() { Label = "Git: Stage", Action = AppAction.StageFile });
                    }

                    bool hasUnstageableStatus = HasStatusInSelection(GitFileStatus.Staged);
                    if (hasUnstageableStatus)
                    {
                        items.Add(new() { Label = "Git: Unstage", Action = AppAction.UnstageFile });
                    }
                }

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
                    items.Add(new() { Label = "Git: Stage all changes", Action = AppAction.StageAll });
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
                    items.Add(new() { Label = "Git: Unstage all", Action = AppAction.UnstageAll });
                    items.Add(new() { Label = "Git: Commit", Action = AppAction.GitCommit });
                }
            }

            items.Add(new() { Label = "Git: Push", Action = AppAction.GitPush });
            items.Add(new() { Label = "Git: Push (force with lease)", Action = AppAction.GitPushForceWithLease });
            items.Add(new() { Label = "Git: Pull", Action = AppAction.GitPull });
            items.Add(new() { Label = "Git: Pull (rebase)", Action = AppAction.GitPullRebase });
            items.Add(new() { Label = "Git: Fetch", Action = AppAction.GitFetch });
        }

        return items.ToArray();
    }

    private List<ActionMenuItem> GetFilteredActionPaletteItems()
    {
        if (_actionMenuStack.Count == 0)
        {
            return [];
        }

        return _actionMenuStack.Peek().GetFilteredItems();
    }

    private void HandleActionPaletteKey(KeyEvent key, PreviewLoader previewLoader, ScreenBuffer buffer, InputPipeline pipeline)
    {
        if (_actionMenuStack.Count == 0)
        {
            return;
        }

        var level = _actionMenuStack.Peek();
        var filtered = level.GetFilteredItems();

        switch (key.Key)
        {
            case ConsoleKey.Escape:
                _actionMenuStack.Pop();
                if (_actionMenuStack.Count == 0)
                {
                    _inputMode = InputMode.Normal;
                }

                break;

            case ConsoleKey.Enter:
                if (filtered.Count > 0 && level.SelectedIndex < filtered.Count)
                {
                    var selected = filtered[level.SelectedIndex];
                    if (selected.IsSubmenu)
                    {
                        _actionMenuStack.Push(new ActionMenuLevel(selected.Label, selected.SubItems!));
                    }
                    else
                    {
                        _actionMenuStack.Clear();
                        _inputMode = InputMode.Normal;
                        DispatchActionPaletteAction(selected.Action, selected.Data, previewLoader, buffer, pipeline);
                    }
                }

                break;

            case ConsoleKey.UpArrow:
                if (level.SelectedIndex > 0)
                {
                    level.SelectedIndex--;
                }

                break;

            case ConsoleKey.DownArrow:
                if (level.SelectedIndex < filtered.Count - 1)
                {
                    level.SelectedIndex++;
                }

                break;

            case ConsoleKey.PageUp:
            {
                int visibleCount = Math.Min(18, filtered.Count);
                level.SelectedIndex = Math.Max(0, level.SelectedIndex - visibleCount);
                break;
            }

            case ConsoleKey.PageDown:
            {
                int visibleCount = Math.Min(18, filtered.Count);
                level.SelectedIndex = Math.Min(filtered.Count - 1, level.SelectedIndex + visibleCount);
                break;
            }

            case ConsoleKey.Home:
                level.SelectedIndex = 0;
                break;

            case ConsoleKey.End:
                level.SelectedIndex = Math.Max(0, filtered.Count - 1);
                break;

            case ConsoleKey.Backspace:
                level.Filter.DeleteBackward();
                level.SelectedIndex = 0;
                level.ScrollOffset = 0;
                break;

            case ConsoleKey.Delete:
                level.Filter.DeleteForward();
                level.SelectedIndex = 0;
                level.ScrollOffset = 0;
                break;

            case ConsoleKey.LeftArrow:
                level.Filter.MoveCursorLeft();
                break;

            case ConsoleKey.RightArrow:
                level.Filter.MoveCursorRight();
                break;

            default:
                if (key.Key == ConsoleKey.K && key.Control)
                {
                    if (level.SelectedIndex > 0)
                    {
                        level.SelectedIndex--;
                    }
                }
                else if (key.Key == ConsoleKey.J && key.Control)
                {
                    if (level.SelectedIndex < filtered.Count - 1)
                    {
                        level.SelectedIndex++;
                    }
                }
                else if (key.KeyChar >= ' ')
                {
                    level.Filter.InsertChar(key.KeyChar);
                    level.SelectedIndex = 0;
                    level.ScrollOffset = 0;
                }

                break;
        }

        // Adjust scroll offset to keep selection visible
        if (_actionMenuStack.Count == 0)
        {
            return;
        }

        level = _actionMenuStack.Peek();
        filtered = level.GetFilteredItems();

        if (filtered.Count > 0)
        {
            level.SelectedIndex = Math.Clamp(level.SelectedIndex, 0, filtered.Count - 1);
        }
        else
        {
            level.SelectedIndex = 0;
        }

        int maxVisible = 18;

        if (level.SelectedIndex < level.ScrollOffset)
        {
            level.ScrollOffset = level.SelectedIndex;
        }
        else if (level.SelectedIndex >= level.ScrollOffset + maxVisible)
        {
            level.ScrollOffset = level.SelectedIndex - maxVisible + 1;
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

    private void DispatchActionPaletteAction(AppAction action, int actionData, PreviewLoader previewLoader, ScreenBuffer buffer, InputPipeline pipeline)
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

            case AppAction.SelectPreviewProvider:
                HandleSelectPreviewProvider(actionData, previewLoader);
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

            case AppAction.GitCommit:
                if (_currentRepoRoot is not null)
                {
                    ShowTextInputDialog("Commit message", "", message =>
                    {
                        string trimmed = message.Trim();

                        if (string.IsNullOrEmpty(trimmed))
                        {
                            ShowNotification("Commit message cannot be empty", NotificationKind.Error);
                            return;
                        }

                        _gitActionRunner?.RunCommit(_currentRepoRoot, trimmed);
                    });
                }

                break;

            case AppAction.GitPush:
                if (_currentRepoRoot is not null)
                {
                    _gitActionRunner?.RunPush(_currentRepoRoot);
                }

                break;

            case AppAction.GitPushForceWithLease:
                if (_currentRepoRoot is not null)
                {
                    _gitActionRunner?.RunPushForceWithLease(_currentRepoRoot);
                }

                break;

            case AppAction.GitPull:
                if (_currentRepoRoot is not null)
                {
                    _gitActionRunner?.RunPull(_currentRepoRoot);
                }

                break;

            case AppAction.GitPullRebase:
                if (_currentRepoRoot is not null)
                {
                    _gitActionRunner?.RunPullRebase(_currentRepoRoot);
                }

                break;

            case AppAction.GitFetch:
                if (_currentRepoRoot is not null)
                {
                    _gitActionRunner?.RunFetch(_currentRepoRoot);
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
        if (_actionMenuStack.Count == 0)
        {
            return;
        }

        var level = _actionMenuStack.Peek();
        var filtered = level.GetFilteredItems();
        int contentWidth = Math.Min(60, width - 8);
        int itemRows = Math.Min(filtered.Count, 18);
        int contentHeight = itemRows + 2; // 1 row for text input + 1 separator + item rows
        string footer = _actionMenuStack.Count > 1
            ? "[↑↓] Navigate  [Enter] Select  [Esc] Back"
            : "[↑↓] Navigate  [Enter] Select  [Esc] Cancel";

        var content = DialogBox.Render(
            buffer, width, height,
            Math.Max(contentWidth, footer.Length),
            contentHeight,
            title: level.Title,
            footer: footer);

        // Row 0: text input with "> " prefix
        var prefixStyle = new CellStyle(new Color(220, 220, 100), DialogBox.BgColor);
        var inputStyle = new CellStyle(new Color(200, 200, 200), DialogBox.BgColor);
        buffer.WriteString(content.Top, content.Left, "> ", prefixStyle);
        level.Filter.Render(buffer, content.Top, content.Left + 2, content.Width - 2, inputStyle);

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
        var submenuIndicatorStyle = new CellStyle(new Color(120, 120, 140), DialogBox.BgColor);
        var submenuIndicatorSelectedStyle = new CellStyle(new Color(20, 20, 35), new Color(200, 200, 200));

        int visibleCount = content.Height - 2;

        for (int i = 0; i < visibleCount; i++)
        {
            int itemIndex = level.ScrollOffset + i;

            if (itemIndex >= filtered.Count)
            {
                break;
            }

            var item = filtered[itemIndex];
            bool isSelected = itemIndex == level.SelectedIndex;
            int row = content.Top + 2 + i;

            CellStyle labelStyle = isSelected ? selectedStyle : normalStyle;

            // Fill entire row with selected background if selected
            if (isSelected)
            {
                buffer.FillRow(row, content.Left, content.Width, ' ', selectedStyle);
            }

            if (item.IsSubmenu)
            {
                // Submenu items show a "▸" indicator on the right
                buffer.WriteString(row, content.Left + 1, item.Label, labelStyle, content.Width - 4);
                CellStyle indStyle = isSelected ? submenuIndicatorSelectedStyle : submenuIndicatorStyle;
                buffer.WriteString(row, content.Left + content.Width - 2, "\u25b8", indStyle);
            }
            else
            {
                string shortcut = item.Shortcut;
                CellStyle scStyle = isSelected ? shortcutSelectedStyle : shortcutStyle;
                buffer.WriteString(row, content.Left + 1, item.Label, labelStyle, content.Width - shortcut.Length - 3);
                int shortcutCol = content.Left + content.Width - shortcut.Length - 1;
                buffer.WriteString(row, shortcutCol, shortcut, scStyle);
            }
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
        const int maxDepth = 8;
        var entries = new List<FileSystemEntry>();

        var fileOptions = new EnumerationOptions
        {
            RecurseSubdirectories = false,
            IgnoreInaccessible = true,
            AttributesToSkip = FileAttributes.Device,
        };

        var dirOptions = new EnumerationOptions
        {
            RecurseSubdirectories = false,
            IgnoreInaccessible = true,
            AttributesToSkip = FileAttributes.Device,
        };

        // Manual recursive walk so we can skip directories before descending
        var stack = new Stack<(string Path, int Depth)>();
        stack.Push((basePath, 0));

        while (stack.Count > 0 && entries.Count < maxEntries)
        {
            if (ct.IsCancellationRequested)
            {
                return;
            }

            (string currentDir, int depth) = stack.Pop();

            // Enumerate files in the current directory
            try
            {
                foreach (string filePath in Directory.EnumerateFiles(currentDir, "*", fileOptions))
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
                // Skip if directory enumeration fails
            }

            // Push child directories onto the stack (if within depth limit)
            if (depth < maxDepth && entries.Count < maxEntries)
            {
                try
                {
                    foreach (string subDir in Directory.EnumerateDirectories(currentDir, "*", dirOptions))
                    {
                        string dirName = Path.GetFileName(subDir);

                        // Always skip .git directories
                        if (dirName.Equals(".git", StringComparison.OrdinalIgnoreCase))
                        {
                            continue;
                        }

                        // Skip dot-prefixed directories when hidden files are not shown
                        if (!showHidden && dirName.StartsWith('.'))
                        {
                            continue;
                        }

                        // Skip system directories on Windows when system files are not shown
                        if (!showSystem && OperatingSystem.IsWindows())
                        {
                            try
                            {
                                var dirInfo = new DirectoryInfo(subDir);

                                if ((dirInfo.Attributes & FileAttributes.Hidden) != 0)
                                {
                                    continue;
                                }

                                if ((dirInfo.Attributes & FileAttributes.System) != 0)
                                {
                                    continue;
                                }
                            }
                            catch
                            {
                                continue;
                            }
                        }

                        stack.Push((subDir, depth + 1));
                    }
                }
                catch
                {
                    // Skip if directory enumeration fails
                }
            }
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
