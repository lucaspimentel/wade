//! Port of `src/Wade/UI/PropertiesOverlay.cs`: the `i` (Properties) overlay
//! with scroll support. Metadata-provider sections are a Phase 7 item; the
//! parameter stays in the signature so Phase 7 is a drop-in.

use crate::fs::directory_contents::{FileSystemEntry, GitFileStatus};
use crate::fs::file_type_labels::get_file_type_label;
use crate::screen::{CellStyle, Color, ScreenBuffer};
use crate::ui::dialog_box::{self, BG_COLOR};

const LABEL_WIDTH: i32 = 12;

fn label_color() -> Color {
    Color { r: 120, g: 120, b: 140 }
}

fn value_color() -> Color {
    Color { r: 200, g: 200, b: 200 }
}

fn git_modified_color() -> Color {
    Color { r: 220, g: 180, b: 50 }
}

fn git_staged_color() -> Color {
    Color { r: 80, g: 200, b: 200 }
}

fn git_untracked_color() -> Color {
    Color { r: 80, g: 200, b: 80 }
}

fn git_conflict_color() -> Color {
    Color { r: 220, g: 80, b: 80 }
}

const LABELS: [&str; 11] = [
    "Name",
    "Path",
    "Type",
    "Target",
    "Size",
    "Created",
    "Modified",
    "Accessed",
    "Attributes",
    "Read-only",
    "Git status",
];

#[derive(Clone, Copy, PartialEq, Eq)]
enum RowKind {
    Blank,
    Header,
    LabelValue,
}

#[derive(Clone)]
struct Row {
    kind: RowKind,
    label: String,
    value: String,
    label_style: CellStyle,
    value_style: CellStyle,
}

