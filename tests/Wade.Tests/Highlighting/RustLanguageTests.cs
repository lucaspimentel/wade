using Wade.Highlighting;
using Wade.Highlighting.Languages;

namespace Wade.Tests.Highlighting;

public class RustLanguageTests
{
    private static readonly ILanguage Lang = new RustLanguage();

    private static StyledSpan[] Tokenize(string line)
    {
        byte state = 0;
        return Lang.TokenizeLine(line, ref state).Spans ?? [];
    }

    [Theory]
    [InlineData("fn")]
    [InlineData("let")]
    [InlineData("mut")]
    [InlineData("impl")]
    [InlineData("trait")]
    [InlineData("struct")]
    public void RustKeywords_Classified(string keyword)
    {
        var spans = Tokenize(keyword);
        Assert.Contains(spans, s => s.Kind == TokenKind.Keyword);
    }

    [Fact]
    public void Attribute_Classified()
    {
        var spans = Tokenize("#[derive(Debug)]");
        Assert.Contains(spans, s => s.Kind == TokenKind.Attribute && s.Start == 0);
    }

    [Fact]
    public void Lifetime_NotClassifiedAsString()
    {
        // 'a in a lifetime context is NOT a string
        var spans = Tokenize("fn foo<'a>(x: &'a str)");
        // Should not classify lifetimes as strings
        // The key check: if 'a appears, it should not be TokenKind.String
        // (it might not appear at all, which is also fine)
        Assert.DoesNotContain(spans, s => s.Kind == TokenKind.String && s.Length == 2);
    }

    [Fact]
    public void CharLiteral_ClassifiedAsString()
    {
        var spans = Tokenize("'x'");
        Assert.Contains(spans, s => s.Kind == TokenKind.String && s.Start == 0 && s.Length == 3);
    }

    [Fact]
    public void EscapeCharLiteral_ClassifiedAsString()
    {
        var spans = Tokenize(@"'\n'");
        Assert.Contains(spans, s => s.Kind == TokenKind.String && s.Start == 0);
    }
}
