//! Port of `src/Wade/Terminal/TerminalCapabilities.cs`: Sixel support and
//! cell pixel size, with the parser for the Unix DA1 / cell-size query
//! responses (the Unix query itself lands with the Phase 9 input work).
//! Rust-only: kitty graphics detection (a graphics query plus XTVERSION).

/// Port of `TerminalCapabilities`.
#[derive(Clone, Copy, Debug, PartialEq, Eq)]
pub struct TerminalCapabilities {
    pub sixel_supported: bool,
    pub cell_pixel_width: i32,
    pub cell_pixel_height: i32,
    /// Rust-only: the terminal supports kitty graphics with Unicode
    /// placeholders (answered the `a=q` query and is kitty or Ghostty).
    pub kitty_graphics: bool,
    /// Rust-only: the terminal shows iTerm2 inline images (`OSC 1337`):
    /// iTerm2 or WezTerm, by XTVERSION or `TERM_PROGRAM`.
    pub iterm_images: bool,
}

impl TerminalCapabilities {
    /// C# `TerminalCapabilities.Default`: no Sixel, 8x16 px cells.
    pub const DEFAULT: Self = Self {
        sixel_supported: false,
        cell_pixel_width: 8,
        cell_pixel_height: 16,
        kitty_graphics: false,
        iterm_images: false,
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

        caps.kitty_graphics = kitty_graphics_ok(data) && xtversion_name(data).is_some_and(is_placeholder_terminal);
        caps.iterm_images = xtversion_name(data).is_some_and(is_iterm_terminal);
        caps
    }
}

/// Terminals that answer the graphics query and also support Unicode
/// placeholders (WezTerm and Konsole answer the query but don't).
fn is_placeholder_terminal(name: &str) -> bool {
    let name = name.to_ascii_lowercase();
    name.starts_with("kitty") || name.starts_with("ghostty")
}

/// Terminals that show iTerm2 inline images, by XTVERSION name.
fn is_iterm_terminal(name: &str) -> bool {
    let name = name.to_ascii_lowercase();
    name.starts_with("iterm2") || name.starts_with("wezterm")
}

/// The `TERM_PROGRAM` fallback for iTerm2 inline images (no XTVERSION
/// reply, or Windows where replies can't be read).
#[must_use]
pub fn iterm_from_term_program(value: Option<&str>) -> bool {
    matches!(value, Some("iTerm.app" | "WezTerm"))
}

/// The payload of every `<prefix>...ESC \` string in `data`.
fn st_strings<'a>(data: &'a [u8], prefix: &'a [u8]) -> impl Iterator<Item = &'a [u8]> + 'a {
    let mut rest = data;
    std::iter::from_fn(move || {
        let start = rest.windows(prefix.len()).position(|w| w == prefix)? + prefix.len();
        let body = &rest[start..];
        let end = body.windows(2).position(|w| w == b"\x1b\\").unwrap_or(body.len());
        rest = &body[end..];
        Some(&body[..end])
    })
}

/// A reply to the graphics query `i=31`: `ESC _ G i=31 ; OK ESC \`.
fn kitty_graphics_ok(data: &[u8]) -> bool {
    st_strings(data, b"\x1b_G").any(|body| {
        let mut parts = body.splitn(2, |&b| b == b';');
        let keys = parts.next().unwrap_or_default();
        let message = parts.next().unwrap_or_default();
        keys.split(|&b| b == b',').any(|kv| kv == b"i=31") && message == b"OK"
    })
}

/// The terminal name from an XTVERSION reply: `DCS > | name(version) ST`
/// (or `name version`).
fn xtversion_name(data: &[u8]) -> Option<&str> {
    let body = st_strings(data, b"\x1bP>|").next()?;
    let text = std::str::from_utf8(body).ok()?;
    let name = text.split(['(', ' ']).next().unwrap_or_default();
    (!name.is_empty()).then_some(name)
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

    const KITTY_OK: &[u8] = b"\x1b_Gi=31;OK\x1b\\";

    fn kitty(data: &[u8]) -> bool {
        Caps::parse_query_responses(data).kitty_graphics
    }

    #[test]
    fn kitty_graphics_needs_the_query_reply_and_an_allowlisted_name() {
        let with = |reply: &[u8], version: &[u8]| [reply, version, b"\x1b[?62;4c"].concat();

        assert!(kitty(&with(KITTY_OK, b"\x1bP>|kitty(0.39.1)\x1b\\")));
        assert!(kitty(&with(KITTY_OK, b"\x1bP>|ghostty 1.1.3\x1b\\")));
        assert!(kitty(&with(b"", &[b"\x1bP>|Ghostty 1.2\x1b\\".as_slice(), KITTY_OK].concat())), "any order");

        assert!(!kitty(&with(KITTY_OK, b"\x1bP>|WezTerm 20240203\x1b\\")), "no placeholders");
        assert!(!kitty(&with(b"\x1b_Gi=31;EINVAL:bad\x1b\\", b"\x1bP>|kitty(0.39.1)\x1b\\")));
        assert!(!kitty(&with(b"", b"\x1bP>|kitty(0.39.1)\x1b\\")), "no graphics reply");
        assert!(!kitty(&with(KITTY_OK, b"")), "no XTVERSION reply");
        assert!(!kitty(b""));
    }

    #[test]
    fn iterm_images_from_xtversion_or_term_program() {
        let iterm = |data: &[u8]| Caps::parse_query_responses(data).iterm_images;
        assert!(iterm(b"\x1bP>|iTerm2 3.5.4\x1b\\\x1b[?62;4c"));
        assert!(iterm(b"\x1bP>|WezTerm 20240203-110809-5046fc22\x1b\\"));
        assert!(!iterm(b"\x1bP>|kitty(0.39.1)\x1b\\"));
        assert!(!iterm(b"\x1b[?62;4c"));

        assert!(super::iterm_from_term_program(Some("iTerm.app")));
        assert!(super::iterm_from_term_program(Some("WezTerm")));
        assert!(!super::iterm_from_term_program(Some("Apple_Terminal")));
        assert!(!super::iterm_from_term_program(None));
    }

    #[test]
    fn kitty_replies_do_not_disturb_the_other_queries() {
        let caps = Caps::parse_query_responses(&[KITTY_OK, b"\x1bP>|kitty(0.39.1)\x1b\\\x1b[6;20;10t\x1b[?62;4c"].concat());
        assert_eq!(
            (caps.kitty_graphics, caps.sixel_supported, caps.cell_pixel_width, caps.cell_pixel_height),
            (true, true, 10, 20)
        );
    }
}
