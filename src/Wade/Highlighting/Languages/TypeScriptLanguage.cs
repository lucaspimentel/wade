using System.Collections.Frozen;

namespace Wade.Highlighting.Languages;

internal sealed class TypeScriptLanguage : JavaScriptLanguage
{
    private static readonly FrozenSet<string> TsKeywords = new[]
    {
        "abstract", "as", "async", "await", "break", "case", "catch", "class",
        "const", "continue", "debugger", "declare", "default", "delete", "do",
        "else", "enum", "export", "extends", "finally", "for", "from", "function",
        "if", "implements", "import", "in", "infer", "instanceof", "interface",
        "is", "keyof", "let", "module", "namespace", "new", "never", "of",
        "override", "private", "protected", "public", "readonly", "return",
        "satisfies", "static", "super", "switch", "this", "throw", "try",
        "type", "typeof", "unique", "var", "void", "while", "with", "yield",
        "get", "set", "asserts",
    }.ToFrozenSet();

    protected override FrozenSet<string> Keywords => TsKeywords;
}
