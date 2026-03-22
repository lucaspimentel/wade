using System.Collections.Frozen;

namespace Wade.Highlighting.Languages;

/// <summary>
/// Abstract base for C-like languages. Implements a single-pass left-to-right scanner
/// with pluggable keyword/constant/builtin sets and extension points for language specifics.
/// </summary>
internal abstract class RegexLanguage : ILanguage
{
    // State constants shared by all subclasses
    protected const byte StateNormal = 0;
    protected const byte StateBlockComment = 1;
    protected const byte StateMultiString = 2;

    protected abstract FrozenSet<string> Keywords { get; }

    protected abstract FrozenSet<string> Constants { get; }

    protected abstract FrozenSet<string> Builtins { get; }

    /// <summary>Single-line comment prefix (e.g. "//"). Null if unsupported.</summary>
    protected virtual string? LineCommentPrefix => "//";

    /// <summary>Block comment open/close (e.g. "/*", "*/"). Null if unsupported.</summary>
    protected virtual (string Open, string Close)? BlockComment => ("/*", "*/");

    public virtual StyledLine TokenizeLine(string line, ref byte state)
    {
        if (line.Length == 0)
        {
            return new StyledLine(line, null);
        }

        var spans = new List<StyledSpan>();

        // Handle continuation of block comment from previous line
        if (state == StateBlockComment)
        {
            (_, string close) = BlockComment!.Value;
            int end = line.IndexOf(close, StringComparison.Ordinal);
            if (end < 0)
            {
                // Entire line is comment
                spans.Add(new StyledSpan(0, line.Length, TokenKind.Comment));
                return MakeResult(line, spans);
            }

            int closeEnd = end + close.Length;
            spans.Add(new StyledSpan(0, closeEnd, TokenKind.Comment));
            state = StateNormal;
            ScanLine(line, closeEnd, spans, ref state);
            return MakeResult(line, spans);
        }

        // Handle continuation of multi-line string from previous line
        if (state == StateMultiString)
        {
            int end = TryEndMultiString(line, 0, spans, ref state);
            if (state == StateMultiString)
            {
                return MakeResult(line, spans); // still in multi-line string
            }

            ScanLine(line, end, spans, ref state);
            return MakeResult(line, spans);
        }

        int prefixEnd = TryMatchLinePrefix(line, spans, ref state);
        if (prefixEnd < 0)
        {
            return MakeResult(line, spans);
        }

        ScanLine(line, prefixEnd, spans, ref state);
        return MakeResult(line, spans);
    }

    /// <summary>
    /// Called before the main scan loop on each line. Subclasses may emit spans and
    /// return the new position to continue scanning from, or -1 to skip main loop entirely.
    /// </summary>
    protected virtual int TryMatchLinePrefix(string line, List<StyledSpan> spans, ref byte state) => 0;

    /// <summary>
    /// Called when a character is not handled by the base scanner.
    /// Returns length of span consumed (0 = not handled).
    /// </summary>
    protected virtual int TryMatchExtension(string line, int pos, List<StyledSpan> spans) => 0;

    /// <summary>
    /// When state == StateMultiString, try to find the end of the multi-line string
    /// starting at <paramref name="pos"/>. Emits spans and updates state.
    /// Returns position after the closing delimiter (or line.Length if still open).
    /// </summary>
    protected virtual int TryEndMultiString(string line, int pos, List<StyledSpan> spans, ref byte state)
    {
        // Default: no multi-line strings; subclasses override
        state = StateNormal;
        return pos;
    }

    protected void ScanLine(string line, int start, List<StyledSpan> spans, ref byte state)
    {
        int pos = start;
        int len = line.Length;

        while (pos < len)
        {
            // Skip whitespace
            if (char.IsWhiteSpace(line[pos]))
            {
                pos++;
                continue;
            }

            // Line comment
            if (LineCommentPrefix is { } lcp && line.AsSpan(pos).StartsWith(lcp))
            {
                spans.Add(new StyledSpan(pos, len - pos, TokenKind.Comment));
                return;
            }

            // Block comment open
            if (BlockComment is { } bc && line.AsSpan(pos).StartsWith(bc.Open))
            {
                int closeIdx = line.IndexOf(bc.Close, pos + bc.Open.Length, StringComparison.Ordinal);
                if (closeIdx >= 0)
                {
                    int commentEnd = closeIdx + bc.Close.Length;
                    spans.Add(new StyledSpan(pos, commentEnd - pos, TokenKind.Comment));
                    pos = commentEnd;
                    continue;
                }

                // Multi-line block comment
                spans.Add(new StyledSpan(pos, len - pos, TokenKind.Comment));
                state = StateBlockComment;
                return;
            }

            // Extension hook (language-specific patterns like attributes, directives)
            int extLen = TryMatchExtension(line, pos, spans);
            if (extLen > 0)
            {
                pos += extLen;
                continue;
            }

            // Strings
            if (TryMatchString(line, pos, spans, ref state, out int strEnd))
            {
                if (state == StateMultiString)
                {
                    return;
                }

                pos = strEnd;
                continue;
            }

            // Numbers
            if (TryMatchNumber(line, pos, out int numEnd))
            {
                spans.Add(new StyledSpan(pos, numEnd - pos, TokenKind.Number));
                pos = numEnd;
                continue;
            }

            // Identifiers / keywords
            if (char.IsLetter(line[pos]) || line[pos] == '_')
            {
                int idEnd = pos + 1;
                while (idEnd < len && (char.IsLetterOrDigit(line[idEnd]) || line[idEnd] == '_'))
                {
                    idEnd++;
                }

                string word = line[pos..idEnd];
                TokenKind kind = ClassifyWord(word);
                if (kind != TokenKind.Plain)
                {
                    spans.Add(new StyledSpan(pos, idEnd - pos, kind));
                }

                pos = idEnd;
                continue;
            }

            // Operators and punctuation
            if (TryMatchOperatorOrPunct(line, pos, spans, out int opEnd))
            {
                pos = opEnd;
                continue;
            }

            pos++;
        }
    }

