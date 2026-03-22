namespace Wade.Highlighting;

internal interface ILanguage
{
    /// <summary>
    /// Tokenizes a single line. <paramref name="state"/> carries multi-line context
    /// (e.g. inside block comment or multi-line string) across calls.
    /// State values: 0 = normal, 1 = block comment, 2 = multi-line string.
    /// </summary>
    public StyledLine TokenizeLine(string line, ref byte state);
}
