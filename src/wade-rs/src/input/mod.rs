//! Port of the terminal-relevant subset of src/Wade/Terminal/InputEvent.cs
//! and src/Wade/Terminal/InputMode.cs.

pub mod decode;
pub mod input_pipeline;
#[cfg(unix)]
pub mod unix;
pub mod vt_parser;
#[cfg(windows)]
pub mod windows;

use std::sync::atomic::{AtomicBool, Ordering};
use std::sync::Arc;

use crate::console_key::ConsoleKey;

/// Port of src/Wade/Terminal/InputMode.cs
#[derive(Clone, Copy, PartialEq, Eq, Debug)]
pub enum InputMode {
    Normal,
    Confirm,
    TextInput,
    Search,
    ExpandedPreview,
    GoToPath,
    Help,
    Config,
    Properties,
    ActionPalette,
    Bookmarks,
    FileFinder,
    ContextMenu,
    FileOperation,
}

#[derive(Clone, Copy, PartialEq, Eq, Debug)]
pub enum MouseButton {
    Left,
    Middle,
    Right,
    ScrollUp,
    ScrollDown,
    None,
}

#[derive(Clone, Copy, PartialEq, Eq, Debug)]
pub struct KeyEvent {
    pub key: ConsoleKey,
    /// UTF-16 code unit, mirroring C# `char UnicodeChar` semantics.
    pub key_char: u16,
    pub shift: bool,
    pub alt: bool,
    pub control: bool,
}

impl KeyEvent {
    /// Virtual key codes that are pure modifier keys (VK_SHIFT, VK_CONTROL, VK_MENU).
    #[must_use]
    pub fn is_modifier_only(&self) -> bool {
        self.key.0 == 16 || self.key.0 == 17 || self.key.0 == 18
    }
}

#[derive(Clone, Copy, PartialEq, Eq, Debug)]
pub struct MouseEvent {
    pub button: MouseButton,
    pub row: i32,
    pub col: i32,
    pub is_release: bool,
}

#[derive(Clone, Copy, PartialEq, Eq, Debug)]
pub struct ResizeEvent {
    pub width: i32,
    pub height: i32,
}

#[derive(Clone, PartialEq, Eq, Debug)]
pub enum InputEvent {
    Key(KeyEvent),
    Mouse(MouseEvent),
    Resize(ResizeEvent),
    Paste(String),
    GitStatusReady(GitStatusReadyEvent),
    GitActionComplete(GitActionCompleteEvent),
    FileOperationComplete(FileOperationCompleteEvent),
    FileOperationProgress(FileOperationProgressEvent),
    DirectorySizeReady(DirectorySizeReadyEvent),
    InlineDirSizeReady(InlineDirSizeReadyEvent),
    InlineDirSizeComplete(InlineDirSizeCompleteEvent),
    FileSystemChanged(FileSystemChangedEvent),
    FileFinderPartialResult(FileFinderPartialResultEvent),
    FileFinderScanComplete(FileFinderScanCompleteEvent),
    FileFinderSearchResult(FileFinderSearchResultEvent),
    PreviewReady(PreviewReadyEvent),
    ImagePreviewReady(ImagePreviewReadyEvent),
    CombinedPreviewReady(CombinedPreviewReadyEvent),
    MetadataReady(MetadataReadyEvent),
    PreviewLoadingComplete(PreviewLoadingCompleteEvent),
    /// The background open of a cloud placeholder finished (C# shows the
    /// notification from the download task itself).
    CloudDownloadComplete(CloudDownloadCompleteEvent),
}

#[derive(Clone, Debug, PartialEq, Eq)]
pub struct CloudDownloadCompleteEvent {
    pub error: Option<String>,
}

/// Port of the `DirectorySizeReadyEvent` record
/// (src/Wade/Terminal/InputEvent.cs).
#[derive(Clone, Debug, PartialEq, Eq)]
pub struct DirectorySizeReadyEvent {
    pub path: String,
    pub total_bytes: i64,
}

/// Port of the `InlineDirSizeReadyEvent` record.
#[derive(Clone, Debug, PartialEq, Eq)]
pub struct InlineDirSizeReadyEvent {
    pub parent_path: String,
    pub directory_path: String,
    pub total_bytes: i64,
}

/// Port of the `InlineDirSizeCompleteEvent` record.
#[derive(Clone, Debug, PartialEq, Eq)]
pub struct InlineDirSizeCompleteEvent {
    pub parent_path: String,
}

/// Port of the `FileFinderPartialResultEvent` record: a batch of entries
/// from the finder's directory walk.
#[derive(Clone, Debug, PartialEq, Eq)]
pub struct FileFinderPartialResultEvent {
    pub base_path: String,
    pub entries: Vec<crate::fs::FileSystemEntry>,
}

/// Port of the `FileFinderScanCompleteEvent` record.
#[derive(Clone, Debug, PartialEq, Eq)]
pub struct FileFinderScanCompleteEvent {
    pub base_path: String,
}

