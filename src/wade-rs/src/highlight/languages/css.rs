//! Port of `CssLanguage`: properties as keys, strings, comments, and color
//! literals in value position followed by a ` ██` swatch in the literal's
//! color. Lines with swatches carry per-char styles (the authoritative
//! render source) and spans shifted past the inserted cells.
//!
//! Rust extends the C# swatches (KNOWN_DEVIATIONS.md): `rgb()`/`hsl()` and
//! named colors, and in brace syntax (`.css`/`.scss`) a value position
//! requires being inside a block, carries across lines, and a run that
//! turns out to be a selector (followed by `{`) loses its swatches.

use crate::highlight::c_like::scan_quoted_string;
use crate::highlight::scan::{index_of, index_of_char, starts_with_at};
use crate::highlight::theme::{self, PLAIN};
use crate::highlight::{Language, StyledLine, StyledSpan, TokenKind};
use crate::screen::{CellStyle, Color};
use crate::text::{is_letter, is_letter_or_digit, is_whitespace};

/// ` ██`: a space and two full blocks.
const SWATCH_LEN: usize = 3;

/// State byte: bit 0 is the block comment, bit 1 a value continuing from
/// the previous line, bits 2-7 the brace depth.
const STATE_COMMENT_BIT: u8 = 0b01;
const STATE_VALUE_BIT: u8 = 0b10;
const DEPTH_SHIFT: u8 = 2;
const MAX_DEPTH: u8 = u8::MAX >> DEPTH_SHIFT;

const fn in_comment(state: u8) -> bool {
    state & STATE_COMMENT_BIT != 0
}

const fn depth(state: u8) -> u8 {
    state >> DEPTH_SHIFT
}

const fn make_state(comment: bool, value: bool, depth: u8) -> u8 {
    (depth << DEPTH_SHIFT) | if value { STATE_VALUE_BIT } else { 0 } | if comment { STATE_COMMENT_BIT } else { 0 }
}

/// A color literal: `len` chars from `start`; the swatch follows it.
struct ColorMatch {
    start: usize,
    len: usize,
    color: Color,
}

/// `indented` is Sass's indented syntax (`.sass`), which has no braces: a
/// value is anything after a `:` on the same line.
pub struct CssLanguage {
    pub indented: bool,
}

/// Scanner state for one line.
struct Scan {
    indented: bool,
    depth: u8,
    after_colon: bool,
    /// Where the current statement (since the last `;`, `{` or `}`) began
    /// in `matches` and `spans`, so a selector run can drop its matches.
    statement_matches: usize,
    statement_spans: usize,
    in_comment: bool,
}

impl Scan {
    fn in_value(&self) -> bool {
        self.after_colon && (self.indented || self.depth > 0)
    }
}

impl Language for CssLanguage {
    fn name(&self) -> &'static str {
        "CssLanguage"
    }

    fn tokenize_line(&self, text: &str, state: &mut u8) -> StyledLine {
        if text.is_empty() {
            return StyledLine::plain(text);
        }

        let line: Vec<char> = text.chars().collect();
        let mut spans = Vec::new();
        let mut matches = Vec::new();
        let mut scan = Scan {
            indented: self.indented,
            depth: depth(*state),
            after_colon: !self.indented && *state & STATE_VALUE_BIT != 0,
            statement_matches: 0,
            statement_spans: 0,
            in_comment: false,
        };

        let mut start = 0;

        if in_comment(*state) {
            let Some(close_index) = index_of(&line, "*/", 0) else {
                spans.push(StyledSpan::new(0, line.len(), TokenKind::Comment));
                return StyledLine::with_spans(text, spans);
            };

            start = close_index + 2;
            spans.push(StyledSpan::new(0, start, TokenKind::Comment));
        }

        scan_css(&line, start, &mut spans, &mut scan, &mut matches);

        let carry_value = !self.indented && scan.depth > 0 && scan.after_colon;
        *state = make_state(scan.in_comment, carry_value, scan.depth);

        if matches.is_empty() {
            return StyledLine::with_spans(text, spans);
        }

        build_swatch_result(&line, &spans, &matches)
    }
}

