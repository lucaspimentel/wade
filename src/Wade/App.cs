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
    private bool _showHelp;

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

    // Search/filter state
    private TextInput? _searchInput;
    private string _searchFilter = "";
    private List<FileSystemEntry>? _filteredEntries;

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
    private bool _sixelPending;

    // Track selected index per directory so we restore position when navigating back
    private readonly Dictionary<string, int> _selectedIndexPerDir = new(StringComparer.OrdinalIgnoreCase);

    // Left pane state cached during Render for mouse hit-testing
    private List<FileSystemEntry>? _leftPaneEntries;
    private int _leftPaneScroll;
    private int _leftPaneSelected;

    public App(WadeConfig config)
    {
        _config = config;
    }

    public void Run()
    {
        _currentPath = Path.GetFullPath(_config.StartPath);
        _directoryContents.ShowHiddenFiles = _config.ShowHiddenFiles;
        _directoryContents.SortMode = _config.SortMode.ToLowerInvariant() switch
        {
            "modified" => SortMode.Modified,
            "size" => SortMode.Size,
            "extension" => SortMode.Extension,
            _ => SortMode.Name,
        };
        _directoryContents.SortAscending = _config.SortAscending;

        using var terminal = new TerminalSetup();
        using var inputSource = InputPipeline.CreatePlatformSource();
        using var pipeline = new InputPipeline(inputSource);
        _previewLoader = new PreviewLoader(pipeline);
        var previewLoader = _previewLoader;

        int lastWidth = Console.WindowWidth;
        int lastHeight = Console.WindowHeight;

        var buffer = new ScreenBuffer(lastWidth, lastHeight);
        _layout.Calculate(lastWidth, lastHeight);
        previewLoader.Configure(_config.ImagePreviewsEnabled, _layout.RightPane.Width, _layout.RightPane.Height);

        bool quit = false;

        while (!quit)
        {
            // Auto-clear expired notifications
            if (_notification is { } notif && notif.IsExpired(Environment.TickCount64))
                _notification = null;

            // Render
            buffer.Clear();
            Render(buffer);
            buffer.Flush(_flushBuffer);

            // Write Sixel data after flush (bypasses cell grid)
            if (_sixelPending && _cachedSixelData is not null)
            {
                _sixelPending = false;
                var sixelPane = _inputMode == InputMode.ExpandedPreview ? _layout.ExpandedPane : _layout.RightPane;
                int cursorRow = sixelPane.Top;
                int cursorCol = sixelPane.Left;

                if (_inputMode == InputMode.ExpandedPreview && _cachedImagePixelWidth > 0 && _cachedImagePixelHeight > 0)
                {
                    (cursorRow, cursorCol) = sixelPane.CenterContent(_cachedImagePixelWidth / 8, _cachedImagePixelHeight / 16);
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
                        buffer.ForceFullRedraw();
                }
                else if (extra is ImagePreviewReadyEvent imagePreviewReady)
                {
                    bool wasImage = _isImagePreview;
                    HandleImagePreviewReady(imagePreviewReady);
                    if (wasImage)
                        buffer.ForceFullRedraw();
                }
                else if (extra is ResizeEvent)
                    inputEvent = extra;
                else if (extra is KeyEvent or MouseEvent)
                    inputEvent = extra;
            }

            // Handle resize events
            if (inputEvent is ResizeEvent resize)
            {
                if (resize.Width != lastWidth || resize.Height != lastHeight)
                {
                    lastWidth = resize.Width;
                    lastHeight = resize.Height;
                    buffer.Resize(lastWidth, lastHeight);
                    _layout.Calculate(lastWidth, lastHeight);
                    var resizePane = _inputMode == InputMode.ExpandedPreview ? _layout.ExpandedPane : _layout.RightPane;
                    previewLoader.Configure(_config.ImagePreviewsEnabled, resizePane.Width, resizePane.Height);
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
                }
                continue;
            }

            // Handle preview ready events
            if (inputEvent is PreviewReadyEvent previewEvt)
            {
                bool wasImage = _isImagePreview;
                HandlePreviewReady(previewEvt);
                if (wasImage)
                    buffer.ForceFullRedraw();
                continue;
            }

            if (inputEvent is ImagePreviewReadyEvent imagePreviewEvt)
            {
                bool wasImage = _isImagePreview;
                HandleImagePreviewReady(imagePreviewEvt);
                if (wasImage)
                    buffer.ForceFullRedraw();
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

                var mouseEntries = GetVisibleEntries();
                HandleMouseEvent(mouseEvent, mouseEntries, previewLoader, buffer);

                // Clamp and adjust scroll after mouse handling
                var currentAfterMouse = GetVisibleEntries();
                if (currentAfterMouse.Count > 0)
                    _selectedIndex = Math.Clamp(_selectedIndex, 0, currentAfterMouse.Count - 1);
                else
                    _selectedIndex = 0;
                AdjustScroll(VisibleFileListHeight);
                continue;
            }

            // Handle key events
            if (inputEvent is not KeyEvent keyEvent)
                continue;

            if (_showHelp)
            {
                _showHelp = false;
                continue;
            }

            // Modal input dispatch — consume all keys when in a modal mode
            switch (_inputMode)
            {
                case InputMode.Search:
                    HandleSearchKey(keyEvent);
                    var searchEntries = GetVisibleEntries();
                    if (searchEntries.Count > 0)
                        _selectedIndex = Math.Clamp(_selectedIndex, 0, searchEntries.Count - 1);
                    else
                        _selectedIndex = 0;
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
                        _selectedIndex--;
                    break;

                case AppAction.NavigateDown:
                    if (_selectedIndex < entries.Count - 1)
                        _selectedIndex++;
                    break;

                case AppAction.Open:
                    if (entries.Count > 0 && entries[_selectedIndex].IsDirectory)
                    {
                        _selectedIndexPerDir[_currentPath] = _selectedIndex;
                        _currentPath = entries[_selectedIndex].FullPath;
                        _selectedIndex = _selectedIndexPerDir.GetValueOrDefault(_currentPath, 0);
                        _scrollOffset = 0;
                        _notification = null;
                        _markedPaths.Clear();
                        ClearSearchFilter();
                        ClearPreviewCache(previewLoader, buffer);
                    }
                    else if (entries.Count > 0 && !entries[_selectedIndex].IsDirectory)
                    {
                        EnterExpandedPreview(previewLoader, buffer);
                    }
                    break;

                case AppAction.Back:
                {
                    if (_currentPath == DirectoryContents.DrivesPath)
                        break; // Already at the top level

                    _notification = null;
                    _markedPaths.Clear();
                    ClearSearchFilter();
                    _selectedIndexPerDir[_currentPath] = _selectedIndex;
                    string oldPath = _currentPath;

                    if (DirectoryContents.IsDriveRoot(_currentPath))
                    {
                        // Go up to the drives list
                        _currentPath = DirectoryContents.DrivesPath;
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
                            _currentPath = parent.FullName;
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

                case AppAction.Search:
                    _inputMode = InputMode.Search;
                    _searchInput = new TextInput(_searchFilter);
                    break;

                case AppAction.ShowHelp:
                    _showHelp = true;
                    break;

                case AppAction.Refresh:
                    _notification = null;
                    _markedPaths.Clear();
                    ClearSearchFilter();
                    _directoryContents.InvalidateAll();
                    ClearPreviewCache(previewLoader, buffer);
                    buffer.ForceFullRedraw();
                    break;

                case AppAction.ToggleMark:
                    if (entries.Count > 0 && _selectedIndex < entries.Count)
                    {
                        string path = entries[_selectedIndex].FullPath;
                        if (!_markedPaths.Remove(path))
                            _markedPaths.Add(path);
                        if (_selectedIndex < entries.Count - 1)
                            _selectedIndex++;
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
                    ShowNotification($"Sort: {FormatSortMode(_directoryContents.SortMode)}");
                    break;

                case AppAction.ToggleSortDirection:
                    _directoryContents.SortAscending = !_directoryContents.SortAscending;
                    _directoryContents.InvalidateAll();
                    ClearPreviewCache(previewLoader, buffer);
                    ShowNotification($"Sort: {(_directoryContents.SortAscending ? "ascending" : "descending")}");
                    break;
            }

            // Clamp selection
            var currentEntries = GetVisibleEntries();
            if (currentEntries.Count > 0)
                _selectedIndex = Math.Clamp(_selectedIndex, 0, currentEntries.Count - 1);
            else
                _selectedIndex = 0;

            // Adjust scroll offset to keep selection visible
            AdjustScroll(VisibleFileListHeight);
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
        PaneRenderer.RenderFileList(buffer, fileListPane, entries, _selectedIndex, _scrollOffset, isActive: true, showIcons: _config.ShowIconsEnabled, showDetails: true, markedPaths: _markedPaths);

        // Search bar at bottom of center pane
        if (showSearchBar)
            RenderSearchBar(buffer);

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
            if (parentSelected < 0) parentSelected = 0;

            int parentScroll = CalculateScroll(parentSelected, _layout.LeftPane.Height, parentEntries.Count);
            PaneRenderer.RenderFileList(buffer, _layout.LeftPane, parentEntries, parentSelected, parentScroll, isActive: false, showIcons: _config.ShowIconsEnabled);

            // Cache for mouse hit-testing
            _leftPaneEntries = parentEntries;
            _leftPaneScroll = parentScroll;
            _leftPaneSelected = parentSelected;
        }

        // Right pane: preview
        if (entries.Count > 0 && _selectedIndex < entries.Count)
        {
            var selected = entries[_selectedIndex];
            if (selected.IsDirectory)
            {
                var previewEntries = _directoryContents.GetEntries(selected.FullPath);
                if (previewEntries.Count > 0)
                    PaneRenderer.RenderFileList(buffer, _layout.RightPane, previewEntries, -1, 0, isActive: false, showIcons: _config.ShowIconsEnabled);
                else
                    PaneRenderer.RenderMessage(buffer, _layout.RightPane, "[empty directory]");
            }
            else
            {
                if (selected.FullPath != _cachedPreviewPath && selected.FullPath != _pendingPreviewPath)
                {
                    _pendingPreviewPath = selected.FullPath;
                    _previewLoading = true;
                    _previewLoader!.BeginLoad(selected.FullPath);
                }

                if (_previewLoading)
                {
                    PaneRenderer.RenderMessage(buffer, _layout.RightPane, "[loading\u2026]");
                }
                else if (_isImagePreview && _cachedSixelData is not null)
                {
                    // Fill right pane with spaces so ScreenBuffer claims the area
                    for (int row = _layout.RightPane.Top; row < _layout.RightPane.Bottom; row++)
                        buffer.FillRow(row, _layout.RightPane.Left, _layout.RightPane.Width, ' ', CellStyle.Default);
                    _sixelPending = true;
                }
                else if (_cachedStyledLines is not null)
                {
                    PaneRenderer.RenderPreview(buffer, _layout.RightPane, _cachedStyledLines);
                }
            }
        }

        // Borders
        PaneRenderer.RenderBorders(buffer, _layout, height);

        // Status bar
        FileSystemEntry? selectedEntry = entries.Count > 0 && _selectedIndex < entries.Count
            ? entries[_selectedIndex]
            : null;
        string displayPath = _currentPath == DirectoryContents.DrivesPath ? "Drives" : _currentPath;
        StatusBar.Render(buffer, _layout.StatusBar, displayPath, entries.Count, _selectedIndex, selectedEntry, _cachedPreviewFileTypeLabel, _cachedPreviewEncoding, _cachedPreviewLineEnding, _notification, _markedPaths.Count, _directoryContents.SortMode, _directoryContents.SortAscending);

        // Help overlay
        if (_showHelp)
            HelpOverlay.Render(buffer, width, height);

        // Modal overlays (render last, on top)
        switch (_inputMode)
        {
            case InputMode.Confirm:
                RenderConfirmDialog(buffer, width, height);
                break;
            case InputMode.TextInput:
                RenderTextInputDialog(buffer, width, height);
                break;
        }
    }

    private void AdjustScroll(int visibleHeight)
    {
        if (_selectedIndex < _scrollOffset)
            _scrollOffset = _selectedIndex;
        else if (_selectedIndex >= _scrollOffset + visibleHeight)
            _scrollOffset = _selectedIndex - visibleHeight + 1;
    }

    private void HandlePreviewReady(PreviewReadyEvent evt)
    {
        if (evt.Path != _pendingPreviewPath)
            return;

        _cachedPreviewPath = evt.Path;
        _cachedStyledLines = evt.StyledLines;
        _cachedPreviewFileTypeLabel = evt.FileTypeLabel;
        _cachedPreviewEncoding = evt.Encoding;
        _cachedPreviewLineEnding = evt.LineEnding;
        _previewLoading = false;
        _isImagePreview = false;
        _cachedSixelData = null;
        _cachedImagePath = null;
    }

    private void HandleImagePreviewReady(ImagePreviewReadyEvent evt)
    {
        if (evt.Path != _pendingPreviewPath)
            return;

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
        _sixelPending = false;

        if (wasImage)
            buffer?.ForceFullRedraw();
    }

    private void HandleMouseEvent(MouseEvent mouse, List<FileSystemEntry> entries, PreviewLoader previewLoader, ScreenBuffer buffer)
    {
        // Scroll wheel → move selection up/down in center pane
        if (mouse.Button == MouseButton.ScrollUp)
        {
            if (_selectedIndex > 0)
                _selectedIndex--;
            return;
        }

        if (mouse.Button == MouseButton.ScrollDown)
        {
            if (_selectedIndex < entries.Count - 1)
                _selectedIndex++;
            return;
        }

        // Ignore releases and non-left-click
        if (mouse.IsRelease || mouse.Button != MouseButton.Left)
            return;

        // Hit-test: which pane was clicked?
        int row = mouse.Row;
        int col = mouse.Col;

        if (HitTestPane(_layout.CenterPane, row, col))
        {
            // Center pane click — select the entry (same as arrow keys)
            int entryIndex = _scrollOffset + (row - _layout.CenterPane.Top);
            if (entryIndex >= 0 && entryIndex < entries.Count)
                _selectedIndex = entryIndex;
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
                    _currentPath = clicked.FullPath;
                    _selectedIndex = _selectedIndexPerDir.GetValueOrDefault(_currentPath, 0);
                    _scrollOffset = 0;
                    _markedPaths.Clear();
                    ClearSearchFilter();
                    ClearPreviewCache(previewLoader, buffer);
                }
                else
                {
                    // File in parent dir — navigate to parent, select file
                    _selectedIndexPerDir[_currentPath] = _selectedIndex;
                    var parentDir = Directory.GetParent(_currentPath);
                    if (parentDir is not null)
                    {
                        _currentPath = parentDir.FullName;
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
                            _currentPath = clicked.FullPath;
                            _selectedIndex = _selectedIndexPerDir.GetValueOrDefault(_currentPath, 0);
                            _scrollOffset = 0;
                            _markedPaths.Clear();
                            ClearSearchFilter();
                            ClearPreviewCache(previewLoader, buffer);
                        }
                        else
                        {
                            // File in previewed directory — navigate there, select the file
                            _selectedIndexPerDir[_currentPath] = _selectedIndex;
                            _currentPath = selected.FullPath;
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
        if (totalCount <= visibleHeight) return 0;
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
            previewLoader.Configure(_config.ImagePreviewsEnabled, _layout.ExpandedPane.Width, _layout.ExpandedPane.Height);
            _cachedSixelData = null;
            _sixelPending = false;
            _pendingPreviewPath = _cachedImagePath;
            _previewLoading = true;
            previewLoader.BeginLoad(_cachedImagePath);
        }

        buffer.ForceFullRedraw();
    }

    private void LeaveExpandedPreview(PreviewLoader previewLoader, ScreenBuffer buffer)
    {
        _inputMode = InputMode.Normal;
        _expandedPreviewScrollOffset = 0;

        previewLoader.Configure(_config.ImagePreviewsEnabled, _layout.RightPane.Width, _layout.RightPane.Height);

        if (_isImagePreview && _cachedImagePath is not null)
        {
            _cachedSixelData = null;
            _sixelPending = false;
            _pendingPreviewPath = _cachedImagePath;
            _previewLoading = true;
            previewLoader.BeginLoad(_cachedImagePath);
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
                    _expandedPreviewScrollOffset--;
                break;

            case ConsoleKey.DownArrow:
            case ConsoleKey.J:
                if (_cachedStyledLines is not null)
                {
                    int maxScroll = Math.Max(0, _cachedStyledLines.Length - _layout.ExpandedPane.Height);
                    if (_expandedPreviewScrollOffset < maxScroll)
                        _expandedPreviewScrollOffset++;
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
                    _expandedPreviewScrollOffset = Math.Max(0, _cachedStyledLines.Length - _layout.ExpandedPane.Height);
                break;
        }
    }

    private void HandleExpandedPreviewMouse(MouseEvent mouse)
    {
        if (mouse.Button == MouseButton.ScrollUp)
        {
            if (_expandedPreviewScrollOffset > 0)
                _expandedPreviewScrollOffset--;
        }
        else if (mouse.Button == MouseButton.ScrollDown)
        {
            if (_cachedStyledLines is not null)
            {
                int maxScroll = Math.Max(0, _cachedStyledLines.Length - _layout.ExpandedPane.Height);
                if (_expandedPreviewScrollOffset < maxScroll)
                    _expandedPreviewScrollOffset++;
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
                buffer.FillRow(row, pane.Left, pane.Width, ' ', CellStyle.Default);
            _sixelPending = true;
        }
        else if (_cachedStyledLines is not null)
        {
            PaneRenderer.RenderPreview(buffer, pane, _cachedStyledLines, _expandedPreviewScrollOffset);
        }

        // Status bar
        var entries = GetVisibleEntries();
        FileSystemEntry? selectedEntry = entries.Count > 0 && _selectedIndex < entries.Count
            ? entries[_selectedIndex]
            : null;
        string displayPath = _currentPath == DirectoryContents.DrivesPath ? "Drives" : _currentPath;
        StatusBar.Render(buffer, _layout.StatusBar, displayPath, entries.Count, _selectedIndex, selectedEntry, _cachedPreviewFileTypeLabel, _cachedPreviewEncoding, _cachedPreviewLineEnding, _notification, _markedPaths.Count, _directoryContents.SortMode, _directoryContents.SortAscending);
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
                    _activeTextInput!.InsertChar(key.KeyChar);
                break;
        }
    }

    private void HandleConfirmKey(KeyEvent key)
    {
        switch (key.Key)
        {
            case ConsoleKey.Y:
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

    // ── Modal entry points ──────────────────────────────────────────────────

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

    private static string FormatSortMode(SortMode mode) => mode switch
    {
        SortMode.Modified => "time",
        SortMode.Size => "size",
        SortMode.Extension => "ext",
        _ => "name",
    };

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
            _inputMode = InputMode.Normal;
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
                    _selectedIndex--;
                break;

            case ConsoleKey.DownArrow:
            {
                var entries = GetVisibleEntries();
                if (_selectedIndex < entries.Count - 1)
                    _selectedIndex++;
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
        string footer = "[Y] Yes  [N] No";
        int contentWidth = Math.Max(message.Length, footer.Length) + 2;
        int contentHeight = 1; // single line for the message

        Rect content = DialogBox.Render(buffer, width, height, contentWidth, contentHeight, title: _confirmTitle, footer: footer);

        var textStyle = new CellStyle(new Color(200, 200, 200), DialogBox.BgColor);
        int msgCol = content.Left + (content.Width - message.Length) / 2;
        buffer.WriteString(content.Top, msgCol, message, textStyle);
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
}
