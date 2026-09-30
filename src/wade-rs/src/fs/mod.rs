//! Port of src/Wade/FileSystem/DirectoryContents.cs (core subset used by the
//! app spine). Junction/app-exec-link reparse detection, cloud placeholders,
//! and drive media-type detection are Phase 9 items; entries always carry the
//! neutral defaults for those fields.

pub mod bookmark_store;
pub mod directory_contents;
pub mod file_operations;
pub mod file_type_labels;
pub mod git_utils;
pub mod path_completion;
pub use directory_contents::{DirectoryContents, FileSystemEntry, GitFileStatus, SortMode, DRIVES_PATH};
