using System.Collections.Frozen;

namespace Wade.Highlighting.Languages;

internal sealed class PowerShellLanguage : RegexLanguage
{
    protected override FrozenSet<string> Keywords { get; } = new[]
    {
        "begin", "break", "catch", "class", "continue", "data", "define",
        "do", "dynamicparam", "else", "elseif", "end", "enum", "exit",
        "filter", "finally", "for", "foreach", "from", "function", "if",
        "in", "inlinescript", "parallel", "param", "process", "return",
        "sequence", "switch", "throw", "trap", "try", "until", "using",
        "var", "while", "workflow",
        // Comparison operators
        "-eq", "-ne", "-lt", "-le", "-gt", "-ge",
        "-and", "-or", "-not", "-xor",
        "-match", "-notmatch", "-like", "-notlike",
        "-contains", "-notcontains", "-in", "-notin",
    }.ToFrozenSet();

    protected override FrozenSet<string> Constants { get; } = new[]
    {
        "$true", "$false", "$null",
    }.ToFrozenSet();

    protected override FrozenSet<string> Builtins { get; } = FrozenSet<string>.Empty;

    protected override string? LineCommentPrefix => "#";
    protected override (string Open, string Close)? BlockComment => ("<#", "#>");

    protected override int TryMatchExtension(string line, int pos, List<StyledSpan> spans)
    {
        // Attributes: [Parameter()], [ValidateNotNull()], etc.
        if (line[pos] == '[')
        {
            int end = pos + 1;
            int depth = 1;
            while (end < line.Length && depth > 0)
            {
                if (line[end] == '[')
                {
                    depth++;
                }
                else if (line[end] == ']')
                {
                    depth--;
                }

                end++;
            }

            if (depth == 0)
            {
                spans.Add(new StyledSpan(pos, end - pos, TokenKind.Attribute));
                return end - pos;
            }
        }

        return 0;
    }
}
