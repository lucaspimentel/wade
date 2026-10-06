//! Port of src/Wade/UI/PaneRenderer.cs (file lists, headers, borders).

use crate::fs::directory_contents::{FileSystemEntry, GitFileStatus};
use crate::screen::{CellStyle, Color, ScreenBuffer};
use crate::ui::file_icons;
use crate::ui::format_helpers::{format_date, format_percent_bar, format_size};
use crate::ui::layout::Layout;
use crate::ui::layout::Rect;
use crate::ui::{self};

// Column widths
const SIZE_WIDTH: i32 = 8;
const GAP_WIDTH: i32 = 2;
const STATUS_COL_WIDTH: i32 = 2;
const FULL_DATE_WIDTH: i32 = 19;
const DATE_ONLY_WIDTH: i32 = 10;
const SHORT_DATE_WIDTH: i32 = 6;

// Tier thresholds: nameWidth must be >= the widest detail column in that tier
const TIER1_DETAIL: i32 = SIZE_WIDTH + GAP_WIDTH + FULL_DATE_WIDTH + GAP_WIDTH; // 31
const TIER2_DETAIL: i32 = SIZE_WIDTH + GAP_WIDTH + DATE_ONLY_WIDTH + GAP_WIDTH; // 22
const TIER3_DETAIL: i32 = SIZE_WIDTH + GAP_WIDTH + SHORT_DATE_WIDTH + GAP_WIDTH; // 18
const TIER4_DETAIL: i32 = SIZE_WIDTH + GAP_WIDTH; // 10

const TIER1_MIN_WIDTH: i32 = 30 + TIER1_DETAIL; // 61
const TIER2_MIN_WIDTH: i32 = 22 + TIER2_DETAIL; // 44
const TIER3_MIN_WIDTH: i32 = 14 + TIER3_DETAIL; // 32
const TIER4_MIN_WIDTH: i32 = SIZE_WIDTH + TIER4_DETAIL; // 18

// Drive view column widths
const DRIVE_FORMAT_WIDTH: i32 = 7;
const DRIVE_LABEL_WIDTH: i32 = 16;
const DRIVE_BAR_MIN_WIDTH: i32 = 10;

// Drive view colors
const DRIVE_BAR_LOW: (u8, u8, u8) = (60, 160, 200);
const DRIVE_BAR_MED: (u8, u8, u8) = (220, 180, 50);
const DRIVE_BAR_HIGH: (u8, u8, u8) = (220, 80, 80);
const DRIVE_BAR_EMPTY: (u8, u8, u8) = (50, 50, 50);

fn color(rgb: (u8, u8, u8)) -> Color {
    Color { r: rgb.0, g: rgb.1, b: rgb.2 }
}

fn style(fg: (u8, u8, u8), bg: Option<(u8, u8, u8)>) -> CellStyle {
    CellStyle { fg: Some(color(fg)), bg: bg.map(color), bold: false, dim: false, inverse: false, underline: false, strikethrough: false }
}

fn cell_style(fg: Option<(u8, u8, u8)>, bg: Option<(u8, u8, u8)>, bold: bool) -> CellStyle {
    CellStyle {
        fg: fg.map(color),
        bg: bg.map(color),
        bold,
        dim: false,
        inverse: false,
        underline: false,
        strikethrough: false,
    }
}

const DIR_SEPARATOR: char = std::path::MAIN_SEPARATOR;

#[derive(Clone, Copy, Debug, Default)]
struct ColumnLayout {
    detail_width: i32,
    date_width: i32,
    status_col_width: i32,
    name_width: i32,
    drive_label_width: i32,
    drive_format_width: i32,
    drive_free_width: i32,
    drive_size_width: i32,
    drive_bar_width: i32,
}

fn clamp(v: i32, min: i32, max: i32) -> i32 {
    v.max(min).min(max)
}