/// Port of `PropertiesOverlay.Render`. Returns the total content row count.
#[allow(clippy::too_many_arguments)]
pub fn render(
    buffer: &mut ScreenBuffer,
    screen_width: i32,
    screen_height: i32,
    entry: &FileSystemEntry,
    directory_size_text: Option<&str>,
    git_status: Option<GitFileStatus>,
    metadata_sections: Option<&[crate::ui::metadata::MetadataSection]>,
    scroll_offset: usize,
) -> usize {
    let values = build_values(entry, directory_size_text, git_status);

    let label_style = CellStyle {
        fg: Some(label_color()),
        bg: Some(BG_COLOR),
        dim: true,
        ..CellStyle::default()
    };
    let value_style = CellStyle {
        fg: Some(value_color()),
        bg: Some(BG_COLOR),
        ..CellStyle::default()
    };

    // Build flat list of all renderable rows
    let mut rows: Vec<Row> = Vec::new();

    for (index, label) in LABELS.iter().enumerate() {
        let mut row_value_style = value_style;

        // Git status row gets colored
        if index == LABELS.len() - 1
            && let Some(status) = git_status
            && status != GitFileStatus::NONE
        {
            row_value_style = CellStyle {
                fg: Some(get_git_status_color(status)),
                bg: Some(BG_COLOR),
                ..CellStyle::default()
            };
        }

        rows.push(Row {
            kind: RowKind::LabelValue,
            label: (*label).to_string(),
            value: values[index].clone(),
            label_style,
            value_style: row_value_style,
        });
    }

    let mut max_value_len = values.iter().map(|value| value.chars().count()).max().unwrap_or(0);

    // Metadata rows (Phase 7 populates these)
    if let Some(sections) = metadata_sections
        && !sections.is_empty()
    {
        let header_style = CellStyle {
            fg: Some(Color { r: 180, g: 180, b: 200 }),
            bg: Some(BG_COLOR),
            bold: true,
            ..CellStyle::default()
        };

        // Blank separator between system props and metadata
        rows.push(Row {
            kind: RowKind::Blank,
            label: String::new(),
            value: String::new(),
            label_style,
            value_style,
        });

        for section in sections {
            if rows.last().is_some_and(|row| row.kind != RowKind::Blank) {
                rows.push(Row {
                    kind: RowKind::Blank,
                    label: String::new(),
                    value: String::new(),
                    label_style,
                    value_style,
                });
            }

            if let Some(header) = &section.header {
                rows.push(Row {
                    kind: RowKind::Header,
                    label: String::new(),
                    value: header.clone(),
                    label_style,
                    value_style: header_style,
                });
            }

            for meta_entry in &section.entries {
                rows.push(Row {
                    kind: RowKind::LabelValue,
                    label: meta_entry.label.clone(),
                    value: meta_entry.value.clone(),
                    label_style,
                    value_style,
                });

                let row_len = if meta_entry.label.is_empty() {
                    meta_entry.value.chars().count() + 2
                } else {
                    LABEL_WIDTH as usize + meta_entry.value.chars().count()
                };

                if row_len > max_value_len + LABEL_WIDTH as usize {
                    max_value_len = row_len - LABEL_WIDTH as usize;
                }
            }
        }
    }

    let total_content_height = rows.len();

    // Compute content width
    let mut content_width = max_value_len as i32 + LABEL_WIDTH;
    let max_content_width = screen_width - 8;

    if content_width > max_content_width {
        content_width = max_content_width;
    }

    // DialogBox uses: borders + title + separator + blank + footer = content + 6
    let max_visible_height = (screen_height - 8).max(1);
    let scrollable = total_content_height as i32 > max_visible_height;
    let visible_height = if scrollable { max_visible_height } else { total_content_height as i32 };

    // Clamp scroll offset
    let max_scroll = (total_content_height as i64 - i64::from(visible_height)).max(0) as usize;
    let scroll_offset = scroll_offset.min(max_scroll);

    let footer = if scrollable {
        "\u{2191}\u{2193} scroll \u{00b7} any key to close"
    } else {
        "Press any key to close"
    };

    let content = dialog_box::render(
        buffer,
        screen_width,
        screen_height,
        content_width,
        visible_height,
        Some("Properties"),
        Some(footer),
    );

    let value_max_width = content_width - LABEL_WIDTH;

    // Render visible slice
    for row_index in 0..visible_height as usize {
        let index = scroll_offset + row_index;

        if index >= rows.len() {
            break;
        }

        let row = &rows[index];
        let y = content.top + row_index as i32;

        match row.kind {
            RowKind::Blank => {}
            RowKind::Header => {
                buffer.write_string(y, content.left, &row.value, row.value_style, i64::from(content_width));
            }
            RowKind::LabelValue if !row.label.is_empty() => {
                buffer.write_string(y, content.left, &row.label, row.label_style, i64::from(LABEL_WIDTH));
                buffer.write_string(
                    y,
                    content.left + LABEL_WIDTH,
                    &row.value,
                    row.value_style,
                    i64::from(value_max_width),
                );
            }
            RowKind::LabelValue => {
                buffer.write_string(
                    y,
                    content.left + 2,
                    &row.value,
                    row.value_style,
                    i64::from(value_max_width),
                );
            }
        }
    }

    total_content_height
}

/// Port of `PropertiesOverlay.BuildValues`.
fn build_values(entry: &FileSystemEntry, directory_size_text: Option<&str>, git_status: Option<GitFileStatus>) -> Vec<String> {
    let em_dash = "\u{2014}";

    let type_label = if entry.is_drive {
        "Drive".to_string()
    } else if entry.is_broken_symlink {
        "Broken Symlink".to_string()
    } else if entry.is_app_exec_link {
        "App Execution Alias".to_string()
    } else if entry.is_junction_point {
        "Junction \u{2192} Directory".to_string()
    } else if entry.is_symlink() {
        if entry.is_directory {
            "Symlink \u{2192} Directory".to_string()
        } else {
            "Symlink \u{2192} File".to_string()
        }
    } else if entry.is_directory {
        "Directory".to_string()
    } else {
        get_file_type_label(&entry.full_path).unwrap_or("File").to_string()
    };

    let target = entry
        .app_exec_link_target
        .clone()
        .or_else(|| entry.link_target.clone())
        .unwrap_or_else(|| em_dash.to_string());

    let size = if entry.is_drive && entry.drive_total_size > 0 {
        let free = crate::ui::format_helpers::format_size(&mut ['\0'; 32], entry.drive_free_space);
        let _ = free;
        format_free_of_total(entry)
    } else if entry.is_directory || entry.is_drive {
        directory_size_text.unwrap_or(em_dash).to_string()
    } else {
        format!(
            "{} ({} bytes)",
            format_size_string(entry.size),
            crate::app::group_thousands(entry.size)
        )
    };

    let file_metadata = std::fs::symlink_metadata(&entry.full_path).ok();

    let (created, accessed, mut attributes, read_only) = collect_platform_facts(
        entry,
        file_metadata.as_ref(),
    );

    if entry.is_app_exec_link {
        attributes = attributes.replace("ReparsePoint", "AppExecLink");
    } else if entry.is_junction_point {
        attributes = attributes.replace("ReparsePoint", "Junction");
    } else if entry.is_symlink() {
        attributes = attributes.replace("ReparsePoint", "Symlink");
    }

    if entry.is_cloud_placeholder {
        attributes = if attributes == "Normal" {
            "Cloud file".to_string()
        } else {
            format!("Cloud file, {attributes}")
        };
    }

    let modified = format_date_time(&entry.last_modified);

    vec![
        entry.name.clone(),
        entry.full_path.clone(),
        type_label,
        target,
        size,
        created.unwrap_or_else(|| "N/A".to_string()),
        modified,
        accessed.unwrap_or_else(|| "N/A".to_string()),
        attributes,
        if read_only { "Yes" } else { "No" }.to_string(),
        format_git_status(git_status),
    ]
}