/// Port of the `FileFinderSearchResultEvent` record: a batch of scored
/// results for search `search_id`.
#[derive(Clone, Debug, PartialEq, Eq)]
pub struct FileFinderSearchResultEvent {
    pub base_path: String,
    pub results: Vec<crate::search::SearchResult>,
    pub is_complete: bool,
    pub search_id: u64,
}

/// Port of the `PreviewReadyEvent` record: text preview lines for `path`.
#[derive(Clone, Debug, PartialEq, Eq)]
pub struct PreviewReadyEvent {
    pub path: String,
    pub styled_lines: Vec<crate::highlight::StyledLine>,
    pub file_type_label: Option<String>,
    pub is_rendered: bool,
    pub is_placeholder: bool,
}

/// Port of the `ImagePreviewReadyEvent` record: Sixel data for `path`.
#[derive(Clone, Debug, PartialEq, Eq)]
pub struct ImagePreviewReadyEvent {
    pub path: String,
    pub sixel_data: String,
    pub pixel_width: i32,
    pub pixel_height: i32,
    pub file_type_label: String,
}

/// Port of the `CombinedPreviewReadyEvent` record: text above an image.
#[derive(Clone, Debug, PartialEq, Eq)]
pub struct CombinedPreviewReadyEvent {
    pub path: String,
    pub styled_lines: Vec<crate::highlight::StyledLine>,
    pub sixel_data: String,
    pub pixel_width: i32,
    pub pixel_height: i32,
    pub file_type_label: Option<String>,
    pub is_rendered: bool,
}

/// Port of the `MetadataReadyEvent` record: merged metadata sections, plus
/// the text encoding and line ending for non-binary files.
#[derive(Clone, Debug, PartialEq, Eq)]
pub struct MetadataReadyEvent {
    pub path: String,
    pub sections: Vec<crate::preview::MetadataSection>,
    pub file_type_label: Option<String>,
    pub encoding: Option<String>,
    pub line_ending: Option<String>,
}

/// Port of the `PreviewLoadingCompleteEvent` record (no preview provider).
#[derive(Clone, Debug, PartialEq, Eq)]
pub struct PreviewLoadingCompleteEvent {
    pub path: String,
}

/// Port of the `FileSystemChangedEvent` record.
#[derive(Clone, Debug, PartialEq, Eq)]
pub struct FileSystemChangedEvent {
    pub directory_path: String,
    pub full_refresh: bool,
}

/// Port of the `FileOperationCompleteEvent` record (src/Wade/Terminal/InputEvent.cs:22).
#[derive(Clone, Debug, PartialEq, Eq)]
pub struct FileOperationCompleteEvent {
    pub success_count: usize,
    pub error_count: usize,
    pub was_cut: bool,
}

/// ENHANCEMENT over C#: per-item progress for the file-operation overlay
/// (C# shows only the operation label).
#[derive(Clone, Debug, PartialEq, Eq)]
pub struct FileOperationProgressEvent {
    pub index: usize,
    pub total: usize,
    pub current_name: String,
}

/// Port of the `GitActionCompleteEvent` record (src/Wade/Terminal/InputEvent.cs:80).
#[derive(Clone, Debug, PartialEq, Eq)]
pub struct GitActionCompleteEvent {
    pub success: bool,
    pub error_message: Option<String>,
}

/// Port of the `GitStatusReadyEvent` record (src/Wade/Terminal/InputEvent.cs:73).
#[derive(Clone, Debug, Default, PartialEq, Eq)]
pub struct GitStatusReadyEvent {
    pub repo_root: String,
    pub branch_name: Option<String>,
    pub statuses: Option<std::collections::HashMap<String, crate::fs::directory_contents::GitFileStatus>>,
    pub ahead: u32,
    pub behind: u32,
}

/// Shared cancellation flag; clones share the same underlying flag, so a
/// token can be handed to the pump thread and loader threads.
#[derive(Default, Clone)]
pub struct CancelToken(Arc<AtomicBool>);

impl CancelToken {
    #[must_use]
    pub fn new() -> Self {
        Self(Arc::new(AtomicBool::new(false)))
    }

    pub fn cancel(&self) {
        self.0.store(true, Ordering::SeqCst);
    }

    #[must_use]
    pub fn is_cancelled(&self) -> bool {
        self.0.load(Ordering::SeqCst)
    }
}

/// Port of src/Wade/Terminal/IInputSource.cs. The source is owned by the
/// pump thread, so it must be Send.
pub trait InputSource: Send {
    fn read_next(&mut self, cancel: &CancelToken) -> Option<InputEvent>;

    /// Non-blocking poll for an already-available event (drains queued
    /// events between frames, mirroring `InputPipeline.TryTake`).
    fn try_take(&mut self) -> Option<InputEvent> {
        None
    }
}


