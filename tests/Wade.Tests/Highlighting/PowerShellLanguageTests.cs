using Wade.Highlighting;
using Wade.Highlighting.Languages;

namespace Wade.Tests.Highlighting;

public class PowerShellLanguageTests
{
    private static readonly ILanguage Lang = new PowerShellLanguage();

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
    public void BlockComment_MultiLine_Classified()
    {
        var lines = TokenizeLines("<# block comment", "middle", "#>");
        Assert.All(lines, l => Assert.Contains(l.Spans ?? [], s => s.Kind == TokenKind.Comment));
    }

    [Fact]
    public void HashComment_SingleLine_Classified()
    {
        var spans = Tokenize("# inline comment");
        Assert.Contains(spans, s => s.Kind == TokenKind.Comment);
    }

    [Fact]
    public void Attribute_Classified()
    {
        var spans = Tokenize("[Parameter()]");
        Assert.Contains(spans, s => s.Kind == TokenKind.Attribute && s.Start == 0);
    }

    [Theory]
    [InlineData("if")]
    [InlineData("function")]
    [InlineData("foreach")]
    [InlineData("param")]
    public void PsKeywords_Classified(string keyword)
    {
        var spans = Tokenize(keyword);
        Assert.Contains(spans, s => s.Kind == TokenKind.Keyword);
    }
}
