//! Port of `YamlLanguage`: comments, document markers, list items,
//! `key: value` pairs and scalar values. Stateless across lines.

use crate::highlight::scan::{index_of, starts_with_at};
use crate::highlight::{Language, StyledLine, StyledSpan, TokenKind};
use crate::text::{is_digit, is_whitespace};

pub struct YamlLanguage;

impl Language for YamlLanguage {
    fn name(&self) -> &'static str {
        "YamlLanguage"
    }

    fn tokenize_line(&self, text: &str, _state: &mut u8) -> StyledLine {
        if text.is_empty() {
            return StyledLine::plain(text);
        }

        let line: Vec<char> = text.chars().collect();
        let len = line.len();
        let mut spans = Vec::new();

        // Skip indentation
        let mut pos = line.iter().take_while(|&&c| c == ' ').count();

        if pos >= len {
            return StyledLine::plain(text);
        }

        let ch = line[pos];

        // Comment
        if ch == '#' {
            spans.push(StyledSpan::new(pos, len - pos, TokenKind::Comment));
            return StyledLine::with_spans(text, spans);
        }

        // Document markers: --- or ...
        if starts_with_at(&line, pos, "---") || starts_with_at(&line, pos, "...") {
            spans.push(StyledSpan::new(pos, 3, TokenKind::Directive));
            return StyledLine::with_spans(text, spans);
        }

        // List item: - value
        if ch == '-' && (pos + 1 >= len || line[pos + 1] == ' ') {
            spans.push(StyledSpan::new(pos, 1, TokenKind::Punctuation));
            pos += 1;

            if pos < len && line[pos] == ' ' {
                pos += 1;
            }

            scan_yaml_value(&line, pos, &mut spans);
            return StyledLine::with_spans(text, spans);
        }

        // key: value
        let key_start = pos;

        if ch == '"' || ch == '\'' {
            // Quoted key
            pos += 1;

            while pos < len && line[pos] != ch {
                pos += 1;
            }

            if pos < len {
                pos += 1;
            }
        } else {
            // Unquoted key: up to ':' (or a comment)
            while pos < len && line[pos] != ':' && line[pos] != '#' {
                pos += 1;
            }
        }

        let key_end = pos;
        let mut after_key = pos;

        while after_key < len && line[after_key] == ' ' {
            after_key += 1;
        }

        if after_key < len && line[after_key] == ':' {
            spans.push(StyledSpan::new(key_start, key_end - key_start, TokenKind::Key));
            spans.push(StyledSpan::new(after_key, 1, TokenKind::Punctuation));

            let mut value_start = after_key + 1;
            while value_start < len && line[value_start] == ' ' {
                value_start += 1;
            }

            if value_start < len {
                scan_yaml_value(&line, value_start, &mut spans);
            }

            return StyledLine::with_spans(text, spans);
        }

        // Plain value (continuation line or block scalar content)
        scan_yaml_value(&line, key_start, &mut spans);
        StyledLine::with_spans(text, spans)
    }
}

/// Port of `ScanYamlValue`.
fn scan_yaml_value(line: &[char], pos: usize, spans: &mut Vec<StyledSpan>) {
    let len = line.len();

    if pos >= len {
        return;
    }

    let ch = line[pos];

    // Trailing comment (" #")
    let comment_index = index_of(line, " #", pos);
    let push_comment = |spans: &mut Vec<StyledSpan>, index: usize| {
        spans.push(StyledSpan::new(index + 1, len - index - 1, TokenKind::Comment));
    };

    // Quoted string
    if ch == '"' || ch == '\'' {
        let mut p = pos + 1;

        while p < len {
            if ch == '"' && line[p] == '\\' {
                p += 2;
                continue;
            }

            if line[p] == ch {
                p += 1;
                break;
            }

            p += 1;
        }

        spans.push(StyledSpan::new(pos, p - pos, TokenKind::String));

        if let Some(index) = comment_index.filter(|&index| index >= p) {
            push_comment(spans, index);
        }

        return;
    }

    // Boolean / null (prefix match, as in C#)
    if ["true", "false", "yes", "no", "null", "~"].iter().any(|word| starts_with_at(line, pos, word)) {
        let comment = comment_index.filter(|&index| index > pos);
        let end = comment.unwrap_or(len);
        spans.push(StyledSpan::new(pos, end - pos, TokenKind::Constant));

        if let Some(index) = comment {
            push_comment(spans, index);
        }

        return;
    }

    // Number: up to whitespace or '#'
    if is_digit(ch) || (ch == '-' && pos + 1 < len && is_digit(line[pos + 1])) {
        let mut p = pos;

        while p < len && !is_whitespace(line[p]) && line[p] != '#' {
            p += 1;
        }

        spans.push(StyledSpan::new(pos, p - pos, TokenKind::Number));

        if let Some(index) = comment_index.filter(|&index| index >= p) {
            push_comment(spans, index);
        }

        return;
    }

    // Plain string: no span, only a trailing comment
    if let Some(index) = comment_index.filter(|&index| index > pos) {
        push_comment(spans, index);
    }
}
