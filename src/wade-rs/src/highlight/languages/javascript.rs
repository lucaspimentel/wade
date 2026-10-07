//! Port of `JavaScriptLanguage`.

use crate::highlight::c_like::CLikeLanguage;
use crate::highlight::c_like::base_try_match_string;
use crate::highlight::{StyledSpan, TokenKind};

/// Template literals: `...` with backslash escapes (single line). Shared
/// with TypeScript, which inherits the C# override.
pub(super) fn match_template_literal(line: &[char], pos: usize, spans: &mut Vec<StyledSpan>) -> Option<usize> {
    if line[pos] != '`' {
        return None;
    }

    let mut p = pos + 1;

    while p < line.len() {
        if line[p] == '\\' {
            p += 2;
            continue;
        }

        if line[p] == '`' {
            p += 1;
            break;
        }

        p += 1;
    }

    spans.push(StyledSpan::new(pos, p - pos, TokenKind::String));
    Some(p)
}

const KEYWORDS: &[&str] = &[
    "async", "await", "break", "case", "catch", "class", "const", "continue", "debugger", "default", "delete", "do",
    "else", "export", "extends", "finally", "for", "from", "function", "if", "import", "in", "instanceof", "let",
    "new", "of", "return", "static", "super", "switch", "this", "throw", "try", "typeof", "var", "void", "while",
    "with", "yield", "get", "set",
];

pub(super) const CONSTANTS: &[&str] = &["true", "false", "null", "undefined", "NaN", "Infinity"];

pub(super) const BUILTINS: &[&str] = &[
    "console", "Math", "JSON", "Object", "Array", "String", "Number", "Boolean", "Symbol", "Map", "Set", "WeakMap",
    "WeakSet", "Promise", "Error", "TypeError", "RangeError", "parseInt", "parseFloat", "isNaN", "isFinite",
    "encodeURI", "decodeURI", "setTimeout", "clearTimeout", "setInterval", "clearInterval", "fetch", "require",
    "module", "exports",
];

pub struct JavaScriptLanguage;

impl CLikeLanguage for JavaScriptLanguage {
    fn name(&self) -> &'static str {
        "JavaScriptLanguage"
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

    fn try_match_string(
        &self,
        line: &[char],
        pos: usize,
        spans: &mut Vec<StyledSpan>,
        _state: &mut u8,
    ) -> Option<usize> {
        match_template_literal(line, pos, spans).or_else(|| base_try_match_string(line, pos, spans))
    }
}
