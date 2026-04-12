using System.Collections.Frozen;
using System.Globalization;
using Wade.Terminal;

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

    protected override FrozenSet<string> Builtins { get; } = FrozenSet<string>.Empty;

    protected override string? LineCommentPrefix => null; // CSS only has /* */

    private readonly record struct HexColorMatch(int Start, int HexLength, Color Color);

    public override StyledLine TokenizeLine(string line, ref byte state)
    {
        if (line.Length == 0)
        {
            return new StyledLine(line, null);
        }

        var spans = new List<StyledSpan>();
        List<HexColorMatch>? matches = null;

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
            ScanCss(line, closeEnd, spans, ref state, ref matches);
        }
        else
        {
            ScanCss(line, 0, spans, ref state, ref matches);
        }

        if (matches is null || matches.Count == 0)
        {
            return MakeResult(line, spans);
        }

        return BuildSwatchResult(line, spans, matches);
    }

    private void ScanCss(string line, int start, List<StyledSpan> spans, ref byte state, ref List<HexColorMatch>? matches)
    {
        int pos = start;
        int len = line.Length;
        bool afterColon = false;

        while (pos < len)
        {
            if (char.IsWhiteSpace(line[pos]))
            {
                pos++;
                continue;
            }

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
            if (line[pos] is '"' or '\'')
            {
                byte dummy = 0;
                bool _ = TryMatchString(line, pos, spans, ref dummy, out int strEnd);
                pos = strEnd;
                continue;
            }

            // Hex color literal — only in value position
            if (afterColon && line[pos] == '#' && TryMatchHexColor(line, pos, out int hexLen, out Color color))
            {
                spans.Add(new StyledSpan(pos, 1 + hexLen, TokenKind.HexColor));
                matches ??= [];
                matches.Add(new HexColorMatch(pos, hexLen, color));
                pos += 1 + hexLen;
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
                if (line[pos] is ';' or '{' or '}')
                {
                    afterColon = false;
                }

                pos++;
                continue;
            }

            if (line[pos] == ':')
            {
                spans.Add(new StyledSpan(pos, 1, TokenKind.Punctuation));
                afterColon = true;
                pos++;
                continue;
            }

            pos++;
        }
    }

    private static bool TryMatchHexColor(string line, int pos, out int hexLength, out Color color)
    {
        hexLength = 0;
        color = default;

        // Count hex digits after the '#'
        int runLen = 0;
        int i = pos + 1;
        while (i < line.Length && IsHexDigit(line[i]))
        {
            runLen++;
            i++;
        }

        // Only accept exact lengths 3, 4, 6, 8
        if (runLen is not (3 or 4 or 6 or 8))
        {
            return false;
        }

        // Trailing character must be non-hex-digit (already guaranteed by loop exit)
        ReadOnlySpan<char> hex = line.AsSpan(pos + 1, runLen);
        if (!TryParseHexColor(hex, out color))
        {
            return false;
        }

        hexLength = runLen;
        return true;
    }

    private static bool IsHexDigit(char c) =>
        (c >= '0' && c <= '9') || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F');

    private static bool TryParseHexColor(ReadOnlySpan<char> hex, out Color color)
    {
        color = default;

        switch (hex.Length)
        {
            case 3:
            case 4:
                if (!TryParseNibble(hex[0], out int r3) ||
                    !TryParseNibble(hex[1], out int g3) ||
                    !TryParseNibble(hex[2], out int b3))
                {
                    return false;
                }

                color = new Color((byte)((r3 << 4) | r3), (byte)((g3 << 4) | g3), (byte)((b3 << 4) | b3));
                return true;

            case 6:
            case 8:
                if (!byte.TryParse(hex[0..2], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out byte r) ||
                    !byte.TryParse(hex[2..4], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out byte g) ||
                    !byte.TryParse(hex[4..6], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out byte b))
                {
                    return false;
                }

                color = new Color(r, g, b);
                return true;

            default:
                return false;
        }
    }

    private static bool TryParseNibble(char c, out int value)
    {
        if (c >= '0' && c <= '9')
        {
            value = c - '0';
            return true;
        }

        if (c >= 'a' && c <= 'f')
        {
            value = 10 + (c - 'a');
            return true;
        }

        if (c >= 'A' && c <= 'F')
        {
            value = 10 + (c - 'A');
            return true;
        }

        value = 0;
        return false;
    }

    private static StyledLine BuildSwatchResult(string line, List<StyledSpan> spans, List<HexColorMatch> matches)
    {
        const int SwatchLen = 3; // ' ██'
        int newLen = line.Length + (matches.Count * SwatchLen);
        var newText = new char[newLen];
        var charStyles = new CellStyle[newLen];

        int matchIdx = 0;
        int outPos = 0;

        for (int i = 0; i < line.Length; i++)
        {
            newText[outPos] = line[i];
            charStyles[outPos] = StyleAt(i, spans);
            outPos++;

            // After writing the last char of a hex literal, append the swatch cells.
            if (matchIdx < matches.Count && i == matches[matchIdx].Start + matches[matchIdx].HexLength)
            {
                Color c = matches[matchIdx].Color;
                var swatchStyle = new CellStyle(c, null);

                newText[outPos] = ' ';
                charStyles[outPos] = default;
                outPos++;

                newText[outPos] = '\u2588';
                charStyles[outPos] = swatchStyle;
                outPos++;

                newText[outPos] = '\u2588';
                charStyles[outPos] = swatchStyle;
                outPos++;

                matchIdx++;
            }
        }

        // Shift span offsets to match the inserted swatch cells so tests (and any
        // future introspection) can still see the tokenization. CharStyles takes
        // priority at render time, so spans don't affect visual output here.
        var shiftedSpans = new StyledSpan[spans.Count];
        for (int s = 0; s < spans.Count; s++)
        {
            StyledSpan span = spans[s];
            int shift = 0;
            for (int m = 0; m < matches.Count; m++)
            {
                int insertionPoint = matches[m].Start + 1 + matches[m].HexLength;
                if (span.Start >= insertionPoint)
                {
                    shift += SwatchLen;
                }
            }

            shiftedSpans[s] = new StyledSpan(span.Start + shift, span.Length, span.Kind);
        }

        return new StyledLine(new string(newText), shiftedSpans, charStyles);
    }

    private static CellStyle StyleAt(int index, List<StyledSpan> spans)
    {
        foreach (StyledSpan span in spans)
        {
            if (index >= span.Start && index < span.Start + span.Length)
            {
                return SyntaxTheme.GetStyle(span.Kind);
            }
        }

        return SyntaxTheme.Plain;
    }
}
