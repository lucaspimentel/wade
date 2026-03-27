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
    private static readonly CellStyle MetaSeparatorStyle = new(new Color(60, 60, 80), null);

    // Action palette state
    private readonly Stack<ActionMenuLevel> _actionMenuStack = new();

    // Bookmark state
    private readonly BookmarkStore _bookmarkStore = new();

    // Clipboard state
    private readonly List<string> _clipboardPaths = [];
    private readonly WadeConfig _config;
    private readonly DirectoryContents _directoryContents = new();
    private readonly StringBuilder _flushBuffer = new(4096);
    private readonly Layout _layout = new();

    // Multi-select state
    private readonly HashSet<string> _markedPaths = new(StringComparer.OrdinalIgnoreCase);

    // Track selected index per directory so we restore position when navigating back
    private readonly Dictionary<string, int> _selectedIndexPerDir = new(StringComparer.OrdinalIgnoreCase);
    private PreviewContext? _activePreviewContext;
    private int _activeProviderIndex;

    // Text input dialog state
    private TextInput? _activeTextInput;
    private string? _aheadBehindText;
    private List<IMetadataProvider>? _applicableMetadataProviders;
    private List<IPreviewProvider>? _applicableProviders;
    private TextInput? _bookmarkInput;
    private int _bookmarkScrollOffset;
    private int _bookmarkSelectedIndex;
    private string? _cachedImagePath;
    private int _cachedImagePixelHeight;
    private int _cachedImagePixelWidth;
    private string? _cachedMetadataFileTypeLabel;

    // Metadata provider state
    private MetadataSection[]? _cachedMetadataSections;
    private string? _cachedPreviewEncoding;
    private string? _cachedPreviewFileTypeLabel;
    private string? _cachedPreviewLineEnding;

    private string? _cachedPreviewPath;

    // Image preview state
    private string? _cachedSixelData;
    private StyledLine[]? _cachedStyledLines;
    private int _cellPixelHeight = 16;
    private int _cellPixelWidth = 8;
    private bool _clipboardIsCut;

    // Config dialog state
    private ConfigDialogState? _configDialogState;

    // Context menu state
    private ContextMenuState? _contextMenuState;
    private string? _confirmMessage;

    // Confirm dialog state
    private string? _confirmTitle;
    private Action? _confirmYesAction;
    private string? _currentBranchName;
    private DriveMediaType _currentDriveMediaType = DriveMediaType.Unknown;

    private string _currentPath = "";
    private string? _currentRepoRoot;

    // Directory size calculation state
    private DirectorySizeLoader? _dirSizeLoader;

    // Expanded preview state
    private int _expandedPreviewScrollOffset;
    private List<FileSystemEntry>? _fileFinderAllEntries;
    private CancellationTokenSource? _fileFinderCts;
    private TextInput? _fileFinderInput;
    private bool _fileFinderScanning;
    private int _fileFinderScrollOffset;

    // File finder state
    private int _fileFinderSelectedIndex;
    private Wade.Search.SearchIndex? _fileFinderSearchIndex;
    private string _fileFinderLastSearchQuery = "";
    private long _fileFinderSearchId;
    private List<Wade.Search.SearchResult>? _fileFinderSearchResults;
    private Dictionary<string, FileSystemEntry>? _fileFinderEntryCache;
    private List<FileSystemEntry>? _filteredEntries;

    // Filesystem watcher state
    private FileSystemWatcherManager? _fsWatcher;
    private GitActionRunner? _gitActionRunner;
    private Dictionary<string, GitFileStatus>? _gitStatuses;

    // Git status state
    private GitStatusLoader? _gitStatusLoader;

    // Go-to-path state
    private TextInput? _goToPathInput;
    private string? _goToPathSuggestion;

    // Terminal capability state
    private bool _imagePreviewsEffective;

    // Inline directory size state
    private InlineDirSizeLoader? _inlineDirSizeLoader;
    private Dictionary<string, long>? _inlineDirSizes;

    // Modal input state
    private InputMode _inputMode = InputMode.Normal;
    private bool _isCombinedPreview;
    private bool _isImagePreview;
    private bool _isPlaceholderPreview;
    private bool _isRenderedPreview;

    // Left pane state cached during Render for mouse hit-testing
    private List<FileSystemEntry>? _leftPaneEntries;
    private int _leftPaneScroll;
    private int _leftPaneSelected;

    // Notification state
    private Notification? _notification;
    private string? _pendingPreviewPath;
    private PreviewLoader? _previewLoader;
    private bool _previewLoading;
    private int _propertiesContentHeight;
    private string? _propertiesDirSizePath;
    private string? _propertiesDirSizeText;
    private int _propertiesScrollOffset;

    private int _scrollOffset;
    private string _searchFilter = "";

    // Search/filter state
    private TextInput? _searchInput;
    private int _selectedIndex;
    private int _sixelImageTop;
    private bool _sixelPending;
    private bool _sixelSupported;
    private Action<string>? _textInputCompleteAction;
    private string? _textInputTitle;

#pragma warning disable CSLINT221 // Consider using a primary constructor
    public App(WadeConfig config)
