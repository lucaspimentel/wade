//! Preview parity: reproduces tests/golden/preview/*.golden.txt, written by
//! tests/Wade.Tests/PreviewGoldenTests.cs, from the fixtures in
//! tests/golden/preview/files.

use std::fmt::Write as _;
use std::path::{Path, PathBuf};

use wade::fs::{file_preview, GitFileStatus};
use wade::highlight::StyledLine;
use wade::input::CancelToken;
use wade::fs::{tar_preview, zip_preview};
use wade::preview::document_metadata::{NuGetMetadataProvider, OfficeMetadataProvider};
use wade::preview::executable_metadata::ExecutableMetadataProvider;
use wade::preview::media_metadata;
use wade::preview::metadata_providers::{ArchiveMetadataProvider, FileMetadataProvider, ShortcutMetadataProvider};
use wade::preview::providers::{HexPreviewProvider, TarContentsPreviewProvider, TextPreviewProvider, ZipContentsPreviewProvider};
use wade::preview::{registry, MetadataEntry, MetadataProvider, MetadataSection, PreviewContext, PreviewProvider, PreviewResult};
use wade::screen::{CellStyle, Color};
use wade::ui::metadata_renderer;

const HEX_HEAD_ROWS: usize = 40;
const HEX_TAIL_ROWS: usize = 3;

/// Providers of later phases: C# lists them, Rust's registries do not yet.
const PENDING_LABELS: &[&str] = &[
    // Preview providers
    "Installer files",
    // Metadata providers
    "MSI metadata",
];

fn golden_dir() -> PathBuf {
    Path::new(env!("CARGO_MANIFEST_DIR")).join("../../tests/golden/preview")
}

fn read(path: &Path) -> String {
    std::fs::read_to_string(path)
        .unwrap_or_else(|e| panic!("read {}: {e}", path.display()))
        .replace("\r\n", "\n")
}

fn escape(text: &str) -> String {
    let mut out = String::new();

    for ch in text.chars() {
        if ch == '\\' {
            out.push_str("\\\\");
        } else if ch < ' ' || ch == '\u{7f}' {
            let _ = write!(out, "\\u{:04x}", ch as u32);
        } else {
            out.push(ch);
        }
    }

    out
}

fn format_color(color: Option<Color>) -> String {
    color.map_or_else(|| "-".to_string(), |c| format!("{},{},{}", c.r, c.g, c.b))
}

fn format_style(style: &CellStyle) -> String {
    let mut flags: String = [
        (style.bold, 'b'),
        (style.dim, 'd'),
        (style.inverse, 'i'),
        (style.underline, 'u'),
        (style.strikethrough, 's'),
    ]
    .iter()
    .filter(|(on, _)| *on)
    .map(|(_, flag)| *flag)
    .collect();

    if flags.is_empty() {
        flags.push('-');
    }

    format!("{}/{}/{flags}", format_color(style.fg), format_color(style.bg))
}

/// Same format as the highlight golden; positions mapped to UTF-16 units.
fn append_styled_line(out: &mut String, line: &StyledLine) {
    let _ = writeln!(out, "text {}", escape(&line.text));
    let mut offsets = vec![0];
    for ch in line.text.chars() {
        offsets.push(offsets.last().unwrap() + ch.len_utf16());
    }

    if let Some(spans) = &line.spans {
        out.push_str("spans");

        for span in spans {
            let start = offsets[span.start.min(offsets.len() - 1)];
            let end = offsets[(span.start + span.len).min(offsets.len() - 1)];
            let _ = write!(out, " {start}+{}:{:?}", end - start, span.kind);
        }

        out.push('\n');
    }

    if let Some(styles) = &line.char_styles {
        let styles: Vec<CellStyle> = line
            .text
            .chars()
            .zip(styles)
            .flat_map(|(ch, style)| std::iter::repeat_n(*style, ch.len_utf16()))
            .collect();
        out.push_str("chars");
        let mut i = 0;

        while i < styles.len() {
            let mut j = i + 1;

            while j < styles.len() && styles[j] == styles[i] {
                j += 1;
            }

            let _ = write!(out, " {i}+{}:{}", j - i, format_style(&styles[i]));
            i = j;
        }

        out.push('\n');
    }
}