/// Size row for drive entries: `"{free} free of {total} ({N0}% used)"`.
fn format_free_of_total(entry: &FileSystemEntry) -> String {
    let free = format_size_string(entry.drive_free_space);
    let total = format_size_string(entry.drive_total_size);
    let used_percent = 100.0 * (entry.drive_total_size - entry.drive_free_space) as f64 / entry.drive_total_size as f64;
    format!(
        "{free} free of {total} ({:.0}% used)",
        used_percent
    )
}

fn format_size_string(bytes: i64) -> String {
    let mut buf = ['\0'; 32];
    let n = crate::ui::format_helpers::format_size(&mut buf, bytes);
    buf[..n].iter().collect()
}

/// Port of the try/catch fact-gathering in `BuildValues` (dates, attributes,
/// read-only). Returns `(created, accessed, attributes, read_only)`; `None`
/// created/accessed mean the C# catch branch fired ("N/A" filled in later).
fn collect_platform_facts(
    entry: &FileSystemEntry,
    metadata: Option<&std::fs::Metadata>,
) -> (Option<String>, Option<String>, String, bool) {
    let Some(metadata) = metadata else {
        return (None, None, "N/A".to_string(), false);
    };

    if entry.is_drive {
        // Drive entries use volume/format facts from the entry itself.
        let mut parts: Vec<String> = Vec::new();

        // Media-type text needs DriveTypeDetector (Phase 9); C# falls back to
        // DriveType.ToString() which Rust cannot query via std. Omitted until
        // Phase 9 (KNOWN_DEVIATIONS.md).
        if let Some(format) = &entry.drive_format {
            parts.push(format.clone());
        }

        if let Some(label) = &entry.drive_label
            && !label.is_empty()
        {
            parts.push(format!("\"{label}\""));
        }

        (None, None, parts.join(", "), false)
    } else {
        let created = metadata
            .created()
            .ok()
            .map(crate::fs::directory_contents::system_time_to_date_parts)
            .map(|parts| format_date_time(&parts));
        let accessed = metadata
            .accessed()
            .ok()
            .map(crate::fs::directory_contents::system_time_to_date_parts)
            .map(|parts| format_date_time(&parts));
        let attributes = format_attributes(entry, metadata);
        let read_only = metadata.permissions().readonly();

        (created, accessed, attributes, read_only)
    }
}

/// Port of `FormatDateTime`: "yyyy-MM-dd hh:mm tt", 12-hour clock,
/// invariant culture.
#[must_use]
pub fn format_date_time(parts: &crate::ui::format_helpers::DateParts) -> String {
    let (hour12, suffix) = match parts.hour {
        0 => (12, "AM"),
        1..=11 => (parts.hour, "AM"),
        12 => (12, "PM"),
        _ => (parts.hour - 12, "PM"),
    };

    format!(
        "{:04}-{:02}-{:02} {:02}:{:02} {}",
        parts.year, parts.month, parts.day, hour12, parts.minute, suffix
    )
}

