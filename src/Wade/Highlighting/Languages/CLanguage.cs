using System.Collections.Frozen;

namespace Wade.Highlighting.Languages;

internal class CLanguage : RegexLanguage
{
    protected override FrozenSet<string> Keywords { get; } = new[]
    {
        "auto", "break", "case", "const", "continue", "default", "do", "else",
        "enum", "extern", "for", "goto", "if", "inline", "register", "restrict",
        "return", "sizeof", "static", "struct", "switch", "typedef", "union",
        "volatile", "while",
        "_Alignas", "_Alignof", "_Atomic", "_Bool", "_Complex", "_Generic",
        "_Imaginary", "_Noreturn", "_Static_assert", "_Thread_local",
    }.ToFrozenSet();

    protected override FrozenSet<string> Constants { get; } = new[]
    {
        "true", "false", "NULL", "nullptr",
    }.ToFrozenSet();

    protected override FrozenSet<string> Builtins { get; } = new[]
    {
        "void", "char", "short", "int", "long", "float", "double", "signed",
        "unsigned", "size_t", "ptrdiff_t", "int8_t", "int16_t", "int32_t",
        "int64_t", "uint8_t", "uint16_t", "uint32_t", "uint64_t", "bool", "FILE",
    }.ToFrozenSet();

    protected override int TryMatchLinePrefix(string line, List<StyledSpan> spans, ref byte state)
    {
        // Preprocessor directives: #include, #define, #ifdef, #endif, #pragma, etc.
        int i = 0;
        while (i < line.Length && line[i] == ' ')
        {
            i++;
        }

        if (i < line.Length && line[i] == '#')
        {
            spans.Add(new StyledSpan(0, line.Length, TokenKind.Directive));
            return -1; // Skip main scan loop
        }

        return 0;
    }
}
