using Wade.Highlighting;
using Wade.Terminal;

namespace Wade.Tests.Highlighting;

public class GlowRendererTests
{
    [Fact]
    public void ParseAnsiOutput_PlainText_NoCharStyles()
    {
        var lines = GlowRenderer.ParseAnsiOutput("hello world");
        Assert.Single(lines);
        Assert.Equal("hello world", lines[0].Text);
        Assert.Null(lines[0].CharStyles);
    }

    [Fact]
    public void ParseAnsiOutput_Bold_SetsCharStylesBold()
    {
        var lines = GlowRenderer.ParseAnsiOutput("\x1b[1mhello\x1b[0m");
        Assert.Single(lines);
        Assert.Equal("hello", lines[0].Text);
        Assert.NotNull(lines[0].CharStyles);
        Assert.All(lines[0].CharStyles!, s => Assert.True(s.Bold));
    }

    [Theory]
    [InlineData(31, 187, 0, 0)]     // Red
    [InlineData(32, 0, 187, 0)]     // Green
    [InlineData(33, 187, 187, 0)]   // Yellow
    [InlineData(34, 0, 0, 187)]     // Blue
    [InlineData(37, 187, 187, 187)] // White
    public void ParseAnsiOutput_StandardFgColor_MapsToCorrectPalette(int sgrCode, byte r, byte g, byte b)
    {
        var lines = GlowRenderer.ParseAnsiOutput($"\x1b[{sgrCode}mX\x1b[0m");
        Assert.Single(lines);
        Assert.NotNull(lines[0].CharStyles);
        var expected = new Color(r, g, b);
        Assert.Equal(expected, lines[0].CharStyles![0].Fg);
    }

    [Theory]
    [InlineData(42, 0, 187, 0)]     // Green BG
    [InlineData(44, 0, 0, 187)]     // Blue BG
    public void ParseAnsiOutput_StandardBgColor_MapsToCorrectPalette(int sgrCode, byte r, byte g, byte b)
    {
        var lines = GlowRenderer.ParseAnsiOutput($"\x1b[{sgrCode}mX\x1b[0m");
        Assert.Single(lines);
        Assert.NotNull(lines[0].CharStyles);
        var expected = new Color(r, g, b);
        Assert.Equal(expected, lines[0].CharStyles![0].Bg);
    }

    [Fact]
    public void ParseAnsiOutput_BrightFgColor_MapsToCorrectPalette()
    {
        // 91 = bright red → palette index 9 = (255, 85, 85)
        var lines = GlowRenderer.ParseAnsiOutput("\x1b[91mX\x1b[0m");
        Assert.Single(lines);
        Assert.NotNull(lines[0].CharStyles);
        Assert.Equal(new Color(255, 85, 85), lines[0].CharStyles![0].Fg);
    }

    [Fact]
    public void ParseAnsiOutput_Underline_SetsAndClears()
    {
        var lines = GlowRenderer.ParseAnsiOutput("\x1b[4mAB\x1b[24mCD");
        Assert.Single(lines);
        Assert.Equal("ABCD", lines[0].Text);
        Assert.NotNull(lines[0].CharStyles);
        var styles = lines[0].CharStyles!;
        Assert.True(styles[0].Underline);  // A
        Assert.True(styles[1].Underline);  // B
        Assert.False(styles[2].Underline); // C
        Assert.False(styles[3].Underline); // D
    }

    [Fact]
    public void ParseAnsiOutput_256Color_6x6x6Cube()
    {
        // 38;5;196 → cube index 196-16=180 → r=5,g=0,b=0 → (255,0,0) using formula r==0?0:55+r*40
        // Actually: 196-16=180, 180/36=5, (180%36)/6=0, 180%6=0 → R=255, G=0, B=0
        var lines = GlowRenderer.ParseAnsiOutput("\x1b[38;5;196mX\x1b[0m");
        Assert.Single(lines);
        Assert.NotNull(lines[0].CharStyles);
        Assert.Equal(new Color(255, 0, 0), lines[0].CharStyles![0].Fg);
    }

    [Fact]
    public void ParseAnsiOutput_24BitColor_ExactRgb()
    {
        var lines = GlowRenderer.ParseAnsiOutput("\x1b[38;2;100;200;50mX\x1b[0m");
        Assert.Single(lines);
        Assert.NotNull(lines[0].CharStyles);
        Assert.Equal(new Color(100, 200, 50), lines[0].CharStyles![0].Fg);
    }

    [Fact]
    public void ParseAnsiOutput_TrailingSpaces_Trimmed()
    {
        var lines = GlowRenderer.ParseAnsiOutput("hello   ");
        Assert.Single(lines);
        Assert.Equal("hello", lines[0].Text);
    }

    [Fact]
    public void ParseAnsiOutput_MultipleLines_CorrectCount()
    {
        var lines = GlowRenderer.ParseAnsiOutput("line1\nline2\nline3");
        Assert.Equal(3, lines.Length);
        Assert.Equal("line1", lines[0].Text);
        Assert.Equal("line2", lines[1].Text);
        Assert.Equal("line3", lines[2].Text);
    }

    [Fact]
    public void ParseAnsiOutput_TrailingEmptyLines_Removed()
    {
        var lines = GlowRenderer.ParseAnsiOutput("hello\n\n\n");
        Assert.Single(lines);
        Assert.Equal("hello", lines[0].Text);
    }
}
