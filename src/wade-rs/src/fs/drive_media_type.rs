//! Port of src/Wade/FileSystem/DriveMediaType.cs and DriveTypeDetector.cs.
//! Windows queries the volume's seek penalty
//! (`IOCTL_STORAGE_QUERY_PROPERTY`); Linux reads
//! `/sys/block/<device>/queue/rotational` for the device mounted at the
//! root. The .NET `DriveInfo.DriveType` it starts from is `GetDriveTypeW`
//! on Windows and, on Linux, the mount's file-system type (a reduced
//! version of .NET's table: network, RAM and optical file systems are
//! recognised, everything else is Fixed).

/// Port of `DriveMediaType`.
#[derive(Clone, Copy, Debug, Default, PartialEq, Eq)]
pub enum DriveMediaType {
    Ssd,
    Hdd,
    Network,
    Removable,
    #[default]
    Unknown,
}

/// Port of .NET `DriveType`, named as `DriveType.ToString()` prints it.
#[derive(Clone, Copy, Debug, PartialEq, Eq)]
pub enum DriveType {
    Unknown,
    NoRootDirectory,
    Removable,
    Fixed,
    Network,
    CDRom,
    Ram,
}

impl DriveType {
    #[must_use]
    pub const fn name(self) -> &'static str {
        match self {
            Self::Unknown => "Unknown",
            Self::NoRootDirectory => "NoRootDirectory",
            Self::Removable => "Removable",
            Self::Fixed => "Fixed",
            Self::Network => "Network",
            Self::CDRom => "CDRom",
            Self::Ram => "Ram",
        }
    }
}

/// Port of `DriveTypeDetector.Detect(new DriveInfo(root))`.
#[must_use]
pub fn detect(root: &str) -> DriveMediaType {
    match drive_type(root) {
        DriveType::Network => DriveMediaType::Network,
        DriveType::Removable => DriveMediaType::Removable,
        DriveType::Fixed => detect_fixed(root),
        _ => DriveMediaType::Unknown,
    }
}

/// .NET `DriveInfo(root).DriveType`.
#[must_use]
pub fn drive_type(root: &str) -> DriveType {
    #[cfg(windows)]
    {
        windows::drive_type(root)
    }
    #[cfg(target_os = "linux")]
    {
        linux_mount_entry(root).map_or(DriveType::Fixed, |(_, fs_type)| drive_type_from_fs_type(&fs_type))
    }
    #[cfg(not(any(windows, target_os = "linux")))]
    {
        let _ = root;
        DriveType::Fixed
    }
}

fn detect_fixed(root: &str) -> DriveMediaType {
    #[cfg(windows)]
    {
        root.chars().next().map_or(DriveMediaType::Unknown, windows::query_seek_penalty)
    }
    #[cfg(target_os = "linux")]
    {
        detect_linux_media_type(root)
    }
    #[cfg(not(any(windows, target_os = "linux")))]
    {
        let _ = root;
        DriveMediaType::Unknown
    }
}

/// Port of `ParseRotationalValue`.
#[must_use]
pub fn parse_rotational_value(content: Option<&str>) -> DriveMediaType {
    match content {
        Some("0") => DriveMediaType::Ssd,
        Some("1") => DriveMediaType::Hdd,
        _ => DriveMediaType::Unknown,
    }
}

/// Port of `ParseSeekPenaltyResult`.
#[must_use]
pub const fn parse_seek_penalty_result(incurs_seek_penalty: bool) -> DriveMediaType {
    if incurs_seek_penalty { DriveMediaType::Hdd } else { DriveMediaType::Ssd }
}

/// Port of `ExtractBaseDevice`: /dev/sda1 → sda, /dev/nvme0n1p1 → nvme0n1.
#[must_use]
pub fn extract_base_device(device_path: &str) -> Option<String> {
    let device = device_path.rsplit(['/', '\\']).next().unwrap_or("");

    if device.is_empty() {
        return None;
    }

    if device.starts_with("nvme") {
        let p_idx = device.rfind('p');
        let n_idx = device.rfind('n');

        if let Some(p) = p_idx
            && p > 0
            && n_idx.is_none_or(|n| p > n)
            && p < device.len() - 1
            && device.as_bytes()[p + 1].is_ascii_digit()
        {
            return Some(device[..p].to_string());
        }

        return Some(device.to_string());
    }

    let trimmed = device.trim_end_matches(|c: char| c.is_ascii_digit());
    (!trimmed.is_empty()).then(|| trimmed.to_string())
}

