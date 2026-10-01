//! Port of the subset of src/Wade/Terminal/AnsiCodes.cs that the
//! ScreenBuffer serializer needs.

pub const RESET_ATTRIBUTES: &str = "\x1b[0m";

/// Appends a cursor move: ESC [ row+1 ; col+1 H
pub fn append_move_cursor(out: &mut String, row: i32, col: i32) {
    out.push('\x1b');
    out.push('[');
    push_i32(out, row + 1);
    out.push(';');
    push_i32(out, col + 1);
    out.push('H');
}

/// Appends an SGR truecolor foreground: ESC [ 38;2;r;g;b m
pub fn append_set_fg(out: &mut String, fg: crate::screen::Color) {
    out.push_str("\x1b[38;2;");
    push_i32(out, i32::from(fg.r));
    out.push(';');
    push_i32(out, i32::from(fg.g));
    out.push(';');
    push_i32(out, i32::from(fg.b));
    out.push('m');
}

/// Appends an SGR truecolor background: ESC [ 48;2;r;g;b m
pub fn append_set_bg(out: &mut String, bg: crate::screen::Color) {
    out.push_str("\x1b[48;2;");
    push_i32(out, i32::from(bg.r));
    out.push(';');
    push_i32(out, i32::from(bg.g));
    out.push(';');
    push_i32(out, i32::from(bg.b));
    out.push('m');
}

fn push_i32(out: &mut String, value: i32) {
    use std::fmt::Write as _;
    let _ = write!(out, "{value}");
}

pub const CLEAR_SCREEN: &str = "\x1b[2J";
pub const ENTER_ALTERNATE_SCREEN: &str = "\x1b[?1049h";
pub const LEAVE_ALTERNATE_SCREEN: &str = "\x1b[?1049l";
pub const HIDE_CURSOR: &str = "\x1b[?25l";
pub const SHOW_CURSOR: &str = "\x1b[?25h";
/// Push the window title on the terminal's title stack (`CSI 22;0 t`).
pub const SAVE_TITLE: &str = "\x1b[22;0t";
pub const CLEAR_TITLE: &str = "\x1b]0;\x07";
pub const ENABLE_MOUSE_REPORTING: &str = "\x1b[?1000h";
pub const DISABLE_MOUSE_REPORTING: &str = "\x1b[?1000l";
pub const ENABLE_SGR_MOUSE_MODE: &str = "\x1b[?1006h";
pub const DISABLE_SGR_MOUSE_MODE: &str = "\x1b[?1006l";
pub const ENABLE_BRACKETED_PASTE: &str = "\x1b[?2004h";
pub const DISABLE_BRACKETED_PASTE: &str = "\x1b[?2004l";

/// `AnsiCodes.MoveCursor`: 0-based row/col to a CUP sequence.
#[must_use]
pub fn move_cursor(row: i32, col: i32) -> String {
    let mut out = String::new();
    append_move_cursor(&mut out, row, col);
    out
}

/// Terminal title (OSC 0): `ESC ] 0 ; <title> BEL`. Port of `AnsiCodes.SetTitle`.
#[must_use]
pub fn set_title(title: &str) -> String {
    format!("\u{1b}]0;{title}\u{7}")
}
