using Markdig;
using Markdig.Syntax;
using Wade.Highlighting;
using Wade.Terminal;

namespace Wade.Tests.Highlighting;

public sealed class MarkdigRendererTests
{
    private static readonly MarkdownPipeline s_pipeline = new MarkdownPipelineBuilder()
        .UsePipeTables()
        .Build();

    private static StyledLine[] Render(string markdown, int width = 80) =>
        MarkdigRenderer.RenderDocument(Markdown.Parse(markdown, s_pipeline), width, CancellationToken.None);

    [Fact]
    public void Heading_H1_IsBold()
    {
        StyledLine[] lines = Render("# Hello");
        Assert.Single(lines);
        Assert.Equal("# Hello", lines[0].Text);
        Assert.NotNull(lines[0].CharStyles);
        Assert.All(lines[0].CharStyles!, s => Assert.True(s.Bold));
    }

    [Theory]
    [InlineData("# H1", 1)]
    [InlineData("## H2", 2)]
    [InlineData("### H3", 3)]
    public void Heading_PrefixMatchesLevel(string markdown, int level)
    {
        StyledLine[] lines = Render(markdown);
        Assert.Single(lines);
        Assert.StartsWith(new string('#', level) + " ", lines[0].Text);
    }

    [Fact]
    public void Paragraph_PlainText_RendersCorrectly()
    {
        StyledLine[] lines = Render("Hello world");
        Assert.Single(lines);
        Assert.Equal("Hello world", lines[0].Text);
    }

    [Fact]
    public void Paragraph_WordWraps()
    {
        StyledLine[] lines = Render("one two three four five six", width: 15);
        Assert.True(lines.Length > 1, "Expected word wrapping to produce multiple lines");
        Assert.True(lines[0].Text.Length <= 15);
    }

    [Fact]
    public void InlineCode_HasSalmonColor()
    {
        StyledLine[] lines = Render("Use `foo` here");
        Assert.Single(lines);
        Assert.NotNull(lines[0].CharStyles);

        // Find the 'f' in foo — should have salmon color (206, 145, 120)
        int fooStart = lines[0].Text.IndexOf('f');
        Assert.True(fooStart >= 0);
        CellStyle codeStyle = lines[0].CharStyles![fooStart];
        Assert.Equal(new Color(206, 145, 120), codeStyle.Fg);
    }

    [Fact]
    public void Link_HasTealColor()
    {
        StyledLine[] lines = Render("[click](https://example.com)");
        Assert.Single(lines);
        Assert.NotNull(lines[0].CharStyles);

        // Link text "click" should have teal color (78, 201, 176)
        int linkStart = lines[0].Text.IndexOf('c');
        Assert.True(linkStart >= 0);
        CellStyle linkStyle = lines[0].CharStyles![linkStart];
        Assert.Equal(new Color(78, 201, 176), linkStyle.Fg);
    }

    [Fact]
    public void Bold_SetsBoldAttribute()
    {
        StyledLine[] lines = Render("some **bold** text");
        Assert.Single(lines);
        Assert.NotNull(lines[0].CharStyles);

        int boldStart = lines[0].Text.IndexOf('b');
        Assert.True(boldStart >= 0);
        Assert.True(lines[0].CharStyles![boldStart].Bold);
    }

    [Fact]
    public void Italic_SetsDimAttribute()
    {
        StyledLine[] lines = Render("some *italic* text");
        Assert.Single(lines);
        Assert.NotNull(lines[0].CharStyles);

        int italicStart = lines[0].Text.IndexOf('i');
        Assert.True(italicStart >= 0);
        Assert.True(lines[0].CharStyles![italicStart].Dim);
    }

    [Fact]
    public void FencedCodeBlock_HasBackgroundColor()
    {
        StyledLine[] lines = Render("```\nvar x = 1;\n```");
        Assert.True(lines.Length >= 1);

        // Find the code line
        StyledLine codeLine = lines.First(l => l.Text.TrimStart().StartsWith("var"));
        Assert.NotNull(codeLine.CharStyles);
        Assert.NotNull(codeLine.CharStyles![0].Bg);
        Assert.Equal(new Color(30, 30, 46), codeLine.CharStyles[0].Bg);
    }

    [Fact]
    public void BulletList_HasMarker()
    {
        StyledLine[] lines = Render("- item one\n- item two");
        Assert.True(lines.Length >= 2);
        Assert.StartsWith("- ", lines[0].Text);
    }

    [Fact]
    public void OrderedList_HasNumberedMarker()
    {
        StyledLine[] lines = Render("1. first\n2. second");
        Assert.True(lines.Length >= 2);
        Assert.StartsWith("1. ", lines[0].Text);
    }

    [Fact]
    public void Blockquote_HasBarPrefix()
    {
        StyledLine[] lines = Render("> quoted text");
        Assert.Single(lines);
        Assert.StartsWith("│ ", lines[0].Text);
        Assert.Contains("quoted text", lines[0].Text);
    }

    [Fact]
    public void HorizontalRule_RendersFullWidth()
    {
        StyledLine[] lines = Render("above\n\n---\n\nbelow");
        StyledLine hrLine = lines.First(l => l.Text.Contains('─'));
        Assert.NotNull(hrLine.CharStyles);
        Assert.All(hrLine.CharStyles!, s => Assert.True(s.Dim));
    }

    [Fact]
    public void EmptyInput_ReturnsEmpty()
    {
        StyledLine[] lines = Render("");
        Assert.Empty(lines);
    }

    [Fact]
    public void Table_RendersHeaderAndRows()
    {
        StyledLine[] lines = Render("| A | B |\n|---|---|\n| 1 | 2 |");
        Assert.True(lines.Length >= 3, "Expected at least header, separator, and one data row");

        // Header row should contain both A and B
        Assert.Contains("A", lines[0].Text);
        Assert.Contains("B", lines[0].Text);

        // Separator row should contain ─
        Assert.Contains("─", lines[1].Text);
    }

    [Fact]
    public void FencedCodeBlock_WithLanguage_HasSyntaxHighlighting()
    {
        StyledLine[] lines = Render("```csharp\nvar x = 1;\n```");
        StyledLine codeLine = lines.First(l => l.Text.TrimStart().Contains("var"));
        Assert.NotNull(codeLine.CharStyles);

        // The 'var' keyword should have syntax highlighting (different color from plain code)
        int varStart = codeLine.Text.IndexOf('v');
        Assert.True(varStart >= 0);
        CellStyle varStyle = codeLine.CharStyles![varStart];
        // Should have the code block background
        Assert.Equal(new Color(30, 30, 46), varStyle.Bg);
    }

    [Fact]
    public void NestedList_IncreasesIndent()
    {
        StyledLine[] lines = Render("- outer\n  - inner");
        Assert.True(lines.Length >= 2);

        // Inner item should be indented more than outer
        int outerIndent = lines[0].Text.Length - lines[0].Text.TrimStart().Length;
        StyledLine innerLine = lines.First(l => l.Text.Contains("inner"));
        int innerIndent = innerLine.Text.Length - innerLine.Text.TrimStart().Length;
        Assert.True(innerIndent > outerIndent);
    }

    [Fact]
    public void Image_RendersAsBracketedText()
    {
        StyledLine[] lines = Render("![alt text](image.png)");
        Assert.Single(lines);
        Assert.Contains("[image: alt text]", lines[0].Text);
    }
}
