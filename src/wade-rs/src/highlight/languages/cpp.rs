//! Port of `CppLanguage`: `CLanguage` with the C++ word sets.

use crate::highlight::StyledSpan;
use crate::highlight::c_like::CLikeLanguage;
use crate::highlight::c_like::match_hash_directive;

const KEYWORDS: &[&str] = &[
    "auto", "break", "case", "const", "continue", "default", "do", "else", "enum", "extern", "for", "goto", "if",
    "inline", "register", "restrict", "return", "sizeof", "static", "struct", "switch", "typedef", "union", "volatile",
    "while", "alignas", "alignof", "and", "and_eq", "asm", "bitand", "bitor", "catch", "class", "compl", "concept",
    "consteval", "constexpr", "constinit", "const_cast", "co_await", "co_return", "co_yield", "decltype", "delete",
    "dynamic_cast", "explicit", "export", "final", "friend", "module", "mutable", "namespace", "new", "noexcept",
    "not", "not_eq", "operator", "or", "or_eq", "override", "private", "protected", "public", "reinterpret_cast",
    "requires", "static_assert", "static_cast", "template", "this", "thread_local", "throw", "try", "typeid",
    "typename", "using", "virtual", "xor", "xor_eq",
];

const CONSTANTS: &[&str] = &["true", "false", "nullptr", "NULL"];

const BUILTINS: &[&str] = &[
    "void", "char", "short", "int", "long", "float", "double", "signed", "unsigned", "size_t", "ptrdiff_t", "int8_t",
    "int16_t", "int32_t", "int64_t", "uint8_t", "uint16_t", "uint32_t", "uint64_t", "bool", "FILE", "string",
    "wstring", "string_view", "span", "vector", "array", "list", "deque", "map", "set", "unordered_map",
    "unordered_set", "queue", "stack", "pair", "tuple", "optional", "variant", "any", "shared_ptr", "unique_ptr",
    "weak_ptr", "nullptr_t", "cout", "cin", "cerr", "endl",
];

pub struct CppLanguage;

impl CLikeLanguage for CppLanguage {
    fn name(&self) -> &'static str {
        "CppLanguage"
    }

    fn keywords(&self) -> &'static [&'static str] {
        KEYWORDS
    }

    fn constants(&self) -> &'static [&'static str] {
        CONSTANTS
    }

    fn builtins(&self) -> &'static [&'static str] {
        BUILTINS
    }

    fn try_match_line_prefix(&self, line: &[char], spans: &mut Vec<StyledSpan>, _state: &mut u8) -> Option<usize> {
        // Inherited from CLanguage: preprocessor directives
        match_hash_directive(line, spans)
    }
}
