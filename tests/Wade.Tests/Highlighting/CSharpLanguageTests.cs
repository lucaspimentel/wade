using Wade.Highlighting;
using Wade.Highlighting.Languages;

namespace Wade.Tests.Highlighting;

public class CSharpLanguageTests
{
    private static readonly ILanguage Lang = new CSharpLanguage();

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
            result[i] = Lang.TokenizeLine(lines[i], ref state);
        return result;
    }

    [Theory]
    [InlineData("namespace")]
    [InlineData("using")]
    [InlineData("var")]
    [InlineData("async")]
    [InlineData("await")]
    [InlineData("record")]
    [InlineData("required")]
    public void CSharpKeywords_Classified(string keyword)
    {
        var spans = Tokenize(keyword);
        Assert.Contains(spans, s => s.Kind == TokenKind.Keyword);
    }

    [Theory]
    [InlineData("[Obsolete]")]
    [InlineData("[Test(\"x\")]")]
    [InlineData("[SerializeField]")]
    public void Attributes_Classified(string line)
    {
        var spans = Tokenize(line);
        Assert.Contains(spans, s => s.Kind == TokenKind.Attribute && s.Start == 0);
    }

    [Theory]
    [InlineData("#if DEBUG")]
    [InlineData("#pragma warning disable")]
    [InlineData("#nullable enable")]
    public void PreprocessorDirectives_ClassifiedAsDirective(string line)
    {
        var spans = Tokenize(line);
        Assert.Contains(spans, s => s.Kind == TokenKind.Directive);
    }

    [Fact]
    public void RawStringLiteral_SingleLine_Classified()
    {
        var spans = Tokenize("\"\"\"triple\"\"\"");
        Assert.Contains(spans, s => s.Kind == TokenKind.String && s.Start == 0);
    }

    [Fact]
    public void RawStringLiteral_MultiLine_SpansAllLines()
    {
        var lines = TokenizeLines("\"\"\"", "content", "\"\"\"");
        Assert.All(lines, l => Assert.Contains(l.Spans ?? [], s => s.Kind == TokenKind.String));
    }

    [Fact]
    public void VerbatimString_Classified()
    {
        var spans = Tokenize("@\"path\\to\"");
        Assert.Contains(spans, s => s.Kind == TokenKind.String && s.Start == 0);
    }

    [Fact]
    public void InterpolatedString_Classified()
    {
        var spans = Tokenize("$\"hello {name}\"");
        Assert.Contains(spans, s => s.Kind == TokenKind.String && s.Start == 0);
    }

    [Theory]
    [InlineData("bool")]
    [InlineData("int")]
    [InlineData("string")]
    [InlineData("void")]
    public void BuiltinTypes_ClassifiedAsBuiltin(string type)
    {
        var spans = Tokenize(type);
        Assert.Contains(spans, s => s.Kind == TokenKind.BuiltinFunc);
    }
}