    /// <summary>
    /// Attempt to match a string literal at <paramref name="pos"/>.
    /// Returns true if a string was matched; sets <paramref name="end"/> to position after.
    /// Base implementation handles single-quoted and double-quoted strings.
    /// </summary>
    protected virtual bool TryMatchString(string line, int pos, List<StyledSpan> spans, ref byte state, out int end)
    {
        char ch = line[pos];
        if (ch is '"' or '\'')
        {
            end = ScanQuotedString(line, pos, ch, spans);
            return true;
        }

        end = pos;
        return false;
    }

    /// <summary>Scan a simple quoted string, handling backslash escapes.</summary>
    protected static int ScanQuotedString(string line, int start, char quote, List<StyledSpan> spans)
    {
        int pos = start + 1;
        while (pos < line.Length)
        {
            char c = line[pos];
            if (c == '\\')
            {
                pos += 2;
                continue;
            }

            if (c == quote)
            {
                pos++;
                break;
            }

            pos++;
        }

        spans.Add(new StyledSpan(start, pos - start, TokenKind.String));
        return pos;
    }

    protected virtual bool TryMatchNumber(string line, int pos, out int end)
    {
        char ch = line[pos];
        if (!char.IsDigit(ch))
        {
            end = pos;
            return false;
        }

        int start = pos;
        // Hex: 0x...
        if (ch == '0' && pos + 1 < line.Length && line[pos + 1] is 'x' or 'X')
        {
            pos += 2;
            while (pos < line.Length && (char.IsAsciiHexDigit(line[pos]) || line[pos] == '_'))
            {
                pos++;
            }

            end = pos;
            return true;
        }

        // Regular number (int / float) with optional _ separators
        bool hasDot = false;
        while (pos < line.Length)
        {
            char c = line[pos];
            if (char.IsDigit(c) || c == '_')
            {
                pos++;
                continue;
            }

            if (c == '.' && !hasDot && pos + 1 < line.Length && char.IsDigit(line[pos + 1]))
            {
                hasDot = true;
                pos++;
                continue;
            }

            break;
        }

        // Skip trailing type suffix (f, d, L, m, u, ul, etc.)
        while (pos < line.Length && char.IsLetter(line[pos]))
        {
            pos++;
        }

        end = pos;
        return pos > start;
    }

    protected virtual TokenKind ClassifyWord(string word)
    {
        if (Keywords.Contains(word))
        {
            return TokenKind.Keyword;
        }

        if (Constants.Contains(word))
        {
            return TokenKind.Constant;
        }

        if (Builtins.Contains(word))
        {
            return TokenKind.BuiltinFunc;
        }

        // PascalCase heuristic -> Type
        if (word.Length >= 2 && char.IsUpper(word[0]) && word.Any(char.IsLower))
        {
            return TokenKind.Type;
        }

        return TokenKind.Plain;
    }

    private static bool TryMatchOperatorOrPunct(string line, int pos, List<StyledSpan> spans, out int end)
    {
        char ch = line[pos];
        if (ch is '{' or '}' or '(' or ')' or '[' or ']' or ';' or ',' or ':')
        {
            spans.Add(new StyledSpan(pos, 1, TokenKind.Punctuation));
            end = pos + 1;
            return true;
        }

        if (ch is '=' or '+' or '-' or '*' or '/' or '%' or '<' or '>' or '!' or '&' or '|' or '^' or '~' or '?' or '@')
        {
            // Multi-char operators: ->, =>, !=, ==, <=, >=, &&, ||, ++, --
            int opLen = 1;
            if (pos + 1 < line.Length)
            {
                char n = line[pos + 1];
                if ((ch == '-' && n == '>') || (ch == '=' && n == '>') ||
                    (ch == '!' && n == '=') || (ch == '=' && n == '=') ||
                    (ch == '<' && n == '=') || (ch == '>' && n == '=') ||
                    (ch == '&' && n == '&') || (ch == '|' && n == '|') ||
                    (ch == '+' && n == '+') || (ch == '-' && n == '-') ||
                    (ch == '<' && n == '<') || (ch == '>' && n == '>'))
                {
                    opLen = 2;
                }
            }

            spans.Add(new StyledSpan(pos, opLen, TokenKind.Operator));
            end = pos + opLen;
            return true;
        }

        if (ch == '.')
        {
            spans.Add(new StyledSpan(pos, 1, TokenKind.Punctuation));
            end = pos + 1;
            return true;
        }

        end = pos;
        return false;
    }

    protected static StyledLine MakeResult(string line, List<StyledSpan> spans) =>
        spans.Count == 0 ? new StyledLine(line, null) : new StyledLine(line, [.. spans]);
}
