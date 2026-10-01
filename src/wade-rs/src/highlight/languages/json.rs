//! Port of `JsonLanguage`: keys vs string values (tracked by `:`), numbers,
//! constants and punctuation. Stateless across lines.

use crate::highlight::scan::starts_with_at;
use crate::highlight::{Language, StyledLine, StyledSpan, TokenKind};
use crate::text::{is_digit, is_whitespace};

pub struct JsonLanguage;

impl Language for JsonLanguage {
    fn name(&self) -> &'static str {
        "JsonLanguage"
    }

    fn tokenize_line(&self, text: &str, _state: &mut u8) -> StyledLine {
        if text.is_empty() {
            return StyledLine::plain(text);
        }

        let line: Vec<char> = text.chars().collect();
        let mut spans = Vec::new();
        scan_json(&line, &mut spans);
        StyledLine::with_spans(text, spans)
    }
}

fn scan_json(line: &[char], spans: &mut Vec<StyledSpan>) {
    let mut pos = 0;
    let len = line.len();

    // Whether a key comes next (as opposed to a value after ':')
    let mut expect_key = true;

    while pos < len {
        if is_whitespace(line[pos]) {
            pos += 1;
            continue;
        }

        let ch = line[pos];

        if matches!(ch, '{' | '}' | '[' | ']' | ',') {
            spans.push(StyledSpan::new(pos, 1, TokenKind::Punctuation));

            if ch == '{' || ch == '[' {
                expect_key = true;
            }

            pos += 1;
            continue;
        }

        if ch == ':' {
            spans.push(StyledSpan::new(pos, 1, TokenKind::Punctuation));
            expect_key = false;
            pos += 1;
            continue;
        }

        // String (key or value)
        if ch == '"' {
            let start = pos;
            pos += 1;

            while pos < len {
                if line[pos] == '\\' {
                    pos += 2;
                    continue;
                }

                if line[pos] == '"' {
                    pos += 1;
                    break;
                }

                pos += 1;
            }

            let kind = if expect_key { TokenKind::Key } else { TokenKind::String };
            spans.push(StyledSpan::new(start, pos - start, kind));
            expect_key = false; // after a key, ':' then a value
            continue;
        }

        // Number
        if is_digit(ch) || ch == '-' {
            let start = pos;

            if ch == '-' {
                pos += 1;
            }

            while pos < len && (is_digit(line[pos]) || matches!(line[pos], '.' | 'e' | 'E' | '+' | '-')) {
                pos += 1;
            }

            spans.push(StyledSpan::new(start, pos - start, TokenKind::Number));
            expect_key = false;
            continue;
        }

        // true / false / null
        if let Some(word) = ["true", "false", "null"].into_iter().find(|word| starts_with_at(line, pos, word)) {
            spans.push(StyledSpan::new(pos, word.len(), TokenKind::Constant));
            pos += word.len();
            expect_key = false;
            continue;
        }

        pos += 1;
    }
}
