//! Port of `src/Wade/UI/DialogBox.cs`: reusable centered dialog box chrome.

use crate::screen::{CellStyle, Color, ScreenBuffer};
use crate::ui::layout::Rect;

pub const BORDER_COLOR: Color = Color {
    r: 100,
    g: 100,
    b: 120,
};
pub const TITLE_COLOR: Color = Color {
    r: 80,
    g: 160,
    b: 255,
};
pub const FOOTER_COLOR: Color = Color {
    r: 120,
    g: 120,
    b: 140,
};
pub const BG_COLOR: Color = Color {
    r: 20,
    g: 20,
    b: 35,
};

fn border_style() -> CellStyle {
    CellStyle {
        fg: Some(BORDER_COLOR),
        bg: Some(BG_COLOR),
        dim: true,
        ..CellStyle::default()
    }
}

/// Renders box chrome centered on screen and returns the content area rect.
pub fn render(
    buffer: &mut ScreenBuffer,
    screen_width: i32,
    screen_height: i32,
    content_width: i32,
    content_height: i32,
    title: Option<&str>,
    footer: Option<&str>,
) -> Rect {
    // Box dimensions: content + 2 border chars + 2 padding chars
    let box_width = content_width + 4;

    // Height: top border + content + bottom border; title adds a row +
    // separator, footer adds a blank row + footer row.
    let mut box_height = 1 + content_height + 1;
    if title.is_some() {
        box_height += 2;
    }

    if footer.is_some() {
        box_height += 2;
    }

    let left = (screen_width - box_width) / 2;
    let top = (screen_height - box_height) / 2;

    let border = border_style();
    let title_style = CellStyle {
        fg: Some(TITLE_COLOR),
        bg: Some(BG_COLOR),
        bold: true,
        ..CellStyle::default()
    };
    let footer_style = CellStyle {
        fg: Some(FOOTER_COLOR),
        bg: Some(BG_COLOR),
        ..CellStyle::default()
    };

    let mut row = top;

    // Top border
    draw_horizontal_border(buffer, row, left, box_width, ('┌', '─', '┐'), border);
    row += 1;

    // Title row + separator
    if let Some(title) = title {
        fill_row(buffer, row, left, box_width);
        buffer.put(row, left, '│', border);
        buffer.put(row, left + box_width - 1, '│', border);
        let title_col = left + (box_width - title.chars().count() as i32) / 2;
        buffer.write_string(row, title_col, title, title_style, i64::from(i32::MAX));
        row += 1;

        draw_horizontal_border(buffer, row, left, box_width, ('├', '─', '┤'), border);
        row += 1;
    }

    // Content area
    let content_top = row;
    let content_left = left + 2; // border + 1 padding

    for _ in 0..content_height {
        fill_row(buffer, row, left, box_width);
        buffer.put(row, left, '│', border);
        buffer.put(row, left + box_width - 1, '│', border);
        row += 1;
    }

    // Footer
    if let Some(footer) = footer {
        fill_row(buffer, row, left, box_width);
        buffer.put(row, left, '│', border);
        buffer.put(row, left + box_width - 1, '│', border);
        row += 1;

        fill_row(buffer, row, left, box_width);
        buffer.put(row, left, '│', border);
        buffer.put(row, left + box_width - 1, '│', border);
        let footer_col = left + (box_width - footer.chars().count() as i32) / 2;
        buffer.write_string(row, footer_col, footer, footer_style, i64::from(i32::MAX));
        row += 1;
    }

    // Bottom border
    draw_horizontal_border(buffer, row, left, box_width, ('└', '─', '┘'), border);

    Rect::new(content_left, content_top, content_width, content_height)
}

fn draw_horizontal_border(buffer: &mut ScreenBuffer, row: i32, left: i32, width: i32, caps: (char, char, char), style: CellStyle) {
    let (left_cap, fill, right_cap) = caps;
    buffer.put(row, left, left_cap, style);
    for c in 1..width - 1 {
        buffer.put(row, left + c, fill, style);
    }

    buffer.put(row, left + width - 1, right_cap, style);
}

fn fill_row(buffer: &mut ScreenBuffer, row: i32, left: i32, width: i32) {
    let style = CellStyle {
        fg: None,
        bg: Some(BG_COLOR),
        ..CellStyle::default()
    };
    for c in 0..width {
        buffer.put(row, left + c, ' ', style);
    }
}
