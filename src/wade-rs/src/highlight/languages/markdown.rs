//! Port of `MarkdownLanguage`: fenced code blocks (state 1), headings,
//! blockquotes, rules, list markers, and the inline code/bold/italic/link
//! patterns. The C# inline patterns are .NET regexes; each is ported as a
//! hand-written matcher with the same leftmost, non-overlapping match
//! semantics (greedy groups try longest first, lazy `.+?` shortest first).

use crate::highlight::{Language, StyledLine, StyledSpan, TokenKind};
use crate::text::{is_digit, is_whitespace};

const STATE_NORMAL: u8 = 0;
const STATE_CODE_FENCE: u8 = 1;

pub struct MarkdownLanguage;

impl Language for MarkdownLanguage {
    fn name(&self) -> &'static str {
        "MarkdownLanguage"
    }

    fn tokenize_line(&self, text: &str, state: &mut u8) -> StyledLine {
        if text.is_empty() {
            return StyledLine::plain(text);
        }

        let line: Vec<char> = text.chars().collect();
        let len = line.len();
        let trimmed: &[char] = &line[line.iter().take_while(|&&c| is_whitespace(c)).count()..];
        let is_fence = trimmed.starts_with(&['`', '`', '`']) || trimmed.starts_with(&['~', '~', '~']);
        let whole_line = |kind| StyledLine::with_spans(text, vec![StyledSpan::new(0, len, kind)]);

        // Inside a fenced code block (the closing fence is code too)
        if *state == STATE_CODE_FENCE {
            if is_fence {
                *state = STATE_NORMAL;
            }

            return whole_line(TokenKind::CodeSpan);
        }

        // Fenced code block open
        if is_fence {
            *state = STATE_CODE_FENCE;
            return whole_line(TokenKind::CodeSpan);
        }

        // Heading: # ... ######
        if line[0] == '#' {
            return whole_line(TokenKind::Heading);
        }

        // Blockquote: > ...
        if line[0] == '>' {
            return StyledLine::with_spans(text, vec![StyledSpan::new(0, 1, TokenKind::Operator)]);
        }

        // Horizontal rule: ---, ***, ___ (dashes or stars may be spaced)
        let only = |marker: char| {
            trimmed.len() >= 3
                && trimmed.iter().all(|&c| c == marker || c == ' ')
                && trimmed.iter().filter(|&&c| c == marker).count() >= 3
        };
        if trimmed == ['_', '_', '_'] || only('-') || only('*') {
            return whole_line(TokenKind::Operator);
        }

        let mut spans = Vec::new();
        let indent = len - trimmed.len();

        // List item: - * + or numbered "1."
        if let Some(&first) = trimmed.first() {
            if first == '-' || first == '*' || first == '+' {
                spans.push(StyledSpan::new(indent, 1, TokenKind::Punctuation));
            } else if is_digit(first) {
                let digits = trimmed.iter().take_while(|&&c| is_digit(c)).count();

                if digits < trimmed.len() && trimmed[digits] == '.' {
                    spans.push(StyledSpan::new(indent, digits + 1, TokenKind::Punctuation));
                }
            }
        }

        // Inline patterns over the whole line, each pattern in turn
        for (start, end) in find_all(&line, match_code_span) {
            spans.push(StyledSpan::new(start, end - start, TokenKind::CodeSpan));
        }

        for (start, end) in find_all(&line, match_bold) {
            spans.push(StyledSpan::new(start, end - start, TokenKind::Bold));
        }

        for (start, end) in find_all(&line, match_italic) {
            spans.push(StyledSpan::new(start, end - start, TokenKind::Italic));
        }

        for (start, end) in find_all(&line, match_link) {
            spans.push(StyledSpan::new(start, end - start, TokenKind::Link));
        }

        StyledLine::with_spans(text, spans)
    }
}

