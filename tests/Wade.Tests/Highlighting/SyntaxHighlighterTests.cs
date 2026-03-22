using Wade.Highlighting;

namespace Wade.Tests.Highlighting;

public class SyntaxHighlighterTests
{
    [Fact]
    public void UnknownExtension_AllLinesNullSpans()
    {
        string[] lines = ["hello world", "line two"];
        StyledLine[] result = SyntaxHighlighter.Highlight(lines, "file.unknown");
        Assert.All(result, l => Assert.Null(l.Spans));
    }

    [Fact]
    public void KnownExtension_ContentLinesHaveSpans()
    {
        string[] lines = ["namespace Foo;", "public class Bar {", "}"];
        StyledLine[] result = SyntaxHighlighter.Highlight(lines, "Program.cs");
        // At least one line should have spans (namespace, class are keywords)
        Assert.Contains(result, l => l.Spans is { Length: > 0 });
    }

    [Fact]
    public void TextPreserved_InResultLines()
    {
        string[] lines = ["var x = 42;"];
        StyledLine[] result = SyntaxHighlighter.Highlight(lines, "Program.cs");
        Assert.Equal("var x = 42;", result[0].Text);
    }

    [Fact]
    public void EmptyLines_Array_ReturnsEmptyResult()
    {
        StyledLine[] result = SyntaxHighlighter.Highlight([], "Program.cs");
        Assert.Empty(result);
    }
}
