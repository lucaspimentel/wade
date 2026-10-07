//! Port of `RustLanguage`.

use crate::highlight::c_like::CLikeLanguage;
use crate::highlight::c_like::base_try_match_string;
use crate::highlight::{StyledSpan, TokenKind};

const KEYWORDS: &[&str] = &[
    "as", "async", "await", "break", "const", "continue", "crate", "dyn", "else", "enum", "extern", "false", "fn",
    "for", "if", "impl", "in", "let", "loop", "match", "mod", "move", "mut", "pub", "ref", "return", "self", "Self",
    "static", "struct", "super", "trait", "true", "type", "union", "unsafe", "use", "where", "while", "yield",
];

const CONSTANTS: &[&str] = &["true", "false", "None", "Some", "Ok", "Err"];

const BUILTINS: &[&str] = &[
    "bool", "char", "f32", "f64", "i8", "i16", "i32", "i64", "i128", "isize", "str", "u8", "u16", "u32", "u64", "u128",
    "usize", "Box", "String", "Vec", "Option", "Result", "HashMap", "HashSet", "println", "print", "eprintln",
    "eprint", "panic", "assert", "assert_eq", "assert_ne", "todo", "unimplemented", "unreachable", "format", "write",
    "writeln",
];

pub struct RustLanguage;

impl CLikeLanguage for RustLanguage {
    fn name(&self) -> &'static str {
        "RustLanguage"
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

    fn try_match_extension(&self, line: &[char], pos: usize, spans: &mut Vec<StyledSpan>) -> usize {
        // Attributes: #[...] or #![...]
        if line[pos] == '#'
            && pos + 1 < line.len()
            && (line[pos + 1] == '[' || (line[pos + 1] == '!' && pos + 2 < line.len() && line[pos + 2] == '['))
        {
            let mut end = pos + 1;
            let mut depth = 0;

            while end < line.len() {
                if line[end] == '[' {
                    depth += 1;
                } else if line[end] == ']' {
                    depth -= 1;

                    if depth == 0 {
                        end += 1;
                        break;
                    }
                }

                end += 1;
            }

            spans.push(StyledSpan::new(pos, end - pos, TokenKind::Attribute));
            return end - pos;
        }

        0
    }

    fn try_match_string(
        &self,
        line: &[char],
        pos: usize,
        spans: &mut Vec<StyledSpan>,
        _state: &mut u8,
    ) -> Option<usize> {
        // Char literals ('x', '\n') but not lifetimes ('a in &'a str)
        if line[pos] == '\'' {
            if pos + 1 < line.len() && line[pos + 1] != '\\' {
                if pos + 2 < line.len() && line[pos + 2] == '\'' {
                    // 'x'
                    spans.push(StyledSpan::new(pos, 3, TokenKind::String));
                    return Some(pos + 3);
                }

                // Otherwise likely a lifetime: not a string
                return None;
            }

            if pos + 1 < line.len() && line[pos + 1] == '\\' {
                // Escape sequence char literal: '\n', '\t', ...
                let mut p = pos + 2;

                while p < line.len() && line[p] != '\'' {
                    p += 1;
                }

                if p < line.len() {
                    p += 1;
                }

                spans.push(StyledSpan::new(pos, p - pos, TokenKind::String));
                return Some(p);
            }
        }

        base_try_match_string(line, pos, spans)
    }
}
