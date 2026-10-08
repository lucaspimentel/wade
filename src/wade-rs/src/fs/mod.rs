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
pub mod lnk;
pub mod path_completion;
pub mod reparse;
pub mod sort_store;
pub mod system_clipboard;
pub mod tar_preview;
pub mod zip_preview;
pub use directory_contents::{DRIVES_PATH, DirectoryContents, FileSystemEntry, GitFileStatus, SortMode};
pub use drive_media_type::DriveMediaType;

/// The key path-keyed stores (bookmarks, saved sorts) compare by: Unicode
/// lowercase on Windows, whose paths are case-insensitive; the path itself
/// elsewhere (macOS paths arrive respelled with the on-disk case, Linux is
/// case-sensitive).
#[must_use]
pub fn path_key(path: &str) -> std::borrow::Cow<'_, str> {
    if cfg!(windows) {
        std::borrow::Cow::Owned(path.to_ascii_lowercase())
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
