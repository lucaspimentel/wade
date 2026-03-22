using Wade.Highlighting;
using Wade.Highlighting.Languages;

namespace Wade.Tests.Highlighting;

public class MarkdownLanguageTests
{
    private static readonly ILanguage Lang = new MarkdownLanguage();

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
    [InlineData("# Heading")]
    [InlineData("## Sub heading")]
    [InlineData("### Third level")]
    public void Headings_ClassifiedAsHeading(string line)
    {
        StyledSpan[] spans = Tokenize(line);
        Assert.Contains(spans, s => s.Kind == TokenKind.Heading && s.Start == 0 && s.Length == line.Length);
    }

    [Fact]
    public void Bold_Classified()
    {
        StyledSpan[] spans = Tokenize("**bold text**");
        Assert.Contains(spans, s => s.Kind == TokenKind.Bold);
    }

    [Fact]
    public void Italic_Classified()
    {
        StyledSpan[] spans = Tokenize("*italic text*");
        Assert.Contains(spans, s => s.Kind == TokenKind.Italic);
    }

    [Fact]
    public void InlineCode_Classified()
    {
        StyledSpan[] spans = Tokenize("`code span`");
        Assert.Contains(spans, s => s.Kind == TokenKind.CodeSpan);
    }

    [Fact]
    public void Link_Classified()
    {
        StyledSpan[] spans = Tokenize("[text](url)");
        Assert.Contains(spans, s => s.Kind == TokenKind.Link);
    }

    [Fact]
    public void CodeFence_LinesInsideClassifiedAsCodeSpan()
    {
        StyledLine[] lines = TokenizeLines("```", "code here", "```");
        Assert.All(lines, l => Assert.Contains(l.Spans ?? [], s => s.Kind == TokenKind.CodeSpan));
    }

    [Fact]
    public void ListItem_PunctuationClassified()
    {
        StyledSpan[] spans = Tokenize("- list item");
        Assert.Contains(spans, s => s.Kind == TokenKind.Punctuation);
    }

    [Fact]
    public void PlainTextLine_ReturnsNullSpans()
    {
        StyledSpan[] spans = Tokenize("just plain text here");
        Assert.Empty(spans);
    }

    [Fact]
    public void EmptyLine_ReturnsNullSpans()
    {
        StyledSpan[] spans = Tokenize("");
        Assert.Empty(spans);
    }
}