#pragma warning restore CSLINT221
    {
        _config = config;
    }

    private int VisibleFileListHeight =>
        _inputMode == InputMode.Search || !string.IsNullOrEmpty(_searchFilter)
            ? _layout.CenterPane.Height - 1
            : _layout.CenterPane.Height;

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
        using IInputSource inputSource = InputPipeline.CreatePlatformSource();
        using var pipeline = new InputPipeline(inputSource);
        _previewLoader = new PreviewLoader(pipeline);
        _dirSizeLoader = new DirectorySizeLoader(pipeline);
        _inlineDirSizeLoader = new InlineDirSizeLoader(pipeline);
        _gitStatusLoader = new GitStatusLoader(pipeline);
        _gitActionRunner = new GitActionRunner(pipeline);
        _fsWatcher = new FileSystemWatcherManager(pipeline);
        PreviewLoader? previewLoader = _previewLoader;

        TerminalCapabilities caps = terminal.Capabilities;
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

            // Ensure filesystem watcher tracks the current directory
            _fsWatcher?.Watch(_currentPath);

            // Render
            buffer.Clear();
            Render(buffer);
            buffer.Flush(_flushBuffer);

            // Write Sixel data after flush (bypasses cell grid)
            if (_sixelPending && _cachedSixelData is not null && _config.PreviewPaneEnabled
                && _inputMode is InputMode.Normal or InputMode.Search or InputMode.ExpandedPreview)
            {
                _sixelPending = false;
                Rect sixelPane = _inputMode == InputMode.ExpandedPreview ? _layout.ExpandedPane : _layout.RightPane;
                int cursorRow = _sixelImageTop > 0 ? _sixelImageTop : sixelPane.Top;
                int cursorCol = sixelPane.Left;

                if (!_isCombinedPreview && _inputMode == InputMode.ExpandedPreview && _cachedImagePixelWidth > 0 && _cachedImagePixelHeight > 0)
                {
                    (cursorRow, cursorCol) =
                        sixelPane.CenterContent(_cachedImagePixelWidth / _cellPixelWidth, _cachedImagePixelHeight / _cellPixelHeight);
                }

                string moveCursor = AnsiCodes.MoveCursor(cursorRow, cursorCol);
                buffer.WriteRaw(moveCursor + _cachedSixelData);
            }

            // Wait for next input event
            InputEvent inputEvent = pipeline.Take();

            // Drain any additional queued events (e.g. rapid key repeats)
            while (pipeline.TryTake(out InputEvent? extra))
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
                else if (extra is PreviewLoadingCompleteEvent loadingCompleteExtra)
                {
                    HandlePreviewLoadingComplete(loadingCompleteExtra);
                }
                else if (extra is MetadataReadyEvent metadataExtra)
                {
                    HandleMetadataReady(metadataExtra);
                }
                else if (extra is DirectorySizeReadyEvent dirSizeExtra)
                {
                    HandleDirectorySizeReady(dirSizeExtra);
                }
                else if (extra is InlineDirSizeReadyEvent inlineDirSizeExtra)
                {
                    HandleInlineDirSizeReady(inlineDirSizeExtra);
                }
                else if (extra is InlineDirSizeCompleteEvent inlineDirSizeCompleteExtra)
                {
                    HandleInlineDirSizeComplete(inlineDirSizeCompleteExtra);
                }
                else if (extra is GitStatusReadyEvent gitExtra)
                {
                    HandleGitStatusReady(gitExtra);
                }
                else if (extra is GitActionCompleteEvent gitActionExtra)
                {
                    HandleGitActionComplete(gitActionExtra);
                }
                else if (extra is FileSystemChangedEvent fsChangedExtra)
                {
                    HandleFileSystemChanged(fsChangedExtra, previewLoader, buffer);
                }
                else if (extra is FileFinderPartialResultEvent partialExtra)
                {
                    if (_inputMode == InputMode.FileFinder && partialExtra.BasePath == _currentPath)
                    {
                        _fileFinderAllEntries ??= new List<FileSystemEntry>();
                        _fileFinderAllEntries.AddRange(partialExtra.Entries);

                        foreach (FileSystemEntry entry in partialExtra.Entries)
                            _fileFinderEntryCache?.TryAdd(entry.FullPath, entry);
                    }
                }
                else if (extra is FileFinderSearchResultEvent searchExtra)
                {
                    HandleFileFinderSearchResult(searchExtra);
                }
                else if (extra is FileFinderScanCompleteEvent scanExtra)
                {
                    if (_inputMode == InputMode.FileFinder && scanExtra.BasePath == _currentPath)
                    {
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
                    Rect resizePane = _inputMode == InputMode.ExpandedPreview ? _layout.ExpandedPane : _layout.RightPane;
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

            if (inputEvent is PreviewLoadingCompleteEvent loadingCompleteEvt)
            {
                HandlePreviewLoadingComplete(loadingCompleteEvt);
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

            if (inputEvent is InlineDirSizeReadyEvent inlineDirSizeEvt)
            {
                HandleInlineDirSizeReady(inlineDirSizeEvt);
                continue;
            }

            if (inputEvent is InlineDirSizeCompleteEvent inlineDirSizeCompleteEvt)
            {
                HandleInlineDirSizeComplete(inlineDirSizeCompleteEvt);
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

            if (inputEvent is FileSystemChangedEvent fsChangedEvt)
            {
                HandleFileSystemChanged(fsChangedEvt, previewLoader, buffer);
                continue;
            }

            if (inputEvent is FileFinderPartialResultEvent partialEvt)
            {
                if (_inputMode == InputMode.FileFinder && partialEvt.BasePath == _currentPath)
                {
                    _fileFinderAllEntries ??= new List<FileSystemEntry>();
                    _fileFinderAllEntries.AddRange(partialEvt.Entries);

                    foreach (FileSystemEntry entry in partialEvt.Entries)
                        _fileFinderEntryCache?.TryAdd(entry.FullPath, entry);
                }

                continue;
            }

            if (inputEvent is FileFinderSearchResultEvent searchResultEvt)
            {
                HandleFileFinderSearchResult(searchResultEvt);
                continue;
            }

            if (inputEvent is FileFinderScanCompleteEvent scanEvt)
            {
                if (_inputMode == InputMode.FileFinder && scanEvt.BasePath == _currentPath)
                {
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

                if (_inputMode == InputMode.ContextMenu)
                {
                    HandleContextMenuMouse(mouseEvent, previewLoader, buffer, pipeline);
                    continue;
                }

                // Discard mouse events while a modal dialog is open
                if (_inputMode is InputMode.Help or InputMode.GoToPath or InputMode.TextInput or InputMode.Confirm or InputMode.Config
                    or InputMode.Properties or InputMode.ActionPalette or InputMode.Bookmarks or InputMode.FileFinder)
                {
                    continue;
                }

                List<FileSystemEntry> mouseEntries = GetVisibleEntries();
                HandleMouseEvent(mouseEvent, mouseEntries, previewLoader, buffer);

                // Clamp and adjust scroll after mouse handling
                List<FileSystemEntry> currentAfterMouse = GetVisibleEntries();
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
                        HandlePropertiesKey(keyEvent);
                    }

                    continue;
                case InputMode.Search:
                    HandleSearchKey(keyEvent);
                    List<FileSystemEntry> searchEntries = GetVisibleEntries();
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

                case InputMode.ContextMenu:
                    HandleContextMenuKey(keyEvent, previewLoader, buffer, pipeline);
                    continue;

                case InputMode.Bookmarks:
                    HandleBookmarkKey(keyEvent, previewLoader, buffer);
                    continue;

                case InputMode.FileFinder:
                    HandleFileFinderKey(keyEvent, previewLoader, buffer, pipeline);
                    continue;

                case InputMode.Normal:
                default:
                    break; // fall through to normal AppAction dispatch
            }

            AppAction action = InputReader.MapKey(keyEvent);
            List<FileSystemEntry> entries = GetVisibleEntries();

            switch (action)
            {
                case AppAction.NavigateUp:
                    _selectedIndex = _selectedIndex > 0 ? _selectedIndex - 1 : entries.Count - 1;
                    break;

                case AppAction.NavigateDown:
                    _selectedIndex = _selectedIndex < entries.Count - 1 ? _selectedIndex + 1 : 0;

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
                        List<FileSystemEntry> driveEntries = _directoryContents.GetEntries(_currentPath);
                        string root = Path.GetPathRoot(oldPath)!.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                        int idx = driveEntries.FindIndex(e => e.Name.Equals(root, StringComparison.OrdinalIgnoreCase));
                        _selectedIndex = idx >= 0 ? idx : 0;
                    }
                    else
                    {
                        DirectoryInfo? parent = Directory.GetParent(_currentPath);
                        if (parent is not null)
                        {
                            _currentPath = PathCompletion.CapitalizeDriveLetter(parent.FullName);
                            UpdateTerminalTitle();
                            RefreshGitStatus();
                            List<FileSystemEntry> parentEntries = _directoryContents.GetEntries(_currentPath);
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
                        _propertiesScrollOffset = 0;
                        FileSystemEntry propsEntry = entries[_selectedIndex];
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
            List<FileSystemEntry> currentEntries = GetVisibleEntries();
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

        _fsWatcher?.Dispose();
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
                string comspec = Environment.GetEnvironmentVariable("COMSPEC") ?? "cmd.exe";
                Process.Start(new ProcessStartInfo(comspec)
                {
                    WorkingDirectory = workingDirectory,
                    UseShellExecute = false,
                });
            }
        }
        else
        {
            string shell = Environment.GetEnvironmentVariable("SHELL") ?? "/bin/sh";
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
                        catch (UnauthorizedAccessException)
                        {
                        }
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
        List<FileSystemEntry> entries = GetVisibleEntries();
        bool showSearchBar = _inputMode == InputMode.Search || !string.IsNullOrEmpty(_searchFilter);
        Rect fileListPane = showSearchBar
            ? _layout.CenterPane with { Height = _layout.CenterPane.Height - 1 }
            : _layout.CenterPane;

        // Center pane: current directory
        PaneRenderer.RenderFileList(
            buffer, fileListPane, entries, _selectedIndex, _scrollOffset,
            isActive: true, showIcons: _config.ShowIconsEnabled,
            showSize: _config.SizeColumnEnabled, showDate: _config.DateColumnEnabled,
            markedPaths: _markedPaths, gitStatuses: _gitStatuses,
            dirSizes: _inlineDirSizes);

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
                DirectoryInfo? parentDir = Directory.GetParent(_currentPath);
                parentKey = parentDir?.FullName ?? DirectoryContents.DrivesPath;
                currentName = Path.GetFileName(_currentPath);
            }

            List<FileSystemEntry> parentEntries = _directoryContents.GetEntries(parentKey);
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
            PaneRenderer.RenderFileList(buffer, _layout.LeftPane, parentEntries, parentSelected, parentScroll, isActive: false,
                showIcons: _config.ShowIconsEnabled);

            // Cache for mouse hit-testing
            _leftPaneEntries = parentEntries;
            _leftPaneScroll = parentScroll;
            _leftPaneSelected = parentSelected;
        }

        // Right pane: preview
        if (_config.PreviewPaneEnabled && entries.Count > 0 && _selectedIndex < entries.Count)
        {
            FileSystemEntry selected = entries[_selectedIndex];
            if (selected.IsDirectory)
            {
                List<FileSystemEntry> previewEntries = _directoryContents.GetEntries(selected.FullPath);
                if (previewEntries.Count > 0)
                {
                    PaneRenderer.RenderFileList(buffer, _layout.RightPane, previewEntries, -1, 0, isActive: false,
                        showIcons: _config.ShowIconsEnabled);
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
                    _applicableMetadataProviders = _config.FileMetadataEnabled
                        ? MetadataProviderRegistry.GetApplicableProviders(selected.FullPath, _activePreviewContext)
                        : null;
                    _applicableProviders = _config.FilePreviewsEnabled
                        ? PreviewProviderRegistry.GetApplicableProviders(selected.FullPath, _activePreviewContext)
                        : [];

                    if (_applicableProviders.Count == 0 && _applicableMetadataProviders is { Count: 0 })
                    {
                        ClearPreviewCache(_previewLoader!);
                        _applicableProviders = [];
                        _cachedPreviewPath = selected.FullPath;
                    }
                    else
                    {
                        bool wasImage = _isImagePreview || _isCombinedPreview;
                        ReloadActiveProvider(selected.FullPath, _previewLoader!);
                        if (wasImage)
                        {
                            buffer.ForceFullRedraw();
                        }
                    }
                }

                if (_applicableProviders is { Count: 0 } && _applicableMetadataProviders is { Count: 0 })
                {
                    string message = _activePreviewContext is { IsBrokenSymlink: true } ? "[broken symlink]"
                        : _activePreviewContext is { IsCloudPlaceholder: true } ? "[cloud file \u2013 not downloaded]"
                        : CliToolHints.GetHint(selected.FullPath) ?? "[no preview available]";
                    PaneRenderer.RenderMessage(buffer, _layout.RightPane, message);
                }
                else if (_previewLoading && _cachedMetadataSections is null)
                {
                    PaneRenderer.RenderMessage(buffer, _layout.RightPane, "[loading\u2026]");
                }
                else if (_cachedMetadataSections is not null && !_previewLoading && !_isImagePreview && !_isCombinedPreview
                         && (_cachedStyledLines is null || _isPlaceholderPreview))
                {
                    // Metadata only (no preview provider, or preview is just a placeholder like "[binary file]")
                    StyledLine[] metadataLines = MetadataRenderer.Render(_cachedMetadataSections, _layout.RightPane.Width);
                    PaneRenderer.RenderPreview(buffer, _layout.RightPane, metadataLines, showLineNumbers: false);
                }
                else if (_cachedMetadataSections is not null && _isImagePreview && _cachedSixelData is not null)
                {
                    // Metadata + image: render metadata at top, image below
                    RenderMetadataWithImage(buffer, _layout.RightPane);
                }
                else if (_cachedMetadataSections is not null && _cachedStyledLines is not null && !_isPlaceholderPreview)
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

                    _sixelImageTop = 0;
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
        StatusBar.Render(buffer, _layout.StatusBar, displayPath, entries.Count, _selectedIndex, selectedEntry, _cachedPreviewFileTypeLabel,
            _cachedPreviewEncoding, _cachedPreviewLineEnding, _notification, _markedPaths.Count, _directoryContents.SortMode,
            _directoryContents.SortAscending, _clipboardPaths.Count, _clipboardIsCut, _currentBranchName, _aheadBehindText);

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
                    GitFileStatus? propGitStatus = _gitStatuses?.TryGetValue(selectedEntry.FullPath, out GitFileStatus gs) == true ? gs : null;
                    // Filter out FileMetadataProvider sections (header = filename) — properties overlay already shows that info
                    MetadataSection[]? propMetadata = _cachedMetadataSections?.Where(s => s.Header != selectedEntry.Name).ToArray();
                    if (propMetadata is { Length: 0 })
                    {
                        propMetadata = null;
                    }

                    _propertiesContentHeight = PropertiesOverlay.Render(buffer, width, height, selectedEntry, _propertiesDirSizeText, propGitStatus,
                        propMetadata, _propertiesScrollOffset);
                    // Clamp scroll offset in case content changed
                    int propsVisibleRows = Math.Max(1, height - 8);
                    int propsMaxScroll = Math.Max(0, _propertiesContentHeight - propsVisibleRows);
                    _propertiesScrollOffset = Math.Clamp(_propertiesScrollOffset, 0, propsMaxScroll);
                }

                break;
            case InputMode.ActionPalette:
                RenderActionPalette(buffer, width, height);
                break;
            case InputMode.ContextMenu:
                if (_contextMenuState is not null)
                {
                    ContextMenuRenderer.Render(buffer, width, height, _contextMenuState);
                }

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
        _isPlaceholderPreview = evt.IsPlaceholder;
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

    private void HandlePreviewLoadingComplete(PreviewLoadingCompleteEvent evt)
    {
        if (evt.Path != _pendingPreviewPath)
        {
            return;
        }

        _cachedPreviewPath = evt.Path;
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
        int n = FormatHelpers.FormatSize(sizeBuf, evt.TotalBytes);
        string formatted = sizeBuf[..n].ToString();
        _propertiesDirSizeText = $"{formatted} ({evt.TotalBytes:N0} bytes)";
    }

    private void HandleInlineDirSizeReady(InlineDirSizeReadyEvent evt)
    {
        if (evt.ParentPath != _currentPath)
        {
            return;
        }

        _inlineDirSizes ??= new Dictionary<string, long>();
        _inlineDirSizes[evt.DirectoryPath] = evt.TotalBytes;
    }

    private void HandleInlineDirSizeComplete(InlineDirSizeCompleteEvent evt)
    {
        if (evt.ParentPath != _currentPath)
        {
            return;
        }

        _directoryContents.DirSizes = _inlineDirSizes;

        if (_directoryContents.SortMode == SortMode.Size)
        {
            _directoryContents.Invalidate(_currentPath);
        }
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
                if (_gitStatuses.TryGetValue(path, out GitFileStatus s) && (s & statusMask) != 0)
                {
                    return true;
                }
            }

            return false;
        }

        List<FileSystemEntry> entries = GetVisibleEntries();
        if (_selectedIndex < entries.Count)
        {
            string path = entries[_selectedIndex].FullPath;
            return _gitStatuses.TryGetValue(path, out GitFileStatus s) && (s & statusMask) != 0;
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

    private void HandleFileSystemChanged(FileSystemChangedEvent evt, PreviewLoader previewLoader, ScreenBuffer buffer)
    {
        if (!string.Equals(evt.DirectoryPath, _currentPath, StringComparison.OrdinalIgnoreCase))
        {
            return; // Stale event for a directory we've navigated away from
        }

        // Preserve selection by name
        List<FileSystemEntry> entries = GetVisibleEntries();
        string? selectedName = _selectedIndex < entries.Count ? entries[_selectedIndex].Name : null;

        // Invalidate cache
        if (evt.FullRefresh)
        {
            _directoryContents.InvalidateAll();
        }
        else
        {
            _directoryContents.Invalidate(_currentPath);
        }

        // Restore selection
        List<FileSystemEntry> newEntries = GetVisibleEntries();
        bool selectedSurvived = false;
        if (selectedName is not null && newEntries.Count > 0)
        {
            int idx = newEntries.FindIndex(e => e.Name.Equals(selectedName, StringComparison.OrdinalIgnoreCase));
            if (idx >= 0)
            {
                _selectedIndex = idx;
                selectedSurvived = true;
            }
            else
            {
                _selectedIndex = Math.Min(_selectedIndex, newEntries.Count - 1);
            }
        }
        else
        {
            _selectedIndex = Math.Min(_selectedIndex, Math.Max(0, newEntries.Count - 1));
        }

        // Only clear preview if the selected file was deleted/renamed away
        if (!selectedSurvived)
        {
            ClearPreviewCache(previewLoader, buffer);
        }

        RefreshGitStatus();
        buffer.ForceFullRedraw();
    }

    private void HandleSelectPreviewProvider(int index, PreviewLoader previewLoader, ScreenBuffer buffer)
    {
        if (_applicableProviders is null || index < 0 || index >= _applicableProviders.Count)
        {
            return;
        }

        List<FileSystemEntry> entries = GetVisibleEntries();
        if (_selectedIndex >= entries.Count)
        {
            return;
        }

        FileSystemEntry selected = entries[_selectedIndex];
        if (selected.IsDirectory)
        {
            return;
        }

        bool wasImage = _isImagePreview || _isCombinedPreview;
        _activeProviderIndex = index;
        ReloadActiveProvider(selected.FullPath, previewLoader);

        if (wasImage)
        {
            buffer.ForceFullRedraw();
        }
    }

    private PreviewContext BuildPreviewContext(int paneWidth, int paneHeight)
    {
        GitFileStatus? gitStatus = null;
        List<FileSystemEntry> entries = GetVisibleEntries();
        if (_selectedIndex < entries.Count && _gitStatuses is not null)
        {
            _gitStatuses.TryGetValue(entries[_selectedIndex].FullPath, out GitFileStatus status);
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
            PdfPreviewEnabled: _config.PdfPreviewEnabled,
            PdfMetadataEnabled: _config.PdfMetadataEnabled,
            MarkdownPreviewEnabled: _config.MarkdownPreviewEnabled,
            FfprobeEnabled: _config.FfprobeEnabled,
            MediainfoEnabled: _config.MediainfoEnabled,
            ZipPreviewEnabled: _config.ZipPreviewEnabled,
            ImagePreviewsEnabled: _imagePreviewsEffective,
            SixelSupported: _sixelSupported,
            ArchiveMetadataEnabled: _config.ArchiveMetadataEnabled);
    }

    private void ReloadActiveProvider(string path, PreviewLoader loader, bool includeMetadata = true)
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

        List<IMetadataProvider>? metadataProviders = includeMetadata ? _applicableMetadataProviders : null;

        if (previewProvider is null && metadataProviders is null or { Count: 0 })
        {
            return;
        }

        _pendingPreviewPath = path;
        _previewLoading = true;
        _cachedStyledLines = null;
        _cachedSixelData = null;
        _cachedImagePath = null;
        _cachedPreviewFileTypeLabel = null;
        _cachedPreviewEncoding = null;
        _cachedPreviewLineEnding = null;
        if (includeMetadata)
        {
            _cachedMetadataSections = null;
            _cachedMetadataFileTypeLabel = null;
        }

        _isImagePreview = false;
        _isCombinedPreview = false;
        _isRenderedPreview = false;
        _isPlaceholderPreview = false;
        loader.BeginLoad(path, metadataProviders, previewProvider, _activePreviewContext);
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
        _isPlaceholderPreview = false;
        _activeProviderIndex = 0;
        _applicableProviders = null;
        _activePreviewContext = null;
        _sixelPending = false;
        _cachedMetadataSections = null;
        _cachedMetadataFileTypeLabel = null;
        _applicableMetadataProviders = null;

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

        // Ignore releases and non-left/right-click
        if (mouse.IsRelease || (mouse.Button != MouseButton.Left && mouse.Button != MouseButton.Right))
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

            // Right-click opens context menu
            if (mouse.Button == MouseButton.Right && entryIndex >= 0 && entryIndex < entries.Count)
            {
                _contextMenuState = new ContextMenuState(BuildContextMenuItems(), row, col);
                _inputMode = InputMode.ContextMenu;
                return;
            }
        }
        else if (HitTestPane(_layout.LeftPane, row, col) && _leftPaneEntries is not null)
        {
            // Left pane click
            int entryIndex = _leftPaneScroll + (row - _layout.LeftPane.Top);
            if (entryIndex >= 0 && entryIndex < _leftPaneEntries.Count)
            {
                FileSystemEntry clicked = _leftPaneEntries[entryIndex];
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
                    DirectoryInfo? parentDir = Directory.GetParent(_currentPath);
                    if (parentDir is not null)
                    {
                        _currentPath = PathCompletion.CapitalizeDriveLetter(parentDir.FullName);
                        UpdateTerminalTitle();
                        RefreshGitStatus();
                        List<FileSystemEntry> parentEntries = _directoryContents.GetEntries(_currentPath);
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
                FileSystemEntry selected = entries[_selectedIndex];
                if (selected.IsDirectory)
                {
                    List<FileSystemEntry> previewEntries = _directoryContents.GetEntries(selected.FullPath);
                    int entryIndex = row - _layout.RightPane.Top; // scroll is always 0 for preview
                    if (entryIndex >= 0 && entryIndex < previewEntries.Count)
                    {
                        FileSystemEntry clicked = previewEntries[entryIndex];
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
                            List<FileSystemEntry> dirEntries = _directoryContents.GetEntries(_currentPath);
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
            ReloadActiveProvider(path, previewLoader, includeMetadata: false);
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

    private void HandlePropertiesKey(KeyEvent key)
    {
        switch (key.Key)
        {
            case ConsoleKey.Escape:
            case ConsoleKey.Enter:
            case ConsoleKey.I:
            case ConsoleKey.Q:
                _inputMode = InputMode.Normal;
                _dirSizeLoader?.Cancel();
                _propertiesDirSizePath = null;
                _propertiesDirSizeText = null;
                break;

            case ConsoleKey.UpArrow:
            case ConsoleKey.K:
                if (_propertiesScrollOffset > 0)
                {
                    _propertiesScrollOffset--;
                }

                break;

            case ConsoleKey.DownArrow:
            case ConsoleKey.J:
                _propertiesScrollOffset++;
                break;

            case ConsoleKey.PageUp:
            {
                int pageSize = Math.Max(1, _propertiesContentHeight / 2);
                _propertiesScrollOffset = Math.Max(0, _propertiesScrollOffset - pageSize);
                break;
            }

            case ConsoleKey.PageDown:
            {
                int pageSize = Math.Max(1, _propertiesContentHeight / 2);
                _propertiesScrollOffset += pageSize;
                break;
            }

            case ConsoleKey.Home:
                _propertiesScrollOffset = 0;
                break;

            case ConsoleKey.End:
                _propertiesScrollOffset = int.MaxValue; // clamped during render
                break;

            default:
                // Any other non-modifier key closes the overlay
                _inputMode = InputMode.Normal;
                _dirSizeLoader?.Cancel();
                _propertiesDirSizePath = null;
                _propertiesDirSizeText = null;
                break;
        }
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

        // Metadata at top, separator row, preview text below
        int metadataRows = Math.Min(metadataLines.Length + 1, pane.Height / 2); // +1 for separator row
        int previewRows = pane.Height - metadataRows;

        var metadataRect = new Rect(pane.Left, pane.Top, pane.Width, metadataRows);
        PaneRenderer.RenderPreview(buffer, metadataRect, metadataLines, showLineNumbers: false);

        // Draw separator line on the last row of the metadata rect
        int separatorRow = pane.Top + metadataRows - 1;
        buffer.FillRow(separatorRow, pane.Left, pane.Width, '\u2500', MetaSeparatorStyle);

        if (previewRows > 0)
        {
            var previewRect = new Rect(pane.Left, pane.Top + metadataRows, pane.Width, previewRows);
            PaneRenderer.RenderPreview(buffer, previewRect, _cachedStyledLines!, showLineNumbers: !_isRenderedPreview);
        }
    }

    private void RenderMetadataWithImage(ScreenBuffer buffer, Rect pane)
    {
        StyledLine[] metadataLines = MetadataRenderer.Render(_cachedMetadataSections!, pane.Width);

        int metadataRows = Math.Min(metadataLines.Length + 1, pane.Height / 2); // +1 for separator row
        int imageRows = pane.Height - metadataRows;

        var metadataRect = new Rect(pane.Left, pane.Top, pane.Width, metadataRows);
        PaneRenderer.RenderPreview(buffer, metadataRect, metadataLines, showLineNumbers: false);

        // Draw separator line on the last row of the metadata rect
        buffer.FillRow(pane.Top + metadataRows - 1, pane.Left, pane.Width, '\u2500', MetaSeparatorStyle);

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
        Rect pane = _layout.ExpandedPane;

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

            _sixelImageTop = 0;
            _sixelPending = true;
        }
        else if (_cachedStyledLines is not null)
        {
            PaneRenderer.RenderPreview(buffer, pane, _cachedStyledLines, _expandedPreviewScrollOffset, showLineNumbers: !_isRenderedPreview);
        }

        // Status bar (expanded preview)
        List<FileSystemEntry> entries = GetVisibleEntries();
        FileSystemEntry? selectedEntry = entries.Count > 0 && _selectedIndex < entries.Count
            ? entries[_selectedIndex]
            : null;
        string displayPath = _cachedPreviewPath ?? _cachedImagePath ?? _currentPath;
        if (displayPath == DirectoryContents.DrivesPath)
        {
            displayPath = "Drives";
        }

        StatusBar.Render(buffer, _layout.StatusBar, displayPath, entries.Count, _selectedIndex, selectedEntry, _cachedPreviewFileTypeLabel,
            _cachedPreviewEncoding, _cachedPreviewLineEnding, _notification, _markedPaths.Count, _directoryContents.SortMode,
            _directoryContents.SortAscending, _clipboardPaths.Count, _clipboardIsCut, _currentBranchName, _aheadBehindText);
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
                List<FileSystemEntry> entries = _directoryContents.GetEntries(_currentPath);
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
        }
        else
        {
            string? repoRoot = GitUtils.FindRepoRoot(_currentPath);
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

        RefreshInlineDirSizes();
    }

    private void RefreshInlineDirSizes()
    {
        _inlineDirSizes = null;
        _directoryContents.DirSizes = null;

        if (!_config.SizeColumnEnabled || _currentPath == DirectoryContents.DrivesPath)
        {
            _inlineDirSizeLoader?.Cancel();
            return;
        }

        // Detect the current drive type
        string? root = Path.GetPathRoot(_currentPath);

        if (root != null)
        {
            _currentDriveMediaType = DriveTypeDetector.Detect(new DriveInfo(root));
        }
        else
        {
            _currentDriveMediaType = DriveMediaType.Unknown;
        }

        if (!ShouldComputeInlineDirSizes(_currentDriveMediaType, _config))
        {
            _inlineDirSizeLoader?.Cancel();
            return;
        }

        List<FileSystemEntry> entries = _directoryContents.GetEntries(_currentPath);
        var dirPaths = new List<string>();

        foreach (FileSystemEntry entry in entries)
        {
            if (entry.IsDirectory && !entry.IsDrive)
            {
                dirPaths.Add(entry.FullPath);
            }
        }

        if (dirPaths.Count > 0)
        {
            _inlineDirSizes = new Dictionary<string, long>();
            _inlineDirSizeLoader?.BeginLoad(_currentPath, dirPaths);
        }
        else
        {
            _inlineDirSizeLoader?.Cancel();
        }
    }

    internal static bool ShouldComputeInlineDirSizes(DriveMediaType driveType, WadeConfig config) =>
        driveType switch
        {
            DriveMediaType.Ssd => config.DirSizeSsdEnabled,
            DriveMediaType.Hdd => config.DirSizeHddEnabled,
            DriveMediaType.Network => config.DirSizeNetworkEnabled,
            DriveMediaType.Removable => config.DirSizeSsdEnabled,
            _ => false,
        };

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

    private void ShowNotification(string message, NotificationKind kind = NotificationKind.Info) =>
        _notification = new Notification(message, kind, Environment.TickCount64);

    // ── Search/filter helpers ────────────────────────────────────────────────

    private List<FileSystemEntry> GetVisibleEntries()
    {
        List<FileSystemEntry> all = _directoryContents.GetEntries(_currentPath);
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

    private void InvalidateFilteredEntries() => _filteredEntries = null;

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
            {
                List<FileSystemEntry> entries = GetVisibleEntries();
                _selectedIndex = _selectedIndex > 0 ? _selectedIndex - 1 : entries.Count - 1;
                break;
            }

            case ConsoleKey.DownArrow:
            {
                List<FileSystemEntry> entries = GetVisibleEntries();
                _selectedIndex = _selectedIndex < entries.Count - 1 ? _selectedIndex + 1 : 0;

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
            CellStyle style = i > 0 ? warnStyle : textStyle;
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
            items[i] = new ActionMenuItem { Label = prefix + _applicableProviders[i].Label, Action = AppAction.SelectPreviewProvider, Data = i };
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
            items.Add(new ActionMenuItem { Label = "Paste", Shortcut = "v", Action = AppAction.Paste });
        }

        items.Add(new ActionMenuItem { Label = "Copy absolute path", Shortcut = "y", Action = AppAction.CopyAbsolutePath });
        items.Add(new ActionMenuItem { Label = "New file", Shortcut = "n", Action = AppAction.NewFile });
        items.Add(new ActionMenuItem { Label = "New directory", Shortcut = "Shift+N", Action = AppAction.NewDirectory });
        items.Add(new ActionMenuItem { Label = "Create symlink", Shortcut = "Ctrl+L", Action = AppAction.CreateSymlink });
        items.Add(new ActionMenuItem { Label = "Properties", Shortcut = "i", Action = AppAction.ShowProperties });

        // Cloud file download — only shown for cloud placeholders
        if (OperatingSystem.IsWindows())
        {
            List<FileSystemEntry> visibleEntries = GetVisibleEntries();
            if (_selectedIndex < visibleEntries.Count && visibleEntries[_selectedIndex].IsCloudPlaceholder)
            {
                items.Add(new ActionMenuItem { Label = "Download cloud file", Action = AppAction.DownloadCloudFile });
            }
        }

        // Preview provider submenu — shown when multiple providers are available
        ActionMenuItem[]? previewSubItems = BuildPreviewMenuItems();
        if (previewSubItems is not null)
        {
            items.Add(new ActionMenuItem { Label = "Change preview", Shortcut = "p", SubItems = previewSubItems });
        }

        items.Add(new ActionMenuItem { Label = "Toggle hidden files", Shortcut = ".", Action = AppAction.ToggleHiddenFiles });
        items.Add(new ActionMenuItem { Label = "Cycle sort mode", Shortcut = "s", Action = AppAction.CycleSortMode });
        items.Add(new ActionMenuItem { Label = "Reverse sort direction", Shortcut = "S", Action = AppAction.ToggleSortDirection });
        items.Add(new ActionMenuItem { Label = "Bookmarks", Shortcut = "b", Action = AppAction.ShowBookmarks });
        items.Add(new ActionMenuItem { Label = "Toggle bookmark", Shortcut = "B", Action = AppAction.ToggleBookmark });
        items.Add(new ActionMenuItem { Label = "Go to path", Shortcut = "g", Action = AppAction.GoToPath });
        items.Add(new ActionMenuItem { Label = "Search / Find file", Shortcut = "Ctrl+F", Action = AppAction.ShowFileFinder });
        items.Add(new ActionMenuItem { Label = "Filter", Shortcut = "/", Action = AppAction.Search });
        items.Add(new ActionMenuItem { Label = "Open terminal here", Shortcut = "Ctrl+T", Action = AppAction.OpenTerminal });
        items.Add(new ActionMenuItem { Label = "Configuration", Shortcut = ",", Action = AppAction.ShowConfig });
        items.Add(new ActionMenuItem { Label = "Help", Shortcut = "?", Action = AppAction.ShowHelp });
        items.Add(new ActionMenuItem { Label = "Refresh", Shortcut = "Ctrl+R", Action = AppAction.Refresh });

        // Git actions — only shown when actionable
        if (_currentRepoRoot is not null)
        {
            items.Add(new ActionMenuItem { Label = "Git: Copy relative path", Shortcut = "Y", Action = AppAction.CopyGitRelativePath });

            if (_gitStatuses is not null)
            {
                List<FileSystemEntry> entries = GetVisibleEntries();
                if (_selectedIndex < entries.Count)
                {
                    bool hasStageableStatus = HasStatusInSelection(GitFileStatus.Modified | GitFileStatus.Untracked);
                    if (hasStageableStatus)
                    {
                        items.Add(new ActionMenuItem { Label = "Git: Stage", Action = AppAction.StageFile });
                    }

                    bool hasUnstageableStatus = HasStatusInSelection(GitFileStatus.Staged);
                    if (hasUnstageableStatus)
                    {
                        items.Add(new ActionMenuItem { Label = "Git: Unstage", Action = AppAction.UnstageFile });
                    }
                }

                bool hasAnyChanges = false;
                foreach (KeyValuePair<string, GitFileStatus> kvp in _gitStatuses)
                {
                    if ((kvp.Value & (GitFileStatus.Modified | GitFileStatus.Untracked)) != 0)
                    {
                        hasAnyChanges = true;
                        break;
                    }
                }

                if (hasAnyChanges)
                {
                    items.Add(new ActionMenuItem { Label = "Git: Stage all changes", Action = AppAction.StageAll });
                }

                bool hasAnyStagedChanges = false;
                foreach (KeyValuePair<string, GitFileStatus> kvp in _gitStatuses)
                {
                    if ((kvp.Value & GitFileStatus.Staged) != 0)
                    {
                        hasAnyStagedChanges = true;
                        break;
                    }
                }

                if (hasAnyStagedChanges)
                {
                    items.Add(new ActionMenuItem { Label = "Git: Unstage all", Action = AppAction.UnstageAll });
                    items.Add(new ActionMenuItem { Label = "Git: Commit", Action = AppAction.GitCommit });
                }
            }

            items.Add(new ActionMenuItem { Label = "Git: Push", Action = AppAction.GitPush });
            items.Add(new ActionMenuItem { Label = "Git: Push (force with lease)", Action = AppAction.GitPushForceWithLease });
            items.Add(new ActionMenuItem { Label = "Git: Pull", Action = AppAction.GitPull });
            items.Add(new ActionMenuItem { Label = "Git: Pull (rebase)", Action = AppAction.GitPullRebase });
            items.Add(new ActionMenuItem { Label = "Git: Fetch", Action = AppAction.GitFetch });
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

        ActionMenuLevel level = _actionMenuStack.Peek();
        List<ActionMenuItem> filtered = level.GetFilteredItems();

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
                    ActionMenuItem selected = filtered[level.SelectedIndex];
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
        List<FileSystemEntry> entries = GetVisibleEntries();

        switch (action)
        {
            case AppAction.OpenExternal:
                if (entries.Count > 0 && _selectedIndex < entries.Count)
                {
                    FileSystemEntry entry = entries[_selectedIndex];
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
                    FileSystemEntry entry = entries[_selectedIndex];
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

                            List<FileSystemEntry> updatedEntries = GetVisibleEntries();
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
                    (List<string> Paths, bool IsCut)? osFiles = SystemClipboard.GetFiles();

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

                        List<FileSystemEntry> updatedEntries = GetVisibleEntries();
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

                        List<FileSystemEntry> updatedEntries = GetVisibleEntries();
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

                FileSystemEntry selectedEntry = entries[_selectedIndex];
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

                        List<FileSystemEntry> updatedEntries = GetVisibleEntries();
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

    private void DispatchActionPaletteAction(AppAction action, int actionData, PreviewLoader previewLoader, ScreenBuffer buffer,
        InputPipeline pipeline)
    {
        if (DispatchFileAction(action))
        {
            return;
        }

        List<FileSystemEntry> entries = GetVisibleEntries();

        switch (action)
        {
            case AppAction.ShowProperties:
                if (entries.Count > 0 && _selectedIndex < entries.Count)
                {
                    _inputMode = InputMode.Properties;
                    _propertiesScrollOffset = 0;
                    FileSystemEntry propsEntry2 = entries[_selectedIndex];
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
                HandleSelectPreviewProvider(actionData, previewLoader, buffer);
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
                    List<string> stagePaths = GetSelectedOrMarkedPaths(entries);
                    if (stagePaths.Count > 0)
                    {
                        _gitActionRunner?.RunStage(_currentRepoRoot, stagePaths);
                    }
                }

                break;

            case AppAction.UnstageFile:
                if (_currentRepoRoot is not null)
                {
                    List<string> unstagePaths = GetSelectedOrMarkedPaths(entries);
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

            case AppAction.DownloadCloudFile:
                if (entries.Count > 0 && _selectedIndex < entries.Count)
                {
                    FileSystemEntry cloudEntry = entries[_selectedIndex];
                    if (cloudEntry.IsCloudPlaceholder)
                    {
                        DownloadCloudFile(cloudEntry.FullPath);
                    }
                }

                break;
        }
    }

    private void DownloadCloudFile(string path)
    {
        ShowNotification("Downloading\u2026");

        Task.Run(() =>
        {
            try
            {
                // Opening the file triggers Windows Cloud Files recall (download)
                using (File.OpenRead(path))
                {
                }

                // Refresh directory to update the cloud placeholder status
                _directoryContents.InvalidateAll();
                ShowNotification("Download complete", NotificationKind.Success);
            }
            catch (Exception ex)
            {
                ShowNotification($"Download failed: {ex.Message}", NotificationKind.Error);
            }
        });
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

        ActionMenuLevel level = _actionMenuStack.Peek();
        List<ActionMenuItem> filtered = level.GetFilteredItems();
        int contentWidth = Math.Min(60, width - 8);
        int itemRows = Math.Min(filtered.Count, 18);
        int contentHeight = itemRows + 2; // 1 row for text input + 1 separator + item rows
        string footer = _actionMenuStack.Count > 1
            ? "[↑↓] Navigate  [Enter] Select  [Esc] Back"
            : "[↑↓] Navigate  [Enter] Select  [Esc] Cancel";

        Rect content = DialogBox.Render(
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

            ActionMenuItem item = filtered[itemIndex];
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

    // ── Context Menu ─────────────────────────────────────────────────────────────

    private ActionMenuItem[] BuildContextMenuItems()
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
            items.Add(new ActionMenuItem { Label = "Paste", Shortcut = "v", Action = AppAction.Paste });
        }

        items.Add(new ActionMenuItem { Label = "Copy path", Shortcut = "y", Action = AppAction.CopyAbsolutePath });
        items.Add(new ActionMenuItem { Label = "Properties", Shortcut = "i", Action = AppAction.ShowProperties });

        // Git stage/unstage — only when applicable
        if (_currentRepoRoot is not null && _gitStatuses is not null)
        {
            List<FileSystemEntry> entries = GetVisibleEntries();
            if (_selectedIndex < entries.Count)
            {
                if (HasStatusInSelection(GitFileStatus.Modified | GitFileStatus.Untracked))
                {
                    items.Add(new ActionMenuItem { Label = "Git: Stage", Action = AppAction.StageFile });
                }

                if (HasStatusInSelection(GitFileStatus.Staged))
                {
                    items.Add(new ActionMenuItem { Label = "Git: Unstage", Action = AppAction.UnstageFile });
                }
            }
        }

        return items.ToArray();
    }

    private void HandleContextMenuKey(KeyEvent key, PreviewLoader previewLoader, ScreenBuffer buffer, InputPipeline pipeline)
    {
        if (_contextMenuState is null)
        {
            _inputMode = InputMode.Normal;
            return;
        }

        switch (key.Key)
        {
            case ConsoleKey.Escape:
                _inputMode = InputMode.Normal;
                _contextMenuState = null;
                break;

            case ConsoleKey.Enter:
                ActionMenuItem selected = _contextMenuState.Items[_contextMenuState.SelectedIndex];
                _inputMode = InputMode.Normal;
                _contextMenuState = null;
                DispatchActionPaletteAction(selected.Action, selected.Data, previewLoader, buffer, pipeline);
                break;

            case ConsoleKey.UpArrow:
                _contextMenuState.MoveUp();
                break;

            case ConsoleKey.DownArrow:
                _contextMenuState.MoveDown();
                break;

            default:
                // Vim-style navigation
                if (key is { Key: ConsoleKey.K, Control: false, Alt: false })
                {
                    _contextMenuState.MoveUp();
                }
                else if (key is { Key: ConsoleKey.J, Control: false, Alt: false })
                {
                    _contextMenuState.MoveDown();
                }

                break;
        }
    }

    private void HandleContextMenuMouse(MouseEvent mouse, PreviewLoader previewLoader, ScreenBuffer buffer, InputPipeline pipeline)
    {
        if (_contextMenuState is null)
        {
            _inputMode = InputMode.Normal;
            return;
        }

        // Any click dismisses the context menu; clicks inside the menu also execute the item
        if (mouse.IsRelease || mouse.Button == MouseButton.ScrollUp || mouse.Button == MouseButton.ScrollDown)
        {
            return;
        }

        int screenWidth = _layout.StatusBar.Width;
        int screenHeight = _layout.StatusBar.Bottom;
        Rect menuRect = ContextMenuRenderer.GetMenuRect(screenWidth, screenHeight, _contextMenuState);

        // Content area is inside the border (top border = row 0, items start at row 1)
        int contentTop = menuRect.Top + 1;
        int contentBottom = menuRect.Top + 1 + _contextMenuState.Items.Length;

        if (mouse.Row >= contentTop && mouse.Row < contentBottom
                                    && mouse.Col >= menuRect.Left && mouse.Col < menuRect.Right)
        {
            // Click on a menu item — select and execute
            int itemIndex = mouse.Row - contentTop;
            if (itemIndex >= 0 && itemIndex < _contextMenuState.Items.Length)
            {
                ActionMenuItem item = _contextMenuState.Items[itemIndex];
                _inputMode = InputMode.Normal;
                _contextMenuState = null;
                DispatchActionPaletteAction(item.Action, item.Data, previewLoader, buffer, pipeline);
                return;
            }
        }

        // Click outside menu — dismiss
        _inputMode = InputMode.Normal;
        _contextMenuState = null;
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
        List<string> filtered = GetFilteredBookmarks();

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
        List<string> filtered = GetFilteredBookmarks();
        int contentWidth = Math.Min(70, width - 8);
        int itemRows = Math.Min(filtered.Count, 18);
        int contentHeight = itemRows + 2; // 1 row for text input + 1 separator + item rows
        const string Footer = "[↑↓] Navigate [Enter] Open [d] Remove [1-9] Jump  [B] Add/Remove  [Esc] Close";

        Rect content = DialogBox.Render(
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
                ? exists ? selectedStyle : dimSelectedStyle
                : exists
                    ? normalStyle
                    : dimStyle;

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
        _configDialogState = ConfigDialogState.FromConfig(_config);
    }

    private void HandleConfigKey(KeyEvent key, PreviewLoader previewLoader, ScreenBuffer buffer)
    {
        ConfigDialogState state = _configDialogState!;
        switch (key.Key)
        {
            case ConsoleKey.UpArrow or ConsoleKey.K:
                state.MoveUp();
                break;

            case ConsoleKey.DownArrow or ConsoleKey.J:
                state.MoveDown();
                break;

            case ConsoleKey.Spacebar:
                state.ToggleSelected();
                break;

            case ConsoleKey.Enter:
                ApplyConfigChanges(previewLoader, buffer);
                break;

            case ConsoleKey.Escape:
                _inputMode = InputMode.Normal;
                break;

            case ConsoleKey.LeftArrow or ConsoleKey.H:
                state.CyclePrevSelected();
                break;

            case ConsoleKey.RightArrow or ConsoleKey.L:
                state.CycleNextSelected();
                break;
        }
    }

    private void ApplyConfigChanges(PreviewLoader previewLoader, ScreenBuffer buffer)
    {
        _configDialogState!.ApplyTo(_config);

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

    private void RenderConfigDialog(ScreenBuffer buffer, int width, int height)
    {
        ConfigDialogState state = _configDialogState!;
        const int ContentWidth = 47;
        int contentHeight = state.Items.Count + 1;
        const string Footer = "[Space] Toggle [\u25c4\u25ba] Cycle [Enter] Save [Esc] Cancel";

        Rect content = DialogBox.Render(buffer, width, height, Math.Max(ContentWidth, Footer.Length), contentHeight, title: "Configuration",
            footer: Footer);

        var normalStyle = new CellStyle(new Color(200, 200, 200), DialogBox.BgColor);
        var selectedStyle = new CellStyle(new Color(20, 20, 35), new Color(200, 200, 200));
        var valueStyle = new CellStyle(new Color(100, 200, 255), DialogBox.BgColor);
        var valueSelectedStyle = new CellStyle(new Color(20, 20, 35), new Color(200, 200, 200));
        var disabledStyle = new CellStyle(new Color(80, 80, 80), DialogBox.BgColor);

        for (int i = 0; i < state.Items.Count; i++)
        {
            ConfigItem item = state.Items[i];
            bool selected = i == state.SelectedIndex;
            CellStyle style, vStyle;

            if (!item.IsEnabled)
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
            string label = item.Indent > 0
                ? new string(' ', item.Indent * 2) + item.Label
                : item.Label;

            const int labelWidth = 34;
            buffer.WriteString(row, content.Left, label, style, labelWidth);
            buffer.WriteString(row, content.Left + labelWidth, item.FormatValue(), vStyle, content.Width - labelWidth);
        }
    }

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
        _fileFinderSearchIndex = new Wade.Search.SearchIndex();
        _fileFinderLastSearchQuery = "";
        _fileFinderSearchResults = null;
        _fileFinderEntryCache = new Dictionary<string, FileSystemEntry>(StringComparer.Ordinal);
        _fileFinderScanning = true;
        _fileFinderCts?.Cancel();
        _fileFinderCts = new CancellationTokenSource();

        string basePath = _currentPath;
        bool showHidden = _directoryContents.ShowHiddenFiles;
        bool showSystem = _directoryContents.ShowSystemFiles;
        CancellationToken ct = _fileFinderCts.Token;
        var searchIndex = _fileFinderSearchIndex;

        Task.Run(() => ScanFilesForFinder(basePath, showHidden, showSystem, pipeline, ct, searchIndex), ct);
    }

    private void CloseFileFinder()
    {
        _fileFinderCts?.Cancel();
        _fileFinderCts = null;
        _inputMode = InputMode.Normal;
        _fileFinderInput = null;
        _fileFinderAllEntries = null;
        _fileFinderSearchIndex?.Dispose();
        _fileFinderSearchIndex = null;
        _fileFinderSearchResults = null;
        _fileFinderEntryCache = null;
        _fileFinderLastSearchQuery = "";
        _fileFinderScanning = false;
    }

    internal static void ScanFilesForFinder(
        string basePath,
        bool showHidden,
        bool showSystem,
        InputPipeline pipeline,
        CancellationToken ct,
        Wade.Search.SearchIndex? searchIndex = null)
    {
        const int maxEntries = 50_000;
        const int flushIntervalMs = 200;
        int totalCount = 0;
        var batch = new List<FileSystemEntry>();
        long lastFlushTicks = Environment.TickCount64;

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

        // BFS walk so current directory entries appear before deeper ones
        var queue = new Queue<string>();
        queue.Enqueue(basePath);

        while (queue.Count > 0 && totalCount < maxEntries)
        {
            if (ct.IsCancellationRequested)
            {
                return;
            }

            string currentDir = queue.Dequeue();

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

                        batch.Add(new FileSystemEntry(
                            fileInfo.Name,
                            fileInfo.FullName,
                            IsDirectory: false,
                            Size: fileInfo.Length,
                            LastModified: fileInfo.LastWriteTime,
                            LinkTarget: fileInfo.LinkTarget,
                            IsBrokenSymlink: false,
                            IsDrive: false));

                        searchIndex?.Add(fileInfo.FullName);
                        totalCount++;
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

            // Enqueue child directories for BFS traversal
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
                    DirectoryInfo? dirInfo = null;

                    if (!showSystem && OperatingSystem.IsWindows())
                    {
                        try
                        {
                            dirInfo = new DirectoryInfo(subDir);

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

                    // Add directory to results
                    try
                    {
                        dirInfo ??= new DirectoryInfo(subDir);

                        batch.Add(new FileSystemEntry(
                            dirInfo.Name,
                            dirInfo.FullName,
                            IsDirectory: true,
                            Size: 0,
                            LastModified: dirInfo.LastWriteTime,
                            LinkTarget: dirInfo.LinkTarget,
                            IsBrokenSymlink: false,
                            IsDrive: false));

                        searchIndex?.Add(dirInfo.FullName);
                        totalCount++;
                    }
                    catch
                    {
                        // Skip directories we can't access
                    }

                    queue.Enqueue(subDir);
                }
            }
            catch
            {
                // Skip if directory enumeration fails
            }

            // Flush batch periodically (time-based throttle)
            if (batch.Count > 0 && Environment.TickCount64 - lastFlushTicks >= flushIntervalMs)
            {
                if (ct.IsCancellationRequested)
                {
                    return;
                }

                pipeline.Inject(new FileFinderPartialResultEvent(basePath, batch));
                batch = new List<FileSystemEntry>();
                lastFlushTicks = Environment.TickCount64;
            }
        }

        if (!ct.IsCancellationRequested)
        {
            // Flush remaining entries
            if (batch.Count > 0)
            {
                pipeline.Inject(new FileFinderPartialResultEvent(basePath, batch));
            }

            pipeline.Inject(new FileFinderScanCompleteEvent(basePath));
        }
    }

    private List<FileSystemEntry> GetFinderDisplayEntries()
    {
        string filter = _fileFinderInput?.Value ?? "";

        if (string.IsNullOrEmpty(filter))
        {
            return _fileFinderAllEntries ?? [];
        }

        if (_fileFinderSearchResults is null)
        {
            return [];
        }

        // Convert SearchResults to FileSystemEntries on demand, caching each one.
        var entries = new List<FileSystemEntry>(_fileFinderSearchResults.Count);

        foreach (Wade.Search.SearchResult sr in _fileFinderSearchResults)
        {
            if (!_fileFinderEntryCache!.TryGetValue(sr.Path, out FileSystemEntry? entry))
            {
                entry = CreateFileSystemEntry(sr.Path);

                if (entry is null)
                {
                    continue;
                }

                _fileFinderEntryCache[sr.Path] = entry;
            }

            entries.Add(entry);
        }

        return entries;
    }

    private void HandleFileFinderSearchResult(FileFinderSearchResultEvent evt)
    {
        if (_inputMode == InputMode.FileFinder
            && evt.BasePath == _currentPath
            && evt.SearchId == _fileFinderSearchId
            && evt.Results.Count > 0)
        {
            _fileFinderSearchResults ??= new List<Wade.Search.SearchResult>();
            _fileFinderSearchResults.AddRange(evt.Results);
        }
    }

    private void StartFinderSearch(InputPipeline pipeline)
    {
        string query = _fileFinderInput?.Value ?? "";

        if (query == _fileFinderLastSearchQuery)
        {
            return;
        }

        _fileFinderLastSearchQuery = query;
        _fileFinderSearchResults = null;
        long searchId = ++_fileFinderSearchId;

        if (string.IsNullOrEmpty(query) || _fileFinderSearchIndex is null)
        {
            _fileFinderSearchIndex?.CancelSearch();
            return;
        }

        var reader = _fileFinderSearchIndex.Search(query);
        string basePath = _currentPath;

        Task.Run(async () =>
        {
            var batch = new List<Wade.Search.SearchResult>();
            long lastFlush = Environment.TickCount64;

            await foreach (Wade.Search.SearchResult result in reader.ReadAllAsync())
            {
                batch.Add(result);

                if (Environment.TickCount64 - lastFlush >= 100)
                {
                    pipeline.Inject(new FileFinderSearchResultEvent(basePath, batch, IsComplete: false, SearchId: searchId));
                    batch = new List<Wade.Search.SearchResult>();
                    lastFlush = Environment.TickCount64;
                }
            }

            // Final flush
            pipeline.Inject(new FileFinderSearchResultEvent(basePath, batch, IsComplete: true, SearchId: searchId));
        });
    }

    private static FileSystemEntry? CreateFileSystemEntry(string fullPath)
    {
        try
        {
            if (Directory.Exists(fullPath))
            {
                var di = new DirectoryInfo(fullPath);

                return new FileSystemEntry(
                    di.Name,
                    di.FullName,
                    IsDirectory: true,
                    Size: 0,
                    LastModified: di.LastWriteTime,
                    LinkTarget: di.LinkTarget,
                    IsBrokenSymlink: false,
                    IsDrive: false);
            }

            if (File.Exists(fullPath))
            {
                var fi = new FileInfo(fullPath);

                return new FileSystemEntry(
                    fi.Name,
                    fi.FullName,
                    IsDirectory: false,
                    Size: fi.Length,
                    LastModified: fi.LastWriteTime,
                    LinkTarget: fi.LinkTarget,
                    IsBrokenSymlink: false,
                    IsDrive: false);
            }
        }
        catch
        {
            // Skip inaccessible paths
        }

        return null;
    }

    private void HandleFileFinderKey(KeyEvent key, PreviewLoader previewLoader, ScreenBuffer buffer, InputPipeline pipeline)
    {
        List<FileSystemEntry> filtered = GetFinderDisplayEntries();

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
                StartFinderSearch(pipeline);
                break;

            case ConsoleKey.Delete:
                _fileFinderInput!.DeleteForward();
                _fileFinderSelectedIndex = 0;
                _fileFinderScrollOffset = 0;
                StartFinderSearch(pipeline);
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
                    StartFinderSearch(pipeline);
                }

                break;
        }

        // Adjust scroll offset to keep selection visible
        filtered = GetFinderDisplayEntries();

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
        List<FileSystemEntry> filtered = GetFinderDisplayEntries();
        int contentWidth = Math.Min(70, width - 8);
        int itemRows = Math.Min(filtered.Count, 18);
        int contentHeight = itemRows + 2; // 1 row for text input + 1 separator + item rows
        const string Footer = "[↑↓] Navigate  [Enter] Open  [Esc] Cancel";
        string title = _fileFinderScanning && _fileFinderAllEntries != null ? "Find File [scanning...]" : "Find File";

        Rect content = DialogBox.Render(
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
            string emptyText = _fileFinderAllEntries == null ? "" : "No matches";
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

            // Left: icon + filename
            Rune icon = FileIcons.GetIcon(entry);
            buffer.Put(row, content.Left + 1, icon, nameStyle);
            buffer.WriteString(row, content.Left + 3, entry.Name, nameStyle, content.Width / 2 - 3);

            // Right: relative parent path (dim hint)
            string? parentDir = Path.GetDirectoryName(Path.GetRelativePath(_currentPath, entry.FullPath));
            string parentHint = string.IsNullOrEmpty(parentDir) ? "." : parentDir + Path.DirectorySeparatorChar;
            int hintMaxLen = content.Width / 2 - 1;
            int hintCol = content.Left + content.Width - Math.Min(parentHint.Length, hintMaxLen) - 1;
            buffer.WriteString(row, hintCol, parentHint, pathStyle, hintMaxLen);
        }
    }
}
