namespace Wade.Imaging;

internal interface IPdfTool
{
    bool IsAvailable { get; }
    string? RenderPage(string pdfPath, int pageNumber, CancellationToken ct);
    // Returns path to temp PNG file, or null on failure
}
