//! Port of `src/Wade/Preview`: preview and metadata providers, their
//! registries, and the shared result types.
//!
//! Providers for formats that land in later phases (images and PDF:
//! Phase 8; MSI, executables, Office, NuGet, media: Phase 9) are absent
//! from the registries (KNOWN_DEVIATIONS.md).

pub mod cli_tool_hints;
pub mod document_metadata;
pub mod executable_metadata;
pub mod image_metadata;
pub mod markdown;
pub mod media_metadata;
pub mod metadata_providers;
pub mod msi;
pub mod pe;
pub mod providers;
pub mod registry;
pub mod text_helper;

use crate::fs::GitFileStatus;
use crate::highlight::StyledLine;
use crate::imaging::{ImageData, ImageProtocol};
use crate::input::CancelToken;

/// Port of `PreviewContext`: pane size, file facts and the config flags
/// providers consult.
#[derive(Clone, Debug, PartialEq, Eq)]
pub struct PreviewContext {
    pub pane_width_cells: i32,
    pub pane_height_cells: i32,
    pub cell_pixel_width: i32,
    pub cell_pixel_height: i32,
    pub is_cloud_placeholder: bool,
    pub is_broken_symlink: bool,
    pub git_status: Option<GitFileStatus>,
    pub repo_root: Option<String>,
    pub pdf_preview_enabled: bool,
    pub pdf_metadata_enabled: bool,
    pub markdown_preview_enabled: bool,
    pub ffprobe_enabled: bool,
    pub mediainfo_enabled: bool,
    pub zip_preview_enabled: bool,
    pub image_previews_enabled: bool,
    /// The image protocol in use, if any (independent of
    /// `image_previews_enabled`, which gates image files only; PDF previews
    /// need just a protocol).
    pub image_protocol: Option<ImageProtocol>,
    pub archive_metadata_enabled: bool,
    /// How much text and how many archive entries to read (Rust only).
    pub limits: PreviewLimits,
}

/// How much a preview reads (Rust only; C# reads 100 lines or entries).
/// The right pane reads its own height; the full-screen preview reads
/// `preview_max_lines` and `preview_max_bytes` and marks a cut-off text.
#[derive(Clone, Copy, Debug, PartialEq, Eq)]
pub struct PreviewLimits {
    /// Text lines, archive entries and gzip text lines.
    pub lines: usize,
    /// Bytes read from a text file or a gzip payload.
    pub bytes: u64,
    /// Append a dim last line to text that was cut off.
    pub mark_truncation: bool,
}

impl PreviewLimits {
    pub const DEFAULT_MAX_LINES: usize = 10_000;
    pub const DEFAULT_MAX_BYTES: u64 = 4 * 1024 * 1024;
    pub const MIN_MAX_LINES: usize = 100;
    pub const MIN_MAX_BYTES: u64 = 64 * 1024;

    /// C#'s 100 lines or entries, without a marker (tests and goldens).
    pub const CSHARP: Self = Self {
        lines: 100,
        bytes: Self::DEFAULT_MAX_BYTES,
        mark_truncation: false,
    };

    /// The limits for a pane `height` rows tall (at least one line).
    #[must_use]
    pub fn for_pane(height: i32, max_bytes: u64) -> Self {
        Self {
            lines: usize::try_from(height).unwrap_or(0).max(1),
            bytes: max_bytes,
            mark_truncation: false,
        }
    }

    /// The full-screen limits from the config.
    #[must_use]
    pub const fn full_screen(max_lines: usize, max_bytes: u64) -> Self {
        Self {
            lines: max_lines,
            bytes: max_bytes,
            mark_truncation: true,
        }
    }
}

/// Where a text preview was cut off.
#[derive(Clone, Copy, Debug, PartialEq, Eq)]
pub enum Truncation {
    Lines,
    Bytes,
}

