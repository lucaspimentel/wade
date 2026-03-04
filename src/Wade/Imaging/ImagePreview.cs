using System.Collections.Frozen;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace Wade.Imaging;

internal sealed record ImagePreviewResult(string SixelData, int PixelWidth, int PixelHeight, string Label);

internal static class ImagePreview
{
    private const int CellPixelWidth = 8;
    private const int CellPixelHeight = 16;

    private static readonly FrozenSet<string> s_imageExtensions =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ".png", ".jpg", ".jpeg", ".gif", ".bmp", ".webp", ".tga", ".tiff", ".pbm"
        }.ToFrozenSet(StringComparer.OrdinalIgnoreCase);

    public static bool IsImageFile(string path)
    {
        string ext = Path.GetExtension(path);
        return ext.Length > 0 && s_imageExtensions.Contains(ext);
    }

    public static ImagePreviewResult? Load(string path, int paneWidthCells, int paneHeightCells, CancellationToken ct)
    {
        try
        {
            int maxPixelWidth = paneWidthCells * CellPixelWidth;
            int maxPixelHeight = paneHeightCells * CellPixelHeight;

            if (maxPixelWidth <= 0 || maxPixelHeight <= 0)
                return null;

            using var stream = File.OpenRead(path);
            using var image = Image.Load<Rgba32>(stream);

            ct.ThrowIfCancellationRequested();

            // Scale to fit pane while preserving aspect ratio
            int srcWidth = image.Width;
            int srcHeight = image.Height;
            double scaleX = (double)maxPixelWidth / srcWidth;
            double scaleY = (double)maxPixelHeight / srcHeight;
            double scale = Math.Min(scaleX, scaleY);
            // Don't upscale
            if (scale > 1.0) scale = 1.0;

            int targetWidth = Math.Max(1, (int)(srcWidth * scale));
            int targetHeight = Math.Max(1, (int)(srcHeight * scale));

            image.Mutate(ctx => ctx.Resize(targetWidth, targetHeight));

            ct.ThrowIfCancellationRequested();

            // Extract RGBA bytes
            var rgba = new byte[targetWidth * targetHeight * 4];
            image.CopyPixelDataTo(rgba);

            ct.ThrowIfCancellationRequested();

            string sixelData = SixelEncoder.Encode(rgba, targetWidth, targetHeight);

            string ext = Path.GetExtension(path).TrimStart('.').ToUpperInvariant();
            string label = $"{ext} Image ({srcWidth}x{srcHeight})";

            return new ImagePreviewResult(sixelData, targetWidth, targetHeight, label);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            return null;
        }
    }
}
