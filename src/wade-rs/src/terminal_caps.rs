//! Port of `src/Wade/Terminal/TerminalCapabilities.cs`: Sixel support and
//! cell pixel size, with the parser for the Unix DA1 / cell-size query
//! responses (the Unix query itself lands with the Phase 9 input work).
//! Rust-only: kitty graphics detection (a graphics query plus XTVERSION),
//! and tmux passthrough (the outer terminal as tmux reports it).

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
    /// Rust-only: inside tmux with `allow-passthrough` on, image output is
    /// wrapped for the outer terminal (see `TmuxPassthrough`).
    pub tmux: Option<TmuxPassthrough>,
}

/// Rust-only: how image output reaches the terminal outside tmux. Data is
/// wrapped in `ESC P tmux; ... ESC \`; Sixel and iTerm2 images also carry
/// their cursor move, offset by the pane's position in the tmux window.
#[derive(Clone, Copy, Debug, Default, PartialEq, Eq)]
pub struct TmuxPassthrough {
    /// tmux itself shows Sixel (3.4+ built with Sixel): Sixel output is
    /// written plainly, like outside tmux
    pub native_sixel: bool,
    /// The pane's top-left cell on the outer terminal, 0-based
    pub pane_top: i32,
    pub pane_left: i32,
}

/// What `tmux display-message` and `show-options` report about the client
/// terminal and the pane.
#[derive(Clone, Debug, Default, PartialEq, Eq)]
pub struct TmuxClient {
    /// The outer terminal's XTVERSION name and version, such as
    /// `kitty(0.35.2)` (`client_termtype`)
    pub termtype: String,
    /// The outer terminal's features as tmux detected them, comma-separated
    /// (`client_termfeatures`)
    pub features: String,
    pub passthrough: bool,
    /// (width, height) in pixels, when tmux knows them
    pub cell_size: Option<(i32, i32)>,
    pub pane_top: i32,
    pub pane_left: i32,
}

/// The `tmux display-message -p` format `TmuxClient::parse` reads.
pub const TMUX_CLIENT_FORMAT: &str = "#{client_termtype}\t#{client_termfeatures}\t#{client_cell_width}\t\
    #{client_cell_height}\t#{pane_top}\t#{pane_left}\t#{status}\t#{status-position}";

impl TmuxClient {
    /// Parses `TMUX_CLIENT_FORMAT` output and the `allow-passthrough` value
    /// (`on` or `all` allow it).
    #[must_use]
    pub fn parse(display: &str, allow_passthrough: &str) -> Self {
        let fields: Vec<&str> = display.trim_end_matches(['\r', '\n']).split('\t').collect();
        let field = |i: usize| fields.get(i).copied().unwrap_or_default().trim();
        let number = |i: usize| field(i).parse::<i32>().ok();
        let cell_size = match (number(2), number(3)) {
            (Some(width), Some(height)) if width > 0 && height > 0 => Some((width, height)),
            _ => None,
        };
        let (pane_top, pane_left) = pane_position(field(4), field(5), field(6), field(7));

        Self {
            termtype: field(0).to_string(),
            features: field(1).to_string(),
            passthrough: matches!(allow_passthrough.trim(), "on" | "all"),
            cell_size,
            pane_top,
            pane_left,
        }
    }
}

/// The pane's top-left cell on the client terminal: its place in the
/// window, below a status line shown at the top.
#[must_use]
pub fn pane_position(pane_top: &str, pane_left: &str, status: &str, status_position: &str) -> (i32, i32) {
    let status_lines = match status.trim() {
        "on" => 1,
        "off" | "" => 0,
        lines => lines.parse().unwrap_or(0),
    };
    let above = if status_position.trim() == "top" { status_lines } else { 0 };
    (pane_top.trim().parse::<i32>().unwrap_or(0) + above, pane_left.trim().parse().unwrap_or(0))
}

impl TerminalCapabilities {
    /// Rust-only: the capabilities inside tmux. `self` holds tmux's own
    /// replies (Sixel when tmux shows it itself). With passthrough allowed,
    /// kitty and iTerm2 images follow the outer terminal's name and Sixel
    /// its features; without it only tmux's own Sixel is left.
    #[must_use]
    pub fn inside_tmux(self, client: &TmuxClient) -> Self {
        let mut caps = Self {
            kitty_graphics: false,
            iterm_images: false,
            tmux: None,
            ..self
        };
        if caps.cell_pixel_width == Self::DEFAULT.cell_pixel_width
            && caps.cell_pixel_height == Self::DEFAULT.cell_pixel_height
            && let Some((width, height)) = client.cell_size
        {
            (caps.cell_pixel_width, caps.cell_pixel_height) = (width, height);
        }

        if client.passthrough {
            let name = client.termtype.split(['(', ' ']).next().unwrap_or_default();
            caps.kitty_graphics = is_placeholder_terminal(name);
            caps.iterm_images = is_iterm_terminal(name);
            caps.tmux = Some(TmuxPassthrough {
                native_sixel: self.sixel_supported,
                pane_top: client.pane_top,
                pane_left: client.pane_left,
            });
            caps.sixel_supported |= client.features.split(',').any(|feature| feature.trim() == "sixel");
        }

        caps
    }
}

/// Wraps `data` for tmux passthrough: `ESC P tmux;`, the data with every
/// ESC doubled, `ESC \`.
#[must_use]
pub fn tmux_wrap(data: &str) -> String {
    let mut out = String::with_capacity(data.len() + data.len() / 16 + 10);
    out.push_str("\x1bPtmux;");
    for c in data.chars() {
        if c == '\x1b' {
            out.push('\x1b');
        }
        out.push(c);
    }
    out.push_str("\x1b\\");
    out
}

