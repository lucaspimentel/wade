using Wade.Highlighting;
using Wade.Highlighting.Languages;

namespace Wade.Tests.Highlighting;

public class RegexLanguageTests
{
    // Use CSharp as the concrete RegexLanguage for shared-scanner tests
    private static readonly ILanguage Lang = new CSharpLanguage();

    private static StyledSpan[] Tokenize(ILanguage lang, string line)
    {
        byte state = 0;
        var result = lang.TokenizeLine(line, ref state);
        return result.Spans ?? [];
    }

    private static StyledLine[] TokenizeLines(ILanguage lang, params string[] lines)
    {
        byte state = 0;
        var result = new StyledLine[lines.Length];
        for (int i = 0; i < lines.Length; i++)
        {
            result[i] = lang.TokenizeLine(lines[i], ref state);
        }

        return result;
    }

    [Fact]
    public void EmptyLine_ReturnsNullSpans()
    {
        var spans = Tokenize(Lang, "");
        Assert.Empty(spans);
    }

    [Fact]
    public void WhitespaceOnlyLine_ReturnsNullSpans()
    {
        var spans = Tokenize(Lang, "   ");
        Assert.Empty(spans);
    }

    [Theory]
    [InlineData("// comment", 0)]
    [InlineData("    // indented comment", 4)]
    public void LineComment_ClassifiesRestAsComment(string line, int expectedStart)
    {
        var spans = Tokenize(Lang, line);
        Assert.Contains(spans, s => s.Kind == TokenKind.Comment && s.Start == expectedStart);
    }

    [Fact]
    public void BlockComment_SingleLine_Classified()
    {
        var spans = Tokenize(Lang, "x /* y */ z");
        Assert.Contains(spans, s => s.Kind == TokenKind.Comment && s.Start == 2);
    }

    [Fact]
    public void BlockComment_MultiLine_AllLinesClassified()
    {
        var lines = TokenizeLines(Lang, "/* start", "middle", "end */");
        // All three lines should have comment spans
        Assert.All(lines, l =>
        {
            var spans = l.Spans ?? [];
            Assert.Contains(spans, s => s.Kind == TokenKind.Comment);
        });
    }

    [Fact]
    public void DoubleQuotedString_Classified()
    {
        var spans = Tokenize(Lang, "\"hello\"");
        Assert.Contains(spans, s => s.Kind == TokenKind.String && s.Start == 0 && s.Length == 7);
    }

    [Fact]
    public void StringWithEscapes_EntireSpanIsString()
    {
        var spans = Tokenize(Lang, "\"say \\\"hi\\\"\"");
        Assert.Contains(spans, s => s.Kind == TokenKind.String && s.Start == 0);
    }

    [Theory]
    [InlineData("42", TokenKind.Number)]
    [InlineData("3.14", TokenKind.Number)]
    [InlineData("0xFF", TokenKind.Number)]
    [InlineData("1_000", TokenKind.Number)]
    public void Numbers_Classified(string line, TokenKind expected)
    {
        var spans = Tokenize(Lang, line);
        Assert.Contains(spans, s => s.Kind == expected);
    }

    [Theory]
    [InlineData("if", TokenKind.Keyword)]
    [InlineData("return", TokenKind.Keyword)]
    [InlineData("class", TokenKind.Keyword)]
    public void Keywords_Classified(string line, TokenKind expected)
    {
        var spans = Tokenize(Lang, line);
        Assert.Contains(spans, s => s.Kind == expected);
    }

    [Fact]
    public void Identifier_NotKeyword_NotClassified()
    {
        var spans = Tokenize(Lang, "iffy");
        Assert.DoesNotContain(spans, s => s.Kind == TokenKind.Keyword);
    }

    [Theory]
    [InlineData("true",  TokenKind.Constant)]
    [InlineData("false", TokenKind.Constant)]
    [InlineData("null",  TokenKind.Constant)]
    public void Constants_Classified(string line, TokenKind expected)
    {
        var spans = Tokenize(Lang, line);
        Assert.Contains(spans, s => s.Kind == expected);
    }

    [Fact]
    public void PascalCase_ClassifiedAsType()
    {
        var spans = Tokenize(Lang, "MyClass");
        Assert.Contains(spans, s => s.Kind == TokenKind.Type && s.Start == 0);
    }

    [Fact]
    public void CamelCase_NotClassifiedAsType()
    {
        var spans = Tokenize(Lang, "myVar");
        Assert.DoesNotContain(spans, s => s.Kind == TokenKind.Type);
    }

    [Theory]
    [InlineData("=",  TokenKind.Operator)]
    [InlineData("+",  TokenKind.Operator)]
    [InlineData("->", TokenKind.Operator)]
    [InlineData("=>", TokenKind.Operator)]
    public void Operators_Classified(string line, TokenKind expected)
    {
        var spans = Tokenize(Lang, line);
        Assert.Contains(spans, s => s.Kind == expected);
    }

    [Theory]
    [InlineData("{", TokenKind.Punctuation)]
    [InlineData("}", TokenKind.Punctuation)]
    [InlineData("(", TokenKind.Punctuation)]
    [InlineData(")", TokenKind.Punctuation)]
    public void Punctuation_Classified(string line, TokenKind expected)
    {
        var spans = Tokenize(Lang, line);
        Assert.Contains(spans, s => s.Kind == expected);
    }
}
