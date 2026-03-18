using Wade.FileSystem;
using Wade.Highlighting;

namespace Wade.Preview;

internal interface IPreviewProvider
{
    string Label { get; }

    bool CanPreview(string path, PreviewContext context);

    PreviewResult? GetPreview(string path, PreviewContext context, CancellationToken ct);
}

internal record PreviewContext(
    int PaneWidthCells,
    int PaneHeightCells,
    int CellPixelWidth,
    int CellPixelHeight,
    bool IsCloudPlaceholder,
    bool IsBrokenSymlink,
    GitFileStatus? GitStatus,
    string? RepoRoot,
    HashSet<string> DisabledTools,
    bool ZipPreviewEnabled,
    bool ImagePreviewsEnabled,
    bool SixelSupported,
    bool ArchiveMetadataEnabled);

internal record PreviewResult
{
    public StyledLine[]? TextLines { get; init; }

    public string? SixelData { get; init; }
    public int SixelPixelWidth { get; init; }
    public int SixelPixelHeight { get; init; }

    public string? FileTypeLabel { get; init; }
    public string? Encoding { get; init; }
    public string? LineEnding { get; init; }

    /// <summary>
    /// When true, line numbers are suppressed in the preview pane
    /// (used for rendered previews like zip contents, hex dump, diff).
    /// </summary>
    public bool IsRendered { get; init; }

    /// <summary>
    /// When true, the preview is a placeholder message (e.g. "[binary file]", "[empty file]")
    /// rather than real content. Used to suppress split layout when metadata is present.
    /// </summary>
    public bool IsPlaceholder { get; init; }
}
