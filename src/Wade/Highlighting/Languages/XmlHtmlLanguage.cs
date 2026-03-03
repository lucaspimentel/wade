namespace Wade.Highlighting.Languages;

internal sealed class XmlHtmlLanguage : ILanguage
{
    private const byte StateNormal  = 0;
    private const byte StateComment = 1;
    private const byte StateTag     = 2;

    public StyledLine TokenizeLine(string line, ref byte state)
    {
        if (line.Length == 0)
            return new StyledLine(line, null);

        var spans = new List<StyledSpan>();
        int pos = 0;
        int len = line.Length;

        if (state == StateComment)
        {
            int closeIdx = line.IndexOf("-->", StringComparison.Ordinal);
            if (closeIdx < 0)
            {
                spans.Add(new StyledSpan(0, len, TokenKind.Comment));
                return MakeResult(line, spans);
            }
            int closeEnd = closeIdx + 3;
            spans.Add(new StyledSpan(0, closeEnd, TokenKind.Comment));
            state = StateNormal;
            pos = closeEnd;
        }

        while (pos < len)
        {
            // Comment: <!-- ... -->
            if (line.AsSpan(pos).StartsWith("<!--"))
            {
                int closeIdx = line.IndexOf("-->", pos + 4, StringComparison.Ordinal);
                if (closeIdx >= 0)
                {
                    spans.Add(new StyledSpan(pos, closeIdx + 3 - pos, TokenKind.Comment));
                    pos = closeIdx + 3;
                    continue;
                }
                spans.Add(new StyledSpan(pos, len - pos, TokenKind.Comment));
                state = StateComment;
                return MakeResult(line, spans);
            }

            // Tag: <tagname ...> or </tagname> or <tagname />
            if (line[pos] == '<')
            {
                int tagStart = pos;
                pos++;
                bool isClose = pos < len && line[pos] == '/';
                if (isClose) pos++;

                // Tag name
                int nameStart = pos;
                while (pos < len && (char.IsLetterOrDigit(line[pos]) || line[pos] == '-' || line[pos] == ':' || line[pos] == '_'))
                    pos++;
                int nameEnd = pos;

                if (nameEnd > nameStart)
                    spans.Add(new StyledSpan(nameStart, nameEnd - nameStart, TokenKind.TagName));

                // Scan attributes until '>'
                while (pos < len && line[pos] != '>')
                {
                    if (char.IsWhiteSpace(line[pos])) { pos++; continue; }

                    if (line[pos] == '/')
                    {
                        spans.Add(new StyledSpan(pos, 1, TokenKind.Punctuation));
                        pos++;
                        continue;
                    }

                    // Attribute name
                    int attrStart = pos;
                    while (pos < len && line[pos] != '=' && line[pos] != '>' && !char.IsWhiteSpace(line[pos]))
                        pos++;
                    if (pos > attrStart)
                        spans.Add(new StyledSpan(attrStart, pos - attrStart, TokenKind.AttrName));

                    if (pos < len && line[pos] == '=')
                    {
                        spans.Add(new StyledSpan(pos, 1, TokenKind.Operator));
                        pos++;

                        // Attribute value
                        if (pos < len && (line[pos] == '"' || line[pos] == '\''))
                        {
                            char q = line[pos];
                            int valStart = pos;
                            pos++;
                            while (pos < len && line[pos] != q) pos++;
                            if (pos < len) pos++;
                            spans.Add(new StyledSpan(valStart, pos - valStart, TokenKind.AttrValue));
                        }
                    }
                }

                if (pos < len && line[pos] == '>')
                {
                    spans.Add(new StyledSpan(pos, 1, TokenKind.Punctuation));
                    pos++;
                }
                continue;
            }

            // Entity reference: &amp; &lt; etc.
            if (line[pos] == '&')
            {
                int entityStart = pos;
                pos++;
                while (pos < len && line[pos] != ';' && !char.IsWhiteSpace(line[pos]))
                    pos++;
                if (pos < len && line[pos] == ';') pos++;
                spans.Add(new StyledSpan(entityStart, pos - entityStart, TokenKind.Constant));
                continue;
            }

            pos++;
        }

        return MakeResult(line, spans);
    }

    private static StyledLine MakeResult(string line, List<StyledSpan> spans) =>
        spans.Count == 0 ? new StyledLine(line, null) : new StyledLine(line, [.. spans]);
}