fn compute_column_layout(pane_width: i32, show_size: bool, show_date: bool, is_drive_view: bool, has_status_col: bool) -> ColumnLayout {
    let mut c = ColumnLayout::default();

    if is_drive_view {
        let min_name = 8;
        let bar_col = DRIVE_BAR_MIN_WIDTH + GAP_WIDTH;
        let fixed_with_label = DRIVE_LABEL_WIDTH + GAP_WIDTH + DRIVE_FORMAT_WIDTH + GAP_WIDTH
            + SIZE_WIDTH + GAP_WIDTH + SIZE_WIDTH + GAP_WIDTH + bar_col;
        let fixed_full = DRIVE_FORMAT_WIDTH + GAP_WIDTH + SIZE_WIDTH + GAP_WIDTH + SIZE_WIDTH + GAP_WIDTH + bar_col;
        let fixed_medium = SIZE_WIDTH + GAP_WIDTH + SIZE_WIDTH + GAP_WIDTH + bar_col;
        let fixed_narrow = SIZE_WIDTH + GAP_WIDTH + bar_col;

        if pane_width >= min_name + fixed_with_label {
            c.drive_label_width = DRIVE_LABEL_WIDTH;
            c.drive_format_width = DRIVE_FORMAT_WIDTH;
            c.drive_free_width = SIZE_WIDTH;
            c.drive_size_width = SIZE_WIDTH;
            c.drive_bar_width = clamp(
                pane_width - min_name - DRIVE_LABEL_WIDTH - GAP_WIDTH
                    - DRIVE_FORMAT_WIDTH - GAP_WIDTH - SIZE_WIDTH - GAP_WIDTH
                    - SIZE_WIDTH - GAP_WIDTH - GAP_WIDTH,
                DRIVE_BAR_MIN_WIDTH,
                30,
            );
            c.detail_width = c.drive_label_width + GAP_WIDTH + c.drive_format_width + GAP_WIDTH
                + c.drive_free_width + GAP_WIDTH + c.drive_size_width + GAP_WIDTH
                + c.drive_bar_width + GAP_WIDTH;
        } else if pane_width >= min_name + fixed_full {
            c.drive_format_width = DRIVE_FORMAT_WIDTH;
            c.drive_free_width = SIZE_WIDTH;
            c.drive_size_width = SIZE_WIDTH;
            c.drive_bar_width = clamp(
                pane_width - min_name - DRIVE_FORMAT_WIDTH - GAP_WIDTH
                    - SIZE_WIDTH - GAP_WIDTH - SIZE_WIDTH - GAP_WIDTH - GAP_WIDTH,
                DRIVE_BAR_MIN_WIDTH,
                30,
            );
            c.detail_width = c.drive_format_width + GAP_WIDTH
                + c.drive_free_width + GAP_WIDTH + c.drive_size_width + GAP_WIDTH
                + c.drive_bar_width + GAP_WIDTH;
        } else if pane_width >= min_name + fixed_medium {
            c.drive_free_width = SIZE_WIDTH;
            c.drive_size_width = SIZE_WIDTH;
            c.drive_bar_width = clamp(
                pane_width - min_name - SIZE_WIDTH - GAP_WIDTH - SIZE_WIDTH - GAP_WIDTH - GAP_WIDTH,
                DRIVE_BAR_MIN_WIDTH,
                30,
            );
            c.detail_width = c.drive_free_width + GAP_WIDTH + c.drive_size_width + GAP_WIDTH + c.drive_bar_width + GAP_WIDTH;
        } else if pane_width >= min_name + fixed_narrow {
            c.drive_size_width = SIZE_WIDTH;
            c.drive_bar_width = clamp(
                pane_width - min_name - SIZE_WIDTH - GAP_WIDTH - GAP_WIDTH,
                DRIVE_BAR_MIN_WIDTH,
                30,
            );
            c.detail_width = c.drive_size_width + GAP_WIDTH + c.drive_bar_width + GAP_WIDTH;
        } else if pane_width >= min_name + bar_col {
            c.drive_bar_width = clamp(pane_width - min_name - GAP_WIDTH, DRIVE_BAR_MIN_WIDTH, 30);
            c.detail_width = c.drive_bar_width + GAP_WIDTH;
        }
    } else if show_size || show_date {
        if show_size && show_date {
            if pane_width >= TIER1_MIN_WIDTH {
                c.date_width = FULL_DATE_WIDTH;
                c.detail_width = SIZE_WIDTH + GAP_WIDTH + FULL_DATE_WIDTH + GAP_WIDTH;
            } else if pane_width >= TIER2_MIN_WIDTH {
                c.date_width = DATE_ONLY_WIDTH;
                c.detail_width = SIZE_WIDTH + GAP_WIDTH + DATE_ONLY_WIDTH + GAP_WIDTH;
            } else if pane_width >= TIER3_MIN_WIDTH {
                c.date_width = SHORT_DATE_WIDTH;
                c.detail_width = SIZE_WIDTH + GAP_WIDTH + SHORT_DATE_WIDTH + GAP_WIDTH;
            } else if pane_width >= TIER4_MIN_WIDTH {
                c.detail_width = SIZE_WIDTH + GAP_WIDTH;
            }
        } else if show_date {
            if pane_width >= TIER1_MIN_WIDTH {
                c.date_width = FULL_DATE_WIDTH;
                c.detail_width = FULL_DATE_WIDTH + GAP_WIDTH;
            } else if pane_width >= TIER2_MIN_WIDTH {
                c.date_width = DATE_ONLY_WIDTH;
                c.detail_width = DATE_ONLY_WIDTH + GAP_WIDTH;
            } else if pane_width >= TIER3_MIN_WIDTH {
                c.date_width = SHORT_DATE_WIDTH;
                c.detail_width = SHORT_DATE_WIDTH + GAP_WIDTH;
            }
        } else if pane_width >= TIER4_MIN_WIDTH {
            c.detail_width = SIZE_WIDTH + GAP_WIDTH;
        }
    }

    let status_col = if has_status_col { STATUS_COL_WIDTH } else { 0 };
    c.status_col_width = status_col;
    c.name_width = pane_width - c.detail_width - status_col;
    c
}

