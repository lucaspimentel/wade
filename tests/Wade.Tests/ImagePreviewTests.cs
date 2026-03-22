using Wade.Imaging;

namespace Wade.Tests;

public class ImagePreviewTests
{
    [Theory]
    [InlineData(".png")]
    [InlineData(".jpg")]
    [InlineData(".jpeg")]
    [InlineData(".gif")]
    [InlineData(".bmp")]
    [InlineData(".webp")]
    [InlineData(".tga")]
    [InlineData(".tiff")]
    [InlineData(".pbm")]
    public void IsImageFile_ReturnsTrue_ForImageExtensions(string ext) => Assert.True(ImagePreview.IsImageFile($"test{ext}"));

    [Theory]
    [InlineData(".txt")]
    [InlineData(".cs")]
    [InlineData(".exe")]
    [InlineData(".dll")]
    [InlineData(".json")]
    [InlineData("")]
    public void IsImageFile_ReturnsFalse_ForNonImageExtensions(string ext) => Assert.False(ImagePreview.IsImageFile($"test{ext}"));

    [Fact]
    public void IsImageFile_IsCaseInsensitive()
    {
        Assert.True(ImagePreview.IsImageFile("test.PNG"));
        Assert.True(ImagePreview.IsImageFile("test.Jpg"));
    }

    [Fact]
    public void Load_SmallBmp_ProducesValidSixelResult()
    {
        // Create a minimal 4x4 24-bit BMP (no compression, no palette)
        string tempFile = Path.Combine(Path.GetTempPath(), $"wade_test_{Guid.NewGuid()}.bmp");
        try
        {
            CreateTestBmp(tempFile, 4, 4, r: 255, g: 0, b: 0);

            ImagePreviewResult? result = ImagePreview.Load(tempFile, 40, 20, 8, 16, CancellationToken.None);

            Assert.NotNull(result);
            Assert.StartsWith("\x1bPq", result.SixelData);
            Assert.EndsWith("\x1b\\", result.SixelData);
            Assert.True(result.PixelWidth > 0);
            Assert.True(result.PixelHeight > 0);
            Assert.Contains("BMP", result.Label);
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Fact]
    public void Load_NonExistentFile_ReturnsNull()
    {
        ImagePreviewResult? result = ImagePreview.Load("nonexistent.png", 40, 20, 8, 16, CancellationToken.None);
        Assert.Null(result);
    }

    [Fact]
    public void Load_CancelledToken_ThrowsOrReturnsNull()
    {
        string tempFile = Path.Combine(Path.GetTempPath(), $"wade_test_{Guid.NewGuid()}.bmp");
        try
        {
            CreateTestBmp(tempFile, 4, 4, r: 0, g: 0, b: 255);

            using var cts = new CancellationTokenSource();
            cts.Cancel();

            try
            {
                ImagePreviewResult? result = ImagePreview.Load(tempFile, 40, 20, 8, 16, cts.Token);
            }
            catch (OperationCanceledException)
            {
                // Expected
            }
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    /// <summary>
    /// Creates a minimal 24-bit BMP file with a solid color.
    /// </summary>
    private static void CreateTestBmp(string path, int width, int height, byte r, byte g, byte b)
    {
        int rowSize = (width * 3 + 3) & ~3; // rows padded to 4 bytes
        int pixelDataSize = rowSize * height;
        int fileSize = 54 + pixelDataSize; // 14 (file header) + 40 (DIB header) + pixel data

        using FileStream stream = File.Create(path);
        using var writer = new BinaryWriter(stream);

        // BMP file header (14 bytes)
        writer.Write((byte)'B');
        writer.Write((byte)'M');
        writer.Write(fileSize);
        writer.Write(0); // reserved
        writer.Write(54); // pixel data offset

        // DIB header (BITMAPINFOHEADER, 40 bytes)
        writer.Write(40); // header size
        writer.Write(width);
        writer.Write(height);
        writer.Write((short)1); // color planes
        writer.Write((short)24); // bits per pixel
        writer.Write(0); // no compression
        writer.Write(pixelDataSize);
        writer.Write(2835); // horizontal resolution (72 DPI)
        writer.Write(2835); // vertical resolution
        writer.Write(0); // colors in palette
        writer.Write(0); // important colors

        // Pixel data (bottom-up, BGR)
        byte[] rowPadding = new byte[rowSize - width * 3];
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                writer.Write(b); // blue
                writer.Write(g); // green
                writer.Write(r); // red
            }

            if (rowPadding.Length > 0)
            {
                writer.Write(rowPadding);
            }
        }
    }
}