/// Port of `ScanCss`, extended with brace depth, statement tracking and
/// the function and named color forms.
fn scan_css(line: &[char], start: usize, spans: &mut Vec<StyledSpan>, scan: &mut Scan, matches: &mut Vec<ColorMatch>) {
    let mut pos = start;
    let len = line.len();

    while pos < len {
        if is_whitespace(line[pos]) {
            pos += 1;
            continue;
        }

        // Block comment
        if starts_with_at(line, pos, "/*") {
            if let Some(close_index) = index_of(line, "*/", pos + 2) {
                spans.push(StyledSpan::new(pos, close_index + 2 - pos, TokenKind::Comment));
                pos = close_index + 2;
                continue;
            }

            spans.push(StyledSpan::new(pos, len - pos, TokenKind::Comment));
            scan.in_comment = true;
            return;
        }

        // String values
        if line[pos] == '"' || line[pos] == '\'' {
            pos = scan_quoted_string(line, pos, line[pos], spans);
            continue;
        }

        // Hex color literal, only in value position
        if scan.in_value()
            && line[pos] == '#'
            && let Some((hex_len, color)) = try_match_hex_color(line, pos)
        {
            spans.push(StyledSpan::new(pos, 1 + hex_len, TokenKind::HexColor));
            matches.push(ColorMatch { start: pos, len: 1 + hex_len, color });
            pos += 1 + hex_len;
            continue;
        }

        // Identifier; a property key when the next non-space is ':'
        if is_letter(line[pos]) || line[pos] == '-' || line[pos] == '_' {
            let id_start = pos;

            while pos < len && (is_letter_or_digit(line[pos]) || line[pos] == '-' || line[pos] == '_') {
                pos += 1;
            }

            let mut after_id = pos;
            while after_id < len && line[after_id] == ' ' {
                after_id += 1;
            }

            if after_id < len && line[after_id] == ':' {
                spans.push(StyledSpan::new(id_start, pos - id_start, TokenKind::Key));
                continue;
            }

            let name = line[id_start..pos].iter().collect::<String>().to_ascii_lowercase();
            let next = line.get(pos).copied();

            // Unquoted url(...): skip its contents so file names aren't colors
            if name == "url"
                && next == Some('(')
                && let Some(close) = index_of_char(line, ')', pos + 1)
                && !line[pos + 1..close].iter().any(|&c| c == '"' || c == '\'')
            {
                spans.push(StyledSpan::new(pos, 1, TokenKind::Punctuation));
                spans.push(StyledSpan::new(close, 1, TokenKind::Punctuation));
                pos = close + 1;
                continue;
            }

            if !scan.in_value() {
                continue;
            }

            if next == Some('(') {
                let hsl = match name.as_str() {
                    "rgb" | "rgba" => Some(false),
                    "hsl" | "hsla" => Some(true),
                    _ => None,
                };

                if let Some(hsl) = hsl
                    && let Some((close, color)) = try_match_color_function(line, pos, hsl)
                {
                    matches.push(ColorMatch {
                        start: id_start,
                        len: close + 1 - id_start,
                        color,
                    });
                }

                continue;
            }

            let prev = if id_start == 0 { None } else { Some(line[id_start - 1]) };

            if !matches!(prev, Some('.' | '#' | '-' | '@' | '$' | '&'))
                && next != Some('.')
                && let Some(color) = named_color(&name)
            {
                matches.push(ColorMatch {
                    start: id_start,
                    len: pos - id_start,
                    color,
                });
            }

            continue;
        }

        // Punctuation
        if matches!(line[pos], '{' | '}' | '(' | ')' | ';' | ',') {
            spans.push(StyledSpan::new(pos, 1, TokenKind::Punctuation));

            if matches!(line[pos], ';' | '{' | '}') {
                if line[pos] == '{' {
                    // The statement was a selector: drop its colors
                    discard_statement(scan, spans, matches);
                    scan.depth = scan.depth.saturating_add(1).min(MAX_DEPTH);
                } else if line[pos] == '}' {
                    scan.depth = scan.depth.saturating_sub(1);
                }

                scan.after_colon = false;
                scan.statement_matches = matches.len();
                scan.statement_spans = spans.len();
            }

            pos += 1;
            continue;
        }

        if line[pos] == ':' {
            spans.push(StyledSpan::new(pos, 1, TokenKind::Punctuation));
            scan.after_colon = true;
            pos += 1;
            continue;
        }

        pos += 1;
    }
}

