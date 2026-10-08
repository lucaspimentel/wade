//! Rust-only: sort orders remembered per directory, stored as plain text at
//! `~/.config/wade/sorts` (next to `bookmarks`). One entry per line:
//! `<mode> <asc|desc> <path>`; the path is the rest of the line.

use std::collections::HashMap;
use std::path::{Path, PathBuf};

use super::SortMode;

/// Per-directory sort orders, keyed case-insensitively on Windows.
pub struct SortStore {
    sorts: HashMap<String, (SortMode, bool)>,
    /// `None` keeps the store in memory only (unit tests).
    file_path: Option<PathBuf>,
}

impl Default for SortStore {
    fn default() -> Self {
        Self::new(None)
    }
}

/// Map key for a path: case-insensitive on Windows, like `BookmarkStore`.
fn key(path: &str) -> String {
    if cfg!(windows) { path.to_lowercase() } else { path.to_string() }
}

fn mode_name(mode: SortMode) -> &'static str {
    match mode {
        SortMode::Name => "name",
        SortMode::Modified => "modified",
        SortMode::Size => "size",
        SortMode::Extension => "extension",
    }
}

fn parse_mode(value: &str) -> Option<SortMode> {
    match value {
        "name" => Some(SortMode::Name),
        "modified" => Some(SortMode::Modified),
        "size" => Some(SortMode::Size),
        "extension" => Some(SortMode::Extension),
        _ => None,
    }
}

/// Parses `<mode> <asc|desc> <path>`; `None` for blank, comment or
/// malformed lines.
fn parse_line(line: &str) -> Option<(String, SortMode, bool)> {
    let line = line.trim_end_matches(['\r', '\n']);

    if line.trim().is_empty() || line.starts_with('#') {
        return None;
    }

    let mut parts = line.splitn(3, ' ');
    let mode = parse_mode(parts.next()?)?;
    let ascending = match parts.next()? {
        "asc" => true,
        "desc" => false,
        _ => return None,
    };
    let path = parts.next().filter(|path| !path.is_empty())?;
    Some((path.to_string(), mode, ascending))
}

impl SortStore {
    #[must_use]
    pub fn new(file_path: Option<PathBuf>) -> Self {
        Self { sorts: HashMap::new(), file_path }
    }

    /// `~/.config/wade/sorts`.
    #[must_use]
    pub fn default_path() -> PathBuf {
        let home = std::env::var_os("USERPROFILE").or_else(|| std::env::var_os("HOME")).unwrap_or_default();
        Path::new(&home).join(".config").join("wade").join("sorts")
    }

    /// Replaces the entries with the file's; a missing or unreadable file
    /// leaves the store empty.
    pub fn load(&mut self) {
        self.sorts.clear();

        let Some(text) = self.file_path.as_ref().and_then(|path| std::fs::read_to_string(path).ok()) else {
            return;
        };

        for (path, mode, ascending) in text.lines().filter_map(parse_line) {
            // macOS: stored spelling, so lines that differ only in case merge
            // (the later wins) and match the paths wade navigates to
            let path = super::file_operations::on_disk_case(&path);
            self.sorts.insert(key(&path), (mode, ascending));
        }
    }

    /// Writes every entry, sorted by key for a stable file. No-op without a
    /// file path.
    pub fn save(&self) -> std::io::Result<()> {
        let Some(file_path) = &self.file_path else {
            return Ok(());
        };

        if let Some(dir) = file_path.parent() {
            std::fs::create_dir_all(dir)?;
        }

        let mut entries: Vec<_> = self.sorts.iter().collect();
        entries.sort_by(|a, b| a.0.cmp(b.0));

        let mut content = String::new();
        for (path, (mode, ascending)) in entries {
            content.push_str(&format!("{} {} {path}\n", mode_name(*mode), if *ascending { "asc" } else { "desc" }));
        }

        std::fs::write(file_path, content)
    }

    #[must_use]
    pub fn get(&self, path: &str) -> Option<(SortMode, bool)> {
        self.sorts.get(&key(path)).copied()
    }

    pub fn set(&mut self, path: &str, mode: SortMode, ascending: bool) {
        self.sorts.insert(key(path), (mode, ascending));
    }

    /// Returns whether the path had a saved sort.
    pub fn remove(&mut self, path: &str) -> bool {
        self.sorts.remove(&key(path)).is_some()
    }
}

