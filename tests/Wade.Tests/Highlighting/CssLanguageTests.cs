using Wade.Highlighting;
using Wade.Highlighting.Languages;

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
            result[i] = Lang.TokenizeLine(lines[i], ref state);
        return result;
    }

    [Fact]
    public void CssProperty_ClassifiedAsKey()
    {
        var spans = Tokenize("color: red;");
        Assert.Contains(spans, s => s.Kind == TokenKind.Key);
    }

    [Fact]
    public void BlockComment_SingleLine_Classified()
    {
        var spans = Tokenize("/* comment */");
        Assert.Contains(spans, s => s.Kind == TokenKind.Comment && s.Start == 0);
    }

    [Fact]
    public void BlockComment_MultiLine_Classified()
    {
        var lines = TokenizeLines("/* start", "middle", "end */");
        Assert.All(lines, l => Assert.Contains(l.Spans ?? [], s => s.Kind == TokenKind.Comment));
    }

    [Fact]
    public void StringValue_Classified()
    {
        var spans = Tokenize("content: \"text\";");
        Assert.Contains(spans, s => s.Kind == TokenKind.String);
    }

    [Fact]
    public void Punctuation_Classified()
    {
        var spans = Tokenize("{ color: red; }");
        Assert.Contains(spans, s => s.Kind == TokenKind.Punctuation);
    }
}
