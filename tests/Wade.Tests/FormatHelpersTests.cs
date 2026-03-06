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
}
