//! Port of `src/Wade/UI/HelpOverlay.cs`: static full-screen help overlay.

use crate::screen::{CellStyle, Color, ScreenBuffer};
use crate::ui::dialog_box::{self, BG_COLOR};
use crate::ui::layout::Rect;

const KEY_COLOR: Color = Color {
    r: 220,
    g: 220,
    b: 100,
};
const DESC_COLOR: Color = Color {
    r: 200,
    g: 200,
    b: 200,
};
const SECTION_COLOR: Color = Color {
    r: 180,
    g: 180,
    b: 220,
};

pub fn render(buffer: &mut ScreenBuffer, screen_width: i32, screen_height: i32) {
    const CONTENT_WIDTH: i32 = 46;
    const CONTENT_HEIGHT: i32 = 14;

    let content: Rect = dialog_box::render(
        buffer,
        screen_width,
        screen_height,
        CONTENT_WIDTH,
        CONTENT_HEIGHT,
        Some("Help"),
        Some("Press any key to close"),
    );

    let key_style = CellStyle {
        fg: Some(KEY_COLOR),
        bg: Some(BG_COLOR),
        ..CellStyle::default()
    };
    let desc_style = CellStyle {
        fg: Some(DESC_COLOR),
        bg: Some(BG_COLOR),
        ..CellStyle::default()
    };
    let section_style = CellStyle {
        fg: Some(SECTION_COLOR),
        bg: Some(BG_COLOR),
        ..CellStyle::default()
    };

    let mut y = content.top;
    let left = content.left;

    let put = |buffer: &mut ScreenBuffer, row: i32, key: &str, desc: &str| {
        buffer.write_string(row, left, key, key_style, i64::from(16));
        buffer.write_string(row, left + 16, desc, desc_style, i64::from(CONTENT_WIDTH - 16));
    };

    buffer.write_string(y, left, "Ctrl+P", key_style, i64::from(8));
    buffer.write_string(y, left + 8, "Open action list (all hotkeys)", desc_style, i64::from(CONTENT_WIDTH - 8));
    y += 1;
    y += 1; // blank line

    buffer.write_string(y, left, "Navigation", section_style, i64::from(CONTENT_WIDTH));
    y += 1;

    put(buffer, y, "↑↓  or  j/k", "Move selection");
    y += 1;
    put(buffer, y, "←→  or  h/l", "Open / go back");
    y += 1;
    put(buffer, y, "PgUp/PgDn", "Scroll by page");
    y += 1;
    put(buffer, y, "Click / Scroll", "Mouse navigation");
    y += 1;
    y += 1; // blank line

    put(buffer, y, "/", "Filter");
    y += 1;
    put(buffer, y, "Ctrl+F", "Find files (Search)");
    y += 1;
    y += 1; // blank line

    buffer.write_string(y, left, "Other", section_style, i64::from(CONTENT_WIDTH));
    y += 1;

    put(buffer, y, ",", "Settings (Configuration)");
    y += 1;
    put(buffer, y, "?", "Help");
}