/// Port of `FormatAttributes` (+ `FormatWindowsAttributes` /
/// `FormatUnixAttributes`).
#[must_use]
fn format_attributes(entry: &FileSystemEntry, metadata: &std::fs::Metadata) -> String {
    #[cfg(windows)]
    {
        format_windows_attributes(entry, metadata)
    }

    #[cfg(not(windows))]
    {
        format_unix_attributes(entry, metadata)
    }
}

#[cfg(windows)]
fn format_windows_attributes(entry: &FileSystemEntry, metadata: &std::fs::Metadata) -> String {
    use std::os::windows::ffi::OsStrExt;

    use windows_sys::Win32::Storage::FileSystem::{
        GetFileAttributesW, FILE_ATTRIBUTE_ARCHIVE, FILE_ATTRIBUTE_COMPRESSED,
        FILE_ATTRIBUTE_ENCRYPTED, FILE_ATTRIBUTE_HIDDEN, FILE_ATTRIBUTE_READONLY,
        FILE_ATTRIBUTE_REPARSE_POINT, FILE_ATTRIBUTE_SYSTEM,
    };

    let wide: Vec<u16> = std::ffi::OsStr::new(&entry.full_path)
        .encode_wide()
        .chain(std::iter::once(0))
        .collect();
    let attrs = unsafe { GetFileAttributesW(wide.as_ptr()) };

    if attrs == u32::MAX {
        // Query failed: fall back to std-derived facts
        if metadata.permissions().readonly() {
            return "ReadOnly".to_string();
        }

        return "Normal".to_string();
    }

    let mut flags_parts: Vec<&str> = Vec::new();

    if attrs & FILE_ATTRIBUTE_READONLY != 0 {
        flags_parts.push("ReadOnly");
    }

    if attrs & FILE_ATTRIBUTE_HIDDEN != 0 {
        flags_parts.push("Hidden");
    }

    if attrs & FILE_ATTRIBUTE_SYSTEM != 0 {
        flags_parts.push("System");
    }

    if attrs & FILE_ATTRIBUTE_REPARSE_POINT != 0 {
        flags_parts.push("ReparsePoint");
    }

    if attrs & FILE_ATTRIBUTE_ARCHIVE != 0 {
        flags_parts.push("Archive");
    }

    if attrs & FILE_ATTRIBUTE_COMPRESSED != 0 {
        flags_parts.push("Compressed");
    }

    if attrs & FILE_ATTRIBUTE_ENCRYPTED != 0 {
        flags_parts.push("Encrypted");
    }

    if flags_parts.is_empty() {
        "Normal".to_string()
    } else {
        flags_parts.join(", ")
    }
}

#[cfg(not(windows))]
fn format_unix_attributes(entry: &FileSystemEntry, metadata: &std::fs::Metadata) -> String {
    let _ = entry;
    let mut flags_parts: Vec<&str> = Vec::new();

    if metadata.permissions().readonly() {
        flags_parts.push("ReadOnly");
    }

    if flags_parts.is_empty() {
        "Normal".to_string()
    } else {
        flags_parts.join(", ")
    }
}

/// Port of `FormatGitStatus` (internal static for testability in C#).
#[must_use]
pub fn format_git_status(status: Option<GitFileStatus>) -> String {
    let Some(status) = status else {
        return "\u{2014}".to_string();
    };

    if status == GitFileStatus::NONE {
        return "\u{2014}".to_string();
    }

    let mut labels: Vec<&str> = Vec::new();

    if status.intersects(GitFileStatus::CONFLICT) {
        labels.push("Conflict");
    }

    if status.intersects(GitFileStatus::STAGED) {
        labels.push("Staged");
    }

    if status.intersects(GitFileStatus::MODIFIED) {
        labels.push("Modified");
    }

    if status.intersects(GitFileStatus::UNTRACKED) {
        labels.push("Untracked");
    }

    if labels.is_empty() {
        "\u{2014}".to_string()
    } else {
        labels.join(", ")
    }
}

/// Port of `GetGitStatusColor`.
#[must_use]
pub fn get_git_status_color(status: GitFileStatus) -> Color {
    if status.intersects(GitFileStatus::CONFLICT) {
        return git_conflict_color();
    }

    if status.intersects(GitFileStatus::STAGED) {
        return git_staged_color();
    }

    if status.intersects(GitFileStatus::MODIFIED) {
        return git_modified_color();
    }

    git_untracked_color()
}
