//! Port of `SyntaxTheme`: VS Code Dark+ inspired token colors.

use super::TokenKind;
use crate::screen::{CellStyle, Color};

const fn style(r: u8, g: u8, b: u8, bold: bool, dim: bool) -> CellStyle {
    CellStyle {
        fg: Some(Color { r, g, b }),
        bg: None,
        bold,
        dim,
        inverse: false,
        underline: false,
        strikethrough: false,
    }
}

pub const PLAIN: CellStyle = style(200, 200, 200, false, false);
const KEYWORD: CellStyle = style(86, 156, 214, true, false); // blue
const STRING: CellStyle = style(206, 145, 120, false, false); // salmon
const COMMENT: CellStyle = style(106, 153, 85, false, false); // green
const NUMBER: CellStyle = style(181, 206, 168, false, false); // light green
const TYPE: CellStyle = style(78, 201, 176, false, false); // teal
const ATTRIBUTE: CellStyle = style(156, 220, 254, false, false); // light blue
const OPERATOR: CellStyle = style(180, 180, 180, false, false); // gray
const PUNCT: CellStyle = style(180, 180, 180, false, false); // gray
const CONSTANT: CellStyle = style(86, 156, 214, false, false); // blue (same as keyword)
const BUILTIN: CellStyle = style(220, 220, 170, false, false); // yellow
const HEADING: CellStyle = style(86, 156, 214, true, false); // blue bold
const BOLD: CellStyle = style(200, 200, 200, true, false);
const ITALIC: CellStyle = style(200, 200, 200, false, true);
const LINK: CellStyle = style(78, 201, 176, false, false); // teal
const CODE_SPAN: CellStyle = style(206, 145, 120, false, false); // salmon
const TAG_NAME: CellStyle = style(86, 156, 214, false, false); // blue
const ATTR_NAME: CellStyle = style(156, 220, 254, false, false); // light blue
const ATTR_VALUE: CellStyle = style(206, 145, 120, false, false); // salmon
const KEY: CellStyle = style(156, 220, 254, false, false); // light blue
const DIRECTIVE: CellStyle = style(155, 155, 155, false, false); // dim gray

/// Port of `SyntaxTheme.GetStyle`.
#[must_use]
pub const fn get_style(kind: TokenKind) -> CellStyle {
    match kind {
        TokenKind::Keyword => KEYWORD,
        TokenKind::String => STRING,
        TokenKind::Comment => COMMENT,
        TokenKind::Number => NUMBER,
        TokenKind::Type => TYPE,
        TokenKind::Attribute => ATTRIBUTE,
        TokenKind::Operator => OPERATOR,
        TokenKind::Punctuation => PUNCT,
        TokenKind::Constant | TokenKind::HexColor => CONSTANT,
        TokenKind::BuiltinFunc => BUILTIN,
        TokenKind::Heading => HEADING,
        TokenKind::Bold => BOLD,
        TokenKind::Italic => ITALIC,
        TokenKind::Link => LINK,
        TokenKind::CodeSpan => CODE_SPAN,
        TokenKind::TagName => TAG_NAME,
        TokenKind::AttrName => ATTR_NAME,
        TokenKind::AttrValue => ATTR_VALUE,
        TokenKind::Key => KEY,
        TokenKind::Directive => DIRECTIVE,
        TokenKind::Plain => PLAIN,
    }
}

#[cfg(test)]
mod tests {
    use super::{get_style, PLAIN};
    use crate::highlight::TokenKind;
    use crate::screen::Color;

    #[test]
    fn theme_matches_csharp_palette() {
        let keyword = get_style(TokenKind::Keyword);
        assert!(keyword.fg == Some(Color { r: 86, g: 156, b: 214 }) && keyword.bold);
        assert!(get_style(TokenKind::Italic).dim);
        assert!(get_style(TokenKind::HexColor) == get_style(TokenKind::Constant));
        assert!(get_style(TokenKind::Plain) == PLAIN);
        assert!(get_style(TokenKind::Directive).fg == Some(Color { r: 155, g: 155, b: 155 }));
    }
}
