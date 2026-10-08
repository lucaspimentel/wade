//! Port of `src/Wade/BookmarkStore.cs`: the plain-text bookmark list stored
//! at `~/.config/wade/bookmarks`.

use std::path::{Path, PathBuf};

/// Case-insensitive path comparison on Windows, ordinal elsewhere (matches
/// the C# `BookmarkStore.PathComparison`).
#[cfg(windows)]
fn paths_equal(a: &str, b: &str) -> bool {
    a.eq_ignore_ascii_case(b)
}

#[cfg(not(windows))]
fn paths_equal(a: &str, b: &str) -> bool {
    a == b
}

/// Port of `BookmarkStore`.
pub struct BookmarkStore {
    bookmarks: Vec<String>,
    file_path: PathBuf,
}

impl BookmarkStore {
    /// Port of the `BookmarkStore(filePath = null)` constructor: defaults to
    /// `~/.config/wade/bookmarks`. Tests pass an explicit path.
    pub fn new(file_path: Option<PathBuf>) -> Self {
        let file_path = file_path.unwrap_or_else(|| {
            let home = std::env::var_os("USERPROFILE").or_else(|| std::env::var_os("HOME")).unwrap_or_default();
            Path::new(&home).join(".config").join("wade").join("bookmarks")
        });

        Self { bookmarks: Vec::new(), file_path }
    }

    /// Port of `Bookmarks`.
    #[must_use]
    pub fn bookmarks(&self) -> &[String] {
        &self.bookmarks
    }

    /// Port of `Load`.
    pub fn load(&mut self) {
        self.bookmarks.clear();

        let Ok(text) = std::fs::read_to_string(&self.file_path) else {
            return;
        };

        for line in text.lines() {
            let trimmed = line.trim();
            if trimmed.is_empty() || trimmed.starts_with('#') {
                continue;
            }

            // macOS: stored spelling, so entries that differ only in case
            // merge (the first wins) and match the paths wade navigates to
            let bookmark = super::file_operations::on_disk_case(trimmed);
            if true {
                self.bookmarks.push(bookmark);
            }
        }
    }

    /// Port of `Save`.
    pub fn save(&self) -> std::io::Result<()> {
        if let Some(dir) = self.file_path.parent() {
            std::fs::create_dir_all(dir)?;
        }

        let mut content = String::new();
        for bookmark in &self.bookmarks {
            content.push_str(bookmark);
            content.push('\n');
        }

        std::fs::write(&self.file_path, content)
    }

    /// Port of `Contains`.
    #[must_use]
    pub fn contains(&self, path: &str) -> bool {
        self.bookmarks.iter().any(|b| paths_equal(b, path))
    }

    /// Port of `Add`: MRU semantics, the added path moves to the top.
    pub fn add(&mut self, path: &str) {
        self.bookmarks.retain(|b| !paths_equal(b, path));
        self.bookmarks.insert(0, path.to_string());
        let _ = self.save();
    }

    /// Port of `Remove`.
    pub fn remove(&mut self, path: &str) {
        self.bookmarks.retain(|b| !paths_equal(b, path));
        let _ = self.save();
    }

    /// Port of `Toggle`.
    pub fn toggle(&mut self, path: &str) {
        if self.contains(path) {
            self.remove(path);
        } else {
            self.add(path);
        }
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    fn store(name: &str) -> BookmarkStore {
        let path = std::env::temp_dir().join(format!("wade-bookmarks-test-{}-{name}", std::process::id()));
        let _ = std::fs::remove_file(&path);
        BookmarkStore::new(Some(path))
    }

    /// macOS: entries are respelled as stored on disk, so spellings that
    /// differ only in case merge (the first wins); elsewhere both stay.
    #[test]
    fn load_merges_spellings_of_one_directory_on_macos() {
        let dir = std::env::temp_dir().join(format!("wade-bookmarks-case-{}", std::process::id()));
        let _ = std::fs::remove_dir_all(&dir);
        std::fs::create_dir_all(dir.join("Dir")).unwrap();
        let stored = dir.join("Dir").to_string_lossy().into_owned();
        let lower = dir.join("dir").to_string_lossy().into_owned();
        let other = dir.join("other").to_string_lossy().into_owned();

        let mut s = store("case.txt");
        std::fs::write(&s.file_path, format!("{lower}\n{other}\n{stored}\n")).unwrap();
        s.load();

        if cfg!(target_os = "macos") {
            assert_eq!(s.bookmarks(), [stored.clone(), other]);
            assert!(s.contains(&stored));
        } else {
            assert_eq!(s.bookmarks(), [lower, other, stored]);
        }
        let _ = std::fs::remove_dir_all(&dir);
    }

    #[test]
    fn add_is_mru_and_persists() {
        let mut s = store("mru.txt");
        s.add(r"C:\a");
        s.add(r"C:\b");
        assert_eq!(s.bookmarks(), &[r"C:\b".to_string(), r"C:\a".to_string()]);

        // Re-adding moves to top instead of duplicating
        s.add(r"C:\a");
        assert_eq!(s.bookmarks(), &[r"C:\a".to_string(), r"C:\b".to_string()]);

        let mut reloaded = BookmarkStore::new(Some(s.file_path.clone()));
        reloaded.load();
        assert_eq!(reloaded.bookmarks(), s.bookmarks());
    }

    #[test]
    fn load_skips_comments_and_blanks() {
        let mut s = store("comments.txt");
        s.add(r"C:\keep");
        // Corrupt-check: write a file with comments and blanks
        let file = s.file_path.clone();
        std::fs::write(&file, "# comment\n\n  C:\\other  \nC:\\keep\n").expect("write");
        s.load();
        assert_eq!(s.bookmarks(), &[r"C:\other".to_string(), r"C:\keep".to_string()]);
    }

    #[test]
    fn load_of_an_empty_or_missing_file_is_empty() {
        let mut empty = store("empty.txt");
        std::fs::write(&empty.file_path, "").expect("write");
        empty.load();
        assert!(empty.bookmarks().is_empty());

        let mut missing = BookmarkStore::new(Some(
            std::env::temp_dir().join(format!("wade-bookmarks-missing-{}", std::process::id())).join("none"),
        ));
        missing.load();
        assert!(missing.bookmarks().is_empty());
    }

    #[test]
    fn remove_drops_one_entry_and_persists() {
        let mut s = store("remove.txt");
        s.add(r"C:\a");
        s.add(r"C:\b");
        s.remove(r"C:\a");
        assert_eq!(s.bookmarks(), &[r"C:\b".to_string()]);
        assert!(!s.contains(r"C:\a"));

        let mut reloaded = BookmarkStore::new(Some(s.file_path.clone()));
        reloaded.load();
        assert_eq!(reloaded.bookmarks(), &[r"C:\b".to_string()]);
    }

    #[test]
    fn toggle_removes_then_adds() {
        let mut s = store("toggle.txt");
        s.toggle(r"C:\x");
        assert!(s.contains(r"C:\x"));
        s.toggle(r"C:\x");
        assert!(!s.contains(r"C:\x"));
    }

    #[test]
    fn contains_ignores_case_on_windows() {
        let mut s = store("case.txt");
        s.add(r"C:\Windows");
        #[cfg(windows)]
        assert!(s.contains(r"c:\WINDOWS"));
        #[cfg(not(windows))]
        assert!(!s.contains(r"c:\WINDOWS"));
    }
}
