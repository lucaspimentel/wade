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
