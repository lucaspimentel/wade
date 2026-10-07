//! wade: TUI file browser (Rust port of the C# Native AOT tool).

pub mod ansi;
pub mod app;
pub mod console_key;
pub mod fs;
pub mod highlight;
pub mod imaging;
pub mod input;
pub mod preview;
pub mod rune_width;
pub mod screen;
pub mod search;
pub mod terminal_caps;
#[cfg(windows)]
pub mod terminal_setup;
#[cfg(unix)]
#[path = "terminal_setup_unix.rs"]
pub mod terminal_setup;
pub mod text;
pub mod ui;
