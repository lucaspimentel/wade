//! Input events, input modes and the platform input sources.

pub mod decode;
pub mod input_pipeline;
#[cfg(unix)]
pub mod unix;
pub mod vt_parser;
#[cfg(windows)]
pub mod windows;

use std::sync::Arc;
use std::sync::atomic::{AtomicBool, Ordering};

use crate::console_key::ConsoleKey;

/// Which part of the UI keys go to.
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
    /// The typed character, `'\0'` when the key types none. Characters
    /// beyond U+FFFF (emoji) arrive as one key.
    pub key_char: char,
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

    /// The typed character when neither Ctrl nor Alt is held
    /// (Shift is already in the character: `K` is Shift+k). Ctrl+Alt
    /// together counts as plain: Windows reports AltGr that way, and
    /// layouts type `[`, `{`, `/` ... with it.
    #[must_use]
    pub fn plain_char(&self) -> Option<char> {
        let altgr = self.control && self.alt;
        if (self.control || self.alt) && !altgr {
            return None;
        }

        self.typed_char()
    }

    /// The typed character whatever the modifiers, `None` for control
    /// characters (Enter, Esc, Ctrl+letter) and keys that type nothing.
    #[must_use]
    pub fn typed_char(&self) -> Option<char> {
        Some(self.key_char).filter(|c| !c.is_control())
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
    /// The background open of a cloud placeholder finished.
    CloudDownloadComplete(CloudDownloadCompleteEvent),
}

#[derive(Clone, Debug, PartialEq, Eq)]
pub struct CloudDownloadCompleteEvent {
    pub error: Option<String>,
}

/// The properties overlay's directory size.
#[derive(Clone, Debug, PartialEq, Eq)]
pub struct DirectorySizeReadyEvent {
    pub path: String,
    pub total_bytes: i64,
}

/// One listed directory's size.
#[derive(Clone, Debug, PartialEq, Eq)]
pub struct InlineDirSizeReadyEvent {
    pub parent_path: String,
    pub directory_path: String,
    pub total_bytes: i64,
}

/// All listed directory sizes are in.
#[derive(Clone, Debug, PartialEq, Eq)]
pub struct InlineDirSizeCompleteEvent {
    pub parent_path: String,
}

/// A batch of entries from the finder's directory walk.
#[derive(Clone, Debug, PartialEq, Eq)]
pub struct FileFinderPartialResultEvent {
    pub base_path: String,
    pub entries: Vec<crate::fs::FileSystemEntry>,
}

/// The finder's directory walk finished.
#[derive(Clone, Debug, PartialEq, Eq)]
pub struct FileFinderScanCompleteEvent {
    pub base_path: String,
}

/// A batch of scored results for search `search_id`.
#[derive(Clone, Debug, PartialEq, Eq)]
pub struct FileFinderSearchResultEvent {
    pub base_path: String,
    pub results: Vec<crate::search::SearchResult>,
    pub is_complete: bool,
    pub search_id: u64,
}

/// Text preview lines for `path`.
#[derive(Clone, Debug, PartialEq, Eq)]
pub struct PreviewReadyEvent {
    pub path: String,
    pub styled_lines: Vec<crate::highlight::StyledLine>,
    pub file_type_label: Option<String>,
    pub is_rendered: bool,
    pub is_placeholder: bool,
    /// The last line is a truncation marker.
    pub has_truncation_marker: bool,
}

/// The encoded image for `path`.
#[derive(Clone, Debug, PartialEq, Eq)]
pub struct ImagePreviewReadyEvent {
    pub path: String,
    pub image: crate::imaging::ImageData,
    pub pixel_width: i32,
    pub pixel_height: i32,
    pub file_type_label: String,
}

/// Text above an image.
#[derive(Clone, Debug, PartialEq, Eq)]
pub struct CombinedPreviewReadyEvent {
    pub path: String,
    pub styled_lines: Vec<crate::highlight::StyledLine>,
    pub image: crate::imaging::ImageData,
    pub pixel_width: i32,
    pub pixel_height: i32,
    pub file_type_label: Option<String>,
    pub is_rendered: bool,
}

/// Merged metadata sections, plus the text encoding and line ending for
/// non-binary files.
#[derive(Clone, Debug, PartialEq, Eq)]
pub struct MetadataReadyEvent {
    pub path: String,
    pub sections: Vec<crate::preview::MetadataSection>,
    pub file_type_label: Option<String>,
    pub encoding: Option<String>,
    pub line_ending: Option<String>,
}

/// The preview load finished without a preview (no preview provider).
#[derive(Clone, Debug, PartialEq, Eq)]
pub struct PreviewLoadingCompleteEvent {
    pub path: String,
}

/// The watched directory changed.
#[derive(Clone, Debug, PartialEq, Eq)]
pub struct FileSystemChangedEvent {
    pub directory_path: String,
    pub full_refresh: bool,
}

/// A file operation finished: success and error counts.
#[derive(Clone, Debug, PartialEq, Eq)]
pub struct FileOperationCompleteEvent {
    pub success_count: usize,
    pub error_count: usize,
    pub was_cut: bool,
}

/// Per-item progress for the file-operation overlay.
#[derive(Clone, Debug, PartialEq, Eq)]
pub struct FileOperationProgressEvent {
    pub index: usize,
    pub total: usize,
    pub current_name: String,
}

/// A git action finished.
#[derive(Clone, Debug, PartialEq, Eq)]
pub struct GitActionCompleteEvent {
    pub success: bool,
    pub error_message: Option<String>,
}

/// A repo's branch, statuses and ahead/behind counts.
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

/// A platform's console input. The source is owned by the
/// pump thread, so it must be Send.
pub trait InputSource: Send {
    fn read_next(&mut self, cancel: &CancelToken) -> Option<InputEvent>;

    /// Non-blocking poll for an already-available event (drains queued
    /// events between frames).
    fn try_take(&mut self) -> Option<InputEvent> {
        None
    }
}