/// Removes the color matches (and hex spans) of the current statement.
fn discard_statement(scan: &Scan, spans: &mut Vec<StyledSpan>, matches: &mut Vec<ColorMatch>) {
    if matches.len() == scan.statement_matches {
        return;
    }

    matches.truncate(scan.statement_matches);
    let mut index = scan.statement_spans;
    while index < spans.len() {
        if spans[index].kind == TokenKind::HexColor {
            spans.remove(index);
        } else {
            index += 1;
        }
    }
}

/// Port of `TryMatchHexColor`: a run of exactly 3, 4, 6 or 8 hex digits
/// after the `#`. Returns (hex length, color); alpha is discarded.
fn try_match_hex_color(line: &[char], pos: usize) -> Option<(usize, Color)> {
    let run_len = line[pos + 1..].iter().take_while(|c| c.is_ascii_hexdigit()).count();

    if !matches!(run_len, 3 | 4 | 6 | 8) {
        return None;
    }

    let hex = &line[pos + 1..pos + 1 + run_len];
    try_parse_hex_color(hex).map(|color| (run_len, color))
}

/// Port of `TryParseHexColor`.
fn try_parse_hex_color(hex: &[char]) -> Option<Color> {
    let nibble = |c: char| c.to_digit(16).map(|d| d as u8);

    match hex.len() {
        3 | 4 => {
            let (r, g, b) = (nibble(hex[0])?, nibble(hex[1])?, nibble(hex[2])?);
            Some(Color {
                r: (r << 4) | r,
                g: (g << 4) | g,
                b: (b << 4) | b,
            })
        }
        6 | 8 => {
            let byte = |i: usize| Some((nibble(hex[i])? << 4) | nibble(hex[i + 1])?);
            Some(Color { r: byte(0)?, g: byte(2)?, b: byte(4)? })
        }
        _ => None,
    }
}

#[derive(Clone, Copy, PartialEq)]
enum Unit {
    Number,
    Percent,
    /// An angle, already in degrees.
    Degrees,
}

/// One function argument: `none`, or a signed decimal with an optional
/// `%` or angle unit.
fn parse_component(text: &str) -> Option<(f64, Unit)> {
    let lower = text.to_ascii_lowercase();

    if lower == "none" {
        return Some((0.0, Unit::Number));
    }

    let number_len = lower
        .char_indices()
        .take_while(|&(i, c)| c.is_ascii_digit() || c == '.' || (i == 0 && (c == '+' || c == '-')))
        .count();
    let (number, suffix) = lower.split_at(number_len);

    if !number.chars().any(|c| c.is_ascii_digit()) {
        return None;
    }

    let value: f64 = number.parse().ok()?;

    match suffix {
        "" => Some((value, Unit::Number)),
        "%" => Some((value, Unit::Percent)),
        "deg" => Some((value, Unit::Degrees)),
        "rad" => Some((value.to_degrees(), Unit::Degrees)),
        "grad" => Some((value * 0.9, Unit::Degrees)),
        "turn" => Some((value * 360.0, Unit::Degrees)),
        _ => None,
    }
}

