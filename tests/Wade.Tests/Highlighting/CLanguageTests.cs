using Wade.Highlighting;
using Wade.Highlighting.Languages;

namespace Wade.Tests.Highlighting;

public class CLanguageTests
{
    private static readonly ILanguage Lang = new CLanguage();

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
    [InlineData("if")]
    [InlineData("else")]
    [InlineData("for")]
    [InlineData("while")]
    [InlineData("struct")]
    [InlineData("typedef")]
    [InlineData("union")]
    [InlineData("extern")]
    [InlineData("register")]
    [InlineData("volatile")]
    public void CKeywords_Classified(string keyword)
    {
        StyledSpan[] spans = Tokenize(keyword);
        Assert.Contains(spans, s => s.Kind == TokenKind.Keyword);
    }

    [Theory]
    [InlineData("NULL")]
    [InlineData("true")]
    [InlineData("false")]
    [InlineData("nullptr")]
    public void Constants_Classified(string constant)
    {
        StyledSpan[] spans = Tokenize(constant);
        Assert.Contains(spans, s => s.Kind == TokenKind.Constant);
    }

    [Theory]
    [InlineData("int")]
    [InlineData("void")]
    [InlineData("char")]
    [InlineData("unsigned")]
    [InlineData("size_t")]
    [InlineData("uint32_t")]
    [InlineData("FILE")]
    public void Builtins_Classified(string builtin)
    {
        StyledSpan[] spans = Tokenize(builtin);
        Assert.Contains(spans, s => s.Kind == TokenKind.BuiltinFunc);
    }

    [Theory]
    [InlineData("#include <stdio.h>")]
    [InlineData("#define MAX 100")]
    [InlineData("#ifdef DEBUG")]
    [InlineData("#endif")]
    [InlineData("  #pragma once")]
    public void PreprocessorDirective_Classified(string line)
    {
        StyledSpan[] spans = Tokenize(line);
        Assert.Contains(spans, s => s.Kind == TokenKind.Directive);
    }

    [Fact]
    public void BlockComment_MultiLine_SpansAllLines()
    {
        StyledLine[] lines = TokenizeLines("/* comment", "   continues", "   end */");
        Assert.All(lines, l => Assert.Contains(l.Spans ?? [], s => s.Kind == TokenKind.Comment));
    }

    [Fact]
    public void LineComment_Classified()
    {
        StyledSpan[] spans = Tokenize("// this is a comment");
        Assert.Contains(spans, s => s.Kind == TokenKind.Comment);
    }

    [Fact]
    public void String_Classified()
    {
        StyledSpan[] spans = Tokenize("\"hello world\"");
        Assert.Contains(spans, s => s.Kind == TokenKind.String);
    }
}
