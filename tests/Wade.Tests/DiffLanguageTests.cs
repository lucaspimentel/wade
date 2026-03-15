using Wade.Highlighting.Languages;
using Wade.Terminal;

namespace Wade.Tests;

public class DiffLanguageTests
{
    private readonly DiffLanguage _lang = new();

    private static readonly Color Green = new(80, 200, 80);
    private static readonly Color Red = new(220, 80, 80);
    private static readonly Color Cyan = new(80, 180, 220);
    private static readonly Color DimGray = new(140, 140, 140);

    [Theory]
    [InlineData("+added line")]
    [InlineData("+")]
    public void AddedLines_GetGreenStyle(string line)
    {
        byte state = 0;
        var result = _lang.TokenizeLine(line, ref state);

        Assert.NotNull(result.CharStyles);
        Assert.All(result.CharStyles, s => Assert.Equal(Green, s.Fg));
    }

    [Theory]
    [InlineData("-removed line")]
    [InlineData("-")]
    public void RemovedLines_GetRedStyle(string line)
    {
        byte state = 0;
        var result = _lang.TokenizeLine(line, ref state);

        Assert.NotNull(result.CharStyles);
        Assert.All(result.CharStyles, s => Assert.Equal(Red, s.Fg));
    }

    [Theory]
    [InlineData("@@ -1,3 +1,4 @@ some context")]
    [InlineData("@@")]
    public void HunkHeaders_GetCyanStyle(string line)
    {
        byte state = 0;
        var result = _lang.TokenizeLine(line, ref state);

        Assert.NotNull(result.CharStyles);
        Assert.All(result.CharStyles, s => Assert.Equal(Cyan, s.Fg));
    }

    [Theory]
    [InlineData("diff --git a/file.txt b/file.txt")]
    [InlineData("index abc123..def456 100644")]
    [InlineData("--- a/file.txt")]
    [InlineData("+++ b/file.txt")]
    public void MetadataLines_GetDimGrayStyle(string line)
    {
        byte state = 0;
        var result = _lang.TokenizeLine(line, ref state);

        Assert.NotNull(result.CharStyles);
        Assert.All(result.CharStyles, s =>
        {
            Assert.Equal(DimGray, s.Fg);
            Assert.True(s.Dim);
        });
    }

    [Theory]
    [InlineData(" context line")]
    [InlineData("some other text")]
    public void ContextLines_GetNoCharStyles(string line)
    {
        byte state = 0;
        var result = _lang.TokenizeLine(line, ref state);

        Assert.Null(result.CharStyles);
    }

    [Fact]
    public void EmptyLine_GetNoCharStyles()
    {
        byte state = 0;
        var result = _lang.TokenizeLine("", ref state);

        Assert.Null(result.CharStyles);
    }

    [Fact]
    public void PreservesLineText()
    {
        byte state = 0;
        string line = "+hello world";
        var result = _lang.TokenizeLine(line, ref state);

        Assert.Equal(line, result.Text);
    }

    [Fact]
    public void CharStylesLength_MatchesLineLength()
    {
        byte state = 0;
        string line = "-removed content here";
        var result = _lang.TokenizeLine(line, ref state);

        Assert.NotNull(result.CharStyles);
        Assert.Equal(line.Length, result.CharStyles.Length);
    }
}