pub struct PaneRenderer;

impl PaneRenderer {
    /// Port of `RenderColumnHeaders`.
    pub fn render_column_headers(
        buffer: &mut ScreenBuffer,
        header_rect: Rect,
        show_icons: bool,
        show_size: bool,
        show_date: bool,
        is_drive_view: bool,
        has_status_col: bool,
    ) {
        let layout = compute_column_layout(header_rect.width, show_size, show_date, is_drive_view, has_status_col);
        let row = header_rect.top;
        let left = header_rect.left;
        let header_style = style((140, 140, 140), None);

        // Fill background
        buffer.fill_row(row, left, header_rect.width, ' ', header_style);

        // Name column header (left-aligned, after icon space)
        let icon_pad = if show_icons { 2 } else { 1 };
        let name_col = left + icon_pad;
        buffer.write_string(row, name_col, "Name", header_style, i64::from(layout.name_width - icon_pad));

        if layout.detail_width > 0 {
            let mut detail_col = left + header_rect.width;

            if is_drive_view {
                if layout.drive_bar_width > 0 {
                    detail_col -= GAP_WIDTH + layout.drive_bar_width;
                    let label = "% Full";
                    let label_start = (layout.drive_bar_width - i32::try_from(label.len()).unwrap_or(0)) / 2;
                    if label_start >= 0 {
                        buffer.write_string(row, detail_col + GAP_WIDTH + label_start, label, header_style, i64::try_from(label.len()).unwrap_or(0));
                    }
                }

                if layout.drive_size_width > 0 {
                    detail_col -= GAP_WIDTH + layout.drive_size_width;
                    buffer.write_string(row, detail_col + GAP_WIDTH + (SIZE_WIDTH - 4), "Size", header_style, 4);
                }

                if layout.drive_free_width > 0 {
                    detail_col -= GAP_WIDTH + layout.drive_free_width;
                    buffer.write_string(row, detail_col + GAP_WIDTH + (SIZE_WIDTH - 4), "Free", header_style, 4);
                }

                if layout.drive_format_width > 0 {
                    detail_col -= GAP_WIDTH + layout.drive_format_width;
                    buffer.write_string(row, detail_col + GAP_WIDTH, "Format", header_style, 6);
                }

                if layout.drive_label_width > 0 {
                    detail_col -= GAP_WIDTH + layout.drive_label_width;
                    buffer.write_string(row, detail_col + GAP_WIDTH, "Label", header_style, 5);
                }
            } else {
                if layout.date_width > 0 {
                    detail_col -= GAP_WIDTH + layout.date_width;
                    let offset = layout.date_width - 4;
                    buffer.write_string(row, detail_col + GAP_WIDTH + offset, "Date", header_style, 4);
                }

                if show_size && layout.detail_width > (if layout.date_width > 0 { layout.date_width + GAP_WIDTH } else { 0 }) {
                    detail_col -= GAP_WIDTH + SIZE_WIDTH;
                    buffer.write_string(row, detail_col + GAP_WIDTH + (SIZE_WIDTH - 4), "Size", header_style, 4);
                }
            }
        }

        // Separator line below headers
        if header_rect.height > 1 {
            let separator_style = style(ui::BORDER_COLOR, None);
            buffer.fill_row(row + 1, left, header_rect.width, '\u{2500}', separator_style);
        }
    }

