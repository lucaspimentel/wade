using Wade.Highlighting;
using Wade.Highlighting.Languages;
using Wade.Terminal;

namespace Wade.Tests.Highlighting;

public class CssLanguageTests
{
    private static readonly ILanguage Lang = new CssLanguage();

    private static StyledSpan[] Tokenize(string line)
    {
        byte state = 0;
        return Lang.TokenizeLine(line, ref state).Spans ?? [];
    }

    private static StyledLine[] TokenizeLines(params string[] lines)
    {
        byte state = 0;
        var result = new StyledLine[lines.Length];
        for (int i = 0; i < lines.Length; i++)
        {
            result[i] = Lang.TokenizeLine(lines[i], ref state);
        }

        return result;
    }

    [Fact]
    public void CssProperty_ClassifiedAsKey()
    {
        StyledSpan[] spans = Tokenize("color: red;");
        Assert.Contains(spans, s => s.Kind == TokenKind.Key);
    }

    [Fact]
    public void BlockComment_SingleLine_Classified()
    {
        StyledSpan[] spans = Tokenize("/* comment */");
        Assert.Contains(spans, s => s.Kind == TokenKind.Comment && s.Start == 0);
    }

    [Fact]
    public void BlockComment_MultiLine_Classified()
    {
        StyledLine[] lines = TokenizeLines("/* start", "middle", "end */");
        Assert.All(lines, l => Assert.Contains(l.Spans ?? [], s => s.Kind == TokenKind.Comment));
    }

    [Fact]
    public void StringValue_Classified()
    {
        StyledSpan[] spans = Tokenize("content: \"text\";");
        Assert.Contains(spans, s => s.Kind == TokenKind.String);
    }

    [Fact]
    public void Punctuation_Classified()
    {
        StyledSpan[] spans = Tokenize("{ color: red; }");
        Assert.Contains(spans, s => s.Kind == TokenKind.Punctuation);
    }

    // ── Hex color swatches ───────────────────────────────────────────────────

    private static StyledLine TokenizeLine(string line)
    {
        byte state = 0;
        return Lang.TokenizeLine(line, ref state);
    }

    [Fact]
    public void HexColor_FullForm_GetsHexColorSpanAndSwatchCells()
    {
        StyledLine result = TokenizeLine("color: #e4e4e4;");

        Assert.NotNull(result.Spans);
        Assert.Contains(result.Spans, s => s.Kind == TokenKind.HexColor);

        Assert.NotNull(result.CharStyles);

        // Swatch cells appear directly after the literal. Find them by color.
        var target = new Color(0xE4, 0xE4, 0xE4);
        Assert.Equal(2, result.CharStyles.Count(cs => cs.Fg == target));
    }

    [Fact]
    public void HexColor_ShortForm_Expands()
    {
        StyledLine result = TokenizeLine("color: #f00;");

        Assert.NotNull(result.CharStyles);
        var red = new Color(0xFF, 0x00, 0x00);
        Assert.Equal(2, result.CharStyles.Count(cs => cs.Fg == red));
    }

    [Fact]
    public void HexColor_EightDigitAlpha_DiscardedKeepsRgb()
    {
        StyledLine result = TokenizeLine("color: #ff000080;");

        Assert.NotNull(result.CharStyles);
        var red = new Color(0xFF, 0x00, 0x00);
        Assert.Equal(2, result.CharStyles.Count(cs => cs.Fg == red));
    }

    [Fact]
    public void HexColor_FourDigitAlphaShort_DiscardedKeepsRgb()
    {
        StyledLine result = TokenizeLine("color: #f008;");

        Assert.NotNull(result.CharStyles);
        var red = new Color(0xFF, 0x00, 0x00);
        Assert.Equal(2, result.CharStyles.Count(cs => cs.Fg == red));
    }

    [Fact]
    public void HexColor_MultipleOnOneLine_BothSwatched()
    {
        StyledLine result = TokenizeLine("background: linear-gradient(#abc, #def);");

        Assert.NotNull(result.CharStyles);

        var abc = new Color(0xAA, 0xBB, 0xCC);
        var def = new Color(0xDD, 0xEE, 0xFF);
        Assert.Equal(2, result.CharStyles.Count(cs => cs.Fg == abc));
        Assert.Equal(2, result.CharStyles.Count(cs => cs.Fg == def));

        Assert.NotNull(result.Spans);
        Assert.Equal(2, result.Spans.Count(s => s.Kind == TokenKind.HexColor));
    }

    [Fact]
    public void SelectorHash_NotSwatched()
    {
        StyledLine result = TokenizeLine("#main { color: red; }");

        // `#main` is an ID selector, not a color. `color: red;` has no hex.
        Assert.Null(result.CharStyles);
        Assert.NotNull(result.Spans);
        Assert.DoesNotContain(result.Spans, s => s.Kind == TokenKind.HexColor);
    }

    [Fact]
    public void HexInsideComment_NotSwatched()
    {
        StyledLine result = TokenizeLine("/* #ff0000 */");

        Assert.Null(result.CharStyles);
        Assert.NotNull(result.Spans);
        Assert.DoesNotContain(result.Spans, s => s.Kind == TokenKind.HexColor);
    }

    [Fact]
    public void HexInsideString_NotSwatched()
    {
        StyledLine result = TokenizeLine("content: \"#ff0000\";");

        Assert.Null(result.CharStyles);
        Assert.NotNull(result.Spans);
        Assert.DoesNotContain(result.Spans, s => s.Kind == TokenKind.HexColor);
    }

    [Theory]
    [InlineData("color: #gg0000;")]  // non-hex chars
    [InlineData("color: #12345;")]    // length 5 not allowed
    [InlineData("color: #1234567;")]  // length 7 not allowed
    [InlineData("color: #12;")]       // length 2 not allowed
    public void HexColor_InvalidLengthOrChars_NotMatched(string line)
    {
        StyledLine result = TokenizeLine(line);

        Assert.Null(result.CharStyles);
        Assert.NotNull(result.Spans);
        Assert.DoesNotContain(result.Spans, s => s.Kind == TokenKind.HexColor);
    }

    [Fact]
    public void NoHexColor_NullCharStyles()
    {
        StyledLine result = TokenizeLine("color: red;");

        Assert.Null(result.CharStyles);
    }

    [Fact]
    public void EmptyLine_NullCharStyles()
    {
        StyledLine result = TokenizeLine(string.Empty);

        Assert.Null(result.CharStyles);
    }

    [Fact]
    public void HexColor_TextLengthIncludesSwatchCells()
    {
        // Original line is 15 chars ("color: #f00000;"), swatch adds 3 more → 18.
        StyledLine result = TokenizeLine("color: #f00000;");

        Assert.Equal(15 + 3, result.Text.Length);
        Assert.Contains('\u2588', result.Text);
    }
}