/// `Regex.Matches`: tries each start position left to right; after a match
/// resumes at its end (matches never overlap and are never empty here).
fn find_all(line: &[char], matcher: fn(&[char], usize) -> Option<usize>) -> Vec<(usize, usize)> {
    let mut matches = Vec::new();
    let mut start = 0;

    while start < line.len() {
        match matcher(line, start) {
            Some(end) => {
                matches.push((start, end));
                start = end;
            }
            None => start += 1,
        }
    }

    matches
}

/// `` (`+)(.+?)\1 ``: a backtick run (longest first, then shorter), lazy
/// content of at least one char, then the same run again.
fn match_code_span(line: &[char], start: usize) -> Option<usize> {
    let run = line[start..].iter().take_while(|&&c| c == '`').count();

    for ticks in (1..=run).rev() {
        let content_start = start + ticks;

        for close in content_start + 1..=line.len().saturating_sub(ticks) {
            if line[close..close + ticks].iter().all(|&c| c == '`') {
                return Some(close + ticks);
            }
        }
    }

    None
}

/// `\*\*(.+?)\*\*|__(.+?)__`.
fn match_bold(line: &[char], start: usize) -> Option<usize> {
    for marker in ['*', '_'] {
        if line[start..].starts_with(&[marker, marker]) {
            let content_start = start + 2;

            for close in content_start + 1..line.len().saturating_sub(1) {
                if line[close] == marker && line[close + 1] == marker {
                    return Some(close + 2);
                }
            }
        }
    }

    None
}

/// `(?<!\*)\*(?!\*)(.+?)(?<!\*)\*(?!\*)` and the same with `_`: a single
/// marker not adjacent to another marker, lazy content, and a closing
/// single marker whose neighbors are not markers.
fn match_italic(line: &[char], start: usize) -> Option<usize> {
    let single = |marker: char, at: usize| {
        line[at] == marker && (at == 0 || line[at - 1] != marker) && line.get(at + 1) != Some(&marker)
    };

    for marker in ['*', '_'] {
        if single(marker, start) {
            for close in start + 2..line.len() {
                if single(marker, close) {
                    return Some(close + 1);
                }
            }
        }
    }

    None
}

/// `\[([^\]]+)\]\(([^)]+)\)`.
fn match_link(line: &[char], start: usize) -> Option<usize> {
    if line[start] != '[' {
        return None;
    }

    let text_len = line[start + 1..].iter().take_while(|&&c| c != ']').count();
    let close_bracket = start + 1 + text_len;

    if text_len == 0 || close_bracket >= line.len() || line.get(close_bracket + 1) != Some(&'(') {
        return None;
    }

    let url_start = close_bracket + 2;
    let url_len = line[url_start..].iter().take_while(|&&c| c != ')').count();
    let close_paren = url_start + url_len;

    (url_len > 0 && close_paren < line.len()).then_some(close_paren + 1)
}

#[cfg(test)]
mod tests {
    use super::{find_all, match_bold, match_code_span, match_italic, match_link};

    fn spans(text: &str, matcher: fn(&[char], usize) -> Option<usize>) -> Vec<(usize, usize)> {
        find_all(&text.chars().collect::<Vec<_>>(), matcher)
    }

    #[test]
    fn code_span_backtracks_like_dotnet() {
        assert_eq!(spans("a `b` c", match_code_span), [(2, 5)]);
        assert_eq!(spans("``a`", match_code_span), [(0, 4)]); // shorter run, content "`a"
        assert_eq!(spans("``x``", match_code_span), [(0, 5)]);
        assert!(spans("``", match_code_span).is_empty());
    }

    #[test]
    fn bold_italic_and_links() {
        assert_eq!(spans("**a** __b__", match_bold), [(0, 5), (6, 11)]);
        assert_eq!(spans("*a* **b** _c_", match_italic), [(0, 3), (10, 13)]);
        assert_eq!(spans("[t](u) [](x) [a]()", match_link), [(0, 6)]);
    }
}