    /// Port of `RenderFileList`.
    #[allow(clippy::too_many_arguments)]
    pub fn render_file_list(
        buffer: &mut ScreenBuffer,
        pane: Rect,
        entries: &[FileSystemEntry],
        selected_index: i32,
        scroll_offset: i32,
        is_active: bool,
        show_icons: bool,
        show_size: bool,
        show_date: bool,
        marked_paths: &std::collections::HashSet<String>,
        git_statuses: Option<&std::collections::HashMap<String, GitFileStatus>>,
        dir_sizes: Option<&std::collections::HashMap<String, i64>>,
        is_drive_view: bool,
    ) {
        let has_cloud_entries = entries.iter().any(|e| e.is_cloud_placeholder);
        let has_status_col = git_statuses.is_some() || has_cloud_entries;
        let col = compute_column_layout(pane.width, show_size, show_date, is_drive_view, has_status_col);
        let date_width = col.date_width;
        let detail_width = col.detail_width;
        let status_col_width = col.status_col_width;
        let name_width = col.name_width;
        let drive_label_width = col.drive_label_width;
        let drive_format_width = col.drive_format_width;
        let drive_free_width = col.drive_free_width;
        let drive_size_width = col.drive_size_width;
        let drive_bar_width = col.drive_bar_width;

        let active_selection = cell_style(Some(ui::SELECTION_FG), Some(ui::SELECTION_BG), true);
        let inactive_selection = cell_style(Some(ui::SELECTION_FG), Some((60, 60, 80)), true);
        let dir_style = cell_style(Some(ui::DIR_COLOR), None, true);
        let file_style = cell_style(Some(ui::FILE_COLOR), None, false);
        let detail_style_default = cell_style(Some(ui::DETAIL_COLOR), None, false);
        let symlink_style = cell_style(Some(ui::SYMLINK_COLOR), None, false);
        let broken_symlink_style = cell_style(Some(ui::BROKEN_SYMLINK_COLOR), None, false);
        let cloud_file_style = cell_style(Some(ui::CLOUD_PLACEHOLDER_COLOR), None, false);
        let cloud_dir_style = cell_style(Some(ui::CLOUD_PLACEHOLDER_COLOR), None, true);
        let marked_style = cell_style(Some(ui::FILE_COLOR), Some(ui::MARKED_BG), false);
        let marked_dir_style = cell_style(Some(ui::DIR_COLOR), Some(ui::MARKED_BG), true);
        let marked_selected_style = cell_style(Some(ui::SELECTION_FG), Some((180, 180, 60)), true);
        let marked_symlink_style = cell_style(Some(ui::SYMLINK_COLOR), Some(ui::MARKED_BG), false);
        let marked_broken_symlink_style = cell_style(Some(ui::BROKEN_SYMLINK_COLOR), Some(ui::MARKED_BG), false);
        let marked_cloud_style = cell_style(Some(ui::CLOUD_PLACEHOLDER_COLOR), Some(ui::MARKED_BG), false);

        for row in 0..pane.height {
            let entry_index = scroll_offset + row;

            if entry_index < 0 || entry_index as usize >= entries.len() {
                // Empty row: just leave blank
                continue;
            }

            let entry = &entries[entry_index as usize];
            let is_selected = entry_index == selected_index;
            let is_marked = marked_paths.contains(&entry.full_path);

            let git_status = get_git_status(&entry.full_path, git_statuses);

            let style = if is_selected && is_marked && is_active {
                marked_selected_style
            } else if is_selected && is_active {
                active_selection
            } else if is_selected {
                inactive_selection
            } else if is_marked && entry.is_broken_symlink {
                marked_broken_symlink_style
            } else if is_marked && entry.is_symlink() {
                marked_symlink_style
            } else if is_marked && entry.is_cloud_placeholder {
                marked_cloud_style
            } else if is_marked {
                get_marked_git_style(git_status, entry.is_directory).unwrap_or(if entry.is_directory { marked_dir_style } else { marked_style })
            } else if entry.is_broken_symlink {
                broken_symlink_style
            } else if entry.is_symlink() {
                symlink_style
            } else if entry.is_cloud_placeholder {
                if entry.is_directory { cloud_dir_style } else { cloud_file_style }
            } else {
                get_git_style(git_status, entry.is_directory).unwrap_or(if entry.is_directory { dir_style } else { file_style })
            };

            let detail_style = if is_selected || is_marked { style } else { detail_style_default };

            // If selected or marked, fill the row background
            if is_selected || is_marked {
                buffer.fill_row(pane.top + row, pane.left, pane.width, ' ', style);
            }

            let screen_row = pane.top + row;
            let entry_col = pane.left;

            // Render name
            #[allow(clippy::needless_late_init, unused_assignments)]
            let name_chars_used: i32;
            if show_icons {
                buffer.put(screen_row, entry_col, file_icons::get_icon(entry), style);
                buffer.put(screen_row, entry_col + 1, ' ', style);

                let name_start = 2;
                let max_name = name_width - name_start;
                let name_len = (i32::try_from(entry.name.len()).unwrap_or(i32::MAX)).min(max_name);
                if i32::try_from(entry.name.len()).unwrap_or(i32::MAX) > max_name && max_name >= 2 {
                    buffer.write_string(screen_row, entry_col + name_start, &entry.name, style, i64::from(max_name - 1));
                    buffer.put(screen_row, entry_col + name_start + max_name - 1, '\u{2026}', style);
                } else {
                    buffer.write_string(screen_row, entry_col + name_start, &entry.name, style, i64::from(max_name));
                }

                name_chars_used = name_start + name_len;
            } else {
                let prefix = if entry.is_directory && !entry.is_drive { DIR_SEPARATOR } else { ' ' };
                buffer.put(screen_row, entry_col, prefix, style);
                let max_name = name_width - 1;
                let name_len = (i32::try_from(entry.name.len()).unwrap_or(i32::MAX)).min(max_name);
                if i32::try_from(entry.name.len()).unwrap_or(i32::MAX) > max_name && max_name >= 2 {
                    buffer.write_string(screen_row, entry_col + 1, &entry.name, style, i64::from(max_name - 1));
                    buffer.put(screen_row, entry_col + 1 + max_name - 1, '\u{2026}', style);
                } else {
                    buffer.write_string(screen_row, entry_col + 1, &entry.name, style, i64::from(max_name));
                }

                name_chars_used = 1 + name_len;
            }

            // Append " → target" suffix for symlinks and app exec aliases
            let link_target: Option<&str> = if entry.is_symlink() {
                entry.link_target.as_deref()
            } else if entry.is_app_exec_link {
                entry.app_exec_link_target.as_deref()
            } else {
                None
            };

            if let Some(link_target) = link_target {
                let mut remaining = name_width - name_chars_used;
                if remaining > 4 {
                    // need room for at least " → X"
                    let suffix_style = if is_selected { style } else if entry.is_broken_symlink { broken_symlink_style } else { detail_style_default };
                    let arrow = " \u{2192} ";
                    let suffix_col = entry_col + name_chars_used;
                    buffer.write_string(screen_row, suffix_col, arrow, suffix_style, i64::from(remaining));
                    remaining -= i32::try_from(arrow.chars().count()).unwrap_or(0);
                    if remaining > 0 {
                        let target_len = i32::try_from(link_target.chars().count()).unwrap_or(i32::MAX);
                        if target_len > remaining && remaining >= 2 {
                            buffer.write_string(screen_row, suffix_col + i32::try_from(arrow.chars().count()).unwrap_or(0), link_target, suffix_style, i64::from(remaining - 1));
                            buffer.put(screen_row, suffix_col + i32::try_from(arrow.chars().count()).unwrap_or(0) + remaining - 1, '\u{2026}', suffix_style);
                        } else {
                            buffer.write_string(screen_row, suffix_col + i32::try_from(arrow.chars().count()).unwrap_or(0), link_target, suffix_style, i64::from(remaining));
                        }
                    }
                }
            }

            // Status column (git status or cloud icon between name and size)
            if status_col_width > 0 {
                if git_status != GitFileStatus::NONE {
                    let git_icon_style = if is_selected { style } else { get_git_icon_style(git_status, is_marked) };
                    if let Some(icon) = file_icons::get_git_status_icon(git_status) {
                        buffer.put(screen_row, entry_col + name_width, icon, git_icon_style);
                    }
                } else if entry.is_cloud_placeholder {
                    let cloud_icon_style = if is_selected {
                        style
                    } else {
                        cell_style(Some(ui::CLOUD_PLACEHOLDER_COLOR), if is_marked { Some(ui::MARKED_BG) } else { None }, false)
                    };
                    buffer.put(screen_row, entry_col + name_width, file_icons::get_cloud_icon(), cloud_icon_style);
                }
            }

            // Render detail columns (right-aligned from pane edge)
            if detail_width > 0 {
                let mut detail_col = pane.left + pane.width;

                if is_drive_view {
                    // Percent bar (rightmost)
                    if drive_bar_width > 0 {
                        detail_col -= GAP_WIDTH + drive_bar_width;

                        if entry.drive_total_size > 0 {
                            let fraction = (entry.drive_total_size - entry.drive_free_space) as f64 / entry.drive_total_size as f64;

                            // Reserve first char as a spacer so the bar doesn't
                            // blend with the selection background color.
                            let actual_bar_width = (drive_bar_width - 1) as usize;
                            let mut bar_buf = vec![' '; actual_bar_width];
                            let bar = format_percent_bar(&mut bar_buf, fraction, actual_bar_width);

                            let bar_color = if fraction > 0.9 {
                                DRIVE_BAR_HIGH
                            } else if fraction > 0.7 {
                                DRIVE_BAR_MED
                            } else {
                                DRIVE_BAR_LOW
                            };

                            let bar_col = detail_col + GAP_WIDTH;
                            let label_end = bar.label_start + bar.label_length;

                            buffer.put(screen_row, bar_col, ' ', CellStyle::default());

                            for (c, ch) in bar_buf.iter().enumerate() {
                                let is_label = c >= bar.label_start && c < label_end;
                                let is_filled = c < bar.filled_count;

                                let char_style = if is_label {
                                    cell_style(Some((0, 0, 0)), Some(if is_filled { bar_color } else { DRIVE_BAR_EMPTY }), false)
                                } else if is_filled {
                                    cell_style(Some(bar_color), None, false)
                                } else {
                                    cell_style(Some(DRIVE_BAR_EMPTY), None, false)
                                };

                                buffer.put(screen_row, bar_col + 1 + c as i32, *ch, char_style);
                            }
                        }
                    }

                    // Total size
                    if drive_size_width > 0 {
                        detail_col -= GAP_WIDTH + drive_size_width;

                        if entry.drive_total_size > 0 {
                            let mut size_buf = vec![' '; SIZE_WIDTH as usize];
                            let mut temp_buf = vec![' '; 32];
                            let size_len = format_size(&mut temp_buf, entry.drive_total_size);
                            if size_len <= SIZE_WIDTH as usize {
                                for i in 0..size_len {
                                    size_buf[SIZE_WIDTH as usize - size_len + i] = temp_buf[i];
                                }
                            }

                            buffer.write_string(screen_row, detail_col + GAP_WIDTH, &size_buf.iter().collect::<String>(), detail_style, i64::from(SIZE_WIDTH));
                        }
                    }

                    // Free space
                    if drive_free_width > 0 {
                        detail_col -= GAP_WIDTH + drive_free_width;

                        if entry.drive_total_size > 0 {
                            let mut size_buf = vec![' '; SIZE_WIDTH as usize];
                            let mut temp_buf = vec![' '; 32];
                            let free_len = format_size(&mut temp_buf, entry.drive_free_space);
                            if free_len <= SIZE_WIDTH as usize {
                                for i in 0..free_len {
                                    size_buf[SIZE_WIDTH as usize - free_len + i] = temp_buf[i];
                                }
                            }

                            buffer.write_string(screen_row, detail_col + GAP_WIDTH, &size_buf.iter().collect::<String>(), detail_style, i64::from(SIZE_WIDTH));
                        }
                    }

                    // File system format
                    if drive_format_width > 0 {
                        detail_col -= GAP_WIDTH + drive_format_width;

                        if let Some(fmt) = &entry.drive_format {
                            let fmt_len = fmt.len().min(drive_format_width as usize);
                            buffer.write_string(screen_row, detail_col + GAP_WIDTH, &fmt[..fmt_len], detail_style, i64::try_from(fmt_len).unwrap_or(0));
                        }
                    }

                    // Volume label
                    if drive_label_width > 0 {
                        detail_col -= GAP_WIDTH + drive_label_width;

                        if let Some(label) = &entry.drive_label {
                            let lbl_len = label.len().min(drive_label_width as usize);
                            buffer.write_string(screen_row, detail_col + GAP_WIDTH, &label[..lbl_len], detail_style, i64::try_from(lbl_len).unwrap_or(0));
                        }
                    }
                } else {
                    // Date column (rightmost)
                    if date_width > 0 {
                        detail_col -= GAP_WIDTH + date_width;
                        let mut date_buf = vec![' '; FULL_DATE_WIDTH as usize];
                        let date_len = format_date(&mut date_buf, entry.last_modified, date_width as usize);
                        buffer.write_string(screen_row, detail_col + GAP_WIDTH, &date_buf[..date_len].iter().collect::<String>(), detail_style, i64::from(date_width));
                    }

                    // Size column
                    if show_size {
                        detail_col -= GAP_WIDTH + SIZE_WIDTH;
                        let display_size: Option<i64> = if !entry.is_directory {
                            Some(entry.size)
                        } else {
                            dir_sizes.and_then(|m| m.get(&entry.full_path)).copied()
                        };

                        if let Some(display) = display_size {
                            let mut size_buf = vec![' '; SIZE_WIDTH as usize];
                            let mut temp_buf = vec![' '; 32];
                            let size_len = format_size(&mut temp_buf, display);
                            if size_len <= SIZE_WIDTH as usize {
                                for i in 0..size_len {
                                    size_buf[SIZE_WIDTH as usize - size_len + i] = temp_buf[i];
                                }
                            }

                            buffer.write_string(screen_row, detail_col + GAP_WIDTH, &size_buf.iter().collect::<String>(), detail_style, i64::from(SIZE_WIDTH));
                        } else if entry.is_directory && dir_sizes.is_some() {
                            // Directory size is loading — show indicator right-aligned
                            let mut size_buf = vec![' '; SIZE_WIDTH as usize];
                            let last = SIZE_WIDTH as usize - 1;
                            size_buf[last] = '\u{2026}';
                            buffer.write_string(screen_row, detail_col + GAP_WIDTH, &size_buf.iter().collect::<String>(), detail_style, i64::from(SIZE_WIDTH));
                        }
                    }
                }
            }
        }
    }

