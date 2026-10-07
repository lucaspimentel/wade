//! Port of src/Wade/FileSystem/DirectoryContents.cs (core subset).

use std::collections::HashMap;
#[cfg(windows)]
use std::os::windows::fs::MetadataExt;
use std::path::{Path, PathBuf};
use std::time::SystemTime;

/// Mirrors the `GitFileStatus` flags enum in src/Wade/FileSystem/GitUtils.cs.
#[derive(Clone, Copy, PartialEq, Eq, Debug, Default)]
pub struct GitFileStatus(pub u32);

impl GitFileStatus {
    pub const NONE: Self = Self(0);
    pub const UNTRACKED: Self = Self(1 << 0);
    pub const MODIFIED: Self = Self(1 << 1);
    pub const STAGED: Self = Self(1 << 2);
    pub const IGNORED: Self = Self(1 << 3);
    pub const CONFLICT: Self = Self(1 << 4);

    /// Any-bit overlap, matching the C# `(s & statusMask) != 0` checks.
    #[must_use]
    pub const fn intersects(self, other: Self) -> bool {
        self.0 & other.0 != 0
    }

    #[must_use]
    pub const fn contains(self, other: Self) -> bool {
        self.0 & other.0 == other.0 && other.0 != 0
    }
}

impl std::ops::BitOr for GitFileStatus {
    type Output = Self;

    fn bitor(self, rhs: Self) -> Self {
        Self(self.0 | rhs.0)
    }
}

impl std::ops::BitOrAssign for GitFileStatus {
    fn bitor_assign(&mut self, rhs: Self) {
        self.0 |= rhs.0;
    }
}

impl std::ops::BitAnd for GitFileStatus {
    type Output = Self;

    fn bitand(self, rhs: Self) -> Self {
        Self(self.0 & rhs.0)
    }
}

impl std::ops::Not for GitFileStatus {
    type Output = Self;

    fn not(self) -> Self {
        Self(!self.0)
    }
}

/// Mirrors the `SortMode` enum.
#[derive(Clone, Copy, PartialEq, Eq, Debug, Default)]
pub enum SortMode {
    #[default]
    Name,
    Modified,
    Size,
    Extension,
}

/// Sentinel path representing the list of drives.
pub const DRIVES_PATH: &str = "::drives";

/// Mirrors `FileSystemEntry`.
#[derive(Clone, Debug, PartialEq, Eq)]
pub struct FileSystemEntry {
    pub name: String,
    pub full_path: String,
    pub is_directory: bool,
    pub size: i64,
    /// Wall-clock local time fields of `LastWriteTime`.
    pub last_modified: super::super::ui::format_helpers::DateParts,
    pub link_target: Option<String>,
    pub is_broken_symlink: bool,
    pub is_drive: bool,
    pub is_cloud_placeholder: bool,
    pub is_junction_point: bool,
    pub is_app_exec_link: bool,
    pub app_exec_link_target: Option<String>,
    pub drive_media_type: super::DriveMediaType,
    pub drive_format: Option<String>,
    pub drive_label: Option<String>,
    pub drive_free_space: i64,
    pub drive_total_size: i64,
}

impl FileSystemEntry {
    #[must_use]
    pub const fn is_symlink(&self) -> bool {
        self.link_target.is_some()
    }
}

/// Mirror of `DirectoryContents`: entry cache plus visibility/sort options.
pub struct DirectoryContents {
    cache: HashMap<String, Vec<FileSystemEntry>>,
    pub show_hidden_files: bool,
    pub show_system_files: bool,
    /// Default sort, for directories without a saved one.
    pub sort_mode: SortMode,
    pub sort_ascending: bool,
    /// Rust only: sorts remembered per directory.
    pub path_sorts: super::sort_store::SortStore,
    /// Port of `DirectoryContents.DirSizes`: inline directory sizes, used
    /// when building directory entries.
    pub dir_sizes: Option<HashMap<String, i64>>,
}

impl Default for DirectoryContents {
    fn default() -> Self {
        Self {
            cache: HashMap::new(),
            show_hidden_files: false,
            show_system_files: false,
            sort_mode: SortMode::Name,
            sort_ascending: true,
            path_sorts: super::sort_store::SortStore::default(),
            dir_sizes: None,
        }
    }
}

