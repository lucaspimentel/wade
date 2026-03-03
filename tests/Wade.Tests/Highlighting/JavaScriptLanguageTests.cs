using Wade.Highlighting;
using Wade.Highlighting.Languages;

namespace Wade.Tests.Highlighting;

public class JavaScriptLanguageTests
{
    private static readonly ILanguage Lang = new JavaScriptLanguage();

    private static StyledSpan[] Tokenize(string line)
    {
        byte state = 0;
        return Lang.TokenizeLine(line, ref state).Spans ?? [];
    }

    [Fact]
    public void TemplateLiteral_Classified()
    {
        var spans = Tokenize("`hello world`");
        Assert.Contains(spans, s => s.Kind == TokenKind.String && s.Start == 0);
    }

    [Theory]
    [InlineData("const")]
    [InlineData("let")]
    [InlineData("function")]
    [InlineData("async")]
    [InlineData("await")]
    [InlineData("import")]
    [InlineData("export")]
    public void JsKeywords_Classified(string keyword)
    {
        var spans = Tokenize(keyword);
        Assert.Contains(spans, s => s.Kind == TokenKind.Keyword);
    }

    [Theory]
    [InlineData("true",      TokenKind.Constant)]
    [InlineData("false",     TokenKind.Constant)]
    [InlineData("null",      TokenKind.Constant)]
    [InlineData("undefined", TokenKind.Constant)]
    public void JsConstants_Classified(string word, TokenKind expected)
    {
        var spans = Tokenize(word);
        Assert.Contains(spans, s => s.Kind == expected);
    }

    [Fact]
    public void ArrowOperator_Classified()
    {
        var spans = Tokenize("=>");
        Assert.Contains(spans, s => s.Kind == TokenKind.Operator);
    }
}
