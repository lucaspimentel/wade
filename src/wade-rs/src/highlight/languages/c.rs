//! Port of `CLanguage`.

use crate::highlight::c_like::CLikeLanguage;
use crate::highlight::c_like::match_hash_directive;
use crate::highlight::StyledSpan;


const KEYWORDS: &[&str] = &[
    "auto", "break", "case", "const", "continue", "default", "do", "else", "enum", "extern",
    "for", "goto", "if", "inline", "register", "restrict", "return", "sizeof", "static",
    "struct", "switch", "typedef", "union", "volatile", "while", "_Alignas", "_Alignof",
    "_Atomic", "_Bool", "_Complex", "_Generic", "_Imaginary", "_Noreturn", "_Static_assert",
    "_Thread_local",
];

const CONSTANTS: &[&str] = &[
    "true", "false", "NULL", "nullptr",
];

const BUILTINS: &[&str] = &[
    "void", "char", "short", "int", "long", "float", "double", "signed", "unsigned", "size_t",
    "ptrdiff_t", "int8_t", "int16_t", "int32_t", "int64_t", "uint8_t", "uint16_t", "uint32_t",
    "uint64_t", "bool", "FILE",
];

pub struct CLanguage;

impl CLikeLanguage for CLanguage {
    fn name(&self) -> &'static str {
        "CLanguage"
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
        // Preprocessor directives: #include, #define, #ifdef, #endif, #pragma, ...
        match_hash_directive(line, spans)
    }
}
