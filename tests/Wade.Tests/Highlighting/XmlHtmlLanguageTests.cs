using Wade.Highlighting;
using Wade.Highlighting.Languages;

namespace Wade.Tests.Highlighting;

public class XmlHtmlLanguageTests
{
    private static readonly ILanguage Lang = new XmlHtmlLanguage();

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
    public void TagWithAttribute_Classified()
    {
        var spans = Tokenize("<div class=\"x\">");
        Assert.Contains(spans, s => s.Kind == TokenKind.TagName);
        Assert.Contains(spans, s => s.Kind == TokenKind.AttrName);
        Assert.Contains(spans, s => s.Kind == TokenKind.AttrValue);
    }

    [Fact]
    public void Comment_SingleLine_Classified()
    {
        var spans = Tokenize("<!-- comment -->");
        Assert.Contains(spans, s => s.Kind == TokenKind.Comment && s.Start == 0);
    }

    [Fact]
    public void Comment_MultiLine_AllLinesClassified()
    {
        var lines = TokenizeLines("<!--", "text", "-->");
        Assert.All(lines, l => Assert.Contains(l.Spans ?? [], s => s.Kind == TokenKind.Comment));
    }

    [Fact]
    public void SelfClosingTag_Classified()
    {
        var spans = Tokenize("<br />");
        Assert.Contains(spans, s => s.Kind == TokenKind.TagName);
    }

    [Fact]
    public void EntityReference_ClassifiedAsConstant()
    {
        var spans = Tokenize("&amp;");
        Assert.Contains(spans, s => s.Kind == TokenKind.Constant && s.Start == 0);
    }

    [Fact]
    public void ClosingTag_Classified()
    {
        var spans = Tokenize("</div>");
        Assert.Contains(spans, s => s.Kind == TokenKind.TagName);
    }
}
