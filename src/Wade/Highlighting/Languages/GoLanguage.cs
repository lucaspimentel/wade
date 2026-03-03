using System.Collections.Frozen;

namespace Wade.Highlighting.Languages;

internal sealed class GoLanguage : RegexLanguage
{
    protected override FrozenSet<string> Keywords { get; } = new[]
    {
        "break", "case", "chan", "const", "continue", "default", "defer", "else",
        "fallthrough", "for", "func", "go", "goto", "if", "import", "interface",
        "map", "package", "range", "return", "select", "struct", "switch",
        "type", "var",
    }.ToFrozenSet();

    protected override FrozenSet<string> Constants { get; } = new[]
    {
        "true", "false", "nil", "iota",
    }.ToFrozenSet();

    protected override FrozenSet<string> Builtins { get; } = new[]
    {
        "append", "cap", "clear", "close", "complex", "copy", "delete",
        "imag", "len", "make", "max", "min", "new", "panic", "print",
        "println", "real", "recover",
        "bool", "byte", "comparable", "complex64", "complex128", "error",
        "float32", "float64", "int", "int8", "int16", "int32", "int64",
        "rune", "string", "uint", "uint8", "uint16", "uint32", "uint64",
        "uintptr", "any",
    }.ToFrozenSet();

    protected override bool TryMatchString(string line, int pos, List<StyledSpan> spans, ref byte state, out int end)
    {
        // Backtick raw strings
        if (line[pos] == '`')
        {
            int p = pos + 1;
            while (p < line.Length && line[p] != '`')
                p++;
            if (p < line.Length) p++; // consume closing backtick
            spans.Add(new StyledSpan(pos, p - pos, TokenKind.String));
            end = p;
            return true;
        }

        return base.TryMatchString(line, pos, spans, ref state, out end);
    }
}