/// `tmux_wrap` for each `ESC \`-terminated command in `data` (the chunks
/// of a kitty transmit), so no single passthrough grows past tmux's limit.
#[must_use]
pub fn tmux_wrap_each(data: &str) -> String {
    data.split_inclusive("\x1b\\").map(tmux_wrap).collect()
}

impl TerminalCapabilities {
    /// C# `TerminalCapabilities.Default`: no Sixel, 8x16 px cells.
    pub const DEFAULT: Self = Self {
        sixel_supported: false,
        cell_pixel_width: 8,
        cell_pixel_height: 16,
        kitty_graphics: false,
        iterm_images: false,
        tmux: None,
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

    fn client(termtype: &str, features: &str, passthrough: bool) -> super::TmuxClient {
        super::TmuxClient {
            termtype: termtype.to_string(),
            features: features.to_string(),
            passthrough,
            cell_size: Some((10, 20)),
            pane_top: 3,
            pane_left: 40,
        }
    }

    #[test]
    fn tmux_client_output_is_parsed() {
        let parsed = super::TmuxClient::parse("kitty(0.35.2)\tRGB,sixel,title\t10\t20\t5\t40\ton\ttop\n", "all\n");
        assert_eq!(
            parsed,
            super::TmuxClient {
                termtype: "kitty(0.35.2)".to_string(),
                features: "RGB,sixel,title".to_string(),
                passthrough: true,
                cell_size: Some((10, 20)),
                pane_top: 6,
                pane_left: 40,
            }
        );

        // tmux 3.2 and older: unknown formats come back empty
        let old = super::TmuxClient::parse("\t\t\t\t0\t0\ton\tbottom\n", "");
        assert_eq!((old.passthrough, old.cell_size, old.pane_top), (false, None, 0));
        assert!(!super::TmuxClient::parse("", "off").passthrough);

        assert_eq!(super::pane_position("2", "7", "off", "top"), (2, 7));
        assert_eq!(super::pane_position("2", "7", "3", "top"), (5, 7));
        assert_eq!(super::pane_position("2", "7", "on", "bottom"), (2, 7));
    }

    #[test]
    fn inside_tmux_images_follow_the_outer_terminal_when_passthrough_is_on() {
        let tmux_da1 = Caps::parse_query_responses(b"\x1bP>|tmux 3.5a\x1b\\\x1b[?1;2c");

        let kitty = tmux_da1.inside_tmux(&client("kitty(0.35.2)", "RGB", true));
        assert!(kitty.kitty_graphics && !kitty.iterm_images && !kitty.sixel_supported);
        let passthrough = kitty.tmux.expect("passthrough");
        assert_eq!((passthrough.native_sixel, passthrough.pane_top, passthrough.pane_left), (false, 3, 40));
        assert_eq!((kitty.cell_pixel_width, kitty.cell_pixel_height), (10, 20), "tmux's cell size");

        let iterm = tmux_da1.inside_tmux(&client("iTerm2 3.5.0", "", true));
        assert!(iterm.iterm_images && !iterm.kitty_graphics);
        let sixel = tmux_da1.inside_tmux(&client("XTerm(390)", "RGB,sixel", true));
        assert!(sixel.sixel_supported && sixel.tmux.is_some());

        let off = tmux_da1.inside_tmux(&client("kitty(0.35.2)", "sixel", false));
        assert!(!off.kitty_graphics && !off.sixel_supported && off.tmux.is_none(), "no passthrough: no images");

        // tmux showing Sixel itself: no passthrough needed for it
        let native = Caps::parse_query_responses(b"\x1b[?1;2;4c\x1b[6;18;9t");
        let without = native.inside_tmux(&client("foot", "", false));
        assert!(without.sixel_supported && without.tmux.is_none());
        assert_eq!((without.cell_pixel_width, without.cell_pixel_height), (9, 18), "tmux's own reply wins");
        assert!(native.inside_tmux(&client("foot", "", true)).tmux.is_some_and(|t| t.native_sixel));
    }

    #[test]
    fn tmux_wrapping_doubles_escapes() {
        assert_eq!(super::tmux_wrap("\x1b[1;1H\x1bPq#0\x1b\\"), "\x1bPtmux;\x1b\x1b[1;1H\x1b\x1bPq#0\x1b\x1b\\\x1b\\");
        assert_eq!(
            super::tmux_wrap_each("\x1b_Ga=T;AA\x1b\\\x1b_Gm=0;BB\x1b\\"),
            "\x1bPtmux;\x1b\x1b_Ga=T;AA\x1b\x1b\\\x1b\\\x1bPtmux;\x1b\x1b_Gm=0;BB\x1b\x1b\\\x1b\\"
        );
        assert_eq!(super::tmux_wrap_each(""), "");
    }

    #[test]
    fn kitty_replies_do_not_disturb_the_other_queries() {
        let caps =
            Caps::parse_query_responses(&[KITTY_OK, b"\x1bP>|kitty(0.39.1)\x1b\\\x1b[6;20;10t\x1b[?62;4c"].concat());
        assert_eq!(
            (caps.kitty_graphics, caps.sixel_supported, caps.cell_pixel_width, caps.cell_pixel_height),
            (true, true, 10, 20)
        );
    }
}
