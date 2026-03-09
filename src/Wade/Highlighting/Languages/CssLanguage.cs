using System.Collections.Frozen;

namespace Wade.Highlighting.Languages;

internal sealed class CssLanguage : RegexLanguage
{
    protected override FrozenSet<string> Keywords { get; } = new[]
    {
        "important", "inherit", "initial", "unset", "revert",
        "auto", "none", "normal", "bold", "italic",
        "flex", "grid", "block", "inline", "inline-block",
        "relative", "absolute", "fixed", "sticky", "static",
        "@media", "@keyframes", "@import", "@charset", "@font-face",
        "@supports", "@layer",
    }.ToFrozenSet();

    protected override FrozenSet<string> Constants { get; } = FrozenSet<string>.Empty;
    protected override FrozenSet<string> Builtins  { get; } = FrozenSet<string>.Empty;

    protected override string? LineCommentPrefix => null; // CSS only has /* */

    public override StyledLine TokenizeLine(string line, ref byte state)
    {
        if (line.Length == 0)
        {
            return new StyledLine(line, null);
        }

        var spans = new List<StyledSpan>();

        if (state == StateBlockComment)
        {
            int closeIdx = line.IndexOf("*/", StringComparison.Ordinal);
            if (closeIdx < 0)
            {
                spans.Add(new StyledSpan(0, line.Length, TokenKind.Comment));
                return MakeResult(line, spans);
            }
            int closeEnd = closeIdx + 2;
            spans.Add(new StyledSpan(0, closeEnd, TokenKind.Comment));
            state = StateNormal;
            ScanCss(line, closeEnd, spans, ref state);
            return MakeResult(line, spans);
        }

        ScanCss(line, 0, spans, ref state);
        return MakeResult(line, spans);
    }

    private void ScanCss(string line, int start, List<StyledSpan> spans, ref byte state)
    {
        int pos = start;
        int len = line.Length;

        while (pos < len)
        {
            if (char.IsWhiteSpace(line[pos])) { pos++; continue; }

            // Block comment
            if (line.AsSpan(pos).StartsWith("/*"))
            {
                int closeIdx = line.IndexOf("*/", pos + 2, StringComparison.Ordinal);
                if (closeIdx >= 0)
                {
                    spans.Add(new StyledSpan(pos, closeIdx + 2 - pos, TokenKind.Comment));
                    pos = closeIdx + 2;
                    continue;
                }
                spans.Add(new StyledSpan(pos, len - pos, TokenKind.Comment));
                state = StateBlockComment;
                return;
            }

            // String values
            if (line[pos] == '"' || line[pos] == '\'')
            {
                byte dummy = 0;
                bool _ = TryMatchString(line, pos, spans, ref dummy, out int strEnd);
                pos = strEnd;
                continue;
            }

            // Property: value pattern — scan identifier, then check for ':'
            if (char.IsLetter(line[pos]) || line[pos] == '-' || line[pos] == '_')
            {
                int idStart = pos;
                while (pos < len && (char.IsLetterOrDigit(line[pos]) || line[pos] == '-' || line[pos] == '_'))
                {
                    pos++;
                }

                // Find next non-whitespace
                int afterId = pos;
                while (afterId < len && line[afterId] == ' ')
                {
                    afterId++;
                }

                if (afterId < len && line[afterId] == ':')
                {
                    // CSS property key
                    spans.Add(new StyledSpan(idStart, pos - idStart, TokenKind.Key));
                }

                continue;
            }

            // Punctuation and other chars
            if (line[pos] is '{' or '}' or '(' or ')' or ';' or ',')
            {
                spans.Add(new StyledSpan(pos, 1, TokenKind.Punctuation));
                pos++;
                continue;
            }

            if (line[pos] == ':')
            {
                spans.Add(new StyledSpan(pos, 1, TokenKind.Punctuation));
                pos++;
                continue;
            }

            pos++;
        }
    }
}
