//! Port of `src/Wade/Highlighting` (minus `MarkdigRenderer`, a Phase 7
//! preview): line tokenizers producing styled spans, the VS Code Dark+
//! theme, and the path -> language map.
//!
//! Span `start`/`len` are char (code point) indices into the line; they
//! equal the C# UTF-16 indices for BMP text.

pub mod c_like;
pub mod language_map;
pub mod languages;
pub mod scan;
pub mod theme;

use crate::screen::CellStyle;

pub use language_map::get_language;

/// Port of `TokenKind`.
#[derive(Clone, Copy, Debug, PartialEq, Eq, Hash)]
pub enum TokenKind {
    Plain,
    Keyword,
    String,
    Comment,
    Number,
    Type,
    Attribute,
    Operator,
    Punctuation,
    Constant,
    BuiltinFunc,
    Heading,
    Bold,
    Italic,
    Link,
    CodeSpan,
    TagName,
    AttrName,
    AttrValue,
    Key,
    Directive,
    HexColor,
}

/// Port of `StyledSpan`.
#[derive(Clone, Copy, Debug, PartialEq, Eq)]
pub struct StyledSpan {
    pub start: usize,
    pub len: usize,
    pub kind: TokenKind,
}

impl StyledSpan {
    #[must_use]
    pub const fn new(start: usize, len: usize, kind: TokenKind) -> Self {
        Self { start, len, kind }
    }
}

/// Port of `StyledLine`. When `char_styles` is set it is the authoritative
/// per-cell style source and renderers ignore `spans`.
#[derive(Clone, Debug, PartialEq, Eq)]
pub struct StyledLine {
    pub text: String,
    pub spans: Option<Vec<StyledSpan>>,
    pub char_styles: Option<Vec<CellStyle>>,
}

impl StyledLine {
    /// A line with no styling.
    #[must_use]
    pub fn plain(text: &str) -> Self {
        Self {
            text: text.to_string(),
            spans: None,
            char_styles: None,
        }
    }

    /// Port of `RegexLanguage.MakeResult`: no spans -> `None`.
    #[must_use]
    pub fn with_spans(text: &str, spans: Vec<StyledSpan>) -> Self {
        Self {
            text: text.to_string(),
            spans: if spans.is_empty() { None } else { Some(spans) },
            char_styles: None,
        }
    }
}

/// Port of `ILanguage`: tokenizes one line; `state` carries multi-line
/// context (block comments, multi-line strings, ...) across calls.
pub trait Language: Sync {
    fn tokenize_line(&self, line: &str, state: &mut u8) -> StyledLine;

    /// The C# class name (used by the parity goldens).
    fn name(&self) -> &'static str;
}

/// Port of `SyntaxHighlighter.Highlight`: tokenizes `lines` with the
/// language for `file_path`, threading state from line to line.
#[must_use]
pub fn highlight(lines: &[&str], file_path: &str) -> Vec<StyledLine> {
    let Some(language) = get_language(file_path) else {
        return lines.iter().map(|line| StyledLine::plain(line)).collect();
    };

    let mut state = 0u8;
    lines.iter().map(|line| language.tokenize_line(line, &mut state)).collect()
}

#[cfg(test)]
mod tests {
    use super::{TokenKind, highlight};

    #[test]
    fn unknown_extension_returns_plain_lines() {
        let lines = highlight(&["fn main() {}", ""], "file.unknown");
        assert_eq!(lines.len(), 2);
        assert!(lines.iter().all(|line| line.spans.is_none() && line.char_styles.is_none()));
        assert_eq!(lines[0].text, "fn main() {}");
    }

    #[test]
    fn state_threads_across_lines() {
        let lines = highlight(&["/* open", "inside", "close */ int x;"], "Program.cs");
        for line in &lines {
            assert_eq!(line.spans.as_ref().unwrap()[0].kind, TokenKind::Comment);
        }
    }

    #[test]
    fn empty_input_returns_empty() {
        assert!(highlight(&[], "Program.cs").is_empty());
    }
}