/// `rgb(`/`rgba(`/`hsl(`/`hsla(` arguments from the `(` at `open` to the
/// first `)`: three channels separated by commas or spaces, then an
/// optional alpha (a fourth comma argument or `/ alpha`), discarded.
/// Returns the `)` index and the color.
fn try_match_color_function(line: &[char], open: usize, hsl: bool) -> Option<(usize, Color)> {
    let close = index_of_char(line, ')', open + 1)?;
    let inner: String = line[open + 1..close].iter().collect();

    if inner.contains('(') {
        return None;
    }

    let (main, slash_alpha) = match inner.split_once('/') {
        Some((main, alpha)) => (main, Some(alpha.trim())),
        None => (inner.as_str(), None),
    };

    let mut args: Vec<&str> = if main.contains(',') {
        main.split(',').map(str::trim).collect()
    } else {
        main.split_whitespace().collect()
    };

    let comma_alpha = if args.len() == 4 && slash_alpha.is_none() && main.contains(',') {
        args.pop()
    } else {
        None
    };

    if args.len() != 3 {
        return None;
    }

    if let Some(alpha) = slash_alpha.or(comma_alpha) {
        let (_, unit) = parse_component(alpha)?;
        if unit == Unit::Degrees {
            return None;
        }
    }

    let parsed: Vec<(f64, Unit)> = args.iter().map(|arg| parse_component(arg)).collect::<Option<_>>()?;

    let color = if hsl {
        let hue = match parsed[0] {
            (value, Unit::Number | Unit::Degrees) => value,
            (_, Unit::Percent) => return None,
        };
        let percent = |(value, unit): (f64, Unit)| (unit != Unit::Degrees).then_some(value.clamp(0.0, 100.0));
        hsl_to_rgb(hue, percent(parsed[1])?, percent(parsed[2])?)
    } else {
        let channel = |(value, unit): (f64, Unit)| match unit {
            Unit::Number => Some(value.clamp(0.0, 255.0).round() as u8),
            Unit::Percent => Some((value.clamp(0.0, 100.0) * 2.55).round() as u8),
            Unit::Degrees => None,
        };
        Color {
            r: channel(parsed[0])?,
            g: channel(parsed[1])?,
            b: channel(parsed[2])?,
        }
    };

    Some((close, color))
}

/// CSS Color 4 `hslToRgb`: hue in degrees, saturation and lightness in
/// percent.
fn hsl_to_rgb(hue: f64, saturation: f64, lightness: f64) -> Color {
    let hue = hue.rem_euclid(360.0);
    let s = saturation / 100.0;
    let l = lightness / 100.0;
    let a = s * l.min(1.0 - l);
    let f = |n: f64| {
        let k = (n + hue / 30.0) % 12.0;
        let value = l - a * (k - 3.0).min(9.0 - k).clamp(-1.0, 1.0);
        (value * 255.0).round() as u8
    };

    Color { r: f(0.0), g: f(8.0), b: f(4.0) }
}

fn named_color(lowercase: &str) -> Option<Color> {
    NAMED_COLORS
        .binary_search_by(|(name, _)| name.cmp(&lowercase))
        .ok()
        .map(|index| NAMED_COLORS[index].1)
}

/// Port of `BuildSwatchResult`: inserts the swatch cells after each color
/// literal, styles every cell, and shifts the spans to the new positions.
fn build_swatch_result(line: &[char], spans: &[StyledSpan], matches: &[ColorMatch]) -> StyledLine {
    let mut new_text = String::with_capacity(line.len() + matches.len() * SWATCH_LEN);
    let mut char_styles = Vec::with_capacity(line.len() + matches.len() * SWATCH_LEN);
    let mut match_index = 0;

    for (i, &ch) in line.iter().enumerate() {
        new_text.push(ch);
        char_styles.push(style_at(i, spans));

        // After the last char of a color literal, append the swatch cells
        if match_index < matches.len() && i + 1 == matches[match_index].start + matches[match_index].len {
            let swatch_style = CellStyle {
                fg: Some(matches[match_index].color),
                ..CellStyle::default()
            };

            new_text.push(' ');
            char_styles.push(CellStyle::default());
            new_text.push('\u{2588}');
            char_styles.push(swatch_style);
            new_text.push('\u{2588}');
            char_styles.push(swatch_style);

            match_index += 1;
        }
    }

    // Shift span offsets past the inserted cells (char_styles drive the
    // render; the spans stay meaningful for inspection)
    let shifted: Vec<StyledSpan> = spans
        .iter()
        .map(|span| {
            let shift = matches.iter().filter(|m| span.start >= m.start + m.len).count() * SWATCH_LEN;
            StyledSpan::new(span.start + shift, span.len, span.kind)
        })
        .collect();

    StyledLine {
        text: new_text,
        spans: Some(shifted),
        char_styles: Some(char_styles),
    }
}

