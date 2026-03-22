using Wade.Imaging;

namespace Wade.Tests;

public class SixelEncoderTests
{
    [Fact]
    public void Encode_SinglePixel_ProducesDcsHeaderAndTerminator()
    {
        // 1x1 red pixel (RGBA)
        byte[] rgba = [255, 0, 0, 255];
        string result = SixelEncoder.Encode(rgba, 1, 1);

        Assert.StartsWith("\x1bPq", result);
        Assert.EndsWith("\x1b\\", result);
        // Should contain raster attributes
        Assert.Contains("\"1;1;1;1", result);
        // Should contain at least one color register
        Assert.Contains("#0;2;", result);
    }

    [Fact]
    public void Encode_SmallImage_ContainsPaletteAndData()
    {
        // 2x6 image (one sixel row) — all blue pixels
        int width = 2;
        int height = 6;
        byte[] rgba = new byte[width * height * 4];
        for (int i = 0; i < width * height; i++)
        {
            rgba[i * 4 + 0] = 0; // R
            rgba[i * 4 + 1] = 0; // G
            rgba[i * 4 + 2] = 255; // B
            rgba[i * 4 + 3] = 255; // A
        }

        string result = SixelEncoder.Encode(rgba, width, height);

        Assert.StartsWith("\x1bPq", result);
        Assert.EndsWith("\x1b\\", result);
        // Should have raster attributes for 2x6
        Assert.Contains("\"1;1;2;6", result);
        // Should have a blue-ish color register (0;0;100 in Sixel percentage)
        Assert.Contains(";0;0;100", result);
    }

    [Fact]
    public void Encode_RepeatedColor_UsesRleSyntax()
    {
        // Wide single-color row — RLE threshold is 4
        int width = 10;
        int height = 1;
        byte[] rgba = new byte[width * height * 4];
        for (int i = 0; i < width * height; i++)
        {
            rgba[i * 4 + 0] = 128;
            rgba[i * 4 + 1] = 128;
            rgba[i * 4 + 2] = 128;
            rgba[i * 4 + 3] = 255;
        }

        string result = SixelEncoder.Encode(rgba, width, height);

        // Should contain RLE: !<count><char>
        Assert.Matches(@"!\d+.", result);
    }

    [Fact]
    public void Encode_ZeroSize_ReturnsEmpty()
    {
        string result = SixelEncoder.Encode([], 0, 0);
        Assert.Equal("", result);
    }

    [Fact]
    public void Encode_ZeroWidth_ReturnsEmpty()
    {
        string result = SixelEncoder.Encode([], 0, 5);
        Assert.Equal("", result);
    }

    [Fact]
    public void Encode_ZeroHeight_ReturnsEmpty()
    {
        string result = SixelEncoder.Encode([], 5, 0);
        Assert.Equal("", result);
    }
}
