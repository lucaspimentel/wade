using Wade.FileSystem;
using Wade.Highlighting;
using Wade.Preview;

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
    bool IsRendered = false,
    bool IsPlaceholder = false) : InputEvent;

internal sealed record ImagePreviewReadyEvent(
    string Path,
    string SixelData,
    int PixelWidth,
    int PixelHeight,
    string FileTypeLabel) : InputEvent;

internal sealed record CombinedPreviewReadyEvent(
    string Path,
    StyledLine[] StyledLines,
    string SixelData,
    int PixelWidth,
    int PixelHeight,
    string? FileTypeLabel,
    string? Encoding,
    string? LineEnding,
    bool IsRendered = false) : InputEvent;

internal sealed record MetadataReadyEvent(
    string Path,
    MetadataSection[] Sections,
    string? FileTypeLabel) : InputEvent;

internal sealed record PreviewLoadingCompleteEvent(string Path) : InputEvent;

internal sealed record DirectorySizeReadyEvent(string Path, long TotalBytes) : InputEvent;

internal sealed record FileFinderScanCompleteEvent(string BasePath, List<FileSystemEntry> Entries) : InputEvent;

internal sealed record GitStatusReadyEvent(
    string RepoRoot,
    string? BranchName,
    Dictionary<string, GitFileStatus>? Statuses,
    int AheadCount = 0,
    int BehindCount = 0) : InputEvent;

internal sealed record GitActionCompleteEvent(bool Success, string? ErrorMessage) : InputEvent;

internal sealed record FileSystemChangedEvent(string DirectoryPath, bool FullRefresh = false) : InputEvent;

internal enum MouseButton
{
    Left,
    Middle,
    Right,
    ScrollUp,
    ScrollDown,
    None,
}
