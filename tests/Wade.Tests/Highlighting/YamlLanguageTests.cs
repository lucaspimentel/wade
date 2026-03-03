using Wade.Highlighting;
using Wade.Highlighting.Languages;

namespace Wade.Tests.Highlighting;

public class YamlLanguageTests
{
    private static readonly ILanguage Lang = new YamlLanguage();

    private static StyledSpan[] Tokenize(string line)
    {
        byte state = 0;
        return Lang.TokenizeLine(line, ref state).Spans ?? [];
    }

    [Fact]
    public void KeyValue_PlainValue_KeyClassified()
    {
        var spans = Tokenize("key: value");
        Assert.Contains(spans, s => s.Kind == TokenKind.Key);
    }

    [Fact]
    public void KeyValue_QuotedValue_StringClassified()
    {
        var spans = Tokenize("key: \"quoted\"");
        Assert.Contains(spans, s => s.Kind == TokenKind.Key);
        Assert.Contains(spans, s => s.Kind == TokenKind.String);
    }

    [Fact]
    public void KeyValue_NumberValue_NumberClassified()
    {
        var spans = Tokenize("count: 42");
        Assert.Contains(spans, s => s.Kind == TokenKind.Key);
        Assert.Contains(spans, s => s.Kind == TokenKind.Number);
    }

    [Fact]
    public void Comment_Classified()
    {
        var spans = Tokenize("# comment");
        Assert.Contains(spans, s => s.Kind == TokenKind.Comment && s.Start == 0);
    }

    [Fact]
    public void ListItem_PunctuationClassified()
    {
        var spans = Tokenize("- item");
        Assert.Contains(spans, s => s.Kind == TokenKind.Punctuation);
    }
}