fn append_result(out: &mut String, name: &str, result: Option<PreviewResult>, head_rows: usize) {
    let Some(result) = result else {
        let _ = writeln!(out, "{name} null");
        return;
    };

    let lines = result.text_lines.unwrap_or_default();
    let _ = writeln!(
        out,
        "{name} label={} rendered={} placeholder={} lines={}",
        result.file_type_label.as_deref().unwrap_or("-"),
        result.is_rendered,
        result.is_placeholder,
        lines.len()
    );

    for (i, line) in lines.iter().enumerate() {
        if i >= head_rows && i + HEX_TAIL_ROWS < lines.len() {
            continue;
        }

        append_styled_line(out, line);
    }
}

fn contexts(repo_root: &str) -> Vec<(&'static str, PreviewContext)> {
    let base = PreviewContext {
        pane_width_cells: 80,
        pane_height_cells: 24,
        cell_pixel_width: 8,
        cell_pixel_height: 16,
        is_cloud_placeholder: false,
        is_broken_symlink: false,
        git_status: None,
        repo_root: None,
        pdf_preview_enabled: true,
        pdf_metadata_enabled: true,
        markdown_preview_enabled: true,
        ffprobe_enabled: true,
        mediainfo_enabled: true,
        zip_preview_enabled: true,
        image_previews_enabled: false,
        sixel_supported: false,
        archive_metadata_enabled: true,
    };

    vec![
        ("default", base.clone()),
        (
            "git-modified",
            PreviewContext {
                git_status: Some(GitFileStatus::MODIFIED),
                repo_root: Some(repo_root.to_string()),
                ..base.clone()
            },
        ),
        (
            "cloud",
            PreviewContext {
                is_cloud_placeholder: true,
                ..base.clone()
            },
        ),
        (
            "archive-metadata-off",
            PreviewContext {
                archive_metadata_enabled: false,
                zip_preview_enabled: false,
                ..base
            },
        ),
    ]
}

/// C#'s provider list with not-yet-ported labels removed.
fn without_pending(line: &str) -> String {
    let Some((prefix, list)) = line.split_once("] ") else {
        return line.to_string();
    };

    let kept: Vec<&str> = list
        .split(" | ")
        .filter(|label| !label.is_empty() && !PENDING_LABELS.contains(label))
        .collect();
    format!("{prefix}] {}", kept.join(" | "))
}

fn assert_matches(expected: &str, actual: &str, golden: &str) {
    for (number, (expected_line, actual_line)) in expected.lines().zip(actual.lines()).enumerate() {
        assert_eq!(actual_line, expected_line, "{golden} line {}", number + 1);
    }

    assert_eq!(actual.lines().count(), expected.lines().count(), "{golden}");
}

#[test]
fn preview_fixtures_match_csharp_golden() {
    let dir = golden_dir();
    let repo_root = dir.to_string_lossy().into_owned();
    let mut files: Vec<PathBuf> = std::fs::read_dir(dir.join("files"))
        .unwrap()
        .map(|entry| entry.unwrap().path())
        .collect();
    files.sort_by(|a, b| a.file_name().cmp(&b.file_name()));

    let mut out = String::new();
    let cancel = CancelToken::new();

    for file in &files {
        let path = file.to_string_lossy().into_owned();
        let name = file.file_name().unwrap().to_string_lossy().into_owned();
        let _ = writeln!(out, "=== {name}");

        let metadata = file_preview::detect_file_metadata(&path);
        let _ = writeln!(
            out,
            "metadata binary={} encoding={} line-ending={}",
            metadata.is_binary,
            if metadata.encoding.is_empty() { "-" } else { &metadata.encoding },
            metadata.line_ending.as_deref().unwrap_or("-")
        );
        let _ = writeln!(out, "label {}", file_preview::get_file_type_label(&path).unwrap_or("-"));

        let (lines, preview_metadata) = file_preview::get_preview_lines(&path);
        let _ = writeln!(
            out,
            "lines {} placeholder={}",
            lines.len(),
            preview_metadata.placeholder_message.as_deref().unwrap_or("-")
        );
        for line in &lines {
            let _ = writeln!(out, "  {}", escape(line));
        }

        let contexts = contexts(&repo_root);

        for (context_name, context) in &contexts {
            let previews: Vec<&str> = registry::applicable_preview_providers(&path, context)
                .iter()
                .map(|provider| provider.label())
                .collect();
            let metadata: Vec<&str> = registry::applicable_metadata_providers(&path, context)
                .iter()
                .map(|provider| provider.label())
                .collect();
            let _ = writeln!(out, "preview-providers[{context_name}] {}", previews.join(" | "));
            let _ = writeln!(out, "metadata-providers[{context_name}] {}", metadata.join(" | "));
        }

        let default_context = &contexts[0].1;

        if TextPreviewProvider.can_preview(&path, default_context) {
            append_result(&mut out, "text", TextPreviewProvider.get_preview(&path, default_context, &cancel), usize::MAX);
        }

        append_result(&mut out, "hex", HexPreviewProvider.get_preview(&path, default_context, &cancel), HEX_HEAD_ROWS);

        if let Some(info) = FileMetadataProvider.get_metadata(&path, default_context, &cancel) {
            out.push_str("file-metadata\n");
            for line in metadata_renderer::render(&info.sections, 40) {
                append_styled_line(&mut out, &line);
            }
        }
    }

    let golden_path = dir.join("preview.golden.txt");
    let expected: String = read(&golden_path)
        .lines()
        .map(|line| {
            if line.starts_with("preview-providers[") || line.starts_with("metadata-providers[") {
                without_pending(line)
            } else {
                line.to_string()
            }
        })
        .collect::<Vec<_>>()
        .join("\n");

    assert_matches(&expected, &out, &golden_path.display().to_string());
}

