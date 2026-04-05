using System.Collections.Frozen;

namespace Wade.Highlighting.Languages;

internal sealed class CppLanguage : CLanguage
{
    private static readonly FrozenSet<string> CppKeywords = new[]
    {
        // C keywords
        "auto", "break", "case", "const", "continue", "default", "do", "else",
        "enum", "extern", "for", "goto", "if", "inline", "register", "restrict",
        "return", "sizeof", "static", "struct", "switch", "typedef", "union",
        "volatile", "while",
        // C++ keywords
        "alignas", "alignof", "and", "and_eq", "asm", "bitand", "bitor",
        "catch", "class", "compl", "concept", "consteval", "constexpr", "constinit",
        "const_cast", "co_await", "co_return", "co_yield", "decltype", "delete",
        "dynamic_cast", "explicit", "export", "final", "friend", "module", "mutable",
        "namespace", "new", "noexcept", "not", "not_eq", "operator", "or", "or_eq",
        "override", "private", "protected", "public", "reinterpret_cast", "requires",
        "static_assert", "static_cast", "template", "this", "thread_local", "throw",
        "try", "typeid", "typename", "using", "virtual", "xor", "xor_eq",
    }.ToFrozenSet();

    private static readonly FrozenSet<string> CppConstants = new[]
    {
        "true", "false", "nullptr", "NULL",
    }.ToFrozenSet();

    private static readonly FrozenSet<string> CppBuiltins = new[]
    {
        // C types
        "void", "char", "short", "int", "long", "float", "double", "signed",
        "unsigned", "size_t", "ptrdiff_t", "int8_t", "int16_t", "int32_t",
        "int64_t", "uint8_t", "uint16_t", "uint32_t", "uint64_t", "bool", "FILE",
        // C++ types
        "string", "wstring", "string_view", "span", "vector", "array", "list",
        "deque", "map", "set", "unordered_map", "unordered_set", "queue", "stack",
        "pair", "tuple", "optional", "variant", "any", "shared_ptr", "unique_ptr",
        "weak_ptr", "nullptr_t", "cout", "cin", "cerr", "endl",
    }.ToFrozenSet();

    protected override FrozenSet<string> Keywords => CppKeywords;

    protected override FrozenSet<string> Constants => CppConstants;

    protected override FrozenSet<string> Builtins => CppBuiltins;
}
