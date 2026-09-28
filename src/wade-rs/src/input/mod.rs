//! Port of the terminal-relevant subset of src/Wade/Terminal/InputEvent.cs
//! and src/Wade/Terminal/InputMode.cs.

pub mod decode;
#[cfg(windows)]
pub mod windows;

use std::sync::atomic::{AtomicBool, Ordering};

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

#[derive(Clone, PartialEq, Eq, Debug)]
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
}

#[derive(Default)]
pub struct CancelToken(AtomicBool);

impl CancelToken {
    #[must_use]
    pub fn new() -> Self {
        Self(AtomicBool::new(false))
    }

    pub fn cancel(&self) {
        self.0.store(true, Ordering::SeqCst);
    }

    #[must_use]
    pub fn is_cancelled(&self) -> bool {
        self.0.load(Ordering::SeqCst)
    }
}

/// Port of src/Wade/Terminal/IInputSource.cs.
pub trait InputSource {
    fn read_next(&mut self, cancel: &CancelToken) -> Option<InputEvent>;
}