#[test]
fn metadata_renderer_cases_match_csharp_golden() {
    let sections = vec![
        MetadataSection {
            header: Some("photo.jpg".to_string()),
            entries: vec![MetadataEntry::new("Size", "1.2 MB"), MetadataEntry::new("Git", "Modified")],
        },
        MetadataSection {
            header: None,
            entries: vec![
                MetadataEntry::new("Resolution", "4000 x 3000"),
                MetadataEntry::new("", "list item value"),
            ],
        },
        MetadataSection {
            header: Some("Empty section".to_string()),
            entries: Vec::new(),
        },
        MetadataSection {
            header: Some("A much longer section header than the pane".to_string()),
            entries: vec![MetadataEntry::new("K", "V")],
        },
    ];

    let mut out = String::new();

    for width in [0, 3, 4, 12, 40] {
        let _ = writeln!(out, "=== width {width}");
        for line in metadata_renderer::render(&sections, width) {
            append_styled_line(&mut out, &line);
        }
    }

    let golden_path = golden_dir().join("metadata-renderer.golden.txt");
    assert_matches(&read(&golden_path), &out, &golden_path.display().to_string());
}

/// The P0 ratio cases of PreviewGoldenTests.RatioCases.
const RATIO_CASES: &[(i64, i64)] = &[
    (0, 5),
    (1, 8),
    (3, 8),
    (5, 8),
    (7, 8),
    (1, 200),
    (3, 200),
    (1, 3),
    (2, 3),
    (5, 5),
    (7, 5),
    (1, 1000),
    (5, 1000),
    (15, 1000),
    (25, 1000),
    (1005, 100_000),
    (99995, 100_000),
    (123_456_789, 1_000_000),
    (4999, 1_000_000),
    (5, 1_000_000_000),
    (i64::MAX / 3, i64::MAX / 7),
];

