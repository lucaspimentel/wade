namespace Wade.Imaging;

internal interface IPdfTool
{
    public bool IsAvailable { get; }

    public string? RenderPage(string pdfPath, int pageNumber, CancellationToken ct);
    // Returns path to temp PNG file, or null on failure
}
