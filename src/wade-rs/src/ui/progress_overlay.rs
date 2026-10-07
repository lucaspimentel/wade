//! Port of `src/Wade/UI/ProgressOverlay.cs`, with an ENHANCEMENT over C#:
//! when per-item progress is available, the overlay shows the item count and
//! current file name (C# shows only the operation label). This divergence is
//! tracked in KNOWN_DEVIATIONS.md, so no golden fixture covers this overlay.

use crate::input::FileOperationProgressEvent;
use crate::screen::{CellStyle, Color, ScreenBuffer};
use crate::ui::dialog_box::{self, BG_COLOR};

/// Port of `ProgressOverlay.Render`.
pub fn render(
    buffer: &mut ScreenBuffer,
    screen_width: i32,
    screen_height: i32,
    operation_label: &str,
    progress: Option<&FileOperationProgressEvent>,
) {
    let base_message = format!("{operation_label}...");

    // Enhancement: a second row with "i/n name" when progress is live
    let detail = progress.map(|p| {
        let total = p.total.to_string();
        let max_name = 40usize;
        let mut name = p.current_name.clone();
        if name.chars().count() > max_name {
            name = name.chars().take(max_name - 1).collect();
            name.push('\u{2026}');
        }

        format!("{}/{} {}", p.index, total, name)
    });

    let detail_len = detail.as_ref().map_or(0, |d| d.chars().count() as i32);
    let content_width = (base_message.chars().count() as i32).max(detail_len).max(20);
    let content_height = if detail.is_some() { 2 } else { 1 };

    let content = dialog_box::render(
        buffer,
        screen_width,
        screen_height,
        content_width,
        content_height,
        None,
        Some("Esc to cancel"),
    );

    let message_style = CellStyle {
        fg: Some(Color { r: 200, g: 200, b: 200 }),
        bg: Some(BG_COLOR),
        ..CellStyle::default()
    };

    buffer.write_string(content.top, content.left, &base_message, message_style, i64::from(content_width));

    if let Some(detail) = detail {
        let detail_style = CellStyle {
            fg: Some(Color { r: 120, g: 120, b: 140 }),
            bg: Some(BG_COLOR),
            ..CellStyle::default()
        };
        buffer.write_string(content.top + 1, content.left, &detail, detail_style, i64::from(content_width));
    }
}
