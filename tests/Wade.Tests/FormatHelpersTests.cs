using Wade.UI;

namespace Wade.Tests;

public class FormatHelpersTests
{
    [Theory]
    [InlineData(0, "0 B")]
    [InlineData(512, "512 B")]
    [InlineData(1023, "1023 B")]
    [InlineData(1024, "1.0 KB")]
    [InlineData(1536, "1.5 KB")]
    [InlineData(1048576, "1.0 MB")]
    [InlineData(1572864, "1.5 MB")]
    [InlineData(1073741824, "1.0 GB")]
    [InlineData(1610612736, "1.5 GB")]
    public void FormatSize_ProducesExpectedOutput(long bytes, string expected)
    {
        Span<char> buf = stackalloc char[32];
        int len = FormatHelpers.FormatSize(buf, bytes);
        Assert.Equal(expected, buf[..len].ToString());
    }

    [Theory]
    [InlineData(19, "2025-03-06 02:30 PM")]
    [InlineData(10, "2025-03-06")]
    [InlineData(6, "Mar 06")]
    public void FormatDate_ProducesExpectedOutput(int maxWidth, string expected)
    {
        var dt = new DateTime(2025, 3, 6, 14, 30, 0);
        Span<char> buf = stackalloc char[32];
        int len = FormatHelpers.FormatDate(buf, dt, maxWidth);
        Assert.Equal(expected, buf[..len].ToString());
    }

    [Fact]
    public void FormatDate_MaxWidthTooSmall_ReturnsZero()
    {
        var dt = new DateTime(2025, 3, 6, 14, 30, 0);
        Span<char> buf = stackalloc char[32];
        int len = FormatHelpers.FormatDate(buf, dt, 5);
        Assert.Equal(0, len);
    }

    [Theory]
    [InlineData(0.0, 10, 0, "\u2591\u2591\u2591\u25910%\u2591\u2591\u2591\u2591")]
    [InlineData(1.0, 10, 10, "\u2588\u2588\u2588100%\u2588\u2588\u2588")]
    [InlineData(0.75, 20, 15, "\u2588\u2588\u2588\u2588\u2588\u2588\u2588\u258875%\u2588\u2588\u2588\u2588\u2591\u2591\u2591\u2591\u2591")]
    [InlineData(0.5, 10, 5, "\u2588\u2588\u258850%\u2591\u2591\u2591\u2591")]
    [InlineData(0.3, 10, 3, "\u2588\u2588\u258830%\u2591\u2591\u2591\u2591")]
    public void FormatPercentBar_ProducesExpectedOutput(double fraction, int barWidth, int expectedFilled, string expected)
    {
        Span<char> buf = stackalloc char[32];
        var result = FormatHelpers.FormatPercentBar(buf, fraction, barWidth);
        Assert.Equal(barWidth, result.Length);
        Assert.Equal(expectedFilled, result.FilledCount);
        Assert.Equal(expected, buf[..result.Length].ToString());
    }

    [Fact]
    public void FormatPercentBar_BufferTooSmall_ReturnsZero()
    {
        Span<char> buf = stackalloc char[3];
        var result = FormatHelpers.FormatPercentBar(buf, 0.5, 5);
        Assert.Equal(0, result.Length);
    }

    [Fact]
    public void FormatPercentBar_FractionClamped()
    {
        Span<char> buf = stackalloc char[10];

        // Fraction > 1.0 should clamp to all filled
        var result = FormatHelpers.FormatPercentBar(buf, 1.5, 5);
        Assert.Equal(5, result.Length);
        Assert.Equal(5, result.FilledCount);

        // Fraction < 0.0 should clamp to all empty
        result = FormatHelpers.FormatPercentBar(buf, -0.5, 5);
        Assert.Equal(5, result.Length);
        Assert.Equal(0, result.FilledCount);
    }

    [Fact]
    public void FormatPercentBar_LabelBounds()
    {
        Span<char> buf = stackalloc char[20];
        var result = FormatHelpers.FormatPercentBar(buf, 0.5, 10);
        // "50%" is 3 chars, centered at (10-3)/2 = 3
        Assert.Equal(3, result.LabelStart);
        Assert.Equal(3, result.LabelLength);
    }
}
