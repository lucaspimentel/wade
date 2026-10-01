//! Port of `CSharpLanguage`.

use crate::highlight::c_like::CLikeLanguage;
use crate::highlight::c_like::{
    base_try_match_string, end_triple_quote_string, match_bracket_attribute, match_hash_directive,
    match_triple_quote_string, scan_quoted_string,
};
use crate::highlight::{StyledSpan, TokenKind};


const KEYWORDS: &[&str] = &[
    "abstract", "as", "async", "await", "base", "break", "case", "catch", "checked", "class",
    "const", "continue", "default", "delegate", "do", "else", "enum", "event", "explicit",
    "extern", "finally", "fixed", "for", "foreach", "goto", "if", "implicit", "in", "interface",
    "internal", "is", "lock", "namespace", "new", "operator", "out", "override", "params",
    "partial", "private", "protected", "public", "readonly", "record", "ref", "required",
    "return", "sealed", "sizeof", "stackalloc", "static", "struct", "switch", "this", "throw",
    "try", "typeof", "unchecked", "unsafe", "using", "var", "virtual", "volatile", "when",
    "where", "while", "with", "yield", "and", "or", "not", "init", "get", "set", "add",
    "remove", "value", "nint", "nuint",
];

const CONSTANTS: &[&str] = &[
    "true", "false", "null",
];

const BUILTINS: &[&str] = &[
    "bool", "byte", "char", "decimal", "double", "dynamic", "float", "int", "long", "object",
    "sbyte", "short", "string", "uint", "ulong", "ushort", "void",
];

pub struct CSharpLanguage;

impl CLikeLanguage for CSharpLanguage {
    fn name(&self) -> &'static str {
        "CSharpLanguage"
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
        // Preprocessor directives: #if, #else, #endif, #define, #pragma, #region, ...
        match_hash_directive(line, spans)
    }

    fn try_match_extension(&self, line: &[char], pos: usize, spans: &mut Vec<StyledSpan>) -> usize {
        // Attributes: [Foo] or [Foo("bar")]
        match_bracket_attribute(line, pos, spans)
    }

    fn try_match_string(&self, line: &[char], pos: usize, spans: &mut Vec<StyledSpan>, state: &mut u8) -> Option<usize> {
        // Raw string literals: """ ... """
        if let Some(end) = match_triple_quote_string(line, pos, spans, state) {
            return Some(end);
        }

        // Verbatim strings: @"..." ("" escapes a quote)
        if line[pos] == '@' && pos + 1 < line.len() && line[pos + 1] == '"' {
            let mut p = pos + 2;

            while p < line.len() {
                if line[p] == '"' {
                    if p + 1 < line.len() && line[p + 1] == '"' {
                        p += 2;
                        continue;
                    }

                    p += 1;
                    break;
                }

                p += 1;
            }

            spans.push(StyledSpan::new(pos, p - pos, TokenKind::String));
            return Some(p);
        }

        // Interpolated strings: $"..." (whole thing is a string)
        if line[pos] == '$' && pos + 1 < line.len() && line[pos + 1] == '"' {
            let end = scan_quoted_string(line, pos + 1, '"', spans);

            // Widen the span to include the '$'
            if let Some(last) = spans.last_mut() {
                *last = StyledSpan::new(pos, last.len + 1, TokenKind::String);
            }

            return Some(end);
        }

        base_try_match_string(line, pos, spans)
    }

    fn try_end_multi_string(&self, line: &[char], pos: usize, spans: &mut Vec<StyledSpan>, state: &mut u8) -> usize {
        end_triple_quote_string(line, pos, spans, state)
    }
}