fn style_at(index: usize, spans: &[StyledSpan]) -> CellStyle {
    spans
        .iter()
        .find(|span| index >= span.start && index < span.start + span.len)
        .map_or(PLAIN, |span| theme::get_style(span.kind))
}

const fn hex(value: u32) -> Color {
    Color {
        r: (value >> 16) as u8,
        g: (value >> 8) as u8,
        b: value as u8,
    }
}

/// The CSS named colors (CSS Color 4), sorted for binary search.
/// `transparent` and `currentcolor` are not colors to swatch.
const NAMED_COLORS: &[(&str, Color)] = &[
    ("aliceblue", hex(0xf0f8ff)),
    ("antiquewhite", hex(0xfaebd7)),
    ("aqua", hex(0x00ffff)),
    ("aquamarine", hex(0x7fffd4)),
    ("azure", hex(0xf0ffff)),
    ("beige", hex(0xf5f5dc)),
    ("bisque", hex(0xffe4c4)),
    ("black", hex(0x000000)),
    ("blanchedalmond", hex(0xffebcd)),
    ("blue", hex(0x0000ff)),
    ("blueviolet", hex(0x8a2be2)),
    ("brown", hex(0xa52a2a)),
    ("burlywood", hex(0xdeb887)),
    ("cadetblue", hex(0x5f9ea0)),
    ("chartreuse", hex(0x7fff00)),
    ("chocolate", hex(0xd2691e)),
    ("coral", hex(0xff7f50)),
    ("cornflowerblue", hex(0x6495ed)),
    ("cornsilk", hex(0xfff8dc)),
    ("crimson", hex(0xdc143c)),
    ("cyan", hex(0x00ffff)),
    ("darkblue", hex(0x00008b)),
    ("darkcyan", hex(0x008b8b)),
    ("darkgoldenrod", hex(0xb8860b)),
    ("darkgray", hex(0xa9a9a9)),
    ("darkgreen", hex(0x006400)),
    ("darkgrey", hex(0xa9a9a9)),
    ("darkkhaki", hex(0xbdb76b)),
    ("darkmagenta", hex(0x8b008b)),
    ("darkolivegreen", hex(0x556b2f)),
    ("darkorange", hex(0xff8c00)),
    ("darkorchid", hex(0x9932cc)),
    ("darkred", hex(0x8b0000)),
    ("darksalmon", hex(0xe9967a)),
    ("darkseagreen", hex(0x8fbc8f)),
    ("darkslateblue", hex(0x483d8b)),
    ("darkslategray", hex(0x2f4f4f)),
    ("darkslategrey", hex(0x2f4f4f)),
    ("darkturquoise", hex(0x00ced1)),
    ("darkviolet", hex(0x9400d3)),
    ("deeppink", hex(0xff1493)),
    ("deepskyblue", hex(0x00bfff)),
    ("dimgray", hex(0x696969)),
    ("dimgrey", hex(0x696969)),
    ("dodgerblue", hex(0x1e90ff)),
    ("firebrick", hex(0xb22222)),
    ("floralwhite", hex(0xfffaf0)),
    ("forestgreen", hex(0x228b22)),
    ("fuchsia", hex(0xff00ff)),
    ("gainsboro", hex(0xdcdcdc)),
    ("ghostwhite", hex(0xf8f8ff)),
    ("gold", hex(0xffd700)),
    ("goldenrod", hex(0xdaa520)),
    ("gray", hex(0x808080)),
    ("green", hex(0x008000)),
    ("greenyellow", hex(0xadff2f)),
    ("grey", hex(0x808080)),
    ("honeydew", hex(0xf0fff0)),
    ("hotpink", hex(0xff69b4)),
    ("indianred", hex(0xcd5c5c)),
    ("indigo", hex(0x4b0082)),
    ("ivory", hex(0xfffff0)),
    ("khaki", hex(0xf0e68c)),
    ("lavender", hex(0xe6e6fa)),
    ("lavenderblush", hex(0xfff0f5)),
    ("lawngreen", hex(0x7cfc00)),
    ("lemonchiffon", hex(0xfffacd)),
    ("lightblue", hex(0xadd8e6)),
    ("lightcoral", hex(0xf08080)),
    ("lightcyan", hex(0xe0ffff)),
    ("lightgoldenrodyellow", hex(0xfafad2)),
    ("lightgray", hex(0xd3d3d3)),
    ("lightgreen", hex(0x90ee90)),
    ("lightgrey", hex(0xd3d3d3)),
    ("lightpink", hex(0xffb6c1)),
    ("lightsalmon", hex(0xffa07a)),
    ("lightseagreen", hex(0x20b2aa)),
    ("lightskyblue", hex(0x87cefa)),
    ("lightslategray", hex(0x778899)),
    ("lightslategrey", hex(0x778899)),
    ("lightsteelblue", hex(0xb0c4de)),
    ("lightyellow", hex(0xffffe0)),
    ("lime", hex(0x00ff00)),
    ("limegreen", hex(0x32cd32)),
    ("linen", hex(0xfaf0e6)),
    ("magenta", hex(0xff00ff)),
    ("maroon", hex(0x800000)),
    ("mediumaquamarine", hex(0x66cdaa)),
    ("mediumblue", hex(0x0000cd)),
    ("mediumorchid", hex(0xba55d3)),
    ("mediumpurple", hex(0x9370db)),
    ("mediumseagreen", hex(0x3cb371)),
    ("mediumslateblue", hex(0x7b68ee)),
    ("mediumspringgreen", hex(0x00fa9a)),
    ("mediumturquoise", hex(0x48d1cc)),
    ("mediumvioletred", hex(0xc71585)),
    ("midnightblue", hex(0x191970)),
    ("mintcream", hex(0xf5fffa)),
    ("mistyrose", hex(0xffe4e1)),
    ("moccasin", hex(0xffe4b5)),
    ("navajowhite", hex(0xffdead)),
    ("navy", hex(0x000080)),
    ("oldlace", hex(0xfdf5e6)),
    ("olive", hex(0x808000)),
    ("olivedrab", hex(0x6b8e23)),
    ("orange", hex(0xffa500)),
    ("orangered", hex(0xff4500)),
    ("orchid", hex(0xda70d6)),
    ("palegoldenrod", hex(0xeee8aa)),
    ("palegreen", hex(0x98fb98)),
    ("paleturquoise", hex(0xafeeee)),
    ("palevioletred", hex(0xdb7093)),
    ("papayawhip", hex(0xffefd5)),
    ("peachpuff", hex(0xffdab9)),
    ("peru", hex(0xcd853f)),
    ("pink", hex(0xffc0cb)),
    ("plum", hex(0xdda0dd)),
    ("powderblue", hex(0xb0e0e6)),
    ("purple", hex(0x800080)),
    ("rebeccapurple", hex(0x663399)),
    ("red", hex(0xff0000)),
    ("rosybrown", hex(0xbc8f8f)),
    ("royalblue", hex(0x4169e1)),
    ("saddlebrown", hex(0x8b4513)),
    ("salmon", hex(0xfa8072)),
    ("sandybrown", hex(0xf4a460)),
    ("seagreen", hex(0x2e8b57)),
    ("seashell", hex(0xfff5ee)),
    ("sienna", hex(0xa0522d)),
    ("silver", hex(0xc0c0c0)),
    ("skyblue", hex(0x87ceeb)),
    ("slateblue", hex(0x6a5acd)),
    ("slategray", hex(0x708090)),
    ("slategrey", hex(0x708090)),
    ("snow", hex(0xfffafa)),
    ("springgreen", hex(0x00ff7f)),
    ("steelblue", hex(0x4682b4)),
    ("tan", hex(0xd2b48c)),
    ("teal", hex(0x008080)),
    ("thistle", hex(0xd8bfd8)),
    ("tomato", hex(0xff6347)),
    ("turquoise", hex(0x40e0d0)),
    ("violet", hex(0xee82ee)),
    ("wheat", hex(0xf5deb3)),
    ("white", hex(0xffffff)),
    ("whitesmoke", hex(0xf5f5f5)),
    ("yellow", hex(0xffff00)),
    ("yellowgreen", hex(0x9acd32)),
];

