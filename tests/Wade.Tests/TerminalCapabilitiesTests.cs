using System.Text;
using Wade.Terminal;

namespace Wade.Tests;

public class TerminalCapabilitiesTests
{
    [Fact]
    public void ParseQueryResponses_EmptyInput_ReturnsDefaults()
    {
        var result = TerminalCapabilities.ParseQueryResponses(ReadOnlySpan<byte>.Empty);

        Assert.False(result.SixelSupported);
        Assert.Equal(8, result.CellPixelWidth);
        Assert.Equal(16, result.CellPixelHeight);
    }

    [Fact]
    public void ParseQueryResponses_DA1WithSixel_DetectsSixelSupport()
    {
        // ESC[?65;1;2;4;6c — param 4 indicates Sixel
        byte[] data = "\x1b[?65;1;2;4;6c"u8.ToArray();

        var result = TerminalCapabilities.ParseQueryResponses(data);

        Assert.True(result.SixelSupported);
    }

    [Fact]
    public void ParseQueryResponses_DA1WithoutSixel_NoSixelSupport()
    {
        // ESC[?65;1;2;6c — no param 4
        byte[] data = "\x1b[?65;1;2;6c"u8.ToArray();

        var result = TerminalCapabilities.ParseQueryResponses(data);

        Assert.False(result.SixelSupported);
    }

    [Fact]
    public void ParseQueryResponses_DA1WithOnly4_DetectsSixel()
    {
        byte[] data = "\x1b[?4c"u8.ToArray();

        var result = TerminalCapabilities.ParseQueryResponses(data);

        Assert.True(result.SixelSupported);
    }

    [Fact]
    public void ParseQueryResponses_CellSizeResponse_ParsesDimensions()
    {
        // ESC[6;20;10t — height=20, width=10
        byte[] data = "\x1b[6;20;10t"u8.ToArray();

        var result = TerminalCapabilities.ParseQueryResponses(data);

        Assert.Equal(10, result.CellPixelWidth);
        Assert.Equal(20, result.CellPixelHeight);
    }

    [Fact]
    public void ParseQueryResponses_BothResponses_ParsesAll()
    {
        // DA1 with Sixel + cell size
        byte[] data = Encoding.ASCII.GetBytes("\x1b[?65;1;4c\x1b[6;24;12t");

        var result = TerminalCapabilities.ParseQueryResponses(data);

        Assert.True(result.SixelSupported);
        Assert.Equal(12, result.CellPixelWidth);
        Assert.Equal(24, result.CellPixelHeight);
    }

    [Fact]
    public void ParseQueryResponses_BothResponsesReversedOrder_ParsesAll()
    {
        // Cell size first, then DA1
        byte[] data = Encoding.ASCII.GetBytes("\x1b[6;18;9t\x1b[?62;4c");

        var result = TerminalCapabilities.ParseQueryResponses(data);

        Assert.True(result.SixelSupported);
        Assert.Equal(9, result.CellPixelWidth);
        Assert.Equal(18, result.CellPixelHeight);
    }

    [Fact]
    public void ParseQueryResponses_GarbledData_ReturnsDefaults()
    {
        byte[] data = [0xFF, 0x00, 0x42, 0x1B, 0x5B, 0xFF];

        var result = TerminalCapabilities.ParseQueryResponses(data);

        Assert.False(result.SixelSupported);
        Assert.Equal(8, result.CellPixelWidth);
        Assert.Equal(16, result.CellPixelHeight);
    }

    [Fact]
    public void ParseQueryResponses_NonCellSizeT_IgnoredGracefully()
    {
        // ESC[1;2;3t — first param is not 6, should not set cell size
        byte[] data = "\x1b[1;2;3t"u8.ToArray();

        var result = TerminalCapabilities.ParseQueryResponses(data);

        Assert.Equal(8, result.CellPixelWidth);
        Assert.Equal(16, result.CellPixelHeight);
    }

    [Theory]
    [InlineData(4, true)]
    [InlineData(14, false)]
    [InlineData(40, false)]
    [InlineData(44, false)]
    public void ParseQueryResponses_DA1ParamMatching_IsExact(int param, bool expectedSixel)
    {
        byte[] data = Encoding.ASCII.GetBytes($"\x1b[?{param}c");

        var result = TerminalCapabilities.ParseQueryResponses(data);

        Assert.Equal(expectedSixel, result.SixelSupported);
    }
}