impl DirectoryContents {
    #[must_use]
    pub fn new() -> Self {
        Self::default()
    }

    /// Mirrors `GetEntries`: drives list, cached entries, or a fresh load.
    pub fn get_entries(&mut self, path: &str) -> Vec<FileSystemEntry> {
        if path == DRIVES_PATH {
            return get_drive_entries();
        }

        if let Some(cached) = self.cache.get(path) {
            return cached.clone();
        }

        let mut entries = load_entries(path, self.show_hidden_files, self.show_system_files, self.dir_sizes.as_ref());
        // C# LoadEntries sorts with the instance's SortMode/SortAscending;
        // load_entries sorts by name, so ties keep name order (stable sort)
        let (sort_mode, sort_ascending, _) = self.sort_for(path);
        sort_entries(&mut entries, sort_mode, sort_ascending);
        self.cache.insert(path.to_string(), entries.clone());
        entries
    }

    /// Rust only: the directory's saved sort, or the default. The flag is
    /// true when the directory has a saved sort.
    #[must_use]
    pub fn sort_for(&self, path: &str) -> (SortMode, bool, bool) {
        match self.path_sorts.get(path) {
            Some((mode, ascending)) => (mode, ascending, true),
            None => (self.sort_mode, self.sort_ascending, false),
        }
    }

    /// Mirrors `DirectoryContents.IsDriveRoot`.
    #[must_use]
    pub fn is_drive_root(path: &str) -> bool {
        let full = absolute(path);
        match Path::new(path).components().next() {
            Some(std::path::Component::Prefix(_)) => {
                // Windows: compare against the root (e.g. "C:\")
                let root = drive_root(path);
                root.is_some_and(|r| r == full)
            }
            _ => full == "/",
        }
    }

    pub fn invalidate(&mut self, path: &str) {
        self.cache.remove(path);
    }

    pub fn invalidate_all(&mut self) {
        self.cache.clear();
    }
}

fn absolute(path: &str) -> String {
    PathBuf::from(path)
        .canonicalize()
        .map(|p| p.to_string_lossy().to_string())
        .unwrap_or_else(|_| PathBuf::from(path).to_string_lossy().to_string())
}

#[cfg(windows)]
#[must_use]
pub fn drive_root(path: &str) -> Option<String> {
    // "C:\foo\bar" -> "C:\"
    let bytes = path.as_bytes();
    if bytes.len() >= 3 && bytes[1] == b':' && (bytes[2] == b'\\' || bytes[2] == b'/') {
        return Some(path[..3].to_string());
    }
    if bytes.len() == 2 && bytes[1] == b':' {
        return Some(format!("{path}\\"));
    }
    None
}

#[cfg(not(windows))]
#[must_use]
pub fn drive_root(_path: &str) -> Option<String> {
    if Path::new(_path).is_absolute() { Some("/".to_string()) } else { None }
}

