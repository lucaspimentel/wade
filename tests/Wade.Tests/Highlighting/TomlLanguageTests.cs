using Wade.Highlighting;
using Wade.Highlighting.Languages;

namespace Wade.Tests.Highlighting;

public class TomlLanguageTests
{
    private static readonly ILanguage Lang = new TomlLanguage();

    private static StyledSpan[] Tokenize(string line)
    {
        byte state = 0;
        return Lang.TokenizeLine(line, ref state).Spans ?? [];
    }

    [Theory]
    [InlineData("[section]")]
    [InlineData("[package]")]
    public void SectionHeader_ClassifiedAsKey(string line)
    {
        StyledSpan[] spans = Tokenize(line);
        Assert.Contains(spans, s => s.Kind == TokenKind.Key && s.Start == 0);
    }

    [Fact]
    public void KeyValue_StringValue_Classified()
    {
        StyledSpan[] spans = Tokenize("key = \"value\"");
        Assert.Contains(spans, s => s.Kind == TokenKind.Key);
        Assert.Contains(spans, s => s.Kind == TokenKind.Operator);
        Assert.Contains(spans, s => s.Kind == TokenKind.String);
    }

    [Fact]
    public void Comment_Classified()
    {
        StyledSpan[] spans = Tokenize("# this is a comment");
        Assert.Contains(spans, s => s.Kind == TokenKind.Comment && s.Start == 0);
    }

    [Fact]
    public void KeyValue_NumberValue_Classified()
    {
        StyledSpan[] spans = Tokenize("count = 42");
        Assert.Contains(spans, s => s.Kind == TokenKind.Key);
        Assert.Contains(spans, s => s.Kind == TokenKind.Number);
    }

    [Theory]
    [InlineData("enabled = true", TokenKind.Constant)]
    [InlineData("enabled = false", TokenKind.Constant)]
    public void KeyValue_BoolValue_Classified(string line, TokenKind expected)
    {
        StyledSpan[] spans = Tokenize(line);
        Assert.Contains(spans, s => s.Kind == expected);
    }
}