    /// Port of `RenderPreview`: styled lines from `scroll_offset`, with an
    /// optional 4-wide right-aligned line number plus a space.
    pub fn render_preview(
        buffer: &mut ScreenBuffer,
        pane: Rect,
        lines: &[crate::highlight::StyledLine],
        scroll_offset: usize,
        show_line_numbers: bool,
    ) {
        let default_style = style(ui::FILE_COLOR, None);
        let line_num_style = style((100, 100, 100), None);
        let line_num_width = if show_line_numbers { 5 } else { 0 };
        let mut content_line_number = scroll_offset;

        for row in 0..pane.height.max(0) {
            let line_index = scroll_offset + row as usize;
            let Some(styled_line) = lines.get(line_index) else {
                break;
            };

            content_line_number += 1;

            if show_line_numbers {
                // C# TryFormat into 4 chars: wider numbers leave it blank
                let number = content_line_number.to_string();
                let text = if number.len() <= 4 { format!("{number:>4}") } else { "    ".to_string() };

                for (i, ch) in text.chars().enumerate() {
                    buffer.put(pane.top + row, pane.left + i as i32, ch, line_num_style);
                }

                buffer.put(pane.top + row, pane.left + 4, ' ', line_num_style);
            }

            let content_col = pane.left + line_num_width;
            let content_width = pane.width - line_num_width;

            if let Some(char_styles) = &styled_line.char_styles {
                Self::render_per_char_content(buffer, pane.top + row, content_col, content_width, &styled_line.text, char_styles, default_style);
            } else if let Some(spans) = styled_line.spans.as_ref().filter(|spans| !spans.is_empty()) {
                Self::render_styled_content(buffer, pane.top + row, content_col, content_width, &styled_line.text, spans, default_style);
            } else {
                buffer.write_string(pane.top + row, content_col, &styled_line.text, default_style, i64::from(content_width));
            }
        }
    }

