using Wade.Highlighting;
using Wade.Highlighting.Languages;

namespace Wade.Tests.Highlighting;

public class CppLanguageTests
{
    private static readonly ILanguage Lang = new CppLanguage();

    private static StyledSpan[] Tokenize(string line)
    {
        byte state = 0;
        return Lang.TokenizeLine(line, ref state).Spans ?? [];
    }

    [Theory]
    [InlineData("class")]
    [InlineData("namespace")]
    [InlineData("template")]
    [InlineData("virtual")]
    [InlineData("override")]
    [InlineData("constexpr")]
    [InlineData("noexcept")]
    [InlineData("decltype")]
    [InlineData("concept")]
    [InlineData("requires")]
    [InlineData("co_await")]
    public void CppKeywords_Classified(string keyword)
    {
        StyledSpan[] spans = Tokenize(keyword);
        Assert.Contains(spans, s => s.Kind == TokenKind.Keyword);
    }

    [Theory]
    [InlineData("if")]
    [InlineData("for")]
    [InlineData("while")]
    [InlineData("struct")]
    public void InheritedCKeywords_Classified(string keyword)
    {
        StyledSpan[] spans = Tokenize(keyword);
        Assert.Contains(spans, s => s.Kind == TokenKind.Keyword);
    }

    [Theory]
    [InlineData("nullptr")]
    [InlineData("NULL")]
    [InlineData("true")]
    [InlineData("false")]
    public void Constants_Classified(string constant)
    {
        StyledSpan[] spans = Tokenize(constant);
        Assert.Contains(spans, s => s.Kind == TokenKind.Constant);
    }

    [Theory]
    [InlineData("vector")]
    [InlineData("string")]
    [InlineData("shared_ptr")]
    [InlineData("unique_ptr")]
    [InlineData("optional")]
    [InlineData("string_view")]
    [InlineData("unordered_map")]
    [InlineData("cout")]
    public void CppBuiltins_Classified(string builtin)
    {
        StyledSpan[] spans = Tokenize(builtin);
        Assert.Contains(spans, s => s.Kind == TokenKind.BuiltinFunc);
    }

    [Theory]
    [InlineData("int")]
    [InlineData("void")]
    [InlineData("size_t")]
    public void InheritedCBuiltins_Classified(string builtin)
    {
        StyledSpan[] spans = Tokenize(builtin);
        Assert.Contains(spans, s => s.Kind == TokenKind.BuiltinFunc);
    }

    [Theory]
    [InlineData("#include <iostream>")]
    [InlineData("#define MAX 100")]
    [InlineData("#pragma once")]
    public void PreprocessorDirective_Classified(string line)
    {
        StyledSpan[] spans = Tokenize(line);
        Assert.Contains(spans, s => s.Kind == TokenKind.Directive);
    }
}
