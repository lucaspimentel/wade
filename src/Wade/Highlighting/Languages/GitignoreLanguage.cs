namespace Wade.Highlighting.Languages;

internal sealed class GitignoreLanguage : ILanguage
{
    public StyledLine TokenizeLine(string line, ref byte state)
    {
        if (line.Length == 0)
        {
            return new StyledLine(line, null);
        }

        // Skip leading whitespace for classification, but keep pos tracking from 0
        int pos = 0;
        while (pos < line.Length && line[pos] == ' ')
        {
            pos++;
        }

        if (pos >= line.Length)
        {
            return new StyledLine(line, null);
        }

        // Comment line
        if (line[pos] == '#')
        {
            return new StyledLine(line, [new StyledSpan(pos, line.Length - pos, TokenKind.Comment)]);
        }

        var spans = new List<StyledSpan>();

        // Negation prefix
        if (line[pos] == '!')
        {
            spans.Add(new StyledSpan(pos, 1, TokenKind.Operator));
            pos++;
        }

        // Scan the pattern
        while (pos < line.Length)
        {
            char ch = line[pos];

            switch (ch)
            {
                // Glob wildcards
                case '*':
                    // ** (doublestar)
                    if (pos + 1 < line.Length && line[pos + 1] == '*')
                    {
                        spans.Add(new StyledSpan(pos, 2, TokenKind.Keyword));
                        pos += 2;
                    }
                    else
                    {
                        spans.Add(new StyledSpan(pos, 1, TokenKind.Keyword));
                        pos++;
                    }
                    break;

                case '?':
                    spans.Add(new StyledSpan(pos, 1, TokenKind.Keyword));
                    pos++;
                    break;

                // Character class [...]
                case '[':
                    int close = line.IndexOf(']', pos + 1);
                    if (close >= 0)
                    {
                        int len = close + 1 - pos;
                        spans.Add(new StyledSpan(pos, len, TokenKind.String));
                        pos = close + 1;
                    }
                    else
                    {
                        pos++;
                    }
                    break;

                // Path separator
                case '/':
                    spans.Add(new StyledSpan(pos, 1, TokenKind.Punctuation));
                    pos++;
                    break;

                // Escape sequence
                case '\\':
                    if (pos + 1 < line.Length)
                    {
                        spans.Add(new StyledSpan(pos, 2, TokenKind.String));
                        pos += 2;
                    }
                    else
                    {
                        pos++;
                    }
                    break;

                default:
                    pos++;
                    break;
            }
        }

        return spans.Count == 0
            ? new StyledLine(line, null)
            : new StyledLine(line, [.. spans]);
    }
}