/// Port of `LoadEntries`: enumerate, filter, sort.
pub fn load_entries(
    path: &str,
    show_hidden: bool,
    show_system: bool,
    dir_sizes: Option<&HashMap<String, i64>>,
) -> Vec<FileSystemEntry> {
    let mut list = Vec::new();
    let dir_info = match std::fs::read_dir(path) {
        Ok(rd) => rd,
        Err(_) => return list, // silently skip inaccessible directories
    };

    for item in dir_info.flatten() {
        let name = item.file_name().to_string_lossy().to_string();
        let file_type = match item.file_type() {
            Ok(ft) => ft,
            Err(_) => continue,
        };
        let metadata = match item.metadata() {
            Ok(m) => m,
            Err(_) => continue,
        };

        #[cfg(windows)]
        let attributes = metadata.file_attributes();
        #[cfg(windows)]
        {
            const FILE_ATTRIBUTE_SYSTEM: u32 = 0x0004;
            const FILE_ATTRIBUTE_HIDDEN: u32 = 0x0002;
            if !show_system && attributes & FILE_ATTRIBUTE_SYSTEM != 0 {
                continue;
            }
            if !show_hidden && (attributes & FILE_ATTRIBUTE_HIDDEN != 0 || name.starts_with('.')) {
                continue;
            }
        }
        #[cfg(not(windows))]
        {
            let _ = show_system;
            if !show_hidden && name.starts_with('.') {
                continue;
            }
        }

        // .NET EnumerateDirectories lists directory symlinks and junctions:
        // on Windows by FILE_ATTRIBUTE_DIRECTORY, on unix by the link's
        // target. `FileType::is_dir` follows neither.
        #[cfg(windows)]
        let is_directory = attributes & 0x0010 != 0;
        #[cfg(not(windows))]
        let is_directory =
            file_type.is_dir() || (file_type.is_symlink() && std::fs::metadata(item.path()).is_ok_and(|m| m.is_dir()));
        let link_target = if file_type.is_symlink() {
            std::fs::read_link(item.path()).ok().map(|p| p.to_string_lossy().to_string())
        } else {
            None
        };

        // Resolve through the entry itself: a relative target is relative
        // to the link's directory, not the process's working directory.
        let is_broken_symlink = link_target.is_some() && std::fs::metadata(item.path()).is_err();

        let full_path = item.path().to_string_lossy().to_string();

        // Ports of CheckIsCloudPlaceholder / CheckIsJunctionPoint /
        // CheckIsAppExecLink: reparse queries only for entries carrying
        // FILE_ATTRIBUTE_REPARSE_POINT, junctions only for directories.
        #[cfg(windows)]
        let (is_cloud_placeholder, is_reparse_point) =
            (is_cloud_placeholder_attributes(attributes), attributes & 0x0400 != 0);
        #[cfg(not(windows))]
        let (is_cloud_placeholder, is_reparse_point) = (false, false);
        let is_junction_point = is_directory && is_reparse_point && super::reparse::is_junction_point(&full_path);
        let is_app_exec_link = !is_directory && is_reparse_point && super::reparse::is_app_exec_link(&full_path);
        let app_exec_link_target = if is_app_exec_link {
            super::reparse::get_app_exec_link_target(&full_path)
        } else {
            None
        };

        list.push(FileSystemEntry {
            name,
            full_path,
            is_directory,
            size: if is_directory {
                // Port of DirectoryContents.cs:193: inline dir sizes take
                // precedence over the placeholder size
                dir_sizes
                    .and_then(|sizes| sizes.get(&item.path().to_string_lossy().to_string()).copied())
                    .unwrap_or(0)
            } else {
                i64::try_from(metadata.len()).unwrap_or(0)
            },
            last_modified: system_time_to_date_parts(metadata.modified().unwrap_or(SystemTime::UNIX_EPOCH)),
            link_target,
            is_broken_symlink,
            is_drive: false,
            is_cloud_placeholder,
            is_junction_point,
            is_app_exec_link,
            app_exec_link_target,
            drive_media_type: crate::fs::DriveMediaType::Unknown,
            drive_format: None,
            drive_label: None,
            drive_free_space: 0,
            drive_total_size: 0,
        });
    }

    sort_entries(&mut list, SortMode::default(), true);
    list
}

/// Port of `SortEntries`: directories always sort first.
pub fn sort_entries(list: &mut [FileSystemEntry], sort_mode: SortMode, sort_ascending: bool) {
    list.sort_by(|a, b| {
        if a.is_directory != b.is_directory {
            return if a.is_directory {
                std::cmp::Ordering::Less
            } else {
                std::cmp::Ordering::Greater
            };
        }

        let cmp = match sort_mode {
            SortMode::Modified => compare_date_parts(&a.last_modified, &b.last_modified),
            SortMode::Size => a.size.cmp(&b.size),
            SortMode::Extension => {
                let a_ext = crate::ui::file_icons::get_extension(&a.name).to_ascii_lowercase();
                let b_ext = crate::ui::file_icons::get_extension(&b.name).to_ascii_lowercase();
                let ext = a_ext.cmp(&b_ext);
                if ext != std::cmp::Ordering::Equal {
                    ext
                } else {
                    a.name.to_uppercase().cmp(&b.name.to_uppercase())
                }
            }
            SortMode::Name => a.name.to_uppercase().cmp(&b.name.to_uppercase()),
        };

        if sort_ascending { cmp } else { cmp.reverse() }
    });
}

fn compare_date_parts(
    a: &super::super::ui::format_helpers::DateParts,
    b: &super::super::ui::format_helpers::DateParts,
) -> std::cmp::Ordering {
    (a.year, a.month, a.day, a.hour, a.minute, a.second, a.nanosecond)
        .cmp(&(b.year, b.month, b.day, b.hour, b.minute, b.second, b.nanosecond))
}

