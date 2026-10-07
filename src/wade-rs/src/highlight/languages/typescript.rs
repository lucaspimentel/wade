//! Port of `TypeScriptLanguage`: `JavaScriptLanguage` with the TypeScript keyword set.

use super::javascript::{BUILTINS, CONSTANTS, match_template_literal};
use crate::highlight::StyledSpan;
use crate::highlight::c_like::CLikeLanguage;
use crate::highlight::c_like::base_try_match_string;

const KEYWORDS: &[&str] = &[
    "abstract", "as", "async", "await", "break", "case", "catch", "class", "const", "continue", "debugger", "declare",
    "default", "delete", "do", "else", "enum", "export", "extends", "finally", "for", "from", "function", "if",
    "implements", "import", "in", "infer", "instanceof", "interface", "is", "keyof", "let", "module", "namespace",
    "new", "never", "of", "override", "private", "protected", "public", "readonly", "return", "satisfies", "static",
    "super", "switch", "this", "throw", "try", "type", "typeof", "unique", "var", "void", "while", "with", "yield",
    "get", "set", "asserts",
];

pub struct TypeScriptLanguage;

impl CLikeLanguage for TypeScriptLanguage {
    fn name(&self) -> &'static str {
        "TypeScriptLanguage"
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
