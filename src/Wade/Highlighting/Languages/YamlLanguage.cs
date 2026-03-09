namespace Wade.Highlighting.Languages;

internal sealed class YamlLanguage : ILanguage
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

        // Skip indentation
        while (pos < len && line[pos] == ' ')
        {
            pos++;
        }

        if (pos >= len)
        {
            return new StyledLine(line, null);
        }

        char ch = line[pos];

        // Comment
        if (ch == '#')
        {
            spans.Add(new StyledSpan(pos, len - pos, TokenKind.Comment));
            return MakeResult(line, spans);
        }

        // Document markers: --- or ...
        if (line.AsSpan(pos).StartsWith("---") || line.AsSpan(pos).StartsWith("..."))
        {
            spans.Add(new StyledSpan(pos, 3, TokenKind.Directive));
            return MakeResult(line, spans);
        }

        // List item: - value
        if (ch == '-' && (pos + 1 >= len || line[pos + 1] == ' '))
        {
            spans.Add(new StyledSpan(pos, 1, TokenKind.Punctuation));
            pos++;
            if (pos < len && line[pos] == ' ')
            {
                pos++;
            }

            ScanYamlValue(line, pos, len, spans);
            return MakeResult(line, spans);
        }

        // Key: value
        int keyStart = pos;
        // Quoted key
        if (ch == '"' || ch == '\'')
        {
            char q = ch;
            pos++;
            while (pos < len && line[pos] != q)
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
            // Unquoted key: scan to ':'
            while (pos < len && line[pos] != ':' && line[pos] != '#')
            {
                pos++;
            }
        }

        int keyEnd = pos;

        // Check for ':'
        int afterKey = pos;
        while (afterKey < len && line[afterKey] == ' ')
        {
            afterKey++;
        }

        if (afterKey < len && line[afterKey] == ':')
        {
            spans.Add(new StyledSpan(keyStart, keyEnd - keyStart, TokenKind.Key));
            spans.Add(new StyledSpan(afterKey, 1, TokenKind.Punctuation));
            int valueStart = afterKey + 1;
            while (valueStart < len && line[valueStart] == ' ')
            {
                valueStart++;
            }

            if (valueStart < len)
            {
                ScanYamlValue(line, valueStart, len, spans);
            }

            return MakeResult(line, spans);
        }

        // Plain value (continuation line, or multiline block)
        ScanYamlValue(line, keyStart, len, spans);
        return MakeResult(line, spans);
    }

    private static void ScanYamlValue(string line, int pos, int len, List<StyledSpan> spans)
    {
        if (pos >= len)
        {
            return;
        }

        char ch = line[pos];

        // Comment at end
        int commentIdx = line.IndexOf(" #", pos, StringComparison.Ordinal);

        // Quoted string
        if (ch == '"' || ch == '\'')
        {
            char q = ch;
            int p = pos + 1;
            while (p < len)
            {
                if (q == '"' && line[p] == '\\') { p += 2; continue; }
                if (line[p] == q) { p++; break; }
                p++;
            }

            spans.Add(new StyledSpan(pos, p - pos, TokenKind.String));
            if (commentIdx >= p)
            {
                spans.Add(new StyledSpan(commentIdx + 1, len - commentIdx - 1, TokenKind.Comment));
            }

            return;
        }

        // Boolean / null
        var valueSpan = line.AsSpan(pos);
        if (valueSpan.StartsWith("true") || valueSpan.StartsWith("false") ||
            valueSpan.StartsWith("yes") || valueSpan.StartsWith("no") ||
            valueSpan.StartsWith("null") || valueSpan.StartsWith("~"))
        {
            int end = commentIdx > pos ? commentIdx : len;
            spans.Add(new StyledSpan(pos, end - pos, TokenKind.Constant));
            if (commentIdx > pos)
            {
                spans.Add(new StyledSpan(commentIdx + 1, len - commentIdx - 1, TokenKind.Comment));
            }

            return;
        }

        // Number
        if (char.IsDigit(ch) || (ch == '-' && pos + 1 < len && char.IsDigit(line[pos + 1])))
        {
            int p = pos;
            while (p < len && !char.IsWhiteSpace(line[p]) && line[p] != '#')
            {
                p++;
            }

            spans.Add(new StyledSpan(pos, p - pos, TokenKind.Number));
            if (commentIdx >= p)
            {
                spans.Add(new StyledSpan(commentIdx + 1, len - commentIdx - 1, TokenKind.Comment));
            }

            return;
        }

        // Plain string — no span (leave as default)
        if (commentIdx > pos)
        {
            spans.Add(new StyledSpan(commentIdx + 1, len - commentIdx - 1, TokenKind.Comment));
        }
    }

    private static StyledLine MakeResult(string line, List<StyledSpan> spans) =>
        spans.Count == 0 ? new StyledLine(line, null) : new StyledLine(line, [.. spans]);
}
