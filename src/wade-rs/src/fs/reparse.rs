//! Port of src/Wade/FileSystem/ReparsePointDetector.cs: junction points
//! (`IO_REPARSE_TAG_MOUNT_POINT`) and app execution aliases
//! (`IO_REPARSE_TAG_APPEXECLINK`). Windows-only; always false/None elsewhere.

const IO_REPARSE_TAG_MOUNT_POINT: u32 = 0xA000_0003;
const IO_REPARSE_TAG_APPEXECLINK: u32 = 0x8000_001B;

/// Reparse data buffer header: ReparseTag(4) + ReparseDataLength(2) + Reserved(2).
const REPARSE_DATA_HEADER_SIZE: usize = 8;
/// AppExecLink data starts with a Version DWORD.
const APP_EXEC_LINK_VERSION_SIZE: usize = 4;

#[must_use]
pub fn is_junction_point(path: &str) -> bool {
    #[cfg(windows)]
    {
        is_junction_tag(windows::get_reparse_tag(path))
    }
    #[cfg(not(windows))]
    {
        let _ = path;
        false
    }
}

#[must_use]
pub fn is_app_exec_link(path: &str) -> bool {
    #[cfg(windows)]
    {
        is_app_exec_link_tag(windows::get_reparse_tag(path))
    }
    #[cfg(not(windows))]
    {
        let _ = path;
        false
    }
}

#[must_use]
pub fn get_app_exec_link_target(path: &str) -> Option<String> {
    #[cfg(windows)]
    {
        windows::get_reparse_data_buffer(path).and_then(|buffer| parse_app_exec_link_target(&buffer))
    }
    #[cfg(not(windows))]
    {
        let _ = path;
        None
    }
}

#[must_use]
pub const fn is_junction_tag(reparse_tag: u32) -> bool {
    reparse_tag == IO_REPARSE_TAG_MOUNT_POINT
}

#[must_use]
pub const fn is_app_exec_link_tag(reparse_tag: u32) -> bool {
    reparse_tag == IO_REPARSE_TAG_APPEXECLINK
}

/// Port of `ParseAppExecLinkTarget`. Buffer layout: header(8), Version(4),
/// then 3 null-terminated UTF-16 strings (package ID, AUMID, target path);
/// the third string is the target executable path.
#[must_use]
pub fn parse_app_exec_link_target(buffer: &[u8]) -> Option<String> {
    let mut offset = REPARSE_DATA_HEADER_SIZE + APP_EXEC_LINK_VERSION_SIZE;

    if buffer.len() <= offset {
        return None;
    }

    for _ in 0..2 {
        offset = find_null_terminator(buffer, offset)? + 2;
    }

    let end = find_null_terminator(buffer, offset)?;

    if end <= offset {
        return None;
    }

    let units: Vec<u16> =
        buffer[offset..end].chunks_exact(2).map(|pair| u16::from_le_bytes([pair[0], pair[1]])).collect();
    Some(String::from_utf16_lossy(&units))
}

fn find_null_terminator(buffer: &[u8], offset: usize) -> Option<usize> {
    let mut i = offset;

    while i + 1 < buffer.len() {
        if buffer[i] == 0 && buffer[i + 1] == 0 {
            return Some(i);
        }

        i += 2;
    }

    None
}

#[cfg(windows)]
mod windows {
    use windows_sys::Win32::Foundation::{CloseHandle, HANDLE, INVALID_HANDLE_VALUE};
    use windows_sys::Win32::Storage::FileSystem::{
        CreateFileW, FILE_ATTRIBUTE_TAG_INFO, FILE_FLAG_BACKUP_SEMANTICS, FILE_FLAG_OPEN_REPARSE_POINT,
        FILE_SHARE_READ, FILE_SHARE_WRITE, FileAttributeTagInfo, GetFileInformationByHandleEx, OPEN_EXISTING,
    };
    use windows_sys::Win32::System::IO::DeviceIoControl;

    const FSCTL_GET_REPARSE_POINT: u32 = 0x0009_00A8;
    /// Max reparse data is 16 KB.
    const MAX_REPARSE_DATA: usize = 16 * 1024;

    struct Handle(HANDLE);

    impl Drop for Handle {
        fn drop(&mut self) {
            unsafe {
                CloseHandle(self.0);
            }
        }
    }

    fn open_reparse_point(path: &str) -> Option<Handle> {
        let wide: Vec<u16> = path.encode_utf16().chain(std::iter::once(0)).collect();
        let handle = unsafe {
            CreateFileW(
                wide.as_ptr(),
                0,
                FILE_SHARE_READ | FILE_SHARE_WRITE,
                std::ptr::null(),
                OPEN_EXISTING,
                FILE_FLAG_BACKUP_SEMANTICS | FILE_FLAG_OPEN_REPARSE_POINT,
                std::ptr::null_mut(),
            )
        };

        (handle != INVALID_HANDLE_VALUE).then_some(Handle(handle))
    }