/// Converts a `SystemTime` into local wall-clock fields (C#
/// `LastWriteTime` is local time).
#[must_use]
pub fn system_time_to_date_parts(t: SystemTime) -> super::super::ui::format_helpers::DateParts {
    use chrono::{Datelike, Timelike};

    let local: chrono::DateTime<chrono::Local> = t.into();
    super::super::ui::format_helpers::DateParts {
        year: local.year(),
        month: local.month(),
        day: local.day(),
        hour: local.hour(),
        minute: local.minute(),
        second: local.second(),
        nanosecond: local.nanosecond(),
    }
}

/// Port of `GetDriveEntries`.
#[cfg(windows)]
pub fn get_drive_entries() -> Vec<FileSystemEntry> {
    use windows_sys::Win32::Storage::FileSystem::{
        GetDiskFreeSpaceExW, GetLogicalDriveStringsW, GetVolumeInformationW,
    };
    let mut list = Vec::new();
    unsafe {
        let mut buf = [0u16; 512];
        let len = GetLogicalDriveStringsW(buf.len() as u32, buf.as_mut_ptr());
        if len == 0 || len as usize > buf.len() {
            return list;
        }

        let mut start = 0usize;
        for i in 0..len as usize {
            if buf[i] == 0 {
                let drive: String = String::from_utf16_lossy(&buf[start..i]);
                start = i + 1;
                if drive.is_empty() {
                    continue;
                }

                let mut free: u64 = 0;
                let mut total: u64 = 0;
                let mut free_for_user: u64 = 0;
                let drive_utf16: Vec<u16> = drive.encode_utf16().chain(std::iter::once(0)).collect();

                let spaces_ok =
                    GetDiskFreeSpaceExW(drive_utf16.as_ptr(), &mut free_for_user, &mut total, &mut free) != 0;

                let mut volume_buf = [0u16; 261];
                let mut fs_buf = [0u16; 64];

                let info_ok = GetVolumeInformationW(
                    drive_utf16.as_ptr(),
                    volume_buf.as_mut_ptr(),
                    volume_buf.len() as u32,
                    std::ptr::null_mut(),
                    std::ptr::null_mut(),
                    std::ptr::null_mut(),
                    fs_buf.as_mut_ptr(),
                    fs_buf.len() as u32,
                ) != 0;

                let label = info_ok.then(|| utf16_until_nul(&volume_buf)).filter(|l| !l.is_empty());
                let format = info_ok.then(|| utf16_until_nul(&fs_buf)).filter(|f| !f.is_empty());

                // Mirrors C#: trim trailing separators for names longer than "X:\"
                let name = if drive.len() > 2 {
                    drive.trim_end_matches(['\\', '/']).to_string()
                } else {
                    drive.clone()
                };

                if !spaces_ok {
                    continue; // mirrors IsReady check
                }

                let root = drive.to_uppercase();
                list.push(FileSystemEntry {
                    name,
                    full_path: root,
                    is_directory: true,
                    size: 0,
                    last_modified: super::super::ui::format_helpers::DateParts::default(),
                    link_target: None,
                    is_broken_symlink: false,
                    is_drive: true,
                    is_cloud_placeholder: false,
                    is_junction_point: false,
                    is_app_exec_link: false,
                    app_exec_link_target: None,
                    drive_media_type: super::drive_media_type::detect(&drive),
                    drive_format: format,
                    drive_label: label,
                    drive_free_space: i64::try_from(free).unwrap_or(0),
                    drive_total_size: i64::try_from(total).unwrap_or(0),
                });
            }
        }
    }

    list
}

#[cfg(windows)]
fn utf16_until_nul(buf: &[u16]) -> String {
    let len = buf.iter().position(|&c| c == 0).unwrap_or(buf.len());
    String::from_utf16_lossy(&buf[..len])
}

/// Port of `IsCloudPlaceholderAttributes`: FILE_ATTRIBUTE_RECALL_ON_DATA_ACCESS
/// or FILE_ATTRIBUTE_RECALL_ON_OPEN.
#[must_use]
pub const fn is_cloud_placeholder_attributes(attribute_bits: u32) -> bool {
    const RECALL_ON_DATA_ACCESS: u32 = 0x0040_0000;
    const RECALL_ON_OPEN: u32 = 0x0004_0000;
    attribute_bits & (RECALL_ON_DATA_ACCESS | RECALL_ON_OPEN) != 0
}

