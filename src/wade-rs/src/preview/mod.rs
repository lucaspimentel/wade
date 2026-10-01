//! Port of `src/Wade/Preview`: preview and metadata providers, their
//! registries, and the shared result types.
//!
//! Providers for formats that land in later phases (images and PDF:
//! Phase 8; MSI, executables, Office, NuGet, media: Phase 9) are absent
//! from the registries (KNOWN_DEVIATIONS.md).

pub mod cli_tool_hints;
pub mod executable_metadata;
pub mod image_metadata;
pub mod markdown;
pub mod metadata_providers;
pub mod pe;
pub mod providers;
pub mod registry;

use crate::fs::GitFileStatus;
use crate::highlight::StyledLine;
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
    pub sixel_supported: bool,
    pub archive_metadata_enabled: bool,
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
    /// Sixel image data and its pixel size (image and PDF previews).
    pub sixel_data: Option<String>,
    pub sixel_pixel_width: i32,
    pub sixel_pixel_height: i32,
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
        sixel_supported: true,
        archive_metadata_enabled: true,
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
