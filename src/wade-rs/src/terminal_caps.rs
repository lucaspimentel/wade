//! Port of `src/Wade/Terminal/TerminalCapabilities.cs`: Sixel support and
//! cell pixel size, with the parser for the Unix DA1 / cell-size query
//! responses (the Unix query itself lands with the Phase 9 input work).

/// Port of `TerminalCapabilities`.
#[derive(Clone, Copy, Debug, PartialEq, Eq)]
pub struct TerminalCapabilities {
    pub sixel_supported: bool,
    pub cell_pixel_width: i32,
    pub cell_pixel_height: i32,
}

impl TerminalCapabilities {
    /// C# `TerminalCapabilities.Default`: no Sixel, 8x16 px cells.
    pub const DEFAULT: Self = Self {
        sixel_supported: false,
        cell_pixel_width: 8,
        cell_pixel_height: 16,
    };

    /// Port of `ParseQueryResponses`: DA1 (`ESC[?..c`, Sixel when a param is
    /// exactly 4) and cell size (`ESC[6;HEIGHT;WIDTHt`) responses.
    #[must_use]
    pub fn parse_query_responses(data: &[u8]) -> Self {
        let mut caps = Self::DEFAULT;
        let mut i = 0;

        while i < data.len() {
            // Look for ESC [
            if data[i] != 0x1b || i + 1 >= data.len() || data[i + 1] != b'[' {
                i += 1;
                continue;
            }

            i += 2;

            if i < data.len() && data[i] == b'?' {
                i += 1;
                let start = i;
                while i < data.len() && data[i] != b'c' {
                    i += 1;
                }

                if i < data.len() {
                    caps.sixel_supported = contains_param(&data[start..i], 4);
                    i += 1;
                }
            } else {
                let start = i;
                while i < data.len() && !(0x40..=0x7e).contains(&data[i]) {
                    i += 1;
                }

                if i < data.len() && data[i] == b't' {
                    parse_cell_size(&data[start..i], &mut caps);
                    i += 1;
                } else if i < data.len() {
                    i += 1;
                }
            }
        }

        caps
    }
}

/// Semicolon-separated numeric params of a CSI sequence (digits only).
fn params(span: &[u8]) -> impl Iterator<Item = Option<i32>> + '_ {
    span.split(|&b| b == b';').map(|part| {
        let mut value: Option<i32> = None;
        for &b in part {
            if b.is_ascii_digit() {
                value = Some(value.unwrap_or(0).wrapping_mul(10).wrapping_add(i32::from(b - b'0')));
            }
        }
        value
    })
}

fn contains_param(span: &[u8], target: i32) -> bool {
    params(span).any(|value| value == Some(target))
}

/// `6;HEIGHT;WIDTH`: only applies when the first param is 6.
fn parse_cell_size(span: &[u8], caps: &mut TerminalCapabilities) {
    let values: Vec<Option<i32>> = params(span).collect();
    if values.first() != Some(&Some(6)) {
        return;
    }

    if let Some(Some(height)) = values.get(1) {
        caps.cell_pixel_height = *height;
    }
    if let Some(Some(width)) = values.get(2) {
        caps.cell_pixel_width = *width;
    }
}

#[cfg(test)]
mod tests {
    //! Port of TerminalCapabilitiesTests.cs.

    use super::TerminalCapabilities as Caps;

    #[test]
    fn empty_and_garbled_input_give_defaults() {
        assert_eq!(Caps::parse_query_responses(b""), Caps::DEFAULT);
        assert_eq!(Caps::parse_query_responses(&[0xff, 0x00, 0x42, 0x1b, 0x5b, 0xff]), Caps::DEFAULT);
    }

    #[test]
    fn da1_sixel_detection() {
        assert!(Caps::parse_query_responses(b"\x1b[?65;1;2;4;6c").sixel_supported);
        assert!(!Caps::parse_query_responses(b"\x1b[?65;1;2;6c").sixel_supported);
        assert!(Caps::parse_query_responses(b"\x1b[?4c").sixel_supported);

        for (param, expected) in [(4, true), (14, false), (40, false), (44, false)] {
            let data = format!("\x1b[?{param}c");
            assert_eq!(Caps::parse_query_responses(data.as_bytes()).sixel_supported, expected, "{param}");
        }
    }

    #[test]
    fn cell_size_responses() {
        let caps = Caps::parse_query_responses(b"\x1b[6;20;10t");
        assert_eq!((caps.cell_pixel_width, caps.cell_pixel_height), (10, 20));

        let caps = Caps::parse_query_responses(b"\x1b[?65;1;4c\x1b[6;24;12t");
        assert_eq!((caps.sixel_supported, caps.cell_pixel_width, caps.cell_pixel_height), (true, 12, 24));

        let caps = Caps::parse_query_responses(b"\x1b[6;18;9t\x1b[?62;4c");
        assert_eq!((caps.sixel_supported, caps.cell_pixel_width, caps.cell_pixel_height), (true, 9, 18));

        assert_eq!(Caps::parse_query_responses(b"\x1b[1;2;3t"), Caps::DEFAULT);
    }
}
