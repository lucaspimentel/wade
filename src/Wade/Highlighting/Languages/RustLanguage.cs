using System.Collections.Frozen;

namespace Wade.Highlighting.Languages;

internal sealed class RustLanguage : RegexLanguage
{
    protected override FrozenSet<string> Keywords { get; } = new[]
    {
        "as", "async", "await", "break", "const", "continue", "crate", "dyn",
        "else", "enum", "extern", "false", "fn", "for", "if", "impl", "in",
        "let", "loop", "match", "mod", "move", "mut", "pub", "ref", "return",
        "self", "Self", "static", "struct", "super", "trait", "true", "type",
        "union", "unsafe", "use", "where", "while", "yield",
    }.ToFrozenSet();

    protected override FrozenSet<string> Constants { get; } = new[]
    {
        "true", "false", "None", "Some", "Ok", "Err",
    }.ToFrozenSet();

    protected override FrozenSet<string> Builtins { get; } = new[]
    {
        "bool", "char", "f32", "f64", "i8", "i16", "i32", "i64", "i128",
        "isize", "str", "u8", "u16", "u32", "u64", "u128", "usize",
        "Box", "String", "Vec", "Option", "Result", "HashMap", "HashSet",
        "println", "print", "eprintln", "eprint", "panic", "assert",
        "assert_eq", "assert_ne", "todo", "unimplemented", "unreachable",
        "format", "write", "writeln",
    }.ToFrozenSet();

    protected override int TryMatchExtension(string line, int pos, List<StyledSpan> spans)
    {
        // Attributes: #[...] or #![...]
        if (line[pos] == '#' && pos + 1 < line.Length && (line[pos + 1] == '[' || (line[pos + 1] == '!' && pos + 2 < line.Length && line[pos + 2] == '[')))
        {
            int end = pos + 1;
            int depth = 0;
            while (end < line.Length)
            {
                if (line[end] == '[') depth++;
                else if (line[end] == ']') { depth--; if (depth == 0) { end++; break; } }
                end++;
            }
            spans.Add(new StyledSpan(pos, end - pos, TokenKind.Attribute));
            return end - pos;
        }
        return 0;
    }

    protected override bool TryMatchString(string line, int pos, List<StyledSpan> spans, ref byte state, out int end)
    {
        // Char literals: 'x' — but NOT lifetimes like 'a in &'a str
        if (line[pos] == '\'')
        {
            // Lifetime: 'identifier (not closed with ')
            // Char literal: 'x' or '\n' etc.
            if (pos + 1 < line.Length && line[pos + 1] != '\\')
            {
                // Check if it's a char literal: 'x' pattern
                if (pos + 2 < line.Length && line[pos + 2] == '\'')
                {
                    // 'x'
                    spans.Add(new StyledSpan(pos, 3, TokenKind.String));
                    end = pos + 3;
                    return true;
                }
                // Otherwise it's likely a lifetime — skip
                end = pos;
                return false;
            }
            if (pos + 1 < line.Length && line[pos + 1] == '\\')
            {
                // Escape sequence char literal: '\n', '\t', etc.
                int p = pos + 2;
                while (p < line.Length && line[p] != '\'') p++;
                if (p < line.Length) p++;
                spans.Add(new StyledSpan(pos, p - pos, TokenKind.String));
                end = p;
                return true;
            }
        }

        return base.TryMatchString(line, pos, spans, ref state, out end);
    }
}
