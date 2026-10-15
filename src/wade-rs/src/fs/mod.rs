//! Ports of src/Wade/FileSystem.

pub mod bookmark_store;
pub mod directory_contents;
pub mod drive_media_type;
pub mod file_operations;
pub mod file_preview;
pub mod file_type_labels;
pub mod git_utils;
pub mod gzip;
pub mod hex_preview;
#[cfg(all(unix, not(target_os = "macos")))]
pub mod linux_trash;
pub mod lnk;
pub mod path_completion;
pub mod reparse;
pub mod sort_store;
pub mod system_clipboard;
pub mod tar_preview;
pub mod zip_preview;
pub use directory_contents::{DRIVES_PATH, DirectoryContents, FileSystemEntry, GitFileStatus, SortMode};
pub use drive_media_type::DriveMediaType;

/// The home directory: `USERPROFILE` (Windows), else `HOME`.
#[must_use]
pub fn home_dir() -> Option<std::path::PathBuf> {
    std::env::var_os("USERPROFILE").or_else(|| std::env::var_os("HOME")).map(std::path::PathBuf::from)
}

/// `~/.config/wade/<name>`: the config file, bookmarks and saved sorts.
#[must_use]
pub fn wade_config_file(name: &str) -> Option<std::path::PathBuf> {
    home_dir().map(|home| home.join(".config").join("wade").join(name))
}

/// The key path-keyed stores (bookmarks, saved sorts) compare by: Unicode
/// lowercase on Windows, whose paths are case-insensitive; the path itself
/// elsewhere (macOS paths arrive respelled with the on-disk case, Linux is
/// case-sensitive).
#[must_use]
pub fn path_key(path: &str) -> std::borrow::Cow<'_, str> {
    if cfg!(windows) {
        std::borrow::Cow::Owned(path.to_lowercase())
    } else {
        std::borrow::Cow::Borrowed(path)
    }
}

#[cfg(test)]
mod tests {
    #[test]
    fn path_key_folds_unicode_case_only_on_windows() {
        assert_eq!(super::path_key(r"C:\Äpfel") == super::path_key(r"c:\äPFEL"), cfg!(windows));
        assert_eq!(super::path_key("/x/a"), "/x/a");
    }
}