/// The .NET Unix `DriveInfo` file-system-type mapping, reduced to the
/// families that matter for `Detect`.
#[must_use]
pub fn drive_type_from_fs_type(fs_type: &str) -> DriveType {
    match fs_type {
        "nfs" | "nfs4" | "cifs" | "smbfs" | "smb3" | "smb2" | "ncpfs" | "afs" | "9p" | "fuse.sshfs" | "sshfs"
        | "davfs" | "fuse.rclone" | "ceph" | "glusterfs" | "fuse.glusterfs" | "lustre" | "coda" => DriveType::Network,
        "tmpfs" | "ramfs" | "proc" | "sysfs" | "devtmpfs" | "devpts" | "cgroup" | "cgroup2" | "debugfs"
        | "securityfs" | "pstore" | "mqueue" | "hugetlbfs" | "tracefs" | "configfs" | "bpf" | "fusectl" => DriveType::Ram,
        "iso9660" | "udf" => DriveType::CDRom,
        _ => DriveType::Fixed,
    }
}

/// Finds the /proc/mounts line for `mount_point` (as `ResolveLinuxBlockDevice`
/// does): returns (device path, file-system type).
#[cfg(target_os = "linux")]
fn linux_mount_entry(mount_point: &str) -> Option<(String, String)> {
    let mounts = std::fs::read_to_string("/proc/mounts").ok()?;
    find_mount_entry(&mounts, mount_point)
}

/// Pure half of the /proc/mounts lookup: the first line whose mount point
/// matches (with or without a trailing '/').
#[must_use]
pub fn find_mount_entry(mounts: &str, mount_point: &str) -> Option<(String, String)> {
    let trimmed = mount_point.trim_end_matches('/');

    for line in mounts.lines() {
        let mut parts = line.splitn(4, ' ');
        let (Some(device), Some(point)) = (parts.next(), parts.next()) else {
            continue;
        };

        if point == mount_point || point == trimmed {
            return Some((device.to_string(), parts.next().unwrap_or("").to_string()));
        }
    }

    None
}

#[cfg(target_os = "linux")]
fn detect_linux_media_type(root: &str) -> DriveMediaType {
    let Some(device) = linux_mount_entry(root).and_then(|(device, _)| extract_base_device(&device)) else {
        return DriveMediaType::Unknown;
    };

    std::fs::read_to_string(format!("/sys/block/{device}/queue/rotational"))
        .map_or(DriveMediaType::Unknown, |content| parse_rotational_value(Some(content.trim())))
}

#[cfg(windows)]
mod windows {
    use super::{parse_seek_penalty_result, DriveMediaType, DriveType};
    use windows_sys::Win32::Foundation::{CloseHandle, INVALID_HANDLE_VALUE};
    use windows_sys::Win32::Storage::FileSystem::{
        CreateFileW, GetDriveTypeW, FILE_SHARE_READ, FILE_SHARE_WRITE, OPEN_EXISTING,
    };
    use windows_sys::Win32::System::IO::DeviceIoControl;

    const IOCTL_STORAGE_QUERY_PROPERTY: u32 = 0x002D_1400;
    const STORAGE_DEVICE_SEEK_PENALTY_PROPERTY: i32 = 7;
    const PROPERTY_STANDARD_QUERY: i32 = 0;

    #[repr(C)]
    struct StoragePropertyQuery {
        property_id: i32,
        query_type: i32,
        additional_parameters: u8,
    }

    #[repr(C)]
    struct DeviceSeekPenaltyDescriptor {
        version: u32,
        size: u32,
        incurs_seek_penalty: u8,
    }

    pub(super) fn drive_type(root: &str) -> DriveType {
        let mut root = root.to_string();

        if !root.ends_with(['\\', '/']) {
            root.push('\\');
        }

        let wide: Vec<u16> = root.encode_utf16().chain(std::iter::once(0)).collect();

        match unsafe { GetDriveTypeW(wide.as_ptr()) } {
            1 => DriveType::NoRootDirectory,
            2 => DriveType::Removable,
            3 => DriveType::Fixed,
            4 => DriveType::Network,
            5 => DriveType::CDRom,
            6 => DriveType::Ram,
            _ => DriveType::Unknown,
        }
    }

