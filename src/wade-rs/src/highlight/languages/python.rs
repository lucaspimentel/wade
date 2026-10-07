//! Port of `PythonLanguage`.

use crate::highlight::c_like::CLikeLanguage;
use crate::highlight::c_like::{STATE_MULTI_STRING, STATE_NORMAL, scan_quoted_string};
use crate::highlight::scan::index_of;
use crate::highlight::{StyledSpan, TokenKind};
use crate::text::is_letter_or_digit;

const KEYWORDS: &[&str] = &[
    "and", "as", "assert", "async", "await", "break", "class", "continue", "def", "del", "elif", "else", "except",
    "finally", "for", "from", "global", "if", "import", "in", "is", "lambda", "nonlocal", "not", "or", "pass", "raise",
    "return", "try", "while", "with", "yield",
];

const CONSTANTS: &[&str] = &["True", "False", "None"];

const BUILTINS: &[&str] = &[
    "abs", "all", "any", "ascii", "bin", "bool", "breakpoint", "bytearray", "bytes", "callable", "chr", "classmethod",
    "compile", "complex", "copyright", "delattr", "dict", "dir", "divmod", "enumerate", "eval", "exec", "filter",
    "float", "format", "frozenset", "getattr", "globals", "hasattr", "hash", "help", "hex", "id", "input", "int",
    "isinstance", "issubclass", "iter", "len", "list", "locals", "map", "max", "memoryview", "min", "next", "object",
    "oct", "open", "ord", "pow", "print", "property", "range", "repr", "reversed", "round", "set", "setattr", "slice",
    "sorted", "staticmethod", "str", "sum", "super", "tuple", "type", "vars", "zip",
];

pub struct PythonLanguage;

impl CLikeLanguage for PythonLanguage {
    fn name(&self) -> &'static str {
        "PythonLanguage"
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

    fn line_comment_prefix(&self) -> Option<&'static str> {
        Some("#")
    }

    fn block_comment(&self) -> Option<(&'static str, &'static str)> {
        None // Python has no block comment
    }

    fn try_match_extension(&self, line: &[char], pos: usize, spans: &mut Vec<StyledSpan>) -> usize {
        // Decorators: @name (dots allowed)
        if line[pos] == '@' {
            let mut end = pos + 1;

            while end < line.len() && (is_letter_or_digit(line[end]) || line[end] == '_' || line[end] == '.') {
                end += 1;
            }

            if end > pos + 1 {
                spans.push(StyledSpan::new(pos, end - pos, TokenKind::Attribute));
                return end - pos;
            }
        }

        0
    }

    fn try_match_string(
        &self,
        line: &[char],
        pos: usize,
        spans: &mut Vec<StyledSpan>,
        state: &mut u8,
    ) -> Option<usize> {
        let mut pos = pos;

        // f-strings, b-strings, r-strings: f"...", b"...", r"...", ...
        if matches!(line[pos], 'f' | 'b' | 'r' | 'u' | 'F' | 'B' | 'R')
            && pos + 1 < line.len()
            && (line[pos + 1] == '"' || line[pos + 1] == '\'')
        {
            pos += 1; // skip prefix
        }

        // Only a lowercase prefix widens the span (as in C#)
        let span_start = if pos > 0 && matches!(line[pos - 1], 'f' | 'b' | 'r' | 'u') {
            pos - 1
        } else {
            pos
        };

        // Triple-quoted strings: """...""" or '''...'''
        if pos + 2 < line.len() {
            let q = line[pos];

            if (q == '"' || q == '\'') && line[pos + 1] == q && line[pos + 2] == q {
                let triple: String = std::iter::repeat_n(q, 3).collect();

                if let Some(close_index) = index_of(line, &triple, pos + 3) {
                    let close_end = close_index + 3;
                    spans.push(StyledSpan::new(span_start, close_end - span_start, TokenKind::String));
                    return Some(close_end);
                }

                // Multi-line triple-quoted string
                spans.push(StyledSpan::new(span_start, line.len() - span_start, TokenKind::String));
                *state = STATE_MULTI_STRING;
                return Some(line.len());
            }
        }

        // Regular strings (single or double quote)
        if pos < line.len() && (line[pos] == '"' || line[pos] == '\'') {
            let end = scan_quoted_string(line, pos, line[pos], spans);

            if span_start < pos
                && let Some(last) = spans.last_mut()
            {
                *last = StyledSpan::new(span_start, last.start + last.len - span_start, TokenKind::String);
            }

            return Some(end);
        }

        None
    }

    fn try_end_multi_string(&self, line: &[char], pos: usize, spans: &mut Vec<StyledSpan>, state: &mut u8) -> usize {
        // Try both triple-quote styles
        for triple in ["\"\"\"", "\'\'\'"] {
            if let Some(close_index) = index_of(line, triple, pos) {
                let close_end = close_index + 3;
                spans.push(StyledSpan::new(pos, close_end - pos, TokenKind::String));
                *state = STATE_NORMAL;
                return close_end;
            }
        }

        spans.push(StyledSpan::new(pos, line.len() - pos, TokenKind::String));
        line.len()
    }
}
