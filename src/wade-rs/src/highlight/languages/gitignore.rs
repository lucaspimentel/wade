//! Port of `GitIgnoreLanguage`: comments, negation, glob wildcards,
//! character classes, separators and escapes in ignore-file patterns.

use crate::highlight::scan::index_of_char;
use crate::highlight::{Language, StyledLine, StyledSpan, TokenKind};

pub struct GitIgnoreLanguage;

impl Language for GitIgnoreLanguage {
    fn name(&self) -> &'static str {
        "GitIgnoreLanguage"
    }

    fn tokenize_line(&self, text: &str, _state: &mut u8) -> StyledLine {
        if text.is_empty() {
            return StyledLine::plain(text);
        }

        let line: Vec<char> = text.chars().collect();

        // Skip leading spaces for classification (positions stay absolute)
        let mut pos = line.iter().take_while(|&&c| c == ' ').count();

        if pos >= line.len() {
            return StyledLine::plain(text);
        }

        // Comment line
        if line[pos] == '#' {
            return StyledLine::with_spans(text, vec![StyledSpan::new(pos, line.len() - pos, TokenKind::Comment)]);
        }

        let mut spans = Vec::new();

        // Negation prefix
        if line[pos] == '!' {
            spans.push(StyledSpan::new(pos, 1, TokenKind::Operator));
            pos += 1;
        }

        while pos < line.len() {
            match line[pos] {
                // Glob wildcards: ** or *
                '*' => {
                    let len = if pos + 1 < line.len() && line[pos + 1] == '*' { 2 } else { 1 };
                    spans.push(StyledSpan::new(pos, len, TokenKind::Keyword));
                    pos += len;
                }
                '?' => {
                    spans.push(StyledSpan::new(pos, 1, TokenKind::Keyword));
                    pos += 1;
                }
                // Character class [...]
                '[' => match index_of_char(&line, ']', pos + 1) {
                    Some(close) => {
                        spans.push(StyledSpan::new(pos, close + 1 - pos, TokenKind::String));
                        pos = close + 1;
                    }
                    None => pos += 1,
                },
                // Path separator
                '/' => {
                    spans.push(StyledSpan::new(pos, 1, TokenKind::Punctuation));
                    pos += 1;
                }
                // Escape sequence
                '\\' => {
                    if pos + 1 < line.len() {
                        spans.push(StyledSpan::new(pos, 2, TokenKind::String));
                        pos += 2;
                    } else {
                        pos += 1;
                    }
                }
                _ => pos += 1,
            }
        }

        StyledLine::with_spans(text, spans)
    }
}
