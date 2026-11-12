//! wade: TUI file browser.

// Tests set up and check their fixtures with `std::fs` directly: the path
// spellings `fs::verbatim` handles are a production concern (clippy.toml).
#![cfg_attr(test, allow(clippy::disallowed_methods))]

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
#[cfg(unix)]
pub mod signals;
pub mod terminal_caps;
#[cfg(windows)]
pub mod terminal_setup;
#[cfg(unix)]
#[path = "terminal_setup_unix.rs"]
pub mod terminal_setup;
pub mod text;
pub mod ui;
