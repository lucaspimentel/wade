//! Port of the `RegexLanguage` base: a single-pass, left-to-right scanner
//! for C-like languages with pluggable keyword/constant/builtin sets and
//! hooks for language specifics. C# subclasses override virtual methods;
//! here a language implements `CLikeLanguage`, overriding the hooks it
//! needs, and gets `Language` through the blanket impl.

use super::scan::{index_of, starts_with_at, substring};
use super::{Language, StyledLine, StyledSpan, TokenKind};
use crate::text::{is_digit, is_letter, is_letter_or_digit, is_lower, is_upper, is_whitespace};

// State values shared by all C-like languages
pub const STATE_NORMAL: u8 = 0;
pub const STATE_BLOCK_COMMENT: u8 = 1;
pub const STATE_MULTI_STRING: u8 = 2;

pub trait CLikeLanguage: Sync {
    fn name(&self) -> &'static str;
    fn keywords(&self) -> &'static [&'static str];
    fn constants(&self) -> &'static [&'static str];
    fn builtins(&self) -> &'static [&'static str];

    /// Single-line comment prefix (`None` if unsupported).
    fn line_comment_prefix(&self) -> Option<&'static str> {
        Some("//")
    }

    /// Block comment open/close (`None` if unsupported).
    fn block_comment(&self) -> Option<(&'static str, &'static str)> {
        Some(("/*", "*/"))
    }

    /// Runs before the main scan: may emit spans and returns the position to
    /// continue from, or `None` to skip the main loop (C# `-1`).
    fn try_match_line_prefix(&self, _line: &[char], _spans: &mut Vec<StyledSpan>, _state: &mut u8) -> Option<usize> {
        Some(0)
    }

    /// Language-specific patterns the base scanner does not know; returns
    /// the length consumed (0 = not handled).
    fn try_match_extension(&self, _line: &[char], _pos: usize, _spans: &mut Vec<StyledSpan>) -> usize {
        0
    }

    /// With `state == STATE_MULTI_STRING`, finds the end of the multi-line
    /// string from `pos`, emitting spans and updating state; returns the
    /// position after the closing delimiter (or the line length).
    fn try_end_multi_string(&self, _line: &[char], pos: usize, _spans: &mut Vec<StyledSpan>, state: &mut u8) -> usize {
        // Default: no multi-line strings
        *state = STATE_NORMAL;
        pos
    }

    /// Matches a string literal at `pos`; returns the end position.
    fn try_match_string(&self, line: &[char], pos: usize, spans: &mut Vec<StyledSpan>, _state: &mut u8) -> Option<usize> {
        base_try_match_string(line, pos, spans)
    }

    fn try_match_number(&self, line: &[char], pos: usize) -> Option<usize> {
        base_try_match_number(line, pos)
    }

    fn classify_word(&self, word: &str) -> TokenKind {
        base_classify_word(self.keywords(), self.constants(), self.builtins(), word)
    }
}

impl<T: CLikeLanguage> Language for T {
    fn name(&self) -> &'static str {
        CLikeLanguage::name(self)
    }

    /// Port of `RegexLanguage.TokenizeLine`.
    fn tokenize_line(&self, text: &str, state: &mut u8) -> StyledLine {
        if text.is_empty() {
            return StyledLine::plain(text);
        }

        let line: Vec<char> = text.chars().collect();
        let mut spans = Vec::new();

        // Continuation of a block comment from the previous line
        if *state == STATE_BLOCK_COMMENT {
            let (_, close) = self.block_comment().expect("block comment state without block comments");

            let Some(end) = index_of(&line, close, 0) else {
                // Entire line is comment
                spans.push(StyledSpan::new(0, line.len(), TokenKind::Comment));
                return StyledLine::with_spans(text, spans);
            };

            let close_end = end + close.chars().count();
            spans.push(StyledSpan::new(0, close_end, TokenKind::Comment));
            *state = STATE_NORMAL;
            scan_line(self, &line, close_end, &mut spans, state);
            return StyledLine::with_spans(text, spans);
        }

        // Continuation of a multi-line string from the previous line
        if *state == STATE_MULTI_STRING {
            let end = self.try_end_multi_string(&line, 0, &mut spans, state);

            if *state != STATE_MULTI_STRING {
                scan_line(self, &line, end, &mut spans, state);
            }

            return StyledLine::with_spans(text, spans);
        }

        if let Some(prefix_end) = self.try_match_line_prefix(&line, &mut spans, state) {
            scan_line(self, &line, prefix_end, &mut spans, state);
        }

        StyledLine::with_spans(text, spans)
    }
}

