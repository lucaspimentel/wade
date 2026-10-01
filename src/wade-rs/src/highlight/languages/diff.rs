//! Port of `DiffLanguage`: full-line colors for unified diff output via
//! per-char styles (added green, removed red, hunk headers cyan, metadata
//! dim gray).

use crate::highlight::{Language, StyledLine};
use crate::screen::{CellStyle, Color};

const fn style(r: u8, g: u8, b: u8, dim: bool) -> CellStyle {
    CellStyle {
        fg: Some(Color { r, g, b }),
        bg: None,
        bold: false,
        dim,
        inverse: false,
        underline: false,
        strikethrough: false,
    }
}

const ADDED: CellStyle = style(80, 200, 80, false);
const REMOVED: CellStyle = style(220, 80, 80, false);
const HUNK_HEADER: CellStyle = style(80, 180, 220, true);
const METADATA: CellStyle = style(140, 140, 140, true);

pub struct DiffLanguage;

impl Language for DiffLanguage {
    fn name(&self) -> &'static str {
        "DiffLanguage"
    }

    fn tokenize_line(&self, line: &str, _state: &mut u8) -> StyledLine {
        let style = match line.chars().next() {
            Some('+') => Some(if line.starts_with("+++") { METADATA } else { ADDED }),
            Some('-') => Some(if line.starts_with("---") { METADATA } else { REMOVED }),
            Some('@') if line.starts_with("@@") => Some(HUNK_HEADER),
            Some('d') if line.starts_with("diff ") => Some(METADATA),
            Some('i') if line.starts_with("index ") => Some(METADATA),
            _ => None,
        };

        let Some(style) = style else {
            return StyledLine::plain(line);
        };

        StyledLine {
            text: line.to_string(),
            spans: None,
            char_styles: Some(vec![style; line.chars().count()]),
        }
    }
}