    pub(super) fn get_reparse_tag(path: &str) -> u32 {
        let Some(handle) = open_reparse_point(path) else {
            return 0;
        };

        let mut info = FILE_ATTRIBUTE_TAG_INFO { FileAttributes: 0, ReparseTag: 0 };
        let ok = unsafe {
            GetFileInformationByHandleEx(
                handle.0,
                FileAttributeTagInfo,
                std::ptr::from_mut(&mut info).cast(),
                std::mem::size_of::<FILE_ATTRIBUTE_TAG_INFO>() as u32,
            )
        };

        if ok != 0 { info.ReparseTag } else { 0 }
    }

    pub(super) fn get_reparse_data_buffer(path: &str) -> Option<Vec<u8>> {
        let handle = open_reparse_point(path)?;
        let mut buffer = vec![0u8; MAX_REPARSE_DATA];
        let mut returned = 0u32;
        let ok = unsafe {
            DeviceIoControl(
                handle.0,
                FSCTL_GET_REPARSE_POINT,
                std::ptr::null(),
                0,
                buffer.as_mut_ptr().cast(),
                buffer.len() as u32,
                &mut returned,
                std::ptr::null_mut(),
            )
        };

        (ok != 0).then_some(buffer)
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    fn utf16(text: &str) -> Vec<u8> {
        text.encode_utf16().flat_map(u16::to_le_bytes).collect()
    }

    fn build_app_exec_link_buffer(package_id: &str, aumid: &str, target: &str) -> Vec<u8> {
        let mut buffer = Vec::new();
        buffer.extend_from_slice(&IO_REPARSE_TAG_APPEXECLINK.to_le_bytes());
        buffer.extend_from_slice(&[0, 0, 0, 0]);
        buffer.extend_from_slice(&3u32.to_le_bytes());

        for text in [package_id, aumid, target] {
            buffer.extend(utf16(text));
            buffer.extend_from_slice(&[0, 0]);
        }

        buffer
    }

    #[test]
    fn is_junction_tag_returns_expected() {
        for (tag, expected) in
            [(0xA000_0003, true), (0xA000_000C, false), (0x8000_001B, false), (0, false), (0x8000_0017, false)]
        {
            assert_eq!(is_junction_tag(tag), expected, "{tag:#x}");
        }
    }

    #[test]
    fn is_app_exec_link_tag_returns_expected() {
        for (tag, expected) in [(0x8000_001B, true), (0xA000_0003, false), (0xA000_000C, false), (0, false)] {
            assert_eq!(is_app_exec_link_tag(tag), expected, "{tag:#x}");
        }
    }

    #[test]
    fn parse_app_exec_link_target_valid_buffer_returns_target_path() {
        let target = r"C:\Program Files\WindowsApps\Microsoft.WindowsTerminal\wt.exe";
        let buffer = build_app_exec_link_buffer(
            "Microsoft.WindowsTerminal_8wekyb3d8bbwe",
            "Microsoft.WindowsTerminal_8wekyb3d8bbwe!App",
            target,
        );
        assert_eq!(parse_app_exec_link_target(&buffer).as_deref(), Some(target));
    }

    #[test]
    fn parse_app_exec_link_target_empty_buffer_returns_none() {
        assert_eq!(parse_app_exec_link_target(&[]), None);
    }

    #[test]
    fn parse_app_exec_link_target_truncated_buffer_returns_none() {
        assert_eq!(parse_app_exec_link_target(&[0u8; 12]), None);
    }

    #[test]
    fn parse_app_exec_link_target_missing_third_string_returns_none() {
        let mut buffer = vec![0u8; 8];
        buffer.extend_from_slice(&3u32.to_le_bytes());
        buffer.extend(utf16("SomePackage"));
        buffer.extend_from_slice(&[0, 0]);
        assert_eq!(parse_app_exec_link_target(&buffer), None);
    }

    #[test]
    fn non_reparse_paths_report_nothing() {
        let dir = std::env::temp_dir();
        let path = dir.to_string_lossy();
        assert!(!is_junction_point(&path));
        assert!(!is_app_exec_link(&path));
        assert_eq!(get_app_exec_link_target(&path), None);
    }

    #[cfg(windows)]
    #[test]
    fn junction_is_detected_and_listed_as_directory() {
        let root = std::env::temp_dir().join(format!("wade-junction-{}", std::process::id()));
        let _ = std::fs::remove_dir_all(&root);
        std::fs::create_dir_all(root.join("real")).unwrap();
        let link = root.join("link");
        let status = std::process::Command::new("cmd")
            .args(["/C", "mklink", "/J"])
            .arg(&link)
            .arg(root.join("real"))
            .output();

        if !status.is_ok_and(|o| o.status.success()) || !link.exists() {
            eprintln!("mklink /J unavailable; skipping");
            let _ = std::fs::remove_dir_all(&root);
            return;
        }

        assert!(is_junction_point(&link.to_string_lossy()));
        assert!(!is_junction_point(&root.join("real").to_string_lossy()));

        let entries = crate::fs::directory_contents::load_entries(&root.to_string_lossy(), true, true, None);
        let entry = entries.iter().find(|e| e.name == "link").unwrap();
        assert!(entry.is_directory && entry.is_junction_point);
        let _ = std::fs::remove_dir(&link);
        let _ = std::fs::remove_dir_all(&root);
    }
}
