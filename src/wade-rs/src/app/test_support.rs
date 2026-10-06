//! Shared helpers for App-level tests: a fixture tree, an App set up as
//! startup sets it up, frame text, key events and an event pump.

use std::path::{Path, PathBuf};
use std::sync::atomic::{AtomicUsize, Ordering};
use std::time::{Duration, Instant};

use super::{App, AppConfig};
use crate::console_key::ConsoleKey;
use crate::input::{InputEvent, KeyEvent};
use crate::screen::ScreenBuffer;

pub(crate) const WIDTH: i32 = 120;
pub(crate) const HEIGHT: i32 = 30;
/// The listed directory's name: unique enough that finding it on screen
/// means the parent pane is drawn.
pub(crate) const CURRENT_DIR: &str = "ZQXcurrent";

static NEXT_ID: AtomicUsize = AtomicUsize::new(0);

/// A fresh fixture tree; returns (parent, listed directory). The listed
/// directory holds `sub/inner.txt`, `a.txt` (1234 bytes), `b.txt`,
/// `.hidden`, `readme.md` and an empty `empty.zip`.
pub(crate) fn fixture() -> (PathBuf, PathBuf) {
    let id = NEXT_ID.fetch_add(1, Ordering::SeqCst);
    let parent = std::env::temp_dir().join(format!("wade-apptest-{}-{id}", std::process::id()));
    let _ = std::fs::remove_dir_all(&parent);
    let root = parent.join(CURRENT_DIR);
    std::fs::create_dir_all(root.join("sub")).unwrap();
    std::fs::write(root.join("sub").join("inner.txt"), "inner").unwrap();
    std::fs::write(root.join("a.txt"), vec![b'a'; 1234]).unwrap();
    std::fs::write(root.join("b.txt"), "b").unwrap();
    std::fs::write(root.join(".hidden"), "h").unwrap();
    std::fs::write(root.join("readme.md"), "# Title\n").unwrap();
    // An empty zip archive: just the end-of-central-directory record
    let mut zip = b"PK\x05\x06".to_vec();
    zip.extend_from_slice(&[0; 18]);
    std::fs::write(root.join("empty.zip"), zip).unwrap();
    (parent, root)
}

/// An App at `root` with `config` applied as startup applies it, laid out
/// as `run` does. The config and bookmark files live in the fixture so
/// tests never touch the user's files.
pub(crate) fn app_at(mut config: AppConfig, root: &Path) -> App {
    config.start_path = root.to_string_lossy().into_owned();
    config.config_file_path = Some(root.join("config.toml").to_string_lossy().into_owned());
    let mut app = App::new(config);
    app.bookmark_store = crate::fs::bookmark_store::BookmarkStore::new(Some(root.join("bookmarks.txt")));
    // Outside the listed directory, so saving a sort doesn't add an entry
    let sorts_file = root.parent().unwrap_or(root).join("sorts.txt");
    app.directory_contents.path_sorts = crate::fs::sort_store::SortStore::new(Some(sorts_file));
    app.apply_startup_config();
    app.set_screen_size(WIDTH, HEIGHT);
    app.layout.calculate(WIDTH, HEIGHT, app.preview_pane_enabled, app.parent_pane_enabled);
    app
}

/// Rendered rows, without the status bar (it shows the current path).
pub(crate) fn frame(app: &mut App) -> Vec<String> {
    let mut buffer = ScreenBuffer::new(WIDTH, HEIGHT);
    app.render(&mut buffer);
    (0..HEIGHT - 1).map(|row| buffer.row_text(row)).collect()
}

/// The status bar row.
pub(crate) fn status_bar(app: &mut App) -> String {
    let mut buffer = ScreenBuffer::new(WIDTH, HEIGHT);
    app.render(&mut buffer);
    buffer.row_text(HEIGHT - 1)
}

pub(crate) fn frame_has(app: &mut App, text: &str) -> bool {
    frame(app).iter().any(|row| row.contains(text))
}

pub(crate) fn row_with(app: &mut App, text: &str) -> String {
    frame(app).into_iter().find(|row| row.contains(text)).unwrap_or_default()
}

pub(crate) fn select(app: &mut App, name: &str) {
    let entries = app.get_visible_entries();
    app.selected_index = entries.iter().position(|e| e.name == name).unwrap_or_else(|| panic!("{name} not listed"));
}

pub(crate) fn visible_names(app: &mut App) -> Vec<String> {
    app.get_visible_entries().into_iter().map(|e| e.name).collect()
}

pub(crate) fn selected_name(app: &mut App) -> String {
    let entries = app.get_visible_entries();
    entries.get(app.selected_index).map(|e| e.name.clone()).unwrap_or_default()
}

/// A key press as the console reports it: letters and digits carry their
/// virtual-key code, everything else only the character.
pub(crate) fn ch(c: char) -> KeyEvent {
    let key = if c.is_ascii_alphanumeric() {
        ConsoleKey(u16::from(c.to_ascii_uppercase() as u8))
    } else if c == ' ' {
        ConsoleKey::Spacebar
    } else {
        ConsoleKey::None
    };
    KeyEvent { key, key_char: c as u16, shift: c.is_ascii_uppercase(), alt: false, control: false }
}

pub(crate) fn key(key: ConsoleKey) -> KeyEvent {
    let key_char = match key {
        ConsoleKey::Enter => 13,
        ConsoleKey::Escape => 27,
        ConsoleKey::Backspace => 8,
        ConsoleKey::Spacebar => 32,
        _ => 0,
    };
    KeyEvent { key, key_char, shift: false, alt: false, control: false }
}

pub(crate) fn ctrl(key: ConsoleKey) -> KeyEvent {
    KeyEvent { key, key_char: 0, shift: false, alt: false, control: true }
}

pub(crate) fn shift(key: ConsoleKey) -> KeyEvent {
    KeyEvent { key, key_char: 0, shift: true, alt: false, control: false }
}

/// Feeds one event through the main-loop step.
pub(crate) fn press(app: &mut App, event: KeyEvent) {
    app.handle_event(InputEvent::Key(event));
}

pub(crate) fn type_text(app: &mut App, text: &str) {
    for c in text.chars() {
        press(app, ch(c));
    }
}

/// Handles queued loader events until `done` holds (5 s timeout).
pub(crate) fn pump_until(app: &mut App, what: &str, done: impl Fn(&App) -> bool) {
    let deadline = Instant::now() + Duration::from_secs(5);

    while !done(app) {
        assert!(Instant::now() < deadline, "timed out waiting for {what}");

        match app.pipeline.try_take() {
            Some(event) => app.handle_event(event),
            None => std::thread::sleep(Duration::from_millis(5)),
        }
    }
}
