//! Port of `HexPreview`: a classic 16-bytes-per-row hex dump of the first
//! 64 KB, with per-cell styles.

use std::io::Read;

use crate::highlight::StyledLine;
use crate::input::CancelToken;
use crate::screen::{CellStyle, Color};

const MAX_BYTES: usize = 64 * 1024;
const BYTES_PER_ROW: usize = 16;
/// Offset(8) + 2 + hex(16 x 3 + 1) + 1 + "|" + ascii(16) + "|"
const ROW_LEN: usize = 78;
const ASCII_START: usize = 60;

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

const OFFSET: CellStyle = style(100, 120, 150, true); // dim blue-gray
const HEX: CellStyle = style(180, 180, 180, false); // light gray
const NULL_BYTE: CellStyle = style(100, 100, 100, true); // dimmer for 0x00
const ASCII: CellStyle = style(120, 200, 120, false); // green
const DOT: CellStyle = style(100, 100, 100, true); // dim non-printable
const SEPARATOR: CellStyle = style(80, 80, 80, true); // dim separators

/// Port of `HexPreview.GetPreviewLines`. `None` on error or cancellation;
/// an empty file is a single `[empty file]` line.
#[must_use]
pub fn get_preview_lines(path: &str, cancel: &CancelToken) -> Option<Vec<StyledLine>> {
    let file = std::fs::File::open(path).ok()?;
    let mut data = Vec::with_capacity(MAX_BYTES);
    let mut limited = file.take(MAX_BYTES as u64);
    let mut chunk = [0u8; 8192];

    loop {
        if cancel.is_cancelled() {
            return None;
        }

        match limited.read(&mut chunk) {
            Ok(0) => break,
            Ok(n) => data.extend_from_slice(&chunk[..n]),
            Err(_) => return None,
        }
    }

    if data.is_empty() {
        return Some(vec![StyledLine::plain("[empty file]")]);
    }

    let mut lines = Vec::with_capacity(data.len().div_ceil(BYTES_PER_ROW));

    for (row, bytes) in data.chunks(BYTES_PER_ROW).enumerate() {
        if cancel.is_cancelled() {
            return None;
        }

        lines.push(format_row(row * BYTES_PER_ROW, bytes));
    }

    Some(lines)
}

/// `"00000000  48 65 6C 6C 6F 20 57 6F  72 6C 64 21 0A 00 FF FE  |Hello World!....|"`
fn format_row(offset: usize, bytes: &[u8]) -> StyledLine {
    let mut chars = [' '; ROW_LEN];
    let mut styles = [CellStyle::default(); ROW_LEN];

    for (i, ch) in format!("{offset:08X}").chars().enumerate() {
        chars[i] = ch;
        styles[i] = OFFSET;
    }

    styles[8] = SEPARATOR;
    styles[9] = SEPARATOR;

    for (i, &byte) in bytes.iter().enumerate() {
        // Extra space between the two groups of 8
        let hex_pos = 10 + i * 3 + usize::from(i >= 8);
        let hex = format!("{byte:02X}");
        let mut hex_chars = hex.chars();
        chars[hex_pos] = hex_chars.next().unwrap();
        chars[hex_pos + 1] = hex_chars.next().unwrap();

        let cell = if byte == 0 { NULL_BYTE } else { HEX };
        styles[hex_pos] = cell;
        styles[hex_pos + 1] = cell;
    }

    // Space between the groups
    styles[34] = SEPARATOR;

    chars[ASCII_START] = '|';
    styles[ASCII_START] = SEPARATOR;

    for i in 0..BYTES_PER_ROW {
        let pos = ASCII_START + 1 + i;

        match bytes.get(i) {
            Some(&byte) if (0x20..=0x7E).contains(&byte) => {
                chars[pos] = char::from(byte);
                styles[pos] = ASCII;
            }
            Some(&byte) => {
                chars[pos] = '.';
                styles[pos] = if byte == 0 { NULL_BYTE } else { DOT };
            }
            None => {
                chars[pos] = ' ';
                styles[pos] = SEPARATOR;
            }
        }
    }

    chars[ASCII_START + 1 + BYTES_PER_ROW] = '|';
    styles[ASCII_START + 1 + BYTES_PER_ROW] = SEPARATOR;

    StyledLine {
        text: chars.iter().collect(),
        spans: None,
        char_styles: Some(styles.to_vec()),
    }
}

#[cfg(test)]
mod tests {
    //! Port of the HexPreviewTests.cs cases the preview golden doesn't
    //! reach (cancellation, missing files, column positions).

    use super::get_preview_lines;
    use crate::input::CancelToken;

    fn hex_file(data: &[u8]) -> String {
        let path = crate::preview::test_path("data.bin");
        std::fs::write(&path, data).unwrap();
        path.to_string_lossy().into_owned()
    }

    #[test]
    fn cancelled_returns_none() {
        let cancel = CancelToken::new();
        cancel.cancel();

        assert!(get_preview_lines(&hex_file(b"ABC"), &cancel).is_none());
    }

    #[test]
    fn nonexistent_file_returns_none() {
        let path = crate::preview::test_path("missing.bin");

        assert!(get_preview_lines(&path.to_string_lossy(), &CancelToken::new()).is_none());
    }

    #[test]
    fn full_row_is_78_chars() {
        let data: Vec<u8> = (0x41..0x51).collect();
        let lines = get_preview_lines(&hex_file(&data), &CancelToken::new()).expect("lines");

        assert_eq!(lines[0].text.chars().count(), 78);
    }

    #[test]
    fn ascii_column_maps_bytes() {
        let cases = [(0x00, '.'), (0x01, '.'), (0x1f, '.'), (0x20, ' '), (0x41, 'A'), (0x7e, '~'), (0x7f, '.'), (0xff, '.')];

        for (byte, expected) in cases {
            let lines = get_preview_lines(&hex_file(&[byte]), &CancelToken::new()).expect("lines");
            // The ASCII column starts at position 61 (after "|")
            assert_eq!(lines[0].text.chars().nth(61), Some(expected), "{byte:#04x}");
        }
    }
}
