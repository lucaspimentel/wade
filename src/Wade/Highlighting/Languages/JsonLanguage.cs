namespace Wade.Highlighting.Languages;

internal sealed class JsonLanguage : ILanguage
{
    public StyledLine TokenizeLine(string line, ref byte state)
    {
        if (line.Length == 0)
            return new StyledLine(line, null);

        var spans = new List<StyledSpan>();
        ScanJson(line, 0, spans);
        return spans.Count == 0
            ? new StyledLine(line, null)
            : new StyledLine(line, [.. spans]);
    }

    private static void ScanJson(string line, int start, List<StyledSpan> spans)
    {
        int pos = start;
        int len = line.Length;

        // Track whether we expect a value next (after ':') or a key
        bool expectKey = true;

        while (pos < len)
        {
            if (char.IsWhiteSpace(line[pos])) { pos++; continue; }

            char ch = line[pos];

            // Punctuation
            if (ch is '{' or '}' or '[' or ']' or ',')
            {
                spans.Add(new StyledSpan(pos, 1, TokenKind.Punctuation));
                if (ch is '{' or '[') expectKey = true;
                pos++;
                continue;
            }

            if (ch == ':')
            {
                spans.Add(new StyledSpan(pos, 1, TokenKind.Punctuation));
                expectKey = false;
                pos++;
                continue;
            }

            // String
            if (ch == '"')
            {
                int strStart = pos;
                pos++;
                while (pos < len)
                {
                    if (line[pos] == '\\') { pos += 2; continue; }
                    if (line[pos] == '"') { pos++; break; }
                    pos++;
                }

                TokenKind kind = expectKey ? TokenKind.Key : TokenKind.String;
                spans.Add(new StyledSpan(strStart, pos - strStart, kind));
                expectKey = false; // after key, expect ':' then value
                continue;
            }

            // Number
            if (char.IsDigit(ch) || ch == '-')
            {
                int numStart = pos;
                if (ch == '-') pos++;
                while (pos < len && (char.IsDigit(line[pos]) || line[pos] == '.' || line[pos] == 'e' || line[pos] == 'E' || line[pos] == '+' || line[pos] == '-'))
                    pos++;
                spans.Add(new StyledSpan(numStart, pos - numStart, TokenKind.Number));
                expectKey = false;
                continue;
            }

            // true / false / null
            if (line.AsSpan(pos).StartsWith("true"))  { spans.Add(new StyledSpan(pos, 4, TokenKind.Constant)); pos += 4; expectKey = false; continue; }
            if (line.AsSpan(pos).StartsWith("false")) { spans.Add(new StyledSpan(pos, 5, TokenKind.Constant)); pos += 5; expectKey = false; continue; }
            if (line.AsSpan(pos).StartsWith("null"))  { spans.Add(new StyledSpan(pos, 4, TokenKind.Constant)); pos += 4; expectKey = false; continue; }

            pos++;
        }
    }
}
