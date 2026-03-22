using Wade.Highlighting;
using Wade.Highlighting.Languages;

namespace Wade.Tests.Highlighting;

public class GoLanguageTests
{
    private static readonly ILanguage Lang = new GoLanguage();

    private static StyledSpan[] Tokenize(string line)
    {
        byte state = 0;
        return Lang.TokenizeLine(line, ref state).Spans ?? [];
    }

    [Fact]
    public void BacktickString_Classified()
    {
        StyledSpan[] spans = Tokenize("`raw string`");
        Assert.Contains(spans, s => s.Kind == TokenKind.String && s.Start == 0);
    }

    [Theory]
    [InlineData("func")]
    [InlineData("defer")]
    [InlineData("go")]
    [InlineData("chan")]
    [InlineData("goroutine")]
    public void GoKeywords_Classified(string keyword)
    {
        // "goroutine" is not a keyword, just test actual ones
        if (keyword == "goroutine")
        {
            return; // skip
        }

        StyledSpan[] spans = Tokenize(keyword);
        Assert.Contains(spans, s => s.Kind == TokenKind.Keyword);
    }

    [Theory]
    [InlineData("nil", TokenKind.Constant)]
    [InlineData("iota", TokenKind.Constant)]
    [InlineData("true", TokenKind.Constant)]
    [InlineData("false", TokenKind.Constant)]
    public void GoConstants_Classified(string word, TokenKind expected)
    {
        StyledSpan[] spans = Tokenize(word);
        Assert.Contains(spans, s => s.Kind == expected);
    }

    [Theory]
    [InlineData("make")]
    [InlineData("append")]
    [InlineData("len")]
    public void GoBuiltins_Classified(string name)
    {
        StyledSpan[] spans = Tokenize(name);
        Assert.Contains(spans, s => s.Kind == TokenKind.BuiltinFunc);
    }
}
