using Wade.Highlighting;
using Wade.Highlighting.Languages;

namespace Wade.Tests.Highlighting;

public class TypeScriptLanguageTests
{
    private static readonly ILanguage Lang = new TypeScriptLanguage();

    private static StyledSpan[] Tokenize(string line)
    {
        byte state = 0;
        return Lang.TokenizeLine(line, ref state).Spans ?? [];
    }

    [Theory]
    [InlineData("interface")]
    [InlineData("type")]
    [InlineData("keyof")]
    [InlineData("readonly")]
    [InlineData("declare")]
    [InlineData("namespace")]
    [InlineData("enum")]
    public void TsExtraKeywords_Classified(string keyword)
    {
        var spans = Tokenize(keyword);
        Assert.Contains(spans, s => s.Kind == TokenKind.Keyword);
    }

    [Fact]
    public void TemplateLiteral_Classified()
    {
        var spans = Tokenize("`typed template`");
        Assert.Contains(spans, s => s.Kind == TokenKind.String);
    }
}