#[test]
fn archive_fixtures_match_csharp_golden() {
    let dir = golden_dir();
    let mut files: Vec<PathBuf> = std::fs::read_dir(dir.join("archives"))
        .unwrap()
        .map(|entry| entry.unwrap().path())
        .collect();
    files.sort_by(|a, b| a.file_name().cmp(&b.file_name()));

    let mut out = String::new();
    let cancel = CancelToken::new();
    let all_contexts = contexts(&dir.to_string_lossy());

    for file in &files {
        let path = file.to_string_lossy().into_owned();
        let _ = writeln!(out, "=== {}", file.file_name().unwrap().to_string_lossy());
        let _ = writeln!(
            out,
            "kind zip={} primary={} tar={} gzip={}",
            zip_preview::is_zip_file(&path),
            zip_preview::is_primary_archive(&path),
            tar_preview::is_tar_archive(&path),
            tar_preview::is_plain_gzip(&path)
        );

        for (context_name, context) in all_contexts.iter().filter(|(name, _)| matches!(*name, "default" | "archive-metadata-off")) {
            let previews: Vec<&str> = registry::applicable_preview_providers(&path, context).iter().map(|p| p.label()).collect();
            let metadata: Vec<&str> = registry::applicable_metadata_providers(&path, context).iter().map(|p| p.label()).collect();
            let _ = writeln!(out, "preview-providers[{context_name}] {}", previews.join(" | "));
            let _ = writeln!(out, "metadata-providers[{context_name}] {}", metadata.join(" | "));
        }

        let default_context = &all_contexts[0].1;
        let archive_providers: [&dyn PreviewProvider; 2] = [&ZipContentsPreviewProvider, &TarContentsPreviewProvider];

        for provider in archive_providers {
            if provider.can_preview(&path, default_context) {
                append_result(&mut out, "archive", provider.get_preview(&path, default_context, &cancel), usize::MAX);
            }
        }

        if ArchiveMetadataProvider.can_provide_metadata(&path, default_context) {
            match ArchiveMetadataProvider.get_metadata(&path, default_context, &cancel) {
                None => out.push_str("archive-metadata null\n"),
                Some(result) => {
                    let _ = writeln!(out, "archive-metadata label={}", result.file_type_label.as_deref().unwrap_or("-"));
                    for section in &result.sections {
                        let _ = writeln!(out, "  [{}]", section.header.as_deref().unwrap_or("-"));
                        for entry in &section.entries {
                            let _ = writeln!(out, "  {}: {}", entry.label, entry.value);
                        }
                    }
                }
            }
        }
    }

    out.push_str("=== ratios\n");
    for &(compressed, total) in RATIO_CASES {
        let _ = writeln!(
            out,
            "{compressed}/{total} {}",
            wade::ui::format_helpers::format_percent_p0(compressed as f64 / total as f64)
        );
    }

    let golden_path = dir.join("archives.golden.txt");
    let expected: String = read(&golden_path)
        .lines()
        .map(|line| {
            if line.starts_with("preview-providers[") || line.starts_with("metadata-providers[") {
                without_pending(line)
            } else {
                line.to_string()
            }
        })
        .collect::<Vec<_>>()
        .join("\n");

    assert_matches(&expected, &out, &golden_path.display().to_string());
}

#[test]
fn shortcut_fixtures_match_csharp_golden() {
    let dir = golden_dir();
    let mut files: Vec<PathBuf> = std::fs::read_dir(dir.join("shortcuts"))
        .unwrap()
        .map(|entry| entry.unwrap().path())
        .collect();
    files.sort_by(|a, b| a.file_name().cmp(&b.file_name()));

    let mut out = String::new();
    let context = &contexts(&dir.to_string_lossy())[0].1;

    for file in &files {
        let path = file.to_string_lossy().into_owned();
        let _ = writeln!(out, "=== {}", file.file_name().unwrap().to_string_lossy());
        let metadata: Vec<&str> = registry::applicable_metadata_providers(&path, context).iter().map(|p| p.label()).collect();
        let _ = writeln!(out, "metadata-providers {}", metadata.join(" | "));

        if !ShortcutMetadataProvider.can_provide_metadata(&path, context) {
            continue;
        }

        let Some(result) = ShortcutMetadataProvider.get_metadata(&path, context, &CancelToken::new()) else {
            out.push_str("shortcut null\n");
            continue;
        };

        let _ = writeln!(out, "shortcut label={}", result.file_type_label.as_deref().unwrap_or("-"));
        for section in &result.sections {
            let _ = writeln!(out, "  [{}]", section.header.as_deref().unwrap_or("-"));
            for entry in &section.entries {
                let _ = writeln!(out, "  {}: {}", entry.label, escape(&entry.value));
            }
        }
    }

    out.push_str("=== hotkeys\n");
    for hot_key in [0x0000u16, 0x0030, 0x0139, 0x0241, 0x045A, 0x0370, 0x0587, 0x0690, 0x0791, 0x0820, 0x00FF, 0xFF41] {
        let _ = writeln!(out, "{hot_key:04X} {}", wade::fs::lnk::decode_hot_key(hot_key));
    }

    let golden_path = dir.join("shortcuts.golden.txt");
    let expected: String = read(&golden_path)
        .lines()
        .map(|line| match line.strip_prefix("metadata-providers ") {
            Some(list) => without_pending(&format!("metadata-providers[x] {list}")).replacen("metadata-providers[x] ", "metadata-providers ", 1),
            None => line.to_string(),
        })
        .collect::<Vec<_>>()
        .join("\n");

    assert_matches(&expected, &out, &golden_path.display().to_string());
}

