//! wade: TUI file browser.

// Tests set up and check their fixtures with `std::fs` directly: the path
// spellings `fs::verbatim` handles are a production concern (clippy.toml).
#![cfg_attr(test, allow(clippy::disallowed_methods))]

pub mod ansi;
pub mod app;
pub mod console_key;
pub mod debug_protocol;
pub mod frame;
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

/// The informational version, `<version>`, plus the commit sha when the build
/// was given one.
#[must_use]
pub fn version_string() -> String {
    match option_env!("WADE_COMMIT") {
        Some(sha) => format!("{}+{sha}", env!("CARGO_PKG_VERSION")),
        None => env!("CARGO_PKG_VERSION").to_string(),
    }
}
