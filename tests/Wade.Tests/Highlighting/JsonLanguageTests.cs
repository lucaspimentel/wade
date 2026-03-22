using Wade.Highlighting;
using Wade.Highlighting.Languages;

namespace Wade.Tests.Highlighting;

public class JsonLanguageTests
{
    private static readonly ILanguage Lang = new JsonLanguage();

    private static StyledSpan[] Tokenize(string line)
    {
        byte state = 0;
        return Lang.TokenizeLine(line, ref state).Spans ?? [];
    }

    [Fact]
    public void KeyStringValue_Classified()
    {
        StyledSpan[] spans = Tokenize("\"key\": \"value\"");
        Assert.Contains(spans, s => s.Kind == TokenKind.Key);
        Assert.Contains(spans, s => s.Kind == TokenKind.String);
    }

    [Fact]
    public void KeyNumberValue_Classified()
    {
        StyledSpan[] spans = Tokenize("\"key\": 42");
        Assert.Contains(spans, s => s.Kind == TokenKind.Key);
        Assert.Contains(spans, s => s.Kind == TokenKind.Number);
    }

    [Theory]
    [InlineData("\"key\": true", TokenKind.Constant)]
    [InlineData("\"key\": false", TokenKind.Constant)]
    [InlineData("\"key\": null", TokenKind.Constant)]
    public void KeyBooleanValue_Classified(string line, TokenKind expected)
    {
        StyledSpan[] spans = Tokenize(line);
        Assert.Contains(spans, s => s.Kind == TokenKind.Key);
        Assert.Contains(spans, s => s.Kind == expected);
    }

    [Fact]
    public void Punctuation_Classified()
    {
        StyledSpan[] spans = Tokenize("{ \"a\": 1 }");
        Assert.Contains(spans, s => s.Kind == TokenKind.Punctuation);
    }

    [Fact]
    public void NestedObject_KeysClassified()
    {
        StyledSpan[] spans = Tokenize("{ \"a\": { \"b\": 1 } }");
        StyledSpan[] keys = spans.Where(s => s.Kind == TokenKind.Key).ToArray();
        Assert.Equal(2, keys.Length);
    }

    [Fact]
    public void EmptyLine_ReturnsNullSpans()
    {
        StyledSpan[] spans = Tokenize("");
        Assert.Empty(spans);
    }
}
