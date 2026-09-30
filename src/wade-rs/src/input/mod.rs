//! Port of the terminal-relevant subset of src/Wade/Terminal/InputEvent.cs
//! and src/Wade/Terminal/InputMode.cs.

pub mod decode;
pub mod input_pipeline;
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


