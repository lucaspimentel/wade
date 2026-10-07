//! Port of src/Wade/UI/StatusBar.cs.

use crate::fs::directory_contents::FileSystemEntry;
use crate::screen::{CellStyle, Color, ScreenBuffer};
use crate::ui::format_helpers::{format_size, month_abbrev};
use crate::ui::layout::Rect;
use crate::ui::notification::{Notification, NotificationKind};

use crate::fs::directory_contents::SortMode;

fn color(rgb: (u8, u8, u8)) -> Color {
    Color { r: rgb.0, g: rgb.1, b: rgb.2 }
}

fn style(fg: (u8, u8, u8), bg: Option<(u8, u8, u8)>, bold: bool, dim: bool) -> CellStyle {
    CellStyle {
        fg: Some(color(fg)),
        bg: bg.map(color),
        bold,
        dim,
        inverse: false,
        underline: false,
        strikethrough: false,
    }
}

const STATUS_FG: (u8, u8, u8) = (180, 180, 180);
const STATUS_BG: (u8, u8, u8) = (30, 30, 50);
const PATH_FG: (u8, u8, u8) = (80, 160, 255);
const SUCCESS_FG: (u8, u8, u8) = (80, 200, 80);
const ERROR_FG: (u8, u8, u8) = (220, 80, 80);

/// Port of `StatusBar.Render`. Metadata parameters are `None` until the
/// preview phases land; the render path is otherwise identical.
#[allow(clippy::too_many_arguments)]
pub fn render(
    buffer: &mut ScreenBuffer,
    rect: Rect,
    current_path: &str,
    item_count: usize,
    selected_index: usize,
    selected_entry: Option<&FileSystemEntry>,
    file_type_label: Option<&str>,
    encoding: Option<&str>,
    line_ending: Option<&str>,
    notification: Option<Notification>,
    marked_count: usize,
    (sort_mode, sort_ascending, sort_overridden): (SortMode, bool, bool),
    clipboard_count: usize,
    clipboard_is_cut: bool,
    branch_name: Option<&str>,
    ahead_behind: Option<&str>,
) {
    // Fill background
    let bg_style = style(STATUS_FG, Some(STATUS_BG), false, false);
    for col in 0..rect.width {
        buffer.put(rect.top, rect.left + col, ' ', bg_style);
    }

    // Left side: current path
    let path_style = style(PATH_FG, Some(STATUS_BG), true, false);
    let path_max_width = rect.width / 2;
    buffer.write_string(rect.top, rect.left + 1, current_path, path_style, i64::from(path_max_width));

    // Branch name (after path)
    let path_len = i32::try_from(current_path.chars().count()).unwrap_or(i32::MAX).min(path_max_width);
    let mut info_col = rect.left + 1 + path_len;
    let mut info_max_width = path_max_width - path_len;

    if let Some(branch_name) = branch_name
        && info_max_width > 4
    {
        let branch_style = style((180, 140, 220), Some(STATUS_BG), false, false);
        buffer.put(rect.top, info_col, ' ', branch_style);
        buffer.put(rect.top, info_col + 1, ' ', branch_style);
        buffer.put(rect.top, info_col + 2, '\u{E0A0}', branch_style); // nf-pl-branch
        buffer.put(rect.top, info_col + 3, ' ', branch_style);
        let max_branch = (i32::try_from(branch_name.chars().count()).unwrap_or(0)).min(info_max_width - 4);
        if max_branch > 0 {
            buffer.write_string(rect.top, info_col + 4, branch_name, branch_style, i64::from(max_branch));
            let total = 4 + max_branch;
            info_col += total;
            info_max_width -= total;
        }

        // Ahead/behind counts (after branch name)
        if let Some(ahead_behind) = ahead_behind {
            let ab_len = i32::try_from(ahead_behind.chars().count()).unwrap_or(0);
            if info_max_width > ab_len {
                let ab_style = style((140, 140, 160), Some(STATUS_BG), false, true);
                buffer.write_string(rect.top, info_col, ahead_behind, ab_style, i64::from(info_max_width));
                let used = ab_len.min(info_max_width);
                info_col += used;
                info_max_width -= used;
            }
        }
    }

    // Mark count
    if marked_count > 0 {
        let mark_style = style((220, 220, 100), Some(STATUS_BG), false, false);
        let mark_text = format!("  {marked_count} marked");
        if info_max_width > 0 {
            buffer.write_string(rect.top, info_col, &mark_text, mark_style, i64::from(info_max_width));
        }

        info_col += i32::try_from(mark_text.chars().count()).unwrap_or(0);
        info_max_width -= i32::try_from(mark_text.chars().count()).unwrap_or(0);
    }

    // Clipboard indicator
    if clipboard_count > 0 && info_max_width > 0 {
        let clip_style = style((140, 180, 220), Some(STATUS_BG), false, false);
        let label = if clipboard_is_cut { " cut]" } else { " copied]" };
        let clip_text = format!("  [{clipboard_count}{label}");
        buffer.write_string(rect.top, info_col, &clip_text, clip_style, i64::from(info_max_width));
    }

    // Right side: always show metadata right-aligned
    let right_text = build_right_text(
        item_count, selected_index, selected_entry, file_type_label, encoding, line_ending, sort_mode, sort_ascending,
        sort_overridden,
    );
    let right_len = i32::try_from(right_text.chars().count()).unwrap_or(0);

    let right_col = rect.width - right_len - 1;
    if right_col > 0 {
        for (i, ch) in right_text.chars().enumerate() {
            buffer.put(rect.top, rect.left + right_col + i as i32, ch, bg_style);
        }
    }

    // Notification: render in the gap between left content and metadata
    if let Some(notif) = notification {
        let notif_fg = match notif.kind {
            NotificationKind::Success => SUCCESS_FG,
            NotificationKind::Error => ERROR_FG,
            NotificationKind::Info => STATUS_FG,
        };
        let notif_style = style(notif_fg, Some(STATUS_BG), false, false);

        // Available gap: from end of left content to 2 chars before metadata
        let gap_end = if right_col > 0 { right_col - 2 } else { rect.width - right_len - 3 };
        let gap_start = info_col + 2;
        let gap_width = gap_end - gap_start;

        if gap_width > 0 {
            let mut message = notif.message.as_str();
            let message_len = i32::try_from(message.chars().count()).unwrap_or(0);
            if message_len > gap_width {
                let take = message.char_indices().nth(gap_width as usize).map(|(i, _)| i).unwrap_or(message.len());
                message = &message[..take];
            }

            // Right-align notification within the gap (closer to metadata)
            let notif_col = gap_end - i32::try_from(message.chars().count()).unwrap_or(0);
            buffer.write_string(rect.top, rect.left + notif_col, message, notif_style, i64::from(i32::MAX));
        }
    }
}

