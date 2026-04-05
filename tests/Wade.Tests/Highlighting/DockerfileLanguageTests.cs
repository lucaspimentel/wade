using Wade.Highlighting;
using Wade.Highlighting.Languages;

namespace Wade.Tests.Highlighting;

public class DockerfileLanguageTests
{
    private static readonly ILanguage Lang = new DockerfileLanguage();

    private static StyledSpan[] Tokenize(string line)
    {
        byte state = 0;
        return Lang.TokenizeLine(line, ref state).Spans ?? [];
    }

    [Theory]
    [InlineData("FROM")]
    [InlineData("RUN")]
    [InlineData("CMD")]
    [InlineData("COPY")]
    [InlineData("ADD")]
    [InlineData("ENTRYPOINT")]
    [InlineData("WORKDIR")]
    [InlineData("ENV")]
    [InlineData("ARG")]
    [InlineData("EXPOSE")]
    [InlineData("VOLUME")]
    [InlineData("USER")]
    [InlineData("LABEL")]
    [InlineData("HEALTHCHECK")]
    [InlineData("STOPSIGNAL")]
    [InlineData("SHELL")]
    [InlineData("ONBUILD")]
    [InlineData("MAINTAINER")]
    public void Instruction_HighlightedAsKeyword(string instruction)
    {
        StyledSpan[] spans = Tokenize($"{instruction} something");
        Assert.Contains(spans, s => s.Kind == TokenKind.Keyword && s.Start == 0 && s.Length == instruction.Length);
    }

    [Fact]
    public void Comment_HighlightedAsComment()
    {
        StyledSpan[] spans = Tokenize("# this is a comment");
        Assert.Single(spans);
        Assert.Equal(TokenKind.Comment, spans[0].Kind);
        Assert.Equal(0, spans[0].Start);
    }

    [Fact]
    public void EmptyLine_ReturnsNullSpans()
    {
        byte state = 0;
        StyledLine result = Lang.TokenizeLine("", ref state);
        Assert.Null(result.Spans);
    }

    [Fact]
    public void QuotedString_HighlightedAsString()
    {
        StyledSpan[] spans = Tokenize("RUN echo \"hello world\"");
        Assert.Contains(spans, s => s.Kind == TokenKind.String);
    }

    [Fact]
    public void SingleQuotedString_HighlightedAsString()
    {
        StyledSpan[] spans = Tokenize("RUN echo 'hello world'");
        Assert.Contains(spans, s => s.Kind == TokenKind.String);
    }

    [Fact]
    public void FromAs_BothHighlightedAsKeyword()
    {
        StyledSpan[] spans = Tokenize("FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build");
        StyledSpan[] keywords = spans.Where(s => s.Kind == TokenKind.Keyword).ToArray();
        Assert.Equal(2, keywords.Length);
        Assert.Equal(0, keywords[0].Start); // FROM
        Assert.Equal(4, keywords[0].Length);
    }

    [Fact]
    public void Variable_DollarName_HighlightedAsConstant()
    {
        StyledSpan[] spans = Tokenize("WORKDIR $APP_DIR");
        Assert.Contains(spans, s => s.Kind == TokenKind.Constant);
    }

    [Fact]
    public void Variable_DollarBrace_HighlightedAsConstant()
    {
        StyledSpan[] spans = Tokenize("ENV PATH=${APP_DIR}/bin:$PATH");
        Assert.Contains(spans, s => s.Kind == TokenKind.Constant);
    }

    [Fact]
    public void Flag_HighlightedAsAttribute()
    {
        StyledSpan[] spans = Tokenize("COPY --from=builder /app /app");
        Assert.Contains(spans, s => s.Kind == TokenKind.Attribute);
    }

    [Fact]
    public void CaseInsensitive_Instructions()
    {
        StyledSpan[] spans = Tokenize("from ubuntu:22.04");
        Assert.Contains(spans, s => s.Kind == TokenKind.Keyword && s.Start == 0);
    }
}