    /// Port of `RenderStyledContent`: the first span covering a position
    /// wins. Positions are chars (C#: UTF-16 units).
    fn render_styled_content(
        buffer: &mut ScreenBuffer,
        row: i32,
        start_col: i32,
        max_width: i32,
        text: &str,
        spans: &[crate::highlight::StyledSpan],
        default_style: CellStyle,
    ) {
        let mut col = start_col;
        let mut chars_written = 0;

        for (pos, ch) in text.chars().enumerate() {
            if chars_written >= max_width {
                break;
            }

            let style = spans
                .iter()
                .find(|span| span.start <= pos && pos < span.start + span.len)
                .map_or(default_style, |span| crate::highlight::theme::get_style(span.kind));

            let w = crate::rune_width::rune_width(ch) as i32;
            if chars_written + w > max_width {
                break;
            }

            buffer.put(row, col, ch, style);
            col += w;
            chars_written += w;
        }
    }

    /// Port of `RenderPerCharContent`: one style per char, default past
    /// the end of `char_styles`.
    fn render_per_char_content(
        buffer: &mut ScreenBuffer,
        row: i32,
        start_col: i32,
        max_width: i32,
        text: &str,
        char_styles: &[CellStyle],
        default_style: CellStyle,
    ) {
        let mut col = start_col;
        let mut chars_written = 0;

        for (index, ch) in text.chars().enumerate() {
            if chars_written >= max_width {
                break;
            }

            let style = char_styles.get(index).copied().unwrap_or(default_style);
            let w = crate::rune_width::rune_width(ch) as i32;
            if chars_written + w > max_width {
                break;
            }

            buffer.put(row, col, ch, style);
            col += w;
            chars_written += w;
        }
    }

