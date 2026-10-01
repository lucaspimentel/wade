//! Preview parity: reproduces tests/golden/preview/*.golden.txt, written by
//! tests/Wade.Tests/PreviewGoldenTests.cs, from the fixtures in
//! tests/golden/preview/files.

use std::fmt::Write as _;
use std::path::{Path, PathBuf};

use wade::fs::{file_preview, GitFileStatus};
use wade::highlight::StyledLine;
use wade::input::CancelToken;
use wade::preview::metadata_providers::FileMetadataProvider;
use wade::preview::providers::{HexPreviewProvider, TextPreviewProvider};
use wade::preview::{registry, MetadataEntry, MetadataProvider, MetadataSection, PreviewContext, PreviewProvider, PreviewResult};
use wade::screen::{CellStyle, Color};
use wade::ui::metadata_renderer;

const HEX_HEAD_ROWS: usize = 40;
const HEX_TAIL_ROWS: usize = 3;

/// Providers of later phases: C# lists them, Rust's registries do not yet.
const PENDING_LABELS: &[&str] = &[
    // Preview providers
    "Image",
    "PDF",
    "Rendered markdown (built-in)",
    "Archive contents",
    "Installer files",
    // Metadata providers
    "Executable metadata",
    "Document metadata",
    "Media info",
    "NuGet metadata",
    "MSI metadata",
    "Shortcut properties",
    "Archive metadata",
    "PDF metadata",
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
