using Wade.Highlighting;
using Wade.Highlighting.Languages;

namespace Wade.Tests.Highlighting;

public class PythonLanguageTests
{
    private static readonly ILanguage Lang = new PythonLanguage();

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
    public void HashComment_Classified()
    {
        StyledSpan[] spans = Tokenize("# this is a comment");
        Assert.Contains(spans, s => s.Kind == TokenKind.Comment && s.Start == 0);
    }

    [Fact]
    public void TripleDoubleQuote_SingleLine_Classified()
    {
        StyledSpan[] spans = Tokenize("\"\"\"docstring\"\"\"");
        Assert.Contains(spans, s => s.Kind == TokenKind.String);
    }

    [Fact]
    public void TripleSingleQuote_SingleLine_Classified()
    {
        StyledSpan[] spans = Tokenize("'''docstring'''");
        Assert.Contains(spans, s => s.Kind == TokenKind.String);
    }

    [Fact]
    public void TripleQuote_MultiLine_SpansAllLines()
    {
        StyledLine[] lines = TokenizeLines("\"\"\"", "content", "\"\"\"");
        Assert.All(lines, l => Assert.Contains(l.Spans ?? [], s => s.Kind == TokenKind.String));
    }

    [Fact]
    public void Decorator_Classified()
    {
        StyledSpan[] spans = Tokenize("@decorator");
        Assert.Contains(spans, s => s.Kind == TokenKind.Attribute && s.Start == 0);
    }

    [Theory]
    [InlineData("def")]
    [InlineData("class")]
    [InlineData("lambda")]
    [InlineData("import")]
    [InlineData("from")]
    public void PythonKeywords_Classified(string keyword)
    {
        StyledSpan[] spans = Tokenize(keyword);
        Assert.Contains(spans, s => s.Kind == TokenKind.Keyword);
    }

    [Theory]
    [InlineData("True", TokenKind.Constant)]
    [InlineData("False", TokenKind.Constant)]
    [InlineData("None", TokenKind.Constant)]
    public void PythonConstants_Classified(string word, TokenKind expected)
    {
        StyledSpan[] spans = Tokenize(word);
        Assert.Contains(spans, s => s.Kind == expected);
    }

    [Theory]
    [InlineData("print")]
    [InlineData("len")]
    [InlineData("range")]
    [InlineData("isinstance")]
    public void Builtins_Classified(string name)
    {
        StyledSpan[] spans = Tokenize(name);
        Assert.Contains(spans, s => s.Kind == TokenKind.BuiltinFunc);
    }

    [Fact]
    public void FString_Classified()
    {
        StyledSpan[] spans = Tokenize("f\"text {var}\"");
        Assert.Contains(spans, s => s.Kind == TokenKind.String);
    }
}