    /// Port of `RenderMessage`.
    pub fn render_message(buffer: &mut ScreenBuffer, pane: Rect, message: &str) {
        // C# uses DimColor (100,100,100), not DetailColor
        let style = style((100, 100, 100), None);
        buffer.write_string(pane.top, pane.left + 1, message, style, i64::from(pane.width - 1));
    }

    /// Port of `RenderBorders`.
    pub fn render_borders(buffer: &mut ScreenBuffer, layout: &Layout, _terminal_height: i32, preview_pane_enabled: bool, parent_pane_enabled: bool) {
        let style = style(ui::BORDER_COLOR, None);
        // Rust: the pane rows (below the tab bar when it is shown)
        let rows = layout.center_pane.top..layout.center_pane.top + layout.center_pane.height;

        if parent_pane_enabled {
            let border_col1 = layout.left_pane.right();
            for row in rows.clone() {
                buffer.put(row, border_col1, '\u{2502}', style);
            }
        }

        if preview_pane_enabled {
            let border_col2 = layout.center_pane.right();
            for row in rows {
                buffer.put(row, border_col2, '\u{2502}', style);
            }
        }
    }
}

fn get_git_status(path: &str, statuses: Option<&std::collections::HashMap<String, GitFileStatus>>) -> GitFileStatus {
    statuses.and_then(|s| s.get(path)).copied().unwrap_or(GitFileStatus::NONE)
}

