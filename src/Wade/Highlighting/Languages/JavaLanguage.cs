using System.Collections.Frozen;

namespace Wade.Highlighting.Languages;

internal sealed class JavaLanguage : RegexLanguage
{
    protected override FrozenSet<string> Keywords { get; } = new[]
    {
        "abstract", "assert", "break", "case", "catch", "class", "const",
        "continue", "default", "do", "else", "enum", "extends", "final",
        "finally", "for", "goto", "if", "implements", "import", "instanceof",
        "interface", "native", "new", "package", "private", "protected",
        "public", "record", "return", "sealed", "static", "strictfp", "super",
        "switch", "synchronized", "this", "throw", "throws", "transient",
        "try", "var", "volatile", "while", "yield",
    }.ToFrozenSet();

    protected override FrozenSet<string> Constants { get; } = new[]
    {
        "true", "false", "null",
    }.ToFrozenSet();

    protected override FrozenSet<string> Builtins { get; } = new[]
    {
        "boolean", "byte", "char", "double", "float", "int", "long", "short", "void",
        "String", "Object", "Integer", "Long", "Double", "Float", "Boolean",
        "Character", "Byte", "Short", "Number",
        "System", "Math", "Arrays", "Collections", "List", "Map", "Set",
        "ArrayList", "HashMap", "HashSet", "Optional",
    }.ToFrozenSet();

    protected override int TryMatchExtension(string line, int pos, List<StyledSpan> spans)
    {
        // Annotations: @Override, @SuppressWarnings("...")
        if (line[pos] == '@')
        {
            int end = pos + 1;
            while (end < line.Length && (char.IsLetterOrDigit(line[end]) || line[end] == '_'))
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
        // Text blocks: """...""" (multi-line)
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

            spans.Add(new StyledSpan(pos, line.Length - pos, TokenKind.String));
            state = StateMultiString;
            end = line.Length;
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