#[cfg(not(windows))]
pub fn get_drive_entries() -> Vec<FileSystemEntry> {
    Vec::new()
}

/// Port of `PathCompletion.CapitalizeDriveLetter` (the pieces the spine
/// needs): uppercases a leading drive letter, e.g. "c:\foo" -> "C:\foo".
#[must_use]
pub fn capitalize_drive_letter(path: &str) -> String {
    let mut chars = path.chars();
    match chars.next() {
        Some(c) if c.is_ascii_lowercase() && chars.next() == Some(':') => {
            let mut owned = path.to_string();
            owned.replace_range(0..1, &c.to_ascii_uppercase().to_string());
            owned
        }
        _ => path.to_string(),
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn is_cloud_placeholder_attributes_detects_recall_flags() {
        for (bits, expected) in [
            (0x0040_0000, true),
            (0x0004_0000, true),
            (0x0044_0000, true),
            (0x0000_4000, false), // FILE_ATTRIBUTE_ENCRYPTED, not a placeholder
            (0x0000_0020, false),
            (0x0000_0000, false),
        ] {
            assert_eq!(is_cloud_placeholder_attributes(bits), expected, "{bits:#x}");
        }
    }

    #[cfg(unix)]
    #[test]
    fn directory_symlinks_are_listed_as_directories() {
        let root = std::env::temp_dir().join(format!("wade-dirlink-{}", std::process::id()));
        let _ = std::fs::remove_dir_all(&root);
        std::fs::create_dir_all(root.join("real")).unwrap();
        std::fs::write(root.join("file.txt"), "x").unwrap();
        std::os::unix::fs::symlink(root.join("real"), root.join("link")).unwrap();
        std::os::unix::fs::symlink(root.join("missing"), root.join("broken")).unwrap();

        let entries = load_entries(&root.to_string_lossy(), true, true, None);
        let kind = |name: &str| entries.iter().find(|e| e.name == name).map(|e| (e.is_directory, e.is_symlink()));

        assert_eq!(kind("real"), Some((true, false)));
        assert_eq!(kind("link"), Some((true, true)));
        assert_eq!(kind("broken"), Some((false, true)));
        assert_eq!(kind("file.txt"), Some((false, false)));
        assert!(entries.iter().all(|e| !e.is_cloud_placeholder && !e.is_junction_point && !e.is_app_exec_link));
        std::fs::remove_dir_all(&root).unwrap();
    }

    fn sort_probe_entry(name: &str, second: u32, nanosecond: u32) -> FileSystemEntry {
        FileSystemEntry {
            name: name.to_string(),
            full_path: name.to_string(),
            is_directory: false,
            size: 0,
            last_modified: crate::ui::format_helpers::DateParts {
                year: 2024,
                month: 1,
                day: 2,
                hour: 3,
                minute: 4,
                second,
                nanosecond,
            },
            link_target: None,
            is_broken_symlink: false,
            is_drive: false,
            is_cloud_placeholder: false,
            is_junction_point: false,
            is_app_exec_link: false,
            app_exec_link_target: None,
            drive_media_type: crate::fs::DriveMediaType::default(),
            drive_format: None,
            drive_label: None,
            drive_free_space: 0,
            drive_total_size: 0,
        }
    }

    #[test]
    fn sort_by_modified_uses_sub_minute_precision() {
        let mut list =
            vec![sort_probe_entry("a", 30, 0), sort_probe_entry("b", 10, 500), sort_probe_entry("c", 10, 100)];
        sort_entries(&mut list, SortMode::Modified, true);
        let names: Vec<&str> = list.iter().map(|e| e.name.as_str()).collect();
        assert_eq!(names, ["c", "b", "a"]);
    }

    #[test]
    fn get_entries_applies_sort_mode_and_direction() {
        let root = std::env::temp_dir().join(format!("wade-getsort-{}", std::process::id()));
        let _ = std::fs::remove_dir_all(&root);
        std::fs::create_dir_all(root.join("dir")).unwrap();
        std::fs::write(root.join("a.txt"), vec![0u8; 10]).unwrap();
        std::fs::write(root.join("b.md"), vec![0u8; 1000]).unwrap();
        std::fs::write(root.join("c.rs"), vec![0u8; 100]).unwrap();
        let path = root.to_string_lossy().into_owned();
        let mut contents = DirectoryContents::new();
        let mut names = |mode: SortMode, ascending: bool| -> Vec<String> {
            contents.sort_mode = mode;
            contents.sort_ascending = ascending;
            contents.invalidate_all();
            contents.get_entries(&path).into_iter().map(|e| e.name).collect()
        };

        // Directories stay first in every mode and direction
        assert_eq!(names(SortMode::Name, true), ["dir", "a.txt", "b.md", "c.rs"]);
        assert_eq!(names(SortMode::Name, false), ["dir", "c.rs", "b.md", "a.txt"]);
        assert_eq!(names(SortMode::Size, true), ["dir", "a.txt", "c.rs", "b.md"]);
        assert_eq!(names(SortMode::Size, false), ["dir", "b.md", "c.rs", "a.txt"]);
        assert_eq!(names(SortMode::Extension, true), ["dir", "b.md", "c.rs", "a.txt"]);
        std::fs::remove_dir_all(&root).unwrap();
    }

    #[test]
    fn a_saved_sort_applies_to_its_directory_only() {
        let root = std::env::temp_dir().join(format!("wade-pathsort-{}", std::process::id()));
        let _ = std::fs::remove_dir_all(&root);
        for dir in ["one", "two"] {
            std::fs::create_dir_all(root.join(dir)).unwrap();
            std::fs::write(root.join(dir).join("a.txt"), vec![0u8; 10]).unwrap();
            std::fs::write(root.join(dir).join("b.txt"), vec![0u8; 1000]).unwrap();
        }
        let one = root.join("one").to_string_lossy().into_owned();
        let two = root.join("two").to_string_lossy().into_owned();
        let mut contents = DirectoryContents::new();
        contents.path_sorts.set(&one, SortMode::Size, false);
        let names = |contents: &mut DirectoryContents, path: &str| -> Vec<String> {
            contents.get_entries(path).into_iter().map(|e| e.name).collect()
        };

        assert_eq!(names(&mut contents, &one), ["b.txt", "a.txt"]);
        assert_eq!(names(&mut contents, &two), ["a.txt", "b.txt"], "the default applies elsewhere");
        assert_eq!(contents.sort_for(&one), (SortMode::Size, false, true));
        assert_eq!(contents.sort_for(&two), (SortMode::Name, true, false));
        std::fs::remove_dir_all(&root).unwrap();
    }

    #[test]
    fn relative_symlink_is_resolved_against_its_own_directory() {
        let root = std::env::temp_dir().join(format!("wade-rellink-{}", std::process::id()));
        let _ = std::fs::remove_dir_all(&root);
        std::fs::create_dir_all(&root).unwrap();
        std::fs::write(root.join("real.txt"), "x").unwrap();

        #[cfg(unix)]
        let bad = std::os::unix::fs::symlink("gone.txt", root.join("bad"));
        #[cfg(windows)]
        let bad = std::os::windows::fs::symlink_file("gone.txt", root.join("bad"));

        if link_file(Path::new("real.txt"), &root.join("good")) && bad.is_ok() {
            let entries = load_entries(&root.to_string_lossy(), true, true, None);
            let broken = |name: &str| entries.iter().find(|e| e.name == name).map(|e| e.is_broken_symlink);

            assert_eq!(broken("good"), Some(false));
            assert_eq!(broken("bad"), Some(true));
        }

        std::fs::remove_dir_all(&root).unwrap();
    }

    /// Links a file and reads it back: false on Windows without Developer
    /// Mode (no link) and under Wine (links that cannot be followed).
    fn link_file(target: &Path, link: &Path) -> bool {
        #[cfg(unix)]
        let linked = std::os::unix::fs::symlink(target, link);
        #[cfg(windows)]
        let linked = std::os::windows::fs::symlink_file(target, link);

        let base = link.parent().unwrap_or(link);
        linked.is_ok() && std::fs::read(base.join(target)).is_ok() && std::fs::read(link).is_ok()
    }

    fn test_dir(name: &str) -> PathBuf {
        let root = std::env::temp_dir().join(format!("wade-dc-{name}-{}", std::process::id()));
        let _ = std::fs::remove_dir_all(&root);
        std::fs::create_dir_all(&root).unwrap();
        root
    }

    fn names(entries: &[FileSystemEntry]) -> Vec<&str> {
        entries.iter().map(|e| e.name.as_str()).collect()
    }

    #[cfg(windows)]
    fn set_attributes(path: &Path, attributes: u32) {
        use std::os::windows::ffi::OsStrExt;
        let wide: Vec<u16> = path.as_os_str().encode_wide().chain(std::iter::once(0)).collect();
        assert_ne!(
            unsafe { windows_sys::Win32::Storage::FileSystem::SetFileAttributesW(wide.as_ptr(), attributes) },
            0
        );
    }

    #[cfg(windows)]
    #[test]
    fn system_hidden_entries_need_both_settings() {
        use windows_sys::Win32::Storage::FileSystem::{
            FILE_ATTRIBUTE_DIRECTORY, FILE_ATTRIBUTE_HIDDEN, FILE_ATTRIBUTE_NORMAL, FILE_ATTRIBUTE_SYSTEM,
        };
        const BOTH: u32 = FILE_ATTRIBUTE_HIDDEN | FILE_ATTRIBUTE_SYSTEM;

        let root = test_dir("syshidden");
        std::fs::create_dir(root.join("SystemHiddenDir")).unwrap();
        for name in ["systemhidden.txt", "systemonly.txt", "hiddenonly.txt", "normal.txt"] {
            std::fs::write(root.join(name), "x").unwrap();
        }
        set_attributes(&root.join("SystemHiddenDir"), FILE_ATTRIBUTE_DIRECTORY | BOTH);
        set_attributes(&root.join("systemhidden.txt"), BOTH);
        set_attributes(&root.join("systemonly.txt"), FILE_ATTRIBUTE_SYSTEM);
        set_attributes(&root.join("hiddenonly.txt"), FILE_ATTRIBUTE_HIDDEN);

        let path = root.to_string_lossy().into_owned();
        let listed = |hidden: bool, system: bool| {
            let mut names: Vec<String> =
                load_entries(&path, hidden, system, None).into_iter().map(|e| e.name).collect();
            names.sort();
            names
        };
        assert_eq!(listed(false, false), ["normal.txt"]);
        assert_eq!(listed(true, false), ["hiddenonly.txt", "normal.txt"]);
        assert_eq!(listed(false, true), ["normal.txt", "systemonly.txt"]);
        assert_eq!(
            listed(true, true),
            ["SystemHiddenDir", "hiddenonly.txt", "normal.txt", "systemhidden.txt", "systemonly.txt"]
        );

        set_attributes(&root.join("SystemHiddenDir"), FILE_ATTRIBUTE_DIRECTORY);
        for name in ["systemhidden.txt", "systemonly.txt", "hiddenonly.txt"] {
            set_attributes(&root.join(name), FILE_ATTRIBUTE_NORMAL);
        }
        std::fs::remove_dir_all(&root).unwrap();
    }

    #[test]
    fn load_entries_of_a_missing_path_is_empty() {
        let missing = std::env::temp_dir().join(format!("wade-dc-missing-{}", std::process::id()));
        assert!(load_entries(&missing.to_string_lossy(), true, true, None).is_empty());
    }

    #[test]
    fn get_entries_is_cached_until_invalidated() {
        let root = test_dir("cache");
        std::fs::write(root.join("first.txt"), "1").unwrap();
        let path = root.to_string_lossy().into_owned();
        let mut contents = DirectoryContents::new();
        assert_eq!(names(&contents.get_entries(&path)), ["first.txt"]);

        std::fs::write(root.join("second.txt"), "2").unwrap();
        assert_eq!(names(&contents.get_entries(&path)), ["first.txt"], "served from the cache");

        contents.invalidate(&path);
        assert_eq!(names(&contents.get_entries(&path)), ["first.txt", "second.txt"]);

        std::fs::write(root.join("third.txt"), "3").unwrap();
        contents.invalidate_all();
        assert_eq!(names(&contents.get_entries(&path)), ["first.txt", "second.txt", "third.txt"]);
        std::fs::remove_dir_all(&root).unwrap();
    }

    #[test]
    fn sort_by_size_uses_inline_dir_sizes_for_directories() {
        let root = test_dir("dirsizes");
        std::fs::create_dir(root.join("big_dir")).unwrap();
        std::fs::create_dir(root.join("small_dir")).unwrap();
        let path = root.to_string_lossy().into_owned();
        let mut contents = DirectoryContents {
            sort_mode: SortMode::Size,
            dir_sizes: Some(HashMap::from([
                (root.join("small_dir").to_string_lossy().into_owned(), 100),
                (root.join("big_dir").to_string_lossy().into_owned(), 5000),
            ])),
            ..DirectoryContents::default()
        };
        // Name order is big_dir, small_dir: only the sizes put small_dir first
        assert_eq!(names(&contents.get_entries(&path)), ["small_dir", "big_dir"]);

        contents.sort_ascending = false;
        contents.invalidate_all();
        assert_eq!(names(&contents.get_entries(&path)), ["big_dir", "small_dir"]);
        std::fs::remove_dir_all(&root).unwrap();
    }

    #[test]
    fn sort_by_extension_breaks_ties_by_name() {
        let mut list =
            vec![sort_probe_entry("c.txt", 0, 0), sort_probe_entry("a.txt", 0, 0), sort_probe_entry("b.md", 0, 0)];
        sort_entries(&mut list, SortMode::Extension, true);
        assert_eq!(names(&list), ["b.md", "a.txt", "c.txt"]);
    }

    #[test]
    fn sort_by_modified_orders_real_files_by_write_time() {
        let root = test_dir("mtime");
        let base = SystemTime::now() - std::time::Duration::from_secs(3600);
        for (name, offset) in [("old.txt", 0), ("new.txt", 120), ("mid.txt", 60)] {
            let file = std::fs::File::create(root.join(name)).unwrap();
            file.set_modified(base + std::time::Duration::from_secs(offset)).unwrap();
        }
        let path = root.to_string_lossy().into_owned();
        let mut contents = DirectoryContents {
            sort_mode: SortMode::Modified,
            ..DirectoryContents::default()
        };
        assert_eq!(names(&contents.get_entries(&path)), ["old.txt", "mid.txt", "new.txt"]);

        contents.sort_ascending = false;
        contents.invalidate_all();
        assert_eq!(names(&contents.get_entries(&path)), ["new.txt", "mid.txt", "old.txt"]);
        std::fs::remove_dir_all(&root).unwrap();
    }

    #[test]
    fn capitalize_drive_letter_matches_csharp_cases() {
        for (input, expected) in [
            (r"c:\Users\foo", r"C:\Users\foo"),
            (r"d:\", r"D:\"),
            (r"C:\Users\foo", r"C:\Users\foo"),
            (r"D:\", r"D:\"),
            ("/usr/local/bin", "/usr/local/bin"),
            ("relative/path", "relative/path"),
            ("", ""),
            ("x", "x"),
        ] {
            assert_eq!(capitalize_drive_letter(input), expected, "{input:?}");
        }
    }

    #[test]
    fn file_symlinks_are_detected() {
        let root = test_dir("filelink");
        std::fs::write(root.join("target.txt"), "x").unwrap();

        if link_file(&root.join("target.txt"), &root.join("link.txt")) {
            let entries = load_entries(&root.to_string_lossy(), true, true, None);
            let link = entries.iter().find(|e| e.name == "link.txt").expect("link listed");
            assert!(link.is_symlink() && !link.is_directory && !link.is_broken_symlink);
            assert_eq!(link.link_target.as_deref(), Some(root.join("target.txt").to_string_lossy().as_ref()));
            let target = entries.iter().find(|e| e.name == "target.txt").expect("target listed");
            assert!(!target.is_symlink());
        }
        std::fs::remove_dir_all(&root).unwrap();
    }

    #[test]
    fn system_time_to_date_parts_matches_chrono_local() {
        use chrono::{Datelike, Timelike};

        let t = SystemTime::UNIX_EPOCH + std::time::Duration::from_secs(1_700_000_000);
        let local: chrono::DateTime<chrono::Local> = t.into();
        let parts = system_time_to_date_parts(t);
        assert_eq!(
            (parts.year, parts.month, parts.day, parts.hour, parts.minute),
            (local.year(), local.month(), local.day(), local.hour(), local.minute())
        );
    }
}
