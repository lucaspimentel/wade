using Wade.Highlighting;
using Wade.Highlighting.Languages;

namespace Wade.Tests.Highlighting;

public class ShellLanguageTests
{
    private static readonly ILanguage Lang = new ShellLanguage();

    private static StyledSpan[] Tokenize(string line)
    {
        byte state = 0;
        return Lang.TokenizeLine(line, ref state).Spans ?? [];
    }

    [Theory]
    [InlineData("# comment")]
    [InlineData("#!/bin/bash")]
    public void HashComment_Classified(string line)
    {
        StyledSpan[] spans = Tokenize(line);
        Assert.Contains(spans, s => s.Kind == TokenKind.Comment && s.Start == 0);
    }

    [Fact]
    public void SingleQuotedString_Classified()
    {
        StyledSpan[] spans = Tokenize("'no escape here'");
        Assert.Contains(spans, s => s.Kind == TokenKind.String && s.Start == 0);
    }

    [Fact]
    public void DoubleQuotedString_Classified()
    {
        StyledSpan[] spans = Tokenize("\"hello world\"");
        Assert.Contains(spans, s => s.Kind == TokenKind.String);
    }

    [Theory]
    [InlineData("if")]
    [InlineData("for")]
    [InlineData("while")]
    [InlineData("function")]
    [InlineData("export")]
    public void ShellKeywords_Classified(string keyword)
    {
        StyledSpan[] spans = Tokenize(keyword);
        Assert.Contains(spans, s => s.Kind == TokenKind.Keyword);
    }
}
