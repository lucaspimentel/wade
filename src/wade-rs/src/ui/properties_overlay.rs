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
    metadata_sections: Option<&[crate::preview::MetadataSection]>,
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

        let em_dash = "\u{2014}".to_string();
        (Some(em_dash.clone()), Some(em_dash), parts.join(", "), false)
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
    let mut flags_parts: Vec<&str> = Vec::new();

    if metadata.permissions().readonly() {
        flags_parts.push("ReadOnly");
    }

    // .NET reports FileAttributes.Hidden on Unix for dot-prefixed names
    if std::path::Path::new(&entry.full_path)
        .file_name()
        .and_then(|name| name.to_str())
        .is_some_and(|name| name.starts_with('.'))
    {
        flags_parts.push("Hidden");
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

#[cfg(test)]
mod tests {
    use super::{format_git_status, get_git_status_color, git_staged_color, render};
    use crate::fs::directory_contents::{FileSystemEntry, GitFileStatus};
    use crate::screen::ScreenBuffer;
    use crate::ui::format_helpers::DateParts;
    use crate::preview::{MetadataEntry, MetadataSection};

    fn entry(name: &str, full_path: &str, is_directory: bool, size: i64) -> FileSystemEntry {
        FileSystemEntry {
            name: name.to_string(),
            full_path: full_path.to_string(),
            is_directory,
            size,
            last_modified: DateParts { year: 2024, month: 1, day: 2, hour: 15, minute: 4 },
            link_target: None,
            is_broken_symlink: false,
            is_drive: false,
            is_cloud_placeholder: false,
            is_junction_point: false,
            is_app_exec_link: false,
            app_exec_link_target: None,
            drive_format: None,
            drive_label: None,
            drive_free_space: 0,
            drive_total_size: 0,
        }
    }

    fn missing(name: &str) -> String {
        std::env::temp_dir()
            .join("wade-po-missing")
            .join(name)
            .to_string_lossy()
            .into_owned()
    }

    fn test_root(name: &str) -> std::path::PathBuf {
        let dir = std::env::temp_dir().join(format!("wade-po-{}-{}", std::process::id(), name));
        let _ = std::fs::remove_dir_all(&dir);
        std::fs::create_dir_all(&dir).unwrap();
        dir
    }

    /// Serializes the buffer and strips escape sequences, like the C#
    /// tests' `Flush` + `StripAnsi`.
    fn flush(buffer: &mut ScreenBuffer) -> String {
        let mut raw = String::new();
        buffer.serialize(&mut raw);

        let mut out = String::new();
        let mut chars = raw.chars();

        while let Some(ch) = chars.next() {
            if ch == '\x1b' {
                if chars.next() == Some('[') {
                    for c in chars.by_ref() {
                        if c.is_ascii_alphabetic() {
                            break;
                        }
                    }
                }
            } else {
                out.push(ch);
            }
        }

        out
    }

    fn render_text(width: i32, height: i32, entry: &FileSystemEntry, git_status: Option<GitFileStatus>) -> String {
        let mut buffer = ScreenBuffer::new(width, height);
        render(&mut buffer, width, height, entry, None, git_status, None, 0);
        flush(&mut buffer)
    }

    fn details(count: usize, label: impl Fn(usize) -> String, value: impl Fn(usize) -> String) -> Vec<MetadataSection> {
        vec![MetadataSection {
            header: Some("Details".to_string()),
            entries: (0..count)
                .map(|i| MetadataEntry { label: label(i), value: value(i) })
                .collect(),
        }]
    }

    #[test]
    fn shows_labels_and_title() {
        let output = render_text(100, 30, &entry("test.txt", &missing("test.txt"), false, 1536), None);

        for expected in [
            "Properties",
            "Press any key to close",
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
        ] {
            assert!(output.contains(expected), "missing {expected:?}");
        }
    }

    #[test]
    fn file_entry_shows_file_type_label() {
        for (file_name, expected_type) in [
            ("readme.md", "Markdown"),
            ("report.pdf", "PDF"),
            ("app.cs", "C#"),
            ("data.unknown", "File"),
        ] {
            let output = render_text(100, 30, &entry(file_name, &missing(file_name), false, 2048), None);
            assert!(output.contains(expected_type), "{file_name}: missing {expected_type:?}");
            assert!(output.contains(file_name));
        }
    }

    #[test]
    fn directory_entry_shows_dash_for_size() {
        let output = render_text(100, 30, &entry("docs", &missing("docs"), true, 0), None);
        assert!(output.contains("Directory"));
        assert!(output.contains('\u{2014}'));
    }

    #[test]
    fn directory_entry_shows_directory_size_text() {
        let mut buffer = ScreenBuffer::new(100, 30);
        render(&mut buffer, 100, 30, &entry("docs", &missing("docs"), true, 0), Some("Calculating\u{2026}"), None, None, 0);
        assert!(flush(&mut buffer).contains("Calculating\u{2026}"));
    }

    #[test]
    fn drive_entry_shows_drive_type() {
        let mut drive = entry("C:\\", "C:\\", true, 0);
        drive.is_drive = true;
        let output = render_text(100, 30, &drive, None);
        assert!(output.contains("Drive"));
    }

    #[test]
    fn ready_drive_shows_dash_for_dates_and_free_of_total() {
        // C# sets Created/Accessed to an em dash for drives (not "N/A")
        let root = test_root("drive");
        let path = root.to_string_lossy().into_owned();
        let mut drive = entry(&path, &path, true, 0);
        drive.is_drive = true;
        drive.drive_format = Some("NTFS".to_string());
        drive.drive_label = Some("Data".to_string());
        drive.drive_free_space = 512 * 1024 * 1024;
        drive.drive_total_size = 1024 * 1024 * 1024;

        let output = render_text(120, 30, &drive, None);
        let _ = std::fs::remove_dir_all(&root);

        assert!(output.contains("512.0 MB free of 1.0 GB (50% used)"), "{output}");
        assert!(output.contains("NTFS, \"Data\""));
        assert!(!output.contains("N/A"));
    }

    #[test]
    fn file_entry_shows_formatted_size() {
        for (bytes, expected) in [(512, "512 B"), (1536, "1.5 KB"), (1_572_864, "1.5 MB")] {
            let output = render_text(120, 30, &entry("file.dat", &missing("file.dat"), false, bytes), None);
            assert!(output.contains(expected), "{bytes}: missing {expected:?}");
            assert!(output.contains("bytes"));
        }
    }

    #[test]
    fn symlink_to_file_shows_symlink_type() {
        let root = test_root("symfile");
        let target = root.join("real.txt");
        std::fs::write(&target, "test").unwrap();
        let target = target.to_string_lossy().into_owned();
        let mut link = entry("link.txt", &root.join("link.txt").to_string_lossy(), false, 100);
        link.link_target = Some(target.clone());

        let output = render_text(120, 30, &link, None);
        let _ = std::fs::remove_dir_all(&root);

        assert!(output.contains("Symlink \u{2192} File"));
        assert!(output.contains(&target));
    }

    #[test]
    fn symlink_to_directory_shows_symlink_type() {
        let root = test_root("symdir");
        let target = root.join("real-dir");
        std::fs::create_dir_all(&target).unwrap();
        let target = target.to_string_lossy().into_owned();
        let mut link = entry("link-dir", &root.join("link-dir").to_string_lossy(), true, 0);
        link.link_target = Some(target.clone());

        let output = render_text(120, 30, &link, None);
        let _ = std::fs::remove_dir_all(&root);

        assert!(output.contains("Symlink \u{2192} Directory"));
        assert!(output.contains(&target));
    }

    #[test]
    fn broken_symlink_shows_broken_type() {
        let target = missing("nonexistent_target_xyz");
        let mut link = entry("broken", &missing("broken"), false, 0);
        link.link_target = Some(target.clone());
        link.is_broken_symlink = true;

        let output = render_text(120, 30, &link, None);
        assert!(output.contains("Broken Symlink"));
        assert!(output.contains(&target));
    }

    #[test]
    fn junction_point_shows_junction_type() {
        let target = missing("real-dir");
        let mut junction = entry("junction-dir", &missing("junction-dir"), true, 0);
        junction.link_target = Some(target.clone());
        junction.is_junction_point = true;

        let output = render_text(120, 30, &junction, None);
        assert!(output.contains("Junction \u{2192} Directory"));
        assert!(output.contains(&target));
    }

    #[test]
    fn app_exec_link_shows_app_exec_type() {
        let target = "C:\\Program Files\\WindowsApps\\Microsoft.WindowsTerminal\\wt.exe";
        let mut alias = entry("wt.exe", "C:\\Users\\test\\AppData\\Local\\Microsoft\\WindowsApps\\wt.exe", false, 0);
        alias.is_app_exec_link = true;
        alias.app_exec_link_target = Some(target.to_string());

        let output = render_text(120, 30, &alias, None);
        assert!(output.contains("App Execution Alias"));
        assert!(output.contains(target));
    }

    #[test]
    fn regular_file_shows_em_dash_for_target() {
        let output = render_text(120, 30, &entry("normal.dat", &missing("normal.dat"), false, 512), None);
        assert!(output.contains("File"));
        assert!(output.contains('\u{2014}'));
    }

    #[test]
    fn missing_path_falls_back_to_na() {
        let output = render_text(120, 30, &entry("gone.txt", &missing("gone.txt"), false, 1), None);
        assert!(output.contains("N/A"));
    }

    #[cfg(not(windows))]
    #[test]
    fn unix_dot_file_reports_hidden() {
        let root = test_root("hidden");
        let path = root.join(".env");
        std::fs::write(&path, "x").unwrap();
        let output = render_text(120, 30, &entry(".env", &path.to_string_lossy(), false, 1), None);
        let _ = std::fs::remove_dir_all(&root);
        assert!(output.contains("Hidden"), "{output}");
    }

    #[test]
    fn shows_git_status_modified() {
        let output = render_text(120, 30, &entry("file.cs", &missing("file.cs"), false, 100), Some(GitFileStatus::MODIFIED));
        assert!(output.contains("Git status"));
        assert!(output.contains("Modified"));
    }

    #[test]
    fn shows_git_status_staged() {
        let output = render_text(120, 30, &entry("file.cs", &missing("file.cs"), false, 100), Some(GitFileStatus::STAGED));
        assert!(output.contains("Git status"));
        assert!(output.contains("Staged"));
    }

    #[test]
    fn git_status_combined_flags_shows_comma_separated() {
        let status = GitFileStatus::MODIFIED | GitFileStatus::STAGED;
        let output = render_text(120, 30, &entry("file.cs", &missing("file.cs"), false, 100), Some(status));
        assert!(output.contains("Staged, Modified"));
        // Staged outranks Modified for the row color
        assert!(get_git_status_color(status) == git_staged_color());
    }

    #[test]
    fn format_git_status_none_returns_em_dash() {
        assert_eq!(format_git_status(Some(GitFileStatus::NONE)), "\u{2014}");
    }

    #[test]
    fn format_git_status_null_returns_em_dash() {
        assert_eq!(format_git_status(None), "\u{2014}");
    }

    #[test]
    fn returns_content_height() {
        let mut buffer = ScreenBuffer::new(100, 40);
        let height = render(&mut buffer, 100, 40, &entry("test.txt", &missing("test.txt"), false, 1024), None, None, None, 0);
        // 11 system property rows (LABELS), no metadata
        assert_eq!(height, 11);
    }

    #[test]
    fn returns_content_height_with_metadata() {
        let mut buffer = ScreenBuffer::new(100, 50);
        let sections = vec![MetadataSection {
            header: Some("Info".to_string()),
            entries: vec![
                MetadataEntry { label: "Key1".to_string(), value: "Value1".to_string() },
                MetadataEntry { label: "Key2".to_string(), value: "Value2".to_string() },
            ],
        }];
        let height = render(
            &mut buffer,
            100,
            50,
            &entry("test.txt", &missing("test.txt"), false, 1024),
            None,
            None,
            Some(&sections),
            0,
        );
        // 11 system rows + 1 blank separator + 1 header + 2 entries
        assert_eq!(height, 15);
    }

    #[test]
    fn no_scroll_when_content_fits() {
        let output = render_text(100, 40, &entry("test.txt", &missing("test.txt"), false, 1024), None);
        assert!(output.contains("Press any key to close"));
        assert!(!output.contains("scroll"));
    }

    #[test]
    fn scrollable_footer_when_content_overflows() {
        let mut buffer = ScreenBuffer::new(100, 20);
        let sections = details(20, |i| format!("Field{i}"), |i| format!("Val{i}"));
        render(&mut buffer, 100, 20, &entry("test.txt", &missing("test.txt"), false, 1024), None, None, Some(&sections), 0);
        assert!(flush(&mut buffer).contains("scroll"));
    }

    #[test]
    fn scroll_offset_skips_top_rows() {
        let mut buffer = ScreenBuffer::new(100, 20);
        let sections = details(20, |i| format!("Field{i:02}"), |i| format!("MetaValue{i:02}"));
        render(&mut buffer, 100, 20, &entry("test.txt", &missing("test.txt"), false, 1024), None, None, Some(&sections), 5);
        let output = flush(&mut buffer);
        assert!(output.contains("Properties"));
        assert!(output.contains("MetaValue"));
        // Rows 0-4 (Name..Size) are scrolled off; the Name value is gone
        assert!(!output.contains("test.txt"));
    }

    #[test]
    fn scroll_offset_is_clamped() {
        let mut buffer = ScreenBuffer::new(100, 20);
        let sections = details(20, |i| format!("Field{i:02}"), |i| format!("MetaValue{i:02}"));
        render(&mut buffer, 100, 20, &entry("test.txt", &missing("test.txt"), false, 1024), None, None, Some(&sections), 999);
        // 34 rows, 12 visible: the clamped offset still shows the last entry
        assert!(flush(&mut buffer).contains("MetaValue19"));
    }
}
