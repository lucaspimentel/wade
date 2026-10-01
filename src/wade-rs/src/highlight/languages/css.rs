//! Port of `CssLanguage`: properties as keys, strings, comments, and hex
//! color literals in value position followed by a ` ██` swatch in the
//! literal's color. Lines with swatches carry per-char styles (the
//! authoritative render source) and spans shifted past the inserted cells.

use crate::highlight::c_like::{scan_quoted_string, STATE_BLOCK_COMMENT, STATE_NORMAL};
use crate::highlight::scan::{index_of, starts_with_at};
use crate::highlight::theme::{self, PLAIN};
use crate::highlight::{Language, StyledLine, StyledSpan, TokenKind};
use crate::screen::{CellStyle, Color};
use crate::text::{is_letter, is_letter_or_digit, is_whitespace};

/// ` ██`: a space and two full blocks.
const SWATCH_LEN: usize = 3;

struct HexColorMatch {
    start: usize,
    hex_len: usize,
    color: Color,
}

pub struct CssLanguage;

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

        if *state == STATE_BLOCK_COMMENT {
            let Some(close_index) = index_of(&line, "*/", 0) else {
                spans.push(StyledSpan::new(0, line.len(), TokenKind::Comment));
                return StyledLine::with_spans(text, spans);
            };

            let close_end = close_index + 2;
            spans.push(StyledSpan::new(0, close_end, TokenKind::Comment));
            *state = STATE_NORMAL;
            scan_css(&line, close_end, &mut spans, state, &mut matches);
        } else {
            scan_css(&line, 0, &mut spans, state, &mut matches);
        }

        if matches.is_empty() {
            return StyledLine::with_spans(text, spans);
        }

        build_swatch_result(&line, &spans, &matches)
    }
}

/// Port of `ScanCss`. `after_colon` is line-local: set on `:`, reset on
/// `;`/`{`/`}`, so ID selectors like `#main` are not colors.
fn scan_css(
    line: &[char],
    start: usize,
    spans: &mut Vec<StyledSpan>,
    state: &mut u8,
    matches: &mut Vec<HexColorMatch>,
) {
    let mut pos = start;
    let len = line.len();
    let mut after_colon = false;

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
            *state = STATE_BLOCK_COMMENT;
            return;
        }

        // String values
        if line[pos] == '"' || line[pos] == '\'' {
            pos = scan_quoted_string(line, pos, line[pos], spans);
            continue;
        }

        // Hex color literal, only in value position
        if after_colon
            && line[pos] == '#'
            && let Some((hex_len, color)) = try_match_hex_color(line, pos)
        {
            spans.push(StyledSpan::new(pos, 1 + hex_len, TokenKind::HexColor));
            matches.push(HexColorMatch {
                start: pos,
                hex_len,
                color,
            });
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
            }

            continue;
        }

        // Punctuation
        if matches!(line[pos], '{' | '}' | '(' | ')' | ';' | ',') {
            spans.push(StyledSpan::new(pos, 1, TokenKind::Punctuation));

            if matches!(line[pos], ';' | '{' | '}') {
                after_colon = false;
            }

            pos += 1;
            continue;
        }

        if line[pos] == ':' {
            spans.push(StyledSpan::new(pos, 1, TokenKind::Punctuation));
            after_colon = true;
            pos += 1;
            continue;
        }

        pos += 1;
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
            Some(Color {
                r: byte(0)?,
                g: byte(2)?,
                b: byte(4)?,
            })
        }
        _ => None,
    }
}

/// Port of `BuildSwatchResult`: inserts the swatch cells after each hex
/// literal, styles every cell, and shifts the spans to the new positions.
fn build_swatch_result(line: &[char], spans: &[StyledSpan], matches: &[HexColorMatch]) -> StyledLine {
    let mut new_text = String::with_capacity(line.len() + matches.len() * SWATCH_LEN);
    let mut char_styles = Vec::with_capacity(line.len() + matches.len() * SWATCH_LEN);
    let mut match_index = 0;

    for (i, &ch) in line.iter().enumerate() {
        new_text.push(ch);
        char_styles.push(style_at(i, spans));

        // After the last char of a hex literal, append the swatch cells
        if match_index < matches.len() && i == matches[match_index].start + matches[match_index].hex_len {
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
            let shift = matches
                .iter()
                .filter(|m| span.start >= m.start + 1 + m.hex_len)
                .count()
                * SWATCH_LEN;
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