#[allow(clippy::too_many_arguments)]
fn build_right_text(
    item_count: usize,
    selected_index: usize,
    selected_entry: Option<&FileSystemEntry>,
    file_type_label: Option<&str>,
    encoding: Option<&str>,
    line_ending: Option<&str>,
    sort_mode: SortMode,
    sort_ascending: bool,
    sort_overridden: bool,
) -> String {
    let mut out = String::new();

    // Sort indicator
    out.push_str(match sort_mode {
        SortMode::Modified => "time",
        SortMode::Size => "size",
        SortMode::Extension => "ext",
        SortMode::Name => "name",
    });
    out.push(if sort_ascending { '\u{2191}' } else { '\u{2193}' });
    // Rust only: the directory has its own saved sort
    if sort_overridden {
        out.push('*');
    }
    out.push_str("  ");

    if let Some(entry) = selected_entry {
        if !entry.is_directory {
            if let Some(label) = file_type_label {
                out.push_str(label);
                out.push_str("  ");
            }
            if let Some(enc) = encoding {
                out.push_str(enc);
                out.push_str("  ");
            }
            if let Some(le) = line_ending {
                out.push_str(le);
                out.push_str("  ");
            }
        }

        if entry.is_directory {
            out.push_str("dir");
        } else {
            let mut buf = vec![' '; 32];
            let n = format_size(&mut buf, entry.size);
            out.push_str(&buf[..n].iter().collect::<String>());
        }
        out.push_str("  ");
    }

    if item_count > 0 {
        out.push_str(&format!("{}/{}", selected_index + 1, item_count));
    } else {
        out.push_str("empty");
    }

    out
}

// Month abbreviations come from FormatHelpers to stay in sync.
#[allow(dead_code)]
fn month_name(month: u32) -> &'static str {
    month_abbrev(month)
}