#[cfg(test)]
mod tests {
    use super::{CssLanguage, NAMED_COLORS};
    use crate::highlight::{Language, TokenKind};
    use crate::screen::Color;

    const CSS: CssLanguage = CssLanguage { indented: false };
    const SASS: CssLanguage = CssLanguage { indented: true };

    /// The swatch colors on each line, tokenizing from state 0.
    fn swatches(language: &CssLanguage, lines: &[&str]) -> Vec<Vec<(u8, u8, u8)>> {
        let mut state = 0u8;

        lines
            .iter()
            .map(|line| {
                let styled = language.tokenize_line(line, &mut state);
                let chars: Vec<char> = styled.text.chars().collect();
                let styles = styled.char_styles.unwrap_or_default();

                (0..chars.len())
                    .filter(|&i| chars[i] == '\u{2588}' && (i == 0 || chars[i - 1] != '\u{2588}'))
                    .map(|i| {
                        let Color { r, g, b } = styles[i].fg.expect("swatch color");
                        (r, g, b)
                    })
                    .collect()
            })
            .collect()
    }

    fn single(line: &str) -> Vec<(u8, u8, u8)> {
        let wrapped = format!("x {{ {line} }}");
        swatches(&CSS, &[&wrapped]).remove(0)
    }

    const WHITE: (u8, u8, u8) = (255, 255, 255);
    const RED: (u8, u8, u8) = (255, 0, 0);
    const ABC: (u8, u8, u8) = (0xaa, 0xbb, 0xcc);

