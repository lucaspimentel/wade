namespace Wade.Imaging;

internal static class ImageConverter
{
    private static readonly IImageConverter[] s_converters = [new PdfImageConverter()];

    public static bool CanConvert(string path) =>
        s_converters.Any(c => c.CanConvert(path));

    public static string? ConvertToImage(string path, CancellationToken ct) =>
        s_converters.FirstOrDefault(c => c.CanConvert(path))?.Convert(path, ct);
}