#[test]
fn executable_fixtures_match_csharp_golden() {
    let dir = golden_dir();
    let mut files: Vec<PathBuf> = std::fs::read_dir(dir.join("executables"))
        .unwrap()
        .map(|entry| entry.unwrap().path())
        .collect();
    files.sort_by(|a, b| a.file_name().cmp(&b.file_name()));

    let mut out = String::new();
    let context = &contexts(&dir.to_string_lossy())[0].1;

    for file in &files {
        let path = file.to_string_lossy().into_owned();
        let _ = writeln!(out, "=== {}", file.file_name().unwrap().to_string_lossy());
        let metadata: Vec<&str> = registry::applicable_metadata_providers(&path, context).iter().map(|p| p.label()).collect();
        let _ = writeln!(out, "metadata-providers {}", metadata.join(" | "));

        if !ExecutableMetadataProvider.can_provide_metadata(&path, context) {
            continue;
        }

        let Some(result) = ExecutableMetadataProvider.get_metadata(&path, context, &CancelToken::new()) else {
            out.push_str("executable null\n");
            continue;
        };

        let _ = writeln!(out, "executable label={}", result.file_type_label.as_deref().unwrap_or("-"));
        for section in &result.sections {
            let _ = writeln!(out, "  [{}]", section.header.as_deref().unwrap_or("-"));
            for entry in &section.entries {
                let _ = writeln!(out, "  {}: {}", entry.label, escape(&entry.value));
            }
        }
    }

    assert_eq!(out, read(&dir.join("executables.golden.txt")));
}

fn append_metadata(out: &mut String, name: &str, result: Option<&wade::preview::MetadataResult>) {
    let Some(result) = result else {
        let _ = writeln!(out, "{name} null");
        return;
    };

    let _ = writeln!(out, "{name} label={}", result.file_type_label.as_deref().unwrap_or("-"));
    for section in &result.sections {
        let _ = writeln!(out, "  [{}]", section.header.as_deref().unwrap_or("-"));
        for entry in &section.entries {
            let _ = writeln!(out, "  {}: {}", entry.label, escape(&entry.value));
        }
    }
}

fn sorted_files(dir: &Path) -> Vec<PathBuf> {
    let mut files: Vec<PathBuf> = std::fs::read_dir(dir).unwrap().map(|entry| entry.unwrap().path()).collect();
    files.sort_by(|a, b| a.file_name().cmp(&b.file_name()));
    files
}

#[test]
fn document_fixtures_match_csharp_golden() {
    let dir = golden_dir();
    let context = &contexts(&dir.to_string_lossy())[0].1;
    let providers: [(&str, &dyn MetadataProvider); 2] = [("office", &OfficeMetadataProvider), ("nuget", &NuGetMetadataProvider)];
    let mut out = String::new();

    for file in sorted_files(&dir.join("documents")) {
        let path = file.to_string_lossy().into_owned();
        let _ = writeln!(out, "=== {}", file.file_name().unwrap().to_string_lossy());
        let metadata: Vec<&str> = registry::applicable_metadata_providers(&path, context).iter().map(|p| p.label()).collect();
        let _ = writeln!(out, "metadata-providers {}", metadata.join(" | "));

        for (name, provider) in providers {
            if provider.can_provide_metadata(&path, context) {
                append_metadata(&mut out, name, provider.get_metadata(&path, context, &CancelToken::new()).as_ref());
            }
        }
    }

    assert_eq!(out, read(&dir.join("documents.golden.txt")));
}

#[test]
fn media_json_fixtures_match_csharp_golden() {
    let dir = golden_dir();
    let mut out = String::new();

    for file in sorted_files(&dir.join("media")) {
        let name = file.file_name().unwrap().to_string_lossy().into_owned();
        let json = std::fs::read_to_string(&file).unwrap();
        let _ = writeln!(out, "=== {name}");
        let parsed = if name.starts_with("ffprobe") {
            media_metadata::parse_ffprobe_json(&json)
        } else {
            media_metadata::parse_mediainfo_json(&json)
        };

        match parsed {
            Ok(sections) => {
                let result = sections.map(|sections| wade::preview::MetadataResult { sections, file_type_label: None });
                append_metadata(&mut out, "media", result.as_ref());
            }
            Err(_) => out.push_str("media error\n"),
        }
    }

    assert_eq!(out, read(&dir.join("media.golden.txt")));
}
