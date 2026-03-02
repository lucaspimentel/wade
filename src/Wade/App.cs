using System.Text;
using Wade.FileSystem;
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
    private string[]? _cachedPreviewLines;

    // Track selected index per directory so we restore position when navigating back
    private readonly Dictionary<string, int> _selectedIndexPerDir = new(StringComparer.OrdinalIgnoreCase);

    public App(WadeConfig config)
    {
        _config = config;
    }

    public void Run()
    {
        _currentPath = Path.GetFullPath(_config.StartPath);

        using var terminal = new TerminalSetup();

        int lastWidth = Console.WindowWidth;
        int lastHeight = Console.WindowHeight;

        var buffer = new ScreenBuffer(lastWidth, lastHeight);
        _layout.Calculate(lastWidth, lastHeight);

        bool quit = false;

        while (!quit)
        {
            // Check for terminal resize
            int width = Console.WindowWidth;
            int height = Console.WindowHeight;
            if (width != lastWidth || height != lastHeight)
            {
                lastWidth = width;
                lastHeight = height;
                buffer.Resize(width, height);
                _layout.Calculate(width, height);
            }

            // Render
            buffer.Clear();
            Render(buffer);
            buffer.Flush(_flushBuffer);

            // Input
            var action = InputReader.Read();

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
                        _cachedPreviewPath = null;
                        _cachedPreviewLines = null;
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
                    _cachedPreviewPath = null;
                    _cachedPreviewLines = null;
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
                if (selected.FullPath != _cachedPreviewPath)
                {
                    _cachedPreviewPath = selected.FullPath;
                    _cachedPreviewLines = FilePreview.GetPreviewLines(selected.FullPath);
                }
                PaneRenderer.RenderPreview(buffer, _layout.RightPane, _cachedPreviewLines!);
            }
        }

        // Borders
        PaneRenderer.RenderBorders(buffer, _layout, height);

        // Status bar
        FileSystemEntry? selectedEntry = entries.Count > 0 && _selectedIndex < entries.Count
            ? entries[_selectedIndex]
            : null;
        string displayPath = _currentPath == DirectoryContents.DrivesPath ? "Drives" : _currentPath;
        StatusBar.Render(buffer, _layout.StatusBar, displayPath, entries.Count, _selectedIndex, selectedEntry);

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

    private static int CalculateScroll(int selectedIndex, int visibleHeight, int totalCount)
    {
        if (totalCount <= visibleHeight) return 0;
        int scroll = selectedIndex - visibleHeight / 2;
        return Math.Clamp(scroll, 0, totalCount - visibleHeight);
    }
}
