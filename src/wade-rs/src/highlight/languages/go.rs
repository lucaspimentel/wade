//! Port of `GoLanguage`.

use crate::highlight::c_like::CLikeLanguage;
use crate::highlight::c_like::base_try_match_string;
use crate::highlight::{StyledSpan, TokenKind};


const KEYWORDS: &[&str] = &[
    "break", "case", "chan", "const", "continue", "default", "defer", "else", "fallthrough",
    "for", "func", "go", "goto", "if", "import", "interface", "map", "package", "range",
    "return", "select", "struct", "switch", "type", "var",
];

const CONSTANTS: &[&str] = &[
    "true", "false", "nil", "iota",
];

const BUILTINS: &[&str] = &[
    "append", "cap", "clear", "close", "complex", "copy", "delete", "imag", "len", "make",
    "max", "min", "new", "panic", "print", "println", "real", "recover", "bool", "byte",
    "comparable", "complex64", "complex128", "error", "float32", "float64", "int", "int8",
    "int16", "int32", "int64", "rune", "string", "uint", "uint8", "uint16", "uint32", "uint64",
    "uintptr", "any",
];

pub struct GoLanguage;

impl CLikeLanguage for GoLanguage {
    fn name(&self) -> &'static str {
        "GoLanguage"
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

    fn try_match_string(&self, line: &[char], pos: usize, spans: &mut Vec<StyledSpan>, _state: &mut u8) -> Option<usize> {
        // Backtick raw strings (single line; unterminated runs to the end)
        if line[pos] == '`' {
            let mut p = pos + 1;

            while p < line.len() && line[p] != '`' {
                p += 1;
            }

            if p < line.len() {
                p += 1; // closing backtick
            }

            spans.push(StyledSpan::new(pos, p - pos, TokenKind::String));
            return Some(p);
        }

        base_try_match_string(line, pos, spans)
    }
}
