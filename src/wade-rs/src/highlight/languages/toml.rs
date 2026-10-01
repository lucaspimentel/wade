//! Port of `TomlLanguage` (also used for INI-like files): section headers,
//! `key = value` pairs with string/constant/number values, comments, and
//! `"""` multi-line strings (state 2).

use crate::highlight::scan::{index_of, index_of_char, starts_with_at};
use crate::highlight::{Language, StyledLine, StyledSpan, TokenKind};
use crate::text::{is_digit, is_letter_or_digit, is_whitespace};

const STATE_MULTI_STRING: u8 = 2;

pub struct TomlLanguage;

impl Language for TomlLanguage {
    fn name(&self) -> &'static str {
        "TomlLanguage"
    }

    fn tokenize_line(&self, text: &str, state: &mut u8) -> StyledLine {
        if text.is_empty() {
            return StyledLine::plain(text);
        }

        let line: Vec<char> = text.chars().collect();
        let len = line.len();
        let mut spans = Vec::new();
        let mut pos = 0;

        // Continuation of a multi-line string
        if *state == STATE_MULTI_STRING {
            let Some(close_index) = index_of(&line, "\"\"\"", 0) else {
                spans.push(StyledSpan::new(0, len, TokenKind::String));
                return StyledLine::with_spans(text, spans);
            };

            let close_end = close_index + 3;
            spans.push(StyledSpan::new(0, close_end, TokenKind::String));
            *state = 0;
            pos = close_end;
        }

        while pos < len {
            if is_whitespace(line[pos]) {
                pos += 1;
                continue;
            }

            let ch = line[pos];

            // Comment
            if ch == '#' {
                spans.push(StyledSpan::new(pos, len - pos, TokenKind::Comment));
                break;
            }

            // Section header: [section] or [[array]]
            if ch == '[' {
                let end = index_of_char(&line, ']', pos);

                if end.is_some() && line[pos + 1] == '[' {
                    // [[array table]]
                    if let Some(end2) = index_of(&line, "]]", pos + 2) {
                        spans.push(StyledSpan::new(pos, end2 + 2 - pos, TokenKind::Key));
                        pos = end2 + 2;
                        continue;
                    }
                } else if let Some(end) = end {
                    spans.push(StyledSpan::new(pos, end + 1 - pos, TokenKind::Key));
                    pos = end + 1;
                    continue;
                }

                spans.push(StyledSpan::new(pos, 1, TokenKind::Punctuation));
                pos += 1;
                continue;
            }

            if matches!(ch, ']' | '{' | '}' | ',') {
                spans.push(StyledSpan::new(pos, 1, TokenKind::Punctuation));
                pos += 1;
                continue;
            }

            // key = value
            if is_letter_or_digit(ch) || ch == '_' || ch == '-' || ch == '"' {
                let key_start = pos;

                if ch == '"' {
                    // Quoted key
                    pos += 1;

                    while pos < len && line[pos] != '"' {
                        pos += 1;
                    }

                    if pos < len {
                        pos += 1;
                    }
                } else {
                    while pos < len && (is_letter_or_digit(line[pos]) || matches!(line[pos], '_' | '-' | '.')) {
                        pos += 1;
                    }
                }

                let key_end = pos;

                while pos < len && line[pos] == ' ' {
                    pos += 1;
                }

                if pos < len && line[pos] == '=' {
                    spans.push(StyledSpan::new(key_start, key_end - key_start, TokenKind::Key));
                    spans.push(StyledSpan::new(pos, 1, TokenKind::Operator));
                    pos += 1;
                    scan_value(&line, pos, &mut spans, state);
                    break;
                }

                // Not a key = value pattern
                continue;
            }

            pos += 1;
        }

        StyledLine::with_spans(text, spans)
    }
}

/// Port of `ScanValue`: one value after `=` (anything after it on the line,
/// such as a trailing comment, is not scanned, as in C#).
fn scan_value(line: &[char], start: usize, spans: &mut Vec<StyledSpan>, state: &mut u8) {
    let len = line.len();
    let mut pos = start;

    while pos < len && line[pos] == ' ' {
        pos += 1;
    }

    if pos >= len {
        return;
    }

    let ch = line[pos];

    // Multi-line string: """
    if starts_with_at(line, pos, "\"\"\"") {
        if let Some(close_index) = index_of(line, "\"\"\"", pos + 3) {
            spans.push(StyledSpan::new(pos, close_index + 3 - pos, TokenKind::String));
            return;
        }

        spans.push(StyledSpan::new(pos, len - pos, TokenKind::String));
        *state = STATE_MULTI_STRING;
        return;
    }

    // Regular string (escapes only in basic "..." strings)
    if ch == '"' || ch == '\'' {
        let quote = ch;
        let mut p = pos + 1;

        while p < len {
            if line[p] == '\\' && quote == '"' {
                p += 2;
                continue;
            }

            if line[p] == quote {
                p += 1;
                break;
            }

            p += 1;
        }

        spans.push(StyledSpan::new(pos, p - pos, TokenKind::String));
        return;
    }

    // Boolean
    if starts_with_at(line, pos, "true") {
        spans.push(StyledSpan::new(pos, 4, TokenKind::Constant));
        return;
    }

    if starts_with_at(line, pos, "false") {
        spans.push(StyledSpan::new(pos, 5, TokenKind::Constant));
        return;
    }

    // Number or date: up to whitespace, '#' or ','
    if is_digit(ch) || ch == '-' || ch == '+' {
        let num_start = pos;

        while pos < len && !is_whitespace(line[pos]) && line[pos] != '#' && line[pos] != ',' {
            pos += 1;
        }

        spans.push(StyledSpan::new(num_start, pos - num_start, TokenKind::Number));
    }

    // Arrays and inline tables are left to the main loop (which has already
    // stopped scanning this line, as in C#)
}
