//! Port of `JavaLanguage`.

use crate::highlight::c_like::CLikeLanguage;
use crate::highlight::c_like::{base_try_match_string, end_triple_quote_string, match_triple_quote_string};
use crate::highlight::{StyledSpan, TokenKind};
use crate::text::is_letter_or_digit;

const KEYWORDS: &[&str] = &[
    "abstract", "assert", "break", "case", "catch", "class", "const", "continue", "default", "do", "else", "enum",
    "extends", "final", "finally", "for", "goto", "if", "implements", "import", "instanceof", "interface", "native",
    "new", "package", "private", "protected", "public", "record", "return", "sealed", "static", "strictfp", "super",
    "switch", "synchronized", "this", "throw", "throws", "transient", "try", "var", "volatile", "while", "yield",
];

const CONSTANTS: &[&str] = &["true", "false", "null"];

const BUILTINS: &[&str] = &[
    "boolean", "byte", "char", "double", "float", "int", "long", "short", "void", "String", "Object", "Integer",
    "Long", "Double", "Float", "Boolean", "Character", "Byte", "Short", "Number", "System", "Math", "Arrays",
    "Collections", "List", "Map", "Set", "ArrayList", "HashMap", "HashSet", "Optional",
];

pub struct JavaLanguage;

impl CLikeLanguage for JavaLanguage {
    fn name(&self) -> &'static str {
        "JavaLanguage"
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
        // Annotations: @Override, @SuppressWarnings("...")
        if line[pos] == '@' {
            let mut end = pos + 1;

            while end < line.len() && (is_letter_or_digit(line[end]) || line[end] == '_') {
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
        // Text blocks: """...""" (multi-line)
        if let Some(end) = match_triple_quote_string(line, pos, spans, state) {
            return Some(end);
        }

        base_try_match_string(line, pos, spans)
    }

    fn try_end_multi_string(&self, line: &[char], pos: usize, spans: &mut Vec<StyledSpan>, state: &mut u8) -> usize {
        end_triple_quote_string(line, pos, spans, state)
    }
}
