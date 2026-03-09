namespace Wade.Highlighting.Languages;

internal sealed class TomlLanguage : ILanguage
{
    public StyledLine TokenizeLine(string line, ref byte state)
    {
        if (line.Length == 0)
        {
            return new StyledLine(line, null);
        }

        var spans = new List<StyledSpan>();
        int len = line.Length;
        int pos = 0;

        // Handle continuation of multi-line string
        if (state == 2)
        {
            int closeIdx = line.IndexOf("\"\"\"", pos, StringComparison.Ordinal);
            if (closeIdx < 0)
            {
                spans.Add(new StyledSpan(0, len, TokenKind.String));
                return MakeResult(line, spans);
            }
            int closeEnd = closeIdx + 3;
            spans.Add(new StyledSpan(0, closeEnd, TokenKind.String));
            state = 0;
            pos = closeEnd;
        }

        while (pos < len)
        {
            if (char.IsWhiteSpace(line[pos])) { pos++; continue; }

            char ch = line[pos];

            // Comment
            if (ch == '#')
            {
                spans.Add(new StyledSpan(pos, len - pos, TokenKind.Comment));
                break;
            }

            // Section header: [section] or [[array]]
            if (ch == '[')
            {
                int end = line.IndexOf(']', pos);
                if (end >= 0 && line[pos + 1] == '[')
                {
                    // [[array table]]
                    int end2 = line.IndexOf("]]", pos + 2, StringComparison.Ordinal);
                    if (end2 >= 0)
                    {
                        spans.Add(new StyledSpan(pos, end2 + 2 - pos, TokenKind.Key));
                        pos = end2 + 2;
                        continue;
                    }
                }
                else if (end >= 0)
                {
                    spans.Add(new StyledSpan(pos, end + 1 - pos, TokenKind.Key));
                    pos = end + 1;
                    continue;
                }

                spans.Add(new StyledSpan(pos, 1, TokenKind.Punctuation));
                pos++;
                continue;
            }

            // Punctuation
            if (ch is ']' or '{' or '}' or ',')
            {
                spans.Add(new StyledSpan(pos, 1, TokenKind.Punctuation));
                pos++;
                continue;
            }

            // Key = value pattern
            if (char.IsLetterOrDigit(ch) || ch == '_' || ch == '-' || ch == '"')
            {
                int keyStart = pos;
                // Quoted key
                if (ch == '"')
                {
                    pos++;
                    while (pos < len && line[pos] != '"')
                    {
                        pos++;
                    }

                    if (pos < len)
                    {
                        pos++;
                    }
                }
                else
                {
                    while (pos < len && (char.IsLetterOrDigit(line[pos]) || line[pos] == '_' || line[pos] == '-' || line[pos] == '.'))
                    {
                        pos++;
                    }
                }

                int keyEnd = pos;

                // Skip whitespace
                while (pos < len && line[pos] == ' ')
                {
                    pos++;
                }

                if (pos < len && line[pos] == '=')
                {
                    spans.Add(new StyledSpan(keyStart, keyEnd - keyStart, TokenKind.Key));
                    spans.Add(new StyledSpan(pos, 1, TokenKind.Operator));
                    pos++;
                    // Scan value
                    ScanValue(line, pos, len, spans, ref state);
                    break;
                }
                // Not a key=value pattern
                continue;
            }

            pos++;
        }

        return MakeResult(line, spans);
    }

    private static void ScanValue(string line, int start, int len, List<StyledSpan> spans, ref byte state)
    {
        int pos = start;
        while (pos < len && line[pos] == ' ')
        {
            pos++;
        }

        if (pos >= len)
        {
            return;
        }

        char ch = line[pos];

        // Multi-line string: """
        if (line.AsSpan(pos).StartsWith("\"\"\""))
        {
            int closeIdx = line.IndexOf("\"\"\"", pos + 3, StringComparison.Ordinal);
            if (closeIdx >= 0)
            {
                spans.Add(new StyledSpan(pos, closeIdx + 3 - pos, TokenKind.String));
                return;
            }
            spans.Add(new StyledSpan(pos, len - pos, TokenKind.String));
            state = 2;
            return;
        }

        // Regular string
        if (ch == '"' || ch == '\'')
        {
            char quote = ch;
            int p = pos + 1;
            while (p < len)
            {
                if (line[p] == '\\' && quote == '"') { p += 2; continue; }
                if (line[p] == quote) { p++; break; }
                p++;
            }

            spans.Add(new StyledSpan(pos, p - pos, TokenKind.String));
            return;
        }

        // Boolean / null
        if (line.AsSpan(pos).StartsWith("true"))  { spans.Add(new StyledSpan(pos, 4, TokenKind.Constant)); return; }
        if (line.AsSpan(pos).StartsWith("false")) { spans.Add(new StyledSpan(pos, 5, TokenKind.Constant)); return; }

        // Number or date
        if (char.IsDigit(ch) || ch == '-' || ch == '+')
        {
            int numStart = pos;
            while (pos < len && !char.IsWhiteSpace(line[pos]) && line[pos] != '#' && line[pos] != ',')
            {
                pos++;
            }

            spans.Add(new StyledSpan(numStart, pos - numStart, TokenKind.Number));
            return;
        }

        // Array [ ... ]
        if (ch == '[')
        {
            // Let the main loop handle it
        }
    }

    private static StyledLine MakeResult(string line, List<StyledSpan> spans) =>
        spans.Count == 0 ? new StyledLine(line, null) : new StyledLine(line, [.. spans]);
}