    pub(super) fn query_seek_penalty(drive_letter: char) -> DriveMediaType {
        let volume: Vec<u16> = format!(r"\\.\{drive_letter}:").encode_utf16().chain(std::iter::once(0)).collect();
        let handle = unsafe {
            CreateFileW(
                volume.as_ptr(),
                0,
                FILE_SHARE_READ | FILE_SHARE_WRITE,
                std::ptr::null(),
                OPEN_EXISTING,
                0,
                std::ptr::null_mut(),
            )
        };

        if handle == INVALID_HANDLE_VALUE {
            return DriveMediaType::Unknown;
        }

        let query = StoragePropertyQuery {
            property_id: STORAGE_DEVICE_SEEK_PENALTY_PROPERTY,
            query_type: PROPERTY_STANDARD_QUERY,
            additional_parameters: 0,
        };
        let mut descriptor = DeviceSeekPenaltyDescriptor { version: 0, size: 0, incurs_seek_penalty: 0 };
        let mut returned = 0u32;
        let ok = unsafe {
            let ok = DeviceIoControl(
                handle,
                IOCTL_STORAGE_QUERY_PROPERTY,
                std::ptr::from_ref(&query).cast(),
                std::mem::size_of::<StoragePropertyQuery>() as u32,
                std::ptr::from_mut(&mut descriptor).cast(),
                std::mem::size_of::<DeviceSeekPenaltyDescriptor>() as u32,
                &mut returned,
                std::ptr::null_mut(),
            );
            CloseHandle(handle);
            ok
        };

        if ok == 0 {
            return DriveMediaType::Unknown;
        }

        parse_seek_penalty_result(descriptor.incurs_seek_penalty != 0)
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn parse_rotational_value_returns_expected() {
        for (content, expected) in [
            (Some("0"), DriveMediaType::Ssd),
            (Some("1"), DriveMediaType::Hdd),
            (None, DriveMediaType::Unknown),
            (Some(""), DriveMediaType::Unknown),
            (Some("2"), DriveMediaType::Unknown),
            (Some("abc"), DriveMediaType::Unknown),
        ] {
            assert_eq!(parse_rotational_value(content), expected, "{content:?}");
        }
    }

    #[test]
    fn parse_seek_penalty_result_returns_expected() {
        assert_eq!(parse_seek_penalty_result(true), DriveMediaType::Hdd);
        assert_eq!(parse_seek_penalty_result(false), DriveMediaType::Ssd);
    }

    #[test]
    fn extract_base_device_returns_expected() {
        for (path, expected) in [
            ("/dev/sda1", Some("sda")),
            ("/dev/sda", Some("sda")),
            ("/dev/vdb2", Some("vdb")),
            ("/dev/nvme0n1p1", Some("nvme0n1")),
            ("/dev/nvme0n1", Some("nvme0n1")),
            ("/dev/nvme1n1p3", Some("nvme1n1")),
            ("/dev/xvda1", Some("xvda")),
            ("", None),
        ] {
            assert_eq!(extract_base_device(path).as_deref(), expected, "{path}");
        }
    }

    #[test]
    fn find_mount_entry_matches_with_and_without_trailing_slash() {
        let mounts = "proc /proc proc rw 0 0\n/dev/nvme0n1p2 / ext4 rw,relatime 0 0\nserver:/x /mnt/nfs nfs4 rw 0 0\n";
        assert_eq!(find_mount_entry(mounts, "/"), Some(("/dev/nvme0n1p2".to_string(), "ext4".to_string())));
        assert_eq!(find_mount_entry(mounts, "/mnt/nfs/"), Some(("server:/x".to_string(), "nfs4".to_string())));
        assert_eq!(find_mount_entry(mounts, "/missing"), None);
    }

    #[test]
    fn drive_type_from_fs_type_maps_families() {
        assert_eq!(drive_type_from_fs_type("ext4"), DriveType::Fixed);
        assert_eq!(drive_type_from_fs_type("nfs4"), DriveType::Network);
        assert_eq!(drive_type_from_fs_type("tmpfs"), DriveType::Ram);
        assert_eq!(drive_type_from_fs_type("iso9660"), DriveType::CDRom);
        assert_eq!(DriveType::CDRom.name(), "CDRom");
    }

    #[test]
    fn detect_does_not_panic_for_current_root() {
        let root = if cfg!(windows) { "C:\\" } else { "/" };
        let _ = detect(root);
    }
}