/// Port of `RegexLanguage.ScanLine`.
pub fn scan_line<L: CLikeLanguage + ?Sized>(
    language: &L,
    line: &[char],
    start: usize,
    spans: &mut Vec<StyledSpan>,
    state: &mut u8,
) {
    let mut pos = start;
    let len = line.len();

    while pos < len {
        // Skip whitespace
        if is_whitespace(line[pos]) {
            pos += 1;
            continue;
        }

        // Line comment
        if let Some(prefix) = language.line_comment_prefix()
            && starts_with_at(line, pos, prefix)
        {
            spans.push(StyledSpan::new(pos, len - pos, TokenKind::Comment));
            return;
        }

        // Block comment open
        if let Some((open, close)) = language.block_comment()
            && starts_with_at(line, pos, open)
        {
            if let Some(close_index) = index_of(line, close, pos + open.chars().count()) {
                let comment_end = close_index + close.chars().count();
                spans.push(StyledSpan::new(pos, comment_end - pos, TokenKind::Comment));
                pos = comment_end;
                continue;
            }

            // Multi-line block comment
            spans.push(StyledSpan::new(pos, len - pos, TokenKind::Comment));
            *state = STATE_BLOCK_COMMENT;
            return;
        }

        // Extension hook (attributes, directives, ...)
        let extension_len = language.try_match_extension(line, pos, spans);
        if extension_len > 0 {
            pos += extension_len;
            continue;
        }

        // Strings
        if let Some(string_end) = language.try_match_string(line, pos, spans, state) {
            if *state == STATE_MULTI_STRING {
                return;
            }

            pos = string_end;
            continue;
        }

        // Numbers
        if let Some(number_end) = language.try_match_number(line, pos) {
            spans.push(StyledSpan::new(pos, number_end - pos, TokenKind::Number));
            pos = number_end;
            continue;
        }

        // Identifiers / keywords
        if is_letter(line[pos]) || line[pos] == '_' {
            let mut id_end = pos + 1;

            while id_end < len && (is_letter_or_digit(line[id_end]) || line[id_end] == '_') {
                id_end += 1;
            }

            let kind = language.classify_word(&substring(line, pos, id_end));
            if kind != TokenKind::Plain {
                spans.push(StyledSpan::new(pos, id_end - pos, kind));
            }

            pos = id_end;
            continue;
        }

        // Operators and punctuation
        if let Some(op_end) = try_match_operator_or_punct(line, pos, spans) {
            pos = op_end;
            continue;
        }

        pos += 1;
    }
}

/// Base `TryMatchString`: single- and double-quoted strings.
pub fn base_try_match_string(line: &[char], pos: usize, spans: &mut Vec<StyledSpan>) -> Option<usize> {
    let ch = line[pos];

    if ch == '"' || ch == '\'' {
        return Some(scan_quoted_string(line, pos, ch, spans));
    }

    None
}

/// Port of `ScanQuotedString`: a quoted string with backslash escapes;
/// unterminated strings run to the end of the line.
pub fn scan_quoted_string(line: &[char], start: usize, quote: char, spans: &mut Vec<StyledSpan>) -> usize {
    let mut pos = start + 1;

    while pos < line.len() {
        let c = line[pos];

        if c == '\\' {
            pos += 2;
            continue;
        }

        if c == quote {
            pos += 1;
            break;
        }

        pos += 1;
    }

    // An escape at the very end steps past the line, as C# does; the span
    // still covers it (C# Length is pos - start)
    spans.push(StyledSpan::new(start, pos - start, TokenKind::String));
    pos
}

/// Base `TryMatchNumber`: hex, ints and floats with `_` separators and
/// trailing letter suffixes.
pub fn base_try_match_number(line: &[char], pos: usize) -> Option<usize> {
    let ch = line[pos];

    if !is_digit(ch) {
        return None;
    }

    let start = pos;
    let mut pos = pos;

    // Hex: 0x...
    if ch == '0' && pos + 1 < line.len() && (line[pos + 1] == 'x' || line[pos + 1] == 'X') {
        pos += 2;

        while pos < line.len() && (line[pos].is_ascii_hexdigit() || line[pos] == '_') {
            pos += 1;
        }

        return Some(pos);
    }

    // Regular number (int / float) with optional _ separators
    let mut has_dot = false;

    while pos < line.len() {
        let c = line[pos];

        if is_digit(c) || c == '_' {
            pos += 1;
            continue;
        }

        if c == '.' && !has_dot && pos + 1 < line.len() && is_digit(line[pos + 1]) {
            has_dot = true;
            pos += 1;
            continue;
        }

        break;
    }

    // Skip a trailing type suffix (f, d, L, m, u, ul, ...)
    while pos < line.len() && is_letter(line[pos]) {
        pos += 1;
    }

    (pos > start).then_some(pos)
}

