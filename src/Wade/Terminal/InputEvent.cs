using Wade.FileSystem;
using Wade.Highlighting;

namespace Wade.Terminal;

internal abstract record InputEvent;

internal sealed record KeyEvent(ConsoleKey Key, char KeyChar, bool Shift, bool Alt, bool Control) : InputEvent
{
    public bool IsModifierOnly => Key is (ConsoleKey)16   // VK_SHIFT
                                      or (ConsoleKey)17   // VK_CONTROL
                                      or (ConsoleKey)18;  // VK_MENU (Alt)
}

internal sealed record MouseEvent(MouseButton Button, int Row, int Col, bool IsRelease) : InputEvent;

internal sealed record ResizeEvent(int Width, int Height) : InputEvent;

internal sealed record PreviewReadyEvent(
    string Path,
    StyledLine[] StyledLines,
    string? FileTypeLabel,
    string? Encoding,
    string? LineEnding,
    bool IsRendered = false) : InputEvent;

internal sealed record ImagePreviewReadyEvent(
    string Path,
    string SixelData,
    int PixelWidth,
    int PixelHeight,
    string FileTypeLabel) : InputEvent;

internal sealed record DirectorySizeReadyEvent(string Path, long TotalBytes) : InputEvent;

internal sealed record FileFinderScanCompleteEvent(string BasePath, List<FileSystemEntry> Entries) : InputEvent;

internal sealed record GitStatusReadyEvent(
    string RepoRoot,
    string? BranchName,
    Dictionary<string, GitFileStatus>? Statuses) : InputEvent;

internal enum MouseButton
{
    Left,
    Middle,
    Right,
    ScrollUp,
    ScrollDown,
    None,
}
