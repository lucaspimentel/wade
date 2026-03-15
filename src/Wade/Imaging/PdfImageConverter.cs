namespace Wade.Imaging;

internal sealed class PdfImageConverter : IImageConverter
{
    private static readonly IPdfTool[] s_tools = [new XpdfPdfTool()];
    private static readonly Lazy<IPdfTool?> s_availableTool = new(FindAvailableTool);

    public bool CanConvert(string path) =>
        Path.GetExtension(path).Equals(".pdf", StringComparison.OrdinalIgnoreCase)
        && s_availableTool.Value is not null;

    public string? Convert(string path, CancellationToken ct) =>
        s_availableTool.Value?.RenderPage(path, pageNumber: 1, ct);

    private static IPdfTool? FindAvailableTool() =>
        s_tools.FirstOrDefault(t => t.IsAvailable);
}
