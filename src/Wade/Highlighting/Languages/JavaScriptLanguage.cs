using System.Collections.Frozen;

namespace Wade.Highlighting.Languages;

internal class JavaScriptLanguage : RegexLanguage
{
    protected override FrozenSet<string> Keywords { get; } = new[]
    {
        "async", "await", "break", "case", "catch", "class", "const", "continue",
        "debugger", "default", "delete", "do", "else", "export", "extends",
        "finally", "for", "from", "function", "if", "import", "in", "instanceof",
        "let", "new", "of", "return", "static", "super", "switch", "this",
        "throw", "try", "typeof", "var", "void", "while", "with", "yield",
        "get", "set",
    }.ToFrozenSet();

    protected override FrozenSet<string> Constants { get; } = new[]
    {
        "true", "false", "null", "undefined", "NaN", "Infinity",
    }.ToFrozenSet();

    protected override FrozenSet<string> Builtins { get; } = new[]
    {
        "console", "Math", "JSON", "Object", "Array", "String", "Number",
        "Boolean", "Symbol", "Map", "Set", "WeakMap", "WeakSet", "Promise",
        "Error", "TypeError", "RangeError", "parseInt", "parseFloat",
        "isNaN", "isFinite", "encodeURI", "decodeURI", "setTimeout",
        "clearTimeout", "setInterval", "clearInterval", "fetch",
        "require", "module", "exports",
    }.ToFrozenSet();

    protected override bool TryMatchString(string line, int pos, List<StyledSpan> spans, ref byte state, out int end)
    {
        // Template literals: `...`
        if (line[pos] == '`')
        {
            int p = pos + 1;
            while (p < line.Length)
            {
                if (line[p] == '\\') { p += 2; continue; }
                if (line[p] == '`') { p++; break; }
                p++;
            }

            spans.Add(new StyledSpan(pos, p - pos, TokenKind.String));
            end = p;
            return true;
        }

        return base.TryMatchString(line, pos, spans, ref state, out end);
    }
}
