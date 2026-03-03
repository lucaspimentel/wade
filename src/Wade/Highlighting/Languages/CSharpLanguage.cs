using System.Collections.Frozen;

namespace Wade.Highlighting.Languages;

internal sealed class CSharpLanguage : RegexLanguage
{
    protected override FrozenSet<string> Keywords { get; } = new[]
    {
        "abstract", "as", "async", "await", "base", "break", "case", "catch",
        "checked", "class", "const", "continue", "default", "delegate", "do",
        "else", "enum", "event", "explicit", "extern", "finally", "fixed",
        "for", "foreach", "goto", "if", "implicit", "in", "interface", "internal",
        "is", "lock", "namespace", "new", "operator", "out", "override", "params",
        "partial", "private", "protected", "public", "readonly", "record", "ref",
        "required", "return", "sealed", "sizeof", "stackalloc", "static", "struct",
        "switch", "this", "throw", "try", "typeof", "unchecked", "unsafe", "using",
        "var", "virtual", "volatile", "when", "where", "while", "with", "yield",
        "and", "or", "not", "init", "get", "set", "add", "remove", "value",
        "nint", "nuint",
    }.ToFrozenSet();

    protected override FrozenSet<string> Constants { get; } = new[]
    {
        "true", "false", "null",
    }.ToFrozenSet();

    protected override FrozenSet<string> Builtins { get; } = new[]
    {
        "bool", "byte", "char", "decimal", "double", "dynamic", "float", "int",
        "long", "object", "sbyte", "short", "string", "uint", "ulong", "ushort",
        "void",
    }.ToFrozenSet();

    protected override int TryMatchLinePrefix(string line, List<StyledSpan> spans, ref byte state)
    {
        // Preprocessor directives: #if, #else, #endif, #define, #pragma, #region, etc.
        int i = 0;
        while (i < line.Length && line[i] == ' ') i++;
        if (i < line.Length && line[i] == '#')
        {
            spans.Add(new StyledSpan(0, line.Length, TokenKind.Directive));
            return -1; // Skip main scan loop
        }
        return 0;
    }

    protected override int TryMatchExtension(string line, int pos, List<StyledSpan> spans)
    {
        // Attributes: [Foo] or [Foo("bar")]
        if (line[pos] == '[')
        {
            int end = pos + 1;
            int depth = 1;
            while (end < line.Length && depth > 0)
            {
                if (line[end] == '[') depth++;
                else if (line[end] == ']') depth--;
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

    protected override bool TryMatchString(string line, int pos, List<StyledSpan> spans, ref byte state, out int end)
    {
        // Raw string literals: """ ... """
        if (line.AsSpan(pos).StartsWith("\"\"\""))
        {
            int closeIdx = line.IndexOf("\"\"\"", pos + 3, StringComparison.Ordinal);
            if (closeIdx >= 0)
            {
                int closeEnd = closeIdx + 3;
                spans.Add(new StyledSpan(pos, closeEnd - pos, TokenKind.String));
                end = closeEnd;
                return true;
            }
            // Multi-line raw string
            spans.Add(new StyledSpan(pos, line.Length - pos, TokenKind.String));
            state = StateMultiString;
            end = line.Length;
            return true;
        }

        // Verbatim strings: @"..."
        if (line[pos] == '@' && pos + 1 < line.Length && line[pos + 1] == '"')
        {
            int p = pos + 2;
            while (p < line.Length)
            {
                if (line[p] == '"')
                {
                    // Escaped double-quote in verbatim string: ""
                    if (p + 1 < line.Length && line[p + 1] == '"') { p += 2; continue; }
                    p++;
                    break;
                }
                p++;
            }
            spans.Add(new StyledSpan(pos, p - pos, TokenKind.String));
            end = p;
            return true;
        }

        // Interpolated strings: $"..." (treat whole thing as string, imperfect but acceptable)
        if (line[pos] == '$' && pos + 1 < line.Length && line[pos + 1] == '"')
        {
            end = ScanQuotedString(line, pos + 1, '"', spans);
            // Overwrite the span start to include '$'
            if (spans.Count > 0)
            {
                var last = spans[^1];
                spans[^1] = new StyledSpan(pos, last.Length + 1, TokenKind.String);
            }
            return true;
        }

        return base.TryMatchString(line, pos, spans, ref state, out end);
    }

    protected override int TryEndMultiString(string line, int pos, List<StyledSpan> spans, ref byte state)
    {
        int closeIdx = line.IndexOf("\"\"\"", pos, StringComparison.Ordinal);
        if (closeIdx >= 0)
        {
            int closeEnd = closeIdx + 3;
            spans.Add(new StyledSpan(pos, closeEnd - pos, TokenKind.String));
            state = StateNormal;
            return closeEnd;
        }
        spans.Add(new StyledSpan(pos, line.Length - pos, TokenKind.String));
        return line.Length;
    }
}
