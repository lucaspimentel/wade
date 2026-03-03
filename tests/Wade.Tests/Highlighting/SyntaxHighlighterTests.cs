using Wade.Highlighting;

namespace Wade.Tests.Highlighting;

public class SyntaxHighlighterTests
{
    [Fact]
    public void HeaderLines_PassThroughWithNullSpans()
    {
        var lines = new[] { "File: test.cs", "Size: 100", "public class Foo {" };
        var result = SyntaxHighlighter.Highlight(lines, headerLineCount: 2, "test.cs");
        Assert.Null(result[0].Spans); // header
        Assert.Null(result[1].Spans); // header
        // Content line may have spans
        Assert.Equal(lines[2], result[2].Text);
    }

    [Fact]
    public void UnknownExtension_AllLinesNullSpans()
    {
        var lines = new[] { "hello world", "line two" };
        var result = SyntaxHighlighter.Highlight(lines, headerLineCount: 0, "file.unknown");
        Assert.All(result, l => Assert.Null(l.Spans));
    }

    [Fact]
    public void KnownExtension_ContentLinesHaveSpans()
    {
        var lines = new[] { "namespace Foo;", "public class Bar {", "}" };
        var result = SyntaxHighlighter.Highlight(lines, headerLineCount: 0, "Program.cs");
        // At least one line should have spans (namespace, class are keywords)
        Assert.Contains(result, l => l.Spans is { Length: > 0 });
    }

    [Fact]
    public void TextPreserved_InResultLines()
    {
        var lines = new[] { "var x = 42;" };
        var result = SyntaxHighlighter.Highlight(lines, headerLineCount: 0, "Program.cs");
        Assert.Equal("var x = 42;", result[0].Text);
    }

    [Fact]
    public void EmptyLines_Array_ReturnsEmptyResult()
    {
        var result = SyntaxHighlighter.Highlight([], headerLineCount: 0, "Program.cs");
        Assert.Empty(result);
    }
}
