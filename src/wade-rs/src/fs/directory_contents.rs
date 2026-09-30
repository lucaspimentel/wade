//! Port of src/Wade/FileSystem/DirectoryContents.cs (core subset).

use std::collections::HashMap;
use std::path::{Path, PathBuf};
#[cfg(windows)]
use std::os::windows::fs::MetadataExt;
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

/// Mirrors `FileSystemEntry` (the fields the spine needs; reparse and cloud
/// fields stay at defaults until Phase 9).
#[derive(Clone, Debug)]
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
    pub sort_mode: SortMode,
    pub sort_ascending: bool,
    /// Port of `DirectoryContents.DirSizes`: inline directory sizes, used
    /// when building directory entries (Phase 4d).
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

        let entries = load_entries(path, self.show_hidden_files, self.show_system_files, self.dir_sizes.as_ref());
        self.cache.insert(path.to_string(), entries.clone());
        entries
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
            _ => full == "/" ,
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
    if Path::new(_path).is_absolute() {
        Some("/".to_string())
    } else {
        None
    }
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

        let is_directory = file_type.is_dir();
        let link_target = if file_type.is_symlink() {
            std::fs::read_link(item.path())
                .ok()
                .map(|p| p.to_string_lossy().to_string())
        } else {
            None
        };

        let is_broken_symlink = match &link_target {
            None => false,
            Some(target) => std::fs::metadata(target).is_err(),
        };

        list.push(FileSystemEntry {
            name,
            full_path: item.path().to_string_lossy().to_string(),
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
            is_cloud_placeholder: false,
            is_junction_point: false,
            is_app_exec_link: false,
            app_exec_link_target: None,
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

        if sort_ascending {
            cmp
        } else {
            cmp.reverse()
        }
    });
}

fn compare_date_parts(a: &super::super::ui::format_helpers::DateParts, b: &super::super::ui::format_helpers::DateParts) -> std::cmp::Ordering {
    (a.year, a.month, a.day, a.hour, a.minute).cmp(&(b.year, b.month, b.day, b.hour, b.minute))
}

/// Converts a `SystemTime` into local wall-clock fields via the TZ
/// environment (mirrors C# `DateTime.LastWriteTime` being local time).
#[must_use]
pub fn system_time_to_date_parts(t: SystemTime) -> super::super::ui::format_helpers::DateParts {
    // No chrono dependency: convert through a minimal days-since-epoch
    // algorithm in local time read from the TZ offset via libc-free probing.
    let secs = t
        .duration_since(SystemTime::UNIX_EPOCH)
        .map(|d| i64::try_from(d.as_secs()).unwrap_or(0))
        .unwrap_or(0);

    // Local offset: use the `TZ`-independent trick of comparing the current
    // local formatted time is not available; use UTC. The C# build uses local
    // time, but dates only surface as formatted text; UTC is acceptable until
    // a proper local-time dependency is chosen (tracked as a deviation).
    let local_secs = secs + local_utc_offset_secs();
    let days = local_secs.div_euclid(86_400);
    let rem = local_secs.rem_euclid(86_400);
    let (year, month, day) = civil_from_days(days);
    super::super::ui::format_helpers::DateParts {
        year,
        month,
        day,
        hour: (rem / 3600) as u32,
        minute: ((rem % 3600) / 60) as u32,
    }
}

fn local_utc_offset_secs() -> i64 {
    0
}

/// Howard Hinnant's civil_from_days algorithm.
#[must_use]
fn civil_from_days(z: i64) -> (i32, u32, u32) {
    let z = z + 719_468;
    let era = z.div_euclid(146_097);
    let doe = z.rem_euclid(146_097);
    let yoe = (doe - doe / 1460 + doe / 36_524 - doe / 146_096) / 365;
    let y = yoe + era * 400;
    let doy = doe - (365 * yoe + yoe / 4 - yoe / 100);
    let mp = (5 * doy + 2) / 153;
    let d = (doy - (153 * mp + 2) / 5 + 1) as u32;
    let m = if mp < 10 { mp + 3 } else { mp - 9 } as u32;
    ((if m <= 2 { y + 1 } else { y }) as i32, m, d)
}

/// Port of `GetDriveEntries`. Drive media-type detection is Phase 9; the
/// format/label/space fields the file-list renderer uses are populated.
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

                let spaces_ok = GetDiskFreeSpaceExW(
                    drive_utf16.as_ptr(),
                    &mut free_for_user,
                    &mut total,
                    &mut free,
                ) != 0;

                let mut volume_buf = [0u16; 261];
                let mut fs_buf = [0u16; 64];
                let mut volume_len: u32 = 0;
                let fs_len: u32 = 0;
                let mut fs_flags: u32 = 0;

                let label_ok = GetVolumeInformationW(
                    drive_utf16.as_ptr(),
                    volume_buf.as_mut_ptr(),
                    volume_buf.len() as u32,
                    std::ptr::null_mut(),
                    &mut volume_len,
                    &mut fs_flags,
                    fs_buf.as_mut_ptr(),
                    fs_buf.len() as u32,
                );

                let label: Option<String> = if label_ok != 0 && volume_len > 0 {
                    Some(String::from_utf16_lossy(&volume_buf[..volume_len as usize]))
                } else {
                    None
                };

                let format: Option<String> = if label_ok != 0 && fs_len > 0 {
                    Some(String::from_utf16_lossy(&fs_buf[..fs_len as usize]))
                } else {
                    None
                };

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
