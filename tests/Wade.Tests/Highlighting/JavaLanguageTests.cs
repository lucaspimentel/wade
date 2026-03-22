using Wade.Highlighting;
using Wade.Highlighting.Languages;

namespace Wade.Tests.Highlighting;

public class JavaLanguageTests
{
    private static readonly ILanguage Lang = new JavaLanguage();

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

    [Theory]
    [InlineData("@Override")]
    [InlineData("@SuppressWarnings")]
    [InlineData("@Inject")]
    public void Annotation_Classified(string line)
    {
        StyledSpan[] spans = Tokenize(line);
        Assert.Contains(spans, s => s.Kind == TokenKind.Attribute && s.Start == 0);
    }

    [Theory]
    [InlineData("abstract")]
    [InlineData("synchronized")]
    [InlineData("implements")]
    [InlineData("extends")]
    public void JavaKeywords_Classified(string keyword)
    {
        StyledSpan[] spans = Tokenize(keyword);
        Assert.Contains(spans, s => s.Kind == TokenKind.Keyword);
    }

    [Fact]
    public void TextBlock_SingleLine_Classified()
    {
        StyledSpan[] spans = Tokenize("\"\"\"text\"\"\"");
        Assert.Contains(spans, s => s.Kind == TokenKind.String);
    }

    [Fact]
    public void TextBlock_MultiLine_SpansAllLines()
    {
        StyledLine[] lines = TokenizeLines("\"\"\"", "    content", "    \"\"\"");
        Assert.All(lines, l => Assert.Contains(l.Spans ?? [], s => s.Kind == TokenKind.String));
    }
}