/// The dim last line of a cut-off text preview.
#[must_use]
pub fn truncation_marker(limits: PreviewLimits, truncation: Truncation) -> StyledLine {
    let text = match truncation {
        Truncation::Lines => format!("\u{2026} preview limited to {} lines", group_thousands(limits.lines as u64)),
        Truncation::Bytes => format!(
            "\u{2026} preview limited to {}",
            crate::ui::format_helpers::format_size_string(i64::try_from(limits.bytes).unwrap_or(i64::MAX))
        ),
    };
    let len = text.chars().count();
    StyledLine::with_spans(&text, vec![crate::highlight::StyledSpan::new(0, len, crate::highlight::TokenKind::Comment)])
}

fn group_thousands(value: u64) -> String {
    let digits = value.to_string();
    let mut out = String::new();
    for (i, ch) in digits.chars().enumerate() {
        if i > 0 && (digits.len() - i).is_multiple_of(3) {
            out.push(',');
        }
        out.push(ch);
    }
    out
}

/// Port of `PreviewResult`.
#[derive(Clone, Debug, Default, PartialEq, Eq)]
pub struct PreviewResult {
    pub text_lines: Option<Vec<StyledLine>>,
    pub file_type_label: Option<String>,
    /// Line numbers are suppressed (zip contents, hex dump, diff, ...).
    pub is_rendered: bool,
    /// A placeholder message rather than content (suppresses the split
    /// layout when metadata is present).
    pub is_placeholder: bool,
    /// Encoded image data and its pixel size (image and PDF previews).
    pub image: Option<ImageData>,
    pub image_pixel_width: i32,
    pub image_pixel_height: i32,
    /// The last text line is a truncation marker: drawn without a line
    /// number (Rust only).
    pub has_truncation_marker: bool,
}

/// Port of `MetadataEntry`. An empty label renders as a list item.
#[derive(Clone, Debug, PartialEq, Eq)]
pub struct MetadataEntry {
    pub label: String,
    pub value: String,
}

impl MetadataEntry {
    #[must_use]
    pub fn new(label: &str, value: &str) -> Self {
        Self {
            label: label.to_string(),
            value: value.to_string(),
        }
    }
}

/// Port of `MetadataSection`.
#[derive(Clone, Debug, PartialEq, Eq)]
pub struct MetadataSection {
    pub header: Option<String>,
    pub entries: Vec<MetadataEntry>,
}

/// Port of `MetadataResult`.
#[derive(Clone, Debug, PartialEq, Eq)]
pub struct MetadataResult {
    pub sections: Vec<MetadataSection>,
    pub file_type_label: Option<String>,
}

/// Port of `IPreviewProvider`.
pub trait PreviewProvider: Sync {
    fn label(&self) -> &'static str;
    fn can_preview(&self, path: &str, context: &PreviewContext) -> bool;
    fn get_preview(&self, path: &str, context: &PreviewContext, cancel: &CancelToken) -> Option<PreviewResult>;
}

/// Port of `IMetadataProvider`.
pub trait MetadataProvider: Sync {
    fn label(&self) -> &'static str;
    fn can_provide_metadata(&self, path: &str, context: &PreviewContext) -> bool;
    fn get_metadata(&self, path: &str, context: &PreviewContext, cancel: &CancelToken) -> Option<MetadataResult>;
}

/// The context the C# provider tests build (`MakeContext`): every flag on.
#[cfg(test)]
pub(crate) fn test_context() -> PreviewContext {
    PreviewContext {
        pane_width_cells: 60,
        pane_height_cells: 30,
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
        image_previews_enabled: true,
        image_protocol: Some(ImageProtocol::Sixel),
        archive_metadata_enabled: true,
        limits: PreviewLimits::CSHARP,
    }
}

/// A unique scratch path under the temp dir for provider tests.
#[cfg(test)]
pub(crate) fn test_path(name: &str) -> std::path::PathBuf {
    use std::sync::atomic::{AtomicUsize, Ordering};
    static NEXT: AtomicUsize = AtomicUsize::new(0);

    let dir = std::env::temp_dir().join(format!(
        "wade-preview-{}-{}",
        std::process::id(),
        NEXT.fetch_add(1, Ordering::Relaxed)
    ));
    std::fs::create_dir_all(&dir).unwrap();
    dir.join(name)
}
