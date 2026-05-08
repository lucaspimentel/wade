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

public sealed class FrontmatterTests
{
    // ── TryExtractFrontmatter ────────────────────────────────────────────────

    [Fact]
    public void TryExtractFrontmatter_ValidBlock_ReturnsTrue()
    {
        const string text = "---\nkey: value\n---\n# Body";
        bool result = MarkdigRenderer.TryExtractFrontmatter(text, out string fm, out string body);

        Assert.True(result);
        Assert.Equal("key: value", fm);
        Assert.Equal("# Body", body);
    }

    [Fact]
    public void TryExtractFrontmatter_CrLfLineEndings_ReturnsTrue()
    {
        const string text = "---\r\nkey: value\r\n---\r\nbody";
        bool result = MarkdigRenderer.TryExtractFrontmatter(text, out string fm, out string body);

        Assert.True(result);
        Assert.Equal("key: value", fm);
        Assert.Equal("body", body);
    }

    [Fact]
    public void TryExtractFrontmatter_NoLeadingDashes_ReturnsFalse()
    {
        const string text = "# Normal markdown\nno frontmatter";
        bool result = MarkdigRenderer.TryExtractFrontmatter(text, out _, out _);

        Assert.False(result);
    }

    [Fact]
    public void TryExtractFrontmatter_NoClosingDashes_ReturnsFalse()
    {
        const string text = "---\nkey: value\n# No closing marker";
        bool result = MarkdigRenderer.TryExtractFrontmatter(text, out _, out _);

        Assert.False(result);
    }

    [Fact]
    public void TryExtractFrontmatter_EmptyBody_SetsEmptyRemainingText()
    {
        const string text = "---\nname: test\n---\n";
        bool result = MarkdigRenderer.TryExtractFrontmatter(text, out string fm, out string body);

        Assert.True(result);
        Assert.Equal("name: test", fm);
        Assert.Equal("", body);
    }

    [Fact]
    public void TryExtractFrontmatter_MultipleKeys_ExtractsAllContent()
    {
        const string text = "---\nname: foo\ndescription: bar baz\nmodel: haiku\n---\ncontent";
        bool result = MarkdigRenderer.TryExtractFrontmatter(text, out string fm, out _);

        Assert.True(result);
        Assert.Contains("name: foo", fm);
        Assert.Contains("description: bar baz", fm);
        Assert.Contains("model: haiku", fm);
    }

    // ── RenderText with frontmatter ──────────────────────────────────────────

    private static StyledLine[] RenderText(string text, int width = 80) =>
        MarkdigRenderer.RenderText(text, width, CancellationToken.None);

    [Fact]
    public void RenderText_WithFrontmatter_EmitsHeaderAndFooterBorders()
    {
        const string text = "---\nname: hello\n---\n# Body";
        StyledLine[] lines = RenderText(text);

        // Header should contain "frontmatter" label
        Assert.Contains(lines, l => l.Text.Contains("frontmatter"));

        // Footer should be all horizontal-rule characters
        StyledLine footer = lines.Last(l => l.Text.Length > 4 && l.Text.All(c => c == '\u2500'));
        Assert.NotNull(footer.CharStyles);
        Assert.All(footer.CharStyles!, s => Assert.True(s.Dim));
    }

    [Fact]
    public void RenderText_WithFrontmatter_EmitsKeyValueRow()
    {
        const string text = "---\nname: hello\n---\n";
        StyledLine[] lines = RenderText(text);

        StyledLine kvLine = lines.First(l => l.Text.Contains("hello"));
        Assert.Contains("name", kvLine.Text);
        Assert.Contains("hello", kvLine.Text);
    }

    [Fact]
    public void RenderText_WithFrontmatter_KeyHasLightBlueStyle()
    {
        const string text = "---\nname: hello\n---\n";
        StyledLine[] lines = RenderText(text);

        // The key "name" should be colored light blue (156, 220, 254) = SyntaxTheme Key color
        StyledLine kvLine = lines.First(l => l.Text.Contains("name") && l.Text.Contains("hello"));
        Assert.NotNull(kvLine.CharStyles);
        int nameStart = kvLine.Text.IndexOf('n'); // 'n' of "name"
        Assert.True(nameStart >= 0);
        Assert.Equal(new Color(156, 220, 254), kvLine.CharStyles![nameStart].Fg);
    }

    [Fact]
    public void RenderText_WithFrontmatter_QuotedValueHasSalmonStyle()
    {
        const string text = "---\ndesc: \"some description\"\n---\n";
        StyledLine[] lines = RenderText(text);

        StyledLine kvLine = lines.First(l => l.Text.Contains("some description"));
        Assert.NotNull(kvLine.CharStyles);
        int quoteStart = kvLine.Text.IndexOf('"');
        Assert.True(quoteStart >= 0);
        // Salmon color (206, 145, 120)
        Assert.Equal(new Color(206, 145, 120), kvLine.CharStyles![quoteStart].Fg);
    }

    [Fact]
    public void RenderText_WithFrontmatter_BodyStillRendered()
    {
        const string text = "---\nname: foo\n---\n# My Heading";
        StyledLine[] lines = RenderText(text);

        // The H1 heading should appear after the frontmatter block
        Assert.Contains(lines, l => l.Text.Contains("My Heading"));
    }

    [Fact]
    public void RenderText_WithFrontmatter_LongValueWraps()
    {
        string longValue = string.Join(" ", Enumerable.Repeat("word", 20));
        string text = $"---\ntitle: {longValue}\n---\n";
        StyledLine[] lines = RenderText(text, width: 40);

        // Should produce multiple lines for the wrapped value
        int valueLines = lines.Count(l => l.Text.TrimStart().StartsWith('"') ||
                                          l.Text.Contains("word"));
        Assert.True(valueLines > 1, "Long value should wrap onto multiple lines");
    }

    [Fact]
    public void RenderText_WithFrontmatter_ValuesAlignedAtSameColumn()
    {
        const string text = "---\nname: foo\ndescription: bar\n---\n";
        StyledLine[] lines = RenderText(text);

        // Both key-value lines should have their values starting at the same column
        StyledLine nameLine = lines.First(l => l.Text.Contains("foo"));
        StyledLine descLine = lines.First(l => l.Text.Contains("bar"));

        int fooStart = nameLine.Text.IndexOf("foo", StringComparison.Ordinal);
        int barStart = descLine.Text.IndexOf("bar", StringComparison.Ordinal);

        Assert.Equal(fooStart, barStart);
    }

    [Fact]
    public void RenderText_WithoutFrontmatter_RendersNormally()
    {
        const string text = "# Plain heading\n\nsome text";
        StyledLine[] lines = RenderText(text);

        Assert.DoesNotContain(lines, l => l.Text.Contains("frontmatter"));
        Assert.Contains(lines, l => l.Text.Contains("Plain heading"));
    }
}