    #[test]
    fn selector_before_brace_loses_its_swatch() {
        assert_eq!(swatches(&CSS, &["a:hover #abc { color: #fff; }"]), [vec![WHITE]]);
    }

    #[test]
    fn values_need_a_block() {
        assert_eq!(swatches(&CSS, &["a:hover #abc,", "b { color: red; }"]), [vec![], vec![RED]]);
        assert_eq!(swatches(&CSS, &["color: #abc;"]), [vec![]]);
    }

    #[test]
    fn nested_blocks_keep_value_positions() {
        let lines = ["@media (min-width: 600px) {", "  .x { color: #abc; }", "  a:hover #fff { color: red; }", "}"];
        assert_eq!(swatches(&CSS, &lines), [vec![], vec![ABC], vec![RED], vec![]]);
        assert_eq!(swatches(&CSS, &[".a { &:hover #abc { color: red; } }"]), [vec![RED]]);
    }

    #[test]
    fn values_continue_across_lines() {
        let lines = [".multi {", "  color:", "    #abc;", "  #fff", "}", "#abc"];
        assert_eq!(swatches(&CSS, &lines), [vec![], vec![], vec![ABC], vec![], vec![], vec![]]);
        assert_eq!(swatches(&CSS, &["x {", "  a: 1; }", "#abc"]), [vec![], vec![], vec![]]);
    }

    #[test]
    fn state_tracks_depth_through_comments() {
        let lines = [".a { /* open", "still */ color: #abc;", "/* x */ color: #fff; }", "color: red;"];
        assert_eq!(swatches(&CSS, &lines), [vec![], vec![ABC], vec![WHITE], vec![]]);
    }

    #[test]
    fn depth_saturates_and_does_not_underflow() {
        let deep = "{".repeat(200);
        let mut state = 0u8;
        CSS.tokenize_line(&deep, &mut state);
        assert_eq!(super::depth(state), super::MAX_DEPTH);

        assert_eq!(swatches(&CSS, &["} }", "color: #abc;", "x { color: #abc; }"]), [vec![], vec![], vec![ABC]]);
    }

    #[test]
    fn sass_values_need_no_block() {
        assert_eq!(
            swatches(&SASS, &[".a", "  color: #abc", "  border: 1px solid red"]),
            [vec![], vec![ABC], vec![RED]]
        );
        assert_eq!(swatches(&CSS, &[".a", "  color: #abc"]), [vec![], vec![]]);
    }