fn get_git_style(status: GitFileStatus, is_directory: bool) -> Option<CellStyle> {
    if status.contains(GitFileStatus::CONFLICT) {
        Some(if is_directory {
            cell_style(Some(ui::GIT_CONFLICT), None, true)
        } else {
            cell_style(Some(ui::GIT_CONFLICT), None, false)
        })
    } else if status.contains(GitFileStatus::STAGED) {
        Some(if is_directory {
            cell_style(Some(ui::GIT_STAGED), None, true)
        } else {
            cell_style(Some(ui::GIT_STAGED), None, false)
        })
    } else if status.contains(GitFileStatus::MODIFIED) {
        Some(if is_directory {
            cell_style(Some(ui::GIT_MODIFIED), None, true)
        } else {
            cell_style(Some(ui::GIT_MODIFIED), None, false)
        })
    } else if status.contains(GitFileStatus::UNTRACKED) {
        Some(if is_directory {
            cell_style(Some(ui::GIT_UNTRACKED), None, true)
        } else {
            cell_style(Some(ui::GIT_UNTRACKED), None, false)
        })
    } else {
        None
    }
}

fn get_marked_git_style(status: GitFileStatus, _is_directory: bool) -> Option<CellStyle> {
    if status.contains(GitFileStatus::CONFLICT) {
        Some(cell_style(Some(ui::GIT_CONFLICT), Some(ui::MARKED_BG), false))
    } else if status.contains(GitFileStatus::STAGED) {
        Some(cell_style(Some(ui::GIT_STAGED), Some(ui::MARKED_BG), false))
    } else if status.contains(GitFileStatus::MODIFIED) {
        Some(cell_style(Some(ui::GIT_MODIFIED), Some(ui::MARKED_BG), false))
    } else if status.contains(GitFileStatus::UNTRACKED) {
        Some(cell_style(Some(ui::GIT_UNTRACKED), Some(ui::MARKED_BG), false))
    } else {
        None
    }
}

fn get_git_icon_style(status: GitFileStatus, is_marked: bool) -> CellStyle {
    let bg = if is_marked { Some(ui::MARKED_BG) } else { None };
    if status.contains(GitFileStatus::CONFLICT) {
        cell_style(Some(ui::GIT_CONFLICT), bg, false)
    } else if status.contains(GitFileStatus::STAGED) {
        cell_style(Some(ui::GIT_STAGED), bg, false)
    } else if status.contains(GitFileStatus::MODIFIED) {
        cell_style(Some(ui::GIT_MODIFIED), bg, false)
    } else if status.contains(GitFileStatus::UNTRACKED) {
        cell_style(Some(ui::GIT_UNTRACKED), bg, false)
    } else {
        cell_style(Some(ui::FILE_COLOR), bg, false)
    }
}