/// Base `ClassifyWord`: keyword, constant, builtin, then the PascalCase
/// -> Type heuristic.
#[must_use]
pub fn base_classify_word(keywords: &[&str], constants: &[&str], builtins: &[&str], word: &str) -> TokenKind {
    if keywords.contains(&word) {
        return TokenKind::Keyword;
    }

    if constants.contains(&word) {
        return TokenKind::Constant;
    }

    if builtins.contains(&word) {
        return TokenKind::BuiltinFunc;
    }

    let mut chars = word.chars();
    if word.chars().count() >= 2 && chars.next().is_some_and(is_upper) && word.chars().any(is_lower) {
        return TokenKind::Type;
    }

    TokenKind::Plain
}

fn try_match_operator_or_punct(line: &[char], pos: usize, spans: &mut Vec<StyledSpan>) -> Option<usize> {
    let ch = line[pos];

    if matches!(ch, '{' | '}' | '(' | ')' | '[' | ']' | ';' | ',' | ':') {
        spans.push(StyledSpan::new(pos, 1, TokenKind::Punctuation));
        return Some(pos + 1);
    }

    if matches!(ch, '=' | '+' | '-' | '*' | '/' | '%' | '<' | '>' | '!' | '&' | '|' | '^' | '~' | '?' | '@') {
        // Multi-char operators: ->, =>, !=, ==, <=, >=, &&, ||, ++, --, <<, >>
        let op_len = if pos + 1 < line.len()
            && matches!(
                (ch, line[pos + 1]),
                ('-', '>')
                    | ('=', '>')
                    | ('!', '=')
                    | ('=', '=')
                    | ('<', '=')
                    | ('>', '=')
                    | ('&', '&')
                    | ('|', '|')
                    | ('+', '+')
                    | ('-', '-')
                    | ('<', '<')
                    | ('>', '>')
            ) {
            2
        } else {
            1
        };

        spans.push(StyledSpan::new(pos, op_len, TokenKind::Operator));
        return Some(pos + op_len);
    }

    if ch == '.' {
        spans.push(StyledSpan::new(pos, 1, TokenKind::Punctuation));
        return Some(pos + 1);
    }

    None
}

/// Shared by C# and PowerShell: `[...]` with nested brackets as an
/// attribute when it closes on this line.
pub fn match_bracket_attribute(line: &[char], pos: usize, spans: &mut Vec<StyledSpan>) -> usize {
    if line[pos] != '[' {
        return 0;
    }

    let mut end = pos + 1;
    let mut depth = 1;

    while end < line.len() && depth > 0 {
        if line[end] == '[' {
            depth += 1;
        } else if line[end] == ']' {
            depth -= 1;
        }

        end += 1;
    }

    if depth == 0 {
        spans.push(StyledSpan::new(pos, end - pos, TokenKind::Attribute));
        return end - pos;
    }

    0
}

/// Shared by C and C#: a line whose first non-space char is `#` is one
/// directive span and skips the main scan.
pub fn match_hash_directive(line: &[char], spans: &mut Vec<StyledSpan>) -> Option<usize> {
    let indent = line.iter().take_while(|&&c| c == ' ').count();

    if indent < line.len() && line[indent] == '#' {
        spans.push(StyledSpan::new(0, line.len(), TokenKind::Directive));
        return None; // Skip main scan loop
    }

    Some(0)
}

/// Shared by C# and Java: closes a `"""` multi-line string.
pub fn end_triple_quote_string(line: &[char], pos: usize, spans: &mut Vec<StyledSpan>, state: &mut u8) -> usize {
    if let Some(close_index) = index_of(line, "\"\"\"", pos) {
        let close_end = close_index + 3;
        spans.push(StyledSpan::new(pos, close_end - pos, TokenKind::String));
        *state = STATE_NORMAL;
        return close_end;
    }

    spans.push(StyledSpan::new(pos, line.len() - pos, TokenKind::String));
    line.len()
}

/// Shared by C# and Java: opens a `"""` string at `pos` (closed on this line
/// or continued as multi-line).
pub fn match_triple_quote_string(
    line: &[char],
    pos: usize,
    spans: &mut Vec<StyledSpan>,
    state: &mut u8,
) -> Option<usize> {
    if !starts_with_at(line, pos, "\"\"\"") {
        return None;
    }

    if let Some(close_index) = index_of(line, "\"\"\"", pos + 3) {
        let close_end = close_index + 3;
        spans.push(StyledSpan::new(pos, close_end - pos, TokenKind::String));
        return Some(close_end);
    }

    // Multi-line string
    spans.push(StyledSpan::new(pos, line.len() - pos, TokenKind::String));
    *state = STATE_MULTI_STRING;
    Some(line.len())
}
