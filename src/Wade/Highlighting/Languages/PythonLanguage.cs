using System.Collections.Frozen;

namespace Wade.Highlighting.Languages;

internal sealed class PythonLanguage : RegexLanguage
{
    private static readonly string[] TripleQuotes = ["\"\"\"", "'''"];

    protected override FrozenSet<string> Keywords { get; } = new[]
    {
        "and", "as", "assert", "async", "await", "break", "class", "continue",
        "def", "del", "elif", "else", "except", "finally", "for", "from",
        "global", "if", "import", "in", "is", "lambda", "nonlocal", "not",
        "or", "pass", "raise", "return", "try", "while", "with", "yield",
    }.ToFrozenSet();

    protected override FrozenSet<string> Constants { get; } = new[]
    {
        "True", "False", "None",
    }.ToFrozenSet();

    protected override FrozenSet<string> Builtins { get; } = new[]
    {
        "abs", "all", "any", "ascii", "bin", "bool", "breakpoint", "bytearray",
        "bytes", "callable", "chr", "classmethod", "compile", "complex", "copyright",
        "delattr", "dict", "dir", "divmod", "enumerate", "eval", "exec", "filter",
        "float", "format", "frozenset", "getattr", "globals", "hasattr", "hash",
        "help", "hex", "id", "input", "int", "isinstance", "issubclass", "iter",
        "len", "list", "locals", "map", "max", "memoryview", "min", "next",
        "object", "oct", "open", "ord", "pow", "print", "property", "range",
        "repr", "reversed", "round", "set", "setattr", "slice", "sorted",
        "staticmethod", "str", "sum", "super", "tuple", "type", "vars", "zip",
    }.ToFrozenSet();

    protected override string? LineCommentPrefix => "#";

    protected override (string Open, string Close)? BlockComment => null; // Python has no block comment

    protected override int TryMatchExtension(string line, int pos, List<StyledSpan> spans)
    {
        // Decorators: @name
        if (line[pos] == '@')
        {
            int end = pos + 1;
            while (end < line.Length && (char.IsLetterOrDigit(line[end]) || line[end] == '_' || line[end] == '.'))
            {
                end++;
            }

            if (end > pos + 1)
            {
                spans.Add(new StyledSpan(pos, end - pos, TokenKind.Attribute));
                return end - pos;
            }
        }

        return 0;
    }

    protected override bool TryMatchString(string line, int pos, List<StyledSpan> spans, ref byte state, out int end)
    {
        // f-strings, b-strings, r-strings: f"...", b"...", r"...", etc.
        char ch = line[pos];
        if ((ch == 'f' || ch == 'b' || ch == 'r' || ch == 'u' || ch == 'F' || ch == 'B' || ch == 'R') &&
            pos + 1 < line.Length && line[pos + 1] is '"' or '\'')
        {
            pos++; // skip prefix
        }

        // Triple-quoted strings: """...""" or '''...'''
        if (pos + 2 < line.Length)
        {
            char q = line[pos];
            if (q is '"' or '\'' && line[pos + 1] == q && line[pos + 2] == q)
            {
                string triple = new(q, 3);
                int closeIdx = line.IndexOf(triple, pos + 3, StringComparison.Ordinal);
                if (closeIdx >= 0)
                {
                    int closeEnd = closeIdx + 3;
                    // Include any prefix char before pos
                    int spanStart = pos > 0 && (line[pos - 1] == 'f' || line[pos - 1] == 'b' || line[pos - 1] == 'r' || line[pos - 1] == 'u')
                        ? pos - 1
                        : pos;
                    spans.Add(new StyledSpan(spanStart, closeEnd - spanStart, TokenKind.String));
                    end = closeEnd;
                    return true;
                }

                // Multi-line triple-quoted string
                int spanStart2 = pos > 0 && (line[pos - 1] == 'f' || line[pos - 1] == 'b' || line[pos - 1] == 'r' || line[pos - 1] == 'u')
                    ? pos - 1
                    : pos;
                spans.Add(new StyledSpan(spanStart2, line.Length - spanStart2, TokenKind.String));
                state = StateMultiString;
                end = line.Length;
                return true;
            }
        }

        // Regular strings (single or double quote)
        if (pos < line.Length && line[pos] is '"' or '\'')
        {
            int spanStart = pos > 0 && (line[pos - 1] == 'f' || line[pos - 1] == 'b' || line[pos - 1] == 'r' || line[pos - 1] == 'u')
                ? pos - 1
                : pos;
            end = ScanQuotedString(line, pos, line[pos], spans);
            if (spans.Count > 0 && spanStart < pos)
            {
                StyledSpan last = spans[^1];
                spans[^1] = new StyledSpan(spanStart, last.Start + last.Length - spanStart, TokenKind.String);
            }

            return true;
        }

        end = pos;
        return false;
    }

    protected override int TryEndMultiString(string line, int pos, List<StyledSpan> spans, ref byte state)
    {
        // Try both triple quote styles
        foreach (string triple in TripleQuotes)
        {
            int closeIdx = line.IndexOf(triple, pos, StringComparison.Ordinal);
            if (closeIdx >= 0)
            {
                int closeEnd = closeIdx + 3;
                spans.Add(new StyledSpan(pos, closeEnd - pos, TokenKind.String));
                state = StateNormal;
                return closeEnd;
            }
        }

        spans.Add(new StyledSpan(pos, line.Length - pos, TokenKind.String));
        return line.Length;
    }
}
