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

    private string? _cachedPreviewPath;
    private StyledLine[]? _cachedStyledLines;
    private string? _cachedPreviewFileTypeLabel;
    private string? _cachedPreviewEncoding;
    private string? _cachedPreviewLineEnding;
    private bool _previewLoading;
    private string? _pendingPreviewPath;
    private PreviewLoader? _previewLoader;

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

        using var terminal = new TerminalSetup();
        using var inputSource = InputPipeline.CreatePlatformSource();
        using var pipeline = new InputPipeline(inputSource);
        _previewLoader = new PreviewLoader(pipeline);
        var previewLoader = _previewLoader;

        int lastWidth = Console.WindowWidth;
        int lastHeight = Console.WindowHeight;

        var buffer = new ScreenBuffer(lastWidth, lastHeight);
        _layout.Calculate(lastWidth, lastHeight);

        bool quit = false;

        while (!quit)
        {
            // Render
            buffer.Clear();
            Render(buffer);
            buffer.Flush(_flushBuffer);

            // Wait for next input event
            var inputEvent = pipeline.Take();

            // Drain any additional queued events (e.g. rapid key repeats)
            while (pipeline.TryTake(out var extra))
            {
                if (extra is PreviewReadyEvent previewReady)
                {
                    HandlePreviewReady(previewReady);
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
                    Console.Write(AnsiCodes.ClearScreen);
                }
                continue;
            }

            // Handle preview ready events
            if (inputEvent is PreviewReadyEvent previewEvt)
            {
                HandlePreviewReady(previewEvt);
                continue;
            }

            // Handle mouse events
            if (inputEvent is MouseEvent mouseEvent)
            {
                var mouseEntries = _directoryContents.GetEntries(_currentPath);
                HandleMouseEvent(mouseEvent, mouseEntries, previewLoader);

                // Clamp and adjust scroll after mouse handling
                var currentAfterMouse = _directoryContents.GetEntries(_currentPath);
                if (currentAfterMouse.Count > 0)
                    _selectedIndex = Math.Clamp(_selectedIndex, 0, currentAfterMouse.Count - 1);
                else
                    _selectedIndex = 0;
                AdjustScroll(_layout.CenterPane.Height);
                continue;
            }

            // Handle key events
            if (inputEvent is not KeyEvent keyEvent)
                continue;

            var action = InputReader.MapKey(keyEvent);

            if (_showHelp)
            {
                _showHelp = false;
                continue;
            }

            var entries = _directoryContents.GetEntries(_currentPath);

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
                        ClearPreviewCache(previewLoader);
                    }
                    break;

                case AppAction.Back:
                {
                    if (_currentPath == DirectoryContents.DrivesPath)
                        break; // Already at the top level

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
                    ClearPreviewCache(previewLoader);
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
                    quit = true;
                    break;

                case AppAction.ShowHelp:
                    _showHelp = true;
                    break;
            }

            // Clamp selection
            var currentEntries = _directoryContents.GetEntries(_currentPath);
            if (currentEntries.Count > 0)
                _selectedIndex = Math.Clamp(_selectedIndex, 0, currentEntries.Count - 1);
            else
                _selectedIndex = 0;

            // Adjust scroll offset to keep selection visible
            AdjustScroll(_layout.CenterPane.Height);
        }
    }

    private void Render(ScreenBuffer buffer)
    {
        int width = buffer.Width;
        int height = buffer.Height;

        // Current directory entries
        var entries = _directoryContents.GetEntries(_currentPath);

        // Center pane: current directory
        PaneRenderer.RenderFileList(buffer, _layout.CenterPane, entries, _selectedIndex, _scrollOffset, isActive: true, showIcons: _config.ShowIconsEnabled);

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
        StatusBar.Render(buffer, _layout.StatusBar, displayPath, entries.Count, _selectedIndex, selectedEntry, _cachedPreviewFileTypeLabel, _cachedPreviewEncoding, _cachedPreviewLineEnding);

        // Help overlay
        if (_showHelp)
            HelpOverlay.Render(buffer, width, height);
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
    }

    private void ClearPreviewCache(PreviewLoader loader)
    {
        loader.Cancel();
        _cachedPreviewPath = null;
        _cachedStyledLines = null;
        _cachedPreviewFileTypeLabel = null;
        _cachedPreviewEncoding = null;
        _cachedPreviewLineEnding = null;
        _previewLoading = false;
        _pendingPreviewPath = null;
    }

    private void HandleMouseEvent(MouseEvent mouse, List<FileSystemEntry> entries, PreviewLoader previewLoader)
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
                    ClearPreviewCache(previewLoader);
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
                        ClearPreviewCache(previewLoader);
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
                            ClearPreviewCache(previewLoader);
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
                            ClearPreviewCache(previewLoader);
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
}