#[cfg(test)]
mod tests {
    use super::{SortMode, SortStore};

    fn test_file(name: &str) -> std::path::PathBuf {
        let dir = std::env::temp_dir().join(format!("wade-sort-store-{name}-{}", std::process::id()));
        let _ = std::fs::remove_dir_all(&dir);
        dir.join("nested").join("sorts")
    }

    /// macOS: keys are respelled as stored on disk, so lines that differ
    /// only in case merge (the later wins) and match the stored spelling.
    #[test]
    fn load_merges_spellings_of_one_directory_on_macos() {
        let file = test_file("case");
        let dir = file.parent().unwrap().to_path_buf();
        std::fs::create_dir_all(dir.join("Dir")).unwrap();
        let stored = dir.join("Dir").to_string_lossy().into_owned();
        let lower = dir.join("dir").to_string_lossy().into_owned();
        std::fs::write(&file, format!("size desc {lower}\nextension asc {stored}\n")).unwrap();

        let mut store = SortStore::new(Some(file.clone()));
        store.load();

        if cfg!(target_os = "macos") {
            assert_eq!(store.sorts.len(), 1);
            assert_eq!(store.get(&stored), Some((SortMode::Extension, true)));
        } else if cfg!(windows) {
            // Windows folds case in the key instead
            assert_eq!(store.get(&lower), Some((SortMode::Extension, true)));
        } else {
            assert_eq!(store.get(&lower), Some((SortMode::Size, false)));
            assert_eq!(store.get(&stored), Some((SortMode::Extension, true)));
        }
        let _ = std::fs::remove_dir_all(dir.parent().unwrap());
    }

    #[test]
    fn save_and_load_round_trip_with_spaces_in_paths() {
        let file = test_file("round-trip");
        let mut store = SortStore::new(Some(file.clone()));
        store.set("/tmp/My Documents", SortMode::Size, false);
        store.set("/tmp/b", SortMode::Extension, true);
        store.save().unwrap();
        assert!(file.exists(), "save creates the directory");

        let mut loaded = SortStore::new(Some(file.clone()));
        loaded.load();
        assert_eq!(loaded.get("/tmp/My Documents"), Some((SortMode::Size, false)));
        assert_eq!(loaded.get("/tmp/b"), Some((SortMode::Extension, true)));
        assert_eq!(loaded.get("/tmp/c"), None);
        let _ = std::fs::remove_dir_all(file.parent().unwrap().parent().unwrap());
    }

    #[test]
    fn comments_and_malformed_lines_are_skipped() {
        let file = test_file("malformed");
        std::fs::create_dir_all(file.parent().unwrap()).unwrap();
        std::fs::write(
            &file,
            "# comment\n\nsize desc /ok\nbogus asc /bad-mode\nsize sideways /bad-dir\nname asc\nmodified asc /also ok\r\n",
        )
        .unwrap();

        let mut store = SortStore::new(Some(file.clone()));
        store.load();
        assert_eq!(store.get("/ok"), Some((SortMode::Size, false)));
        assert_eq!(store.get("/also ok"), Some((SortMode::Modified, true)));
        assert_eq!(store.get("/bad-mode"), None);
        assert_eq!(store.get("/bad-dir"), None);
        assert_eq!(store.sorts.len(), 2);
        let _ = std::fs::remove_dir_all(file.parent().unwrap().parent().unwrap());
    }

    #[test]
    fn remove_and_missing_file() {
        let mut store = SortStore::new(Some(test_file("missing")));
        store.load();
        assert_eq!(store.get("/a"), None, "a missing file is an empty store");
        store.set("/a", SortMode::Size, true);
        assert!(store.remove("/a"));
        assert!(!store.remove("/a"));
        assert_eq!(store.get("/a"), None);
    }

    #[test]
    fn without_a_file_path_nothing_is_written() {
        let mut store = SortStore::new(None);
        store.set("/a", SortMode::Size, true);
        store.save().unwrap();
        store.load();
        assert_eq!(store.get("/a"), None, "load clears; there is no file to read");
    }

    #[test]
    fn lookups_follow_the_platform_case_rules() {
        let mut store = SortStore::new(None);
        store.set("C:\\Users\\Me", SortMode::Size, true);
        let other_case = store.get("c:\\users\\me");

        if cfg!(windows) {
            assert_eq!(other_case, Some((SortMode::Size, true)));
        } else {
            assert_eq!(other_case, None);
        }
    }
}