    #[test]
    fn color_functions() {
        type Case = (&'static str, Option<(u8, u8, u8)>);
        let cases: &[Case] = &[
            ("a: rgb(255, 0, 0);", Some(RED)),
            ("a: rgb(255 0 0 / 50%);", Some(RED)),
            ("a: rgba(0,0,255,.5);", Some((0, 0, 255))),
            ("a: rgb(100% 0% 0%);", Some(RED)),
            ("a: rgb(300, -5, 127.6);", Some((255, 0, 128))),
            ("a: hsl(120, 100%, 50%);", Some((0, 255, 0))),
            ("a: hsl(120deg 100% 25%);", Some((0, 128, 0))),
            ("a: hsl(0.5turn 100% 50%);", Some((0, 255, 255))),
            ("a: HSLA(240,100%,50%,0.3);", Some((0, 0, 255))),
            ("a: hsl(30 100% 50%);", Some((255, 128, 0))),
            ("a: hsl(-120 100% 50%);", Some((0, 0, 255))),
            ("a: hsl(0 0% 20%);", Some((51, 51, 51))),
            ("a: rgb(var(--r), 0, 0);", None),
            ("a: rgb(1, 2);", None),
            ("a: rgb(1, 2, 3", None),
            ("a: rgb(1 2 3 4);", None),
            ("a: rgb(1, 2, 3, 4, 5);", None),
            ("a: rgb(1deg, 2, 3);", None),
            ("a: hsl(10%, 2%, 3%);", None),
            ("a: rgb(1px, 2, 3);", None),
            ("a: foo(1, 2, 3);", None),
        ];

        for (line, expected) in cases {
            assert_eq!(single(line), expected.iter().copied().collect::<Vec<_>>(), "{line}");
        }
    }

    #[test]
    fn named_colors() {
        assert_eq!(single("color: red;"), [RED]);
        assert_eq!(single("border: 1px solid RebeccaPurple;"), [(0x66, 0x33, 0x99)]);
        assert_eq!(single("--accent: tomato;"), [(0xff, 0x63, 0x47)]);

        for line in [
            "color: transparent;",
            "color: currentColor;",
            "content: \"red\";",
            "background: url(red.png);",
            "x: red-ish;",
            "x: #red;",
            "x: .red;",
            "x: $red;",
            "x: red.png;",
            "x: red(1);",
            "red: 1;",
        ] {
            assert_eq!(single(line), [], "{line}");
        }

        assert_eq!(swatches(&CSS, &[".red { }"]), [vec![]]);
    }

    #[test]
    fn named_color_table_is_sorted_lowercase_css4() {
        assert_eq!(NAMED_COLORS.len(), 148);
        assert!(NAMED_COLORS.windows(2).all(|pair| pair[0].0 < pair[1].0));
        assert!(NAMED_COLORS.iter().all(|(name, _)| name.chars().all(|c| c.is_ascii_lowercase())));
    }

    #[test]
    fn spans_shift_past_mixed_swatches() {
        let mut state = 0u8;
        let styled = CSS.tokenize_line("x { a: #fff rgb(0 0 0) red; }", &mut state);
        assert_eq!(styled.text, "x { a: #fff \u{2588}\u{2588} rgb(0 0 0) \u{2588}\u{2588} red \u{2588}\u{2588}; }");

        let spans = styled.spans.unwrap();
        let text: Vec<char> = styled.text.chars().collect();
        let at = |kind: TokenKind, ch: char| spans.iter().any(|span| span.kind == kind && text[span.start] == ch);

        let hex = spans.iter().find(|span| span.kind == TokenKind::HexColor).unwrap();
        assert_eq!(text[hex.start..hex.start + hex.len].iter().collect::<String>(), "#fff");
        assert!(at(TokenKind::Punctuation, '('));
        assert!(at(TokenKind::Punctuation, ';'));
        let semicolon = spans.iter().rfind(|span| span.kind == TokenKind::Punctuation && text[span.start] == ';');
        // The original `;` at 26 moves past three 3-cell swatches
        assert_eq!(semicolon.unwrap().start, 26 + 9);
    }
}
