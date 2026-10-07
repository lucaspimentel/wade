//! Port of src/Wade/FileSystem/SystemClipboard.cs.
//!
//! Text: Windows `CF_UNICODETEXT`; unix pipes into pbcopy, wl-copy, xclip
//! or xsel (first that succeeds), as C# does.
//!
//! Files: Windows `CF_HDROP` plus "Preferred DropEffect" (copy=1, move=2).
//! C# has no unix file clipboard; Rust adds one (accepted deviation):
//! Linux writes `x-special/gnome-copied-files` through wl-copy/xclip and
//! reads it back (falling back to `text/uri-list`), macOS goes through
//! NSPasteboard file URLs via `osascript -l JavaScript` (always a copy).

use std::io::Write as _;
use std::process::{Command, Stdio};
use std::time::{Duration, Instant};

/// C# `process.WaitForExit(3000)`.
const TOOL_TIMEOUT: Duration = Duration::from_millis(3000);

/// Port of `SetText`: true on success, false when no clipboard mechanism
/// is available.
#[must_use]
pub fn set_text(text: &str) -> bool {
    #[cfg(windows)]
    {
        windows::set_text(text)
    }
    #[cfg(not(windows))]
    {
        const TOOLS: &[&[&str]] = &[
            &["pbcopy"],
            &["wl-copy"],
            &["xclip", "-selection", "clipboard"],
            &["xsel", "--clipboard", "--input"],
        ];
        TOOLS.iter().any(|tool| run_with_input(tool, text.as_bytes()))
    }
}

/// Port of `SetFiles`: publishes paths for Explorer (or a unix file
/// manager) to paste.
#[must_use]
pub fn set_files(paths: &[String], is_cut: bool) -> bool {
    if paths.is_empty() {
        return false;
    }

    #[cfg(windows)]
    {
        windows::set_files(paths, is_cut)
    }
    #[cfg(target_os = "macos")]
    {
        let _ = is_cut;
        let mut args = vec!["-l", "JavaScript", "-e", MACOS_SET_FILES];
        args.extend(paths.iter().map(String::as_str));
        run_tool("osascript", &args, None).is_some()
    }
    #[cfg(all(unix, not(target_os = "macos")))]
    {
        let data = format_gnome_copied_files(paths, is_cut);
        [
            &["wl-copy", "--type", GNOME_COPIED_FILES][..],
            &["xclip", "-selection", "clipboard", "-t", GNOME_COPIED_FILES][..],
        ]
        .iter()
        .any(|tool| run_with_input(tool, data.as_bytes()))
    }
}

/// Port of `GetFiles`: paths on the OS clipboard (e.g. files copied in
/// Explorer) and whether they were cut.
#[must_use]
pub fn get_files() -> Option<(Vec<String>, bool)> {
    #[cfg(windows)]
    {
        windows::get_files()
    }
    #[cfg(target_os = "macos")]
    {
        let out = run_tool("osascript", &["-l", "JavaScript", "-e", MACOS_GET_FILES], None)?;
        let paths: Vec<String> = out.lines().filter(|l| !l.is_empty()).map(str::to_string).collect();
        (!paths.is_empty()).then_some((paths, false))
    }
    #[cfg(all(unix, not(target_os = "macos")))]
    {
        let read = |mime: &str| -> Option<String> {
            [
                vec!["wl-paste", "--no-newline", "--type", mime],
                vec!["xclip", "-selection", "clipboard", "-o", "-t", mime],
            ]
            .iter()
            .find_map(|tool| run_tool(tool[0], &tool[1..], None))
        };

        if let Some(result) = read(GNOME_COPIED_FILES).and_then(|data| parse_gnome_copied_files(&data)) {
            return Some(result);
        }

        let paths = parse_uri_list(&read("text/uri-list")?);
        (!paths.is_empty()).then_some((paths, false))
    }
}

#[cfg(target_os = "macos")]
const MACOS_SET_FILES: &str = "function run(argv) { ObjC.import('AppKit'); \
    var pb = $.NSPasteboard.generalPasteboard; pb.clearContents; \
    var urls = argv.map(function (p) { return $.NSURL.fileURLWithPath(p); }); \
    return pb.writeObjects($(urls)) ? '' : 'fail'; }";

#[cfg(target_os = "macos")]
const MACOS_GET_FILES: &str = "ObjC.import('AppKit'); \
    var pb = $.NSPasteboard.generalPasteboard; \
    var urls = pb.readObjectsForClassesOptions($([$.NSURL]), $({'NSPasteboardURLReadingFileURLsOnlyKey': true})); \
    var out = []; if (urls) { for (var i = 0; i < urls.count; i++) out.push(urls.objectAtIndex(i).path.js); } \
    out.join('\\n')";

pub const GNOME_COPIED_FILES: &str = "x-special/gnome-copied-files";

/// GNOME's `x-special/gnome-copied-files`: the action line ("copy"/"cut")
/// followed by one `file://` URI per line.
#[must_use]
pub fn format_gnome_copied_files(paths: &[String], is_cut: bool) -> String {
    let mut out = String::from(if is_cut { "cut" } else { "copy" });

    for path in paths {
        out.push('\n');
        out.push_str(&path_to_file_uri(path));
    }

    out
}

/// Parses `x-special/gnome-copied-files`; None when the action line is
/// missing or no file URI follows.
#[must_use]
pub fn parse_gnome_copied_files(data: &str) -> Option<(Vec<String>, bool)> {
    let mut lines = data.lines();
    let is_cut = match lines.next()?.trim() {
        "cut" => true,
        "copy" => false,
        _ => return None,
    };
    let paths: Vec<String> = lines.filter_map(|line| file_uri_to_path(line.trim())).collect();
    (!paths.is_empty()).then_some((paths, is_cut))
}

/// Parses `text/uri-list` (RFC 2483): one URI per line, `#` comments,
/// CRLF or LF; only local `file:` URIs are kept.
#[must_use]
pub fn parse_uri_list(data: &str) -> Vec<String> {
    data.lines()
        .map(str::trim)
        .filter(|line| !line.is_empty() && !line.starts_with('#'))
        .filter_map(file_uri_to_path)
        .collect()
}

/// `file:///a%20b` or `file://localhost/a%20b` -> `/a b`.
#[must_use]
pub fn file_uri_to_path(uri: &str) -> Option<String> {
    let rest = uri.strip_prefix("file://")?;
    let path = if rest.starts_with('/') { rest } else { rest.strip_prefix("localhost")? };

    if !path.starts_with('/') {
        return None;
    }

    percent_decode(path)
}

/// `/a b` -> `file:///a%20b` (RFC 3986 path characters kept as is).
#[must_use]
pub fn path_to_file_uri(path: &str) -> String {
    let mut out = String::from("file://");

    for byte in path.bytes() {
        if byte.is_ascii_alphanumeric() || b"/-._~!$&'()*+,;=:@".contains(&byte) {
            out.push(byte as char);
        } else {
            out.push_str(&format!("%{byte:02X}"));
        }
    }

    out
}

fn percent_decode(text: &str) -> Option<String> {
    let bytes = text.as_bytes();
    let mut out = Vec::with_capacity(bytes.len());
    let mut i = 0;

    while i < bytes.len() {
        if bytes[i] == b'%' {
            let hex = std::str::from_utf8(bytes.get(i + 1..i + 3)?).ok()?;
            out.push(u8::from_str_radix(hex, 16).ok()?);
            i += 3;
        } else {
            out.push(bytes[i]);
            i += 1;
        }
    }

    String::from_utf8(out).ok()
}

/// Runs a clipboard tool with `input` on stdin; true when it exits 0
/// within the timeout.
#[cfg(not(windows))]
fn run_with_input(tool: &[&str], input: &[u8]) -> bool {
    run_tool(tool[0], &tool[1..], Some(input)).is_some()
}

/// Runs a tool, optionally feeding stdin; returns stdout when it exits 0
/// within the timeout. A tool that is missing, fails or hangs is None.
/// When feeding stdin, stdout is discarded: forking tools (wl-copy, xclip)
/// leave a daemon that would hold a stdout pipe open.
#[cfg_attr(windows, allow(dead_code))]
fn run_tool(program: &str, args: &[&str], input: Option<&[u8]>) -> Option<String> {
    let mut child = Command::new(program)
        .args(args)
        .stdin(if input.is_some() { Stdio::piped() } else { Stdio::null() })
        .stdout(if input.is_some() { Stdio::null() } else { Stdio::piped() })
        .stderr(Stdio::null())
        .spawn()
        .ok()?;

    if let Some(input) = input {
        let mut stdin = child.stdin.take()?;
        let _ = stdin.write_all(input);
    }

    // Read stdout on a thread so a chatty tool cannot block on a full pipe.
    let reader = child.stdout.take().map(|mut stdout| {
        std::thread::spawn(move || {
            let mut out = Vec::new();
            let _ = std::io::Read::read_to_end(&mut stdout, &mut out);
            out
        })
    });

    let deadline = Instant::now() + TOOL_TIMEOUT;

    let status = loop {
        match child.try_wait() {
            Ok(Some(status)) => break status,
            Ok(None) if Instant::now() < deadline => std::thread::sleep(Duration::from_millis(10)),
            _ => {
                let _ = child.kill();
                let _ = child.wait();
                return None;
            }
        }
    };

    let out = match reader {
        Some(reader) => reader.join().ok()?,
        None => Vec::new(),
    };
    status.success().then(|| String::from_utf8_lossy(&out).into_owned())
}

#[cfg(windows)]
mod windows {
    use windows_sys::Win32::Foundation::{GlobalFree, HANDLE};
    use windows_sys::Win32::System::DataExchange::{
        CloseClipboard, EmptyClipboard, GetClipboardData, OpenClipboard, RegisterClipboardFormatW, SetClipboardData,
    };
    use windows_sys::Win32::System::Memory::{GMEM_MOVEABLE, GlobalAlloc, GlobalLock, GlobalUnlock};
    use windows_sys::Win32::UI::Shell::{DragQueryFileW, HDROP};

    const CF_UNICODETEXT: u32 = 13;
    const CF_HDROP: u32 = 15;
    const DROPEFFECT_COPY: u32 = 1;
    const DROPEFFECT_MOVE: u32 = 2;

    /// Opens the clipboard for the scope of the guard.
    struct Clipboard;

    impl Clipboard {
        fn open() -> Option<Self> {
            (unsafe { OpenClipboard(std::ptr::null_mut()) } != 0).then_some(Self)
        }
    }

    impl Drop for Clipboard {
        fn drop(&mut self) {
            unsafe {
                CloseClipboard();
            }
        }
    }

    /// Allocates a movable global block holding `bytes`; the clipboard
    /// owns it once `SetClipboardData` succeeds.
    fn global_from_bytes(bytes: &[u8]) -> Option<HANDLE> {
        unsafe {
            let handle = GlobalAlloc(GMEM_MOVEABLE, bytes.len());

            if handle.is_null() {
                return None;
            }

            let locked = GlobalLock(handle);

            if locked.is_null() {
                GlobalFree(handle);
                return None;
            }

            std::ptr::copy_nonoverlapping(bytes.as_ptr(), locked.cast::<u8>(), bytes.len());
            GlobalUnlock(handle);
            Some(handle)
        }
    }

    fn set_data(format: u32, bytes: &[u8]) -> bool {
        let Some(handle) = global_from_bytes(bytes) else {
            return false;
        };

        unsafe {
            if SetClipboardData(format, handle).is_null() {
                GlobalFree(handle);
                return false;
            }
        }

        true
    }

    fn utf16_bytes(text: &str) -> Vec<u8> {
        text.encode_utf16().chain(std::iter::once(0)).flat_map(u16::to_le_bytes).collect()
    }

    fn drop_effect_format() -> u32 {
        let name: Vec<u16> = "Preferred DropEffect".encode_utf16().chain(std::iter::once(0)).collect();
        unsafe { RegisterClipboardFormatW(name.as_ptr()) }
    }

    pub(super) fn set_text(text: &str) -> bool {
        let Some(_clipboard) = Clipboard::open() else {
            return false;
        };

        unsafe {
            EmptyClipboard();
        }

        set_data(CF_UNICODETEXT, &utf16_bytes(text))
    }

    pub(super) fn set_files(paths: &[String], is_cut: bool) -> bool {
        let Some(_clipboard) = Clipboard::open() else {
            return false;
        };

        unsafe {
            EmptyClipboard();
        }

        // DROPFILES header: pFiles=20, pt={0,0}, fNC=0, fWide=1; then the
        // double-null-terminated UTF-16 path list.
        let mut data = Vec::new();

        for value in [20u32, 0, 0, 0, 1] {
            data.extend_from_slice(&value.to_le_bytes());
        }

        for path in paths {
            data.extend(utf16_bytes(path));
        }

        data.extend_from_slice(&[0, 0]);

        if !set_data(CF_HDROP, &data) {
            return false;
        }

        let format = drop_effect_format();

        if format != 0 {
            let effect = if is_cut { DROPEFFECT_MOVE } else { DROPEFFECT_COPY };
            let _ = set_data(format, &effect.to_le_bytes());
        }

        true
    }

    pub(super) fn get_files() -> Option<(Vec<String>, bool)> {
        let _clipboard = Clipboard::open()?;

        unsafe {
            let hdrop: HDROP = GetClipboardData(CF_HDROP);

            if hdrop.is_null() {
                return None;
            }

            let count = DragQueryFileW(hdrop, 0xFFFF_FFFF, std::ptr::null_mut(), 0);

            if count == 0 {
                return None;
            }

            let mut paths = Vec::with_capacity(count as usize);
            let mut buffer = [0u16; 1024];

            for i in 0..count {
                let copied = DragQueryFileW(hdrop, i, buffer.as_mut_ptr(), buffer.len() as u32);

                if copied > 0 {
                    paths.push(String::from_utf16_lossy(&buffer[..copied as usize]));
                }
            }

            let mut is_cut = false;
            let format = drop_effect_format();

            if format != 0 {
                let effect = GetClipboardData(format);

                if !effect.is_null() {
                    let locked = GlobalLock(effect);

                    if !locked.is_null() {
                        let value = std::ptr::read_unaligned(locked.cast::<u32>());
                        is_cut = value & DROPEFFECT_MOVE != 0;
                        GlobalUnlock(effect);
                    }
                }
            }

            Some((paths, is_cut))
        }
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn gnome_copied_files_round_trips() {
        let paths = vec!["/home/me/a b.txt".to_string(), "/tmp/ü%.md".to_string()];
        let data = format_gnome_copied_files(&paths, true);
        assert_eq!(data, "cut\nfile:///home/me/a%20b.txt\nfile:///tmp/%C3%BC%25.md");
        assert_eq!(parse_gnome_copied_files(&data), Some((paths.clone(), true)));
        assert_eq!(parse_gnome_copied_files(&format_gnome_copied_files(&paths, false)), Some((paths, false)));
    }

    #[test]
    fn gnome_copied_files_rejects_bad_input() {
        assert_eq!(parse_gnome_copied_files(""), None);
        assert_eq!(parse_gnome_copied_files("move\nfile:///a"), None);
        assert_eq!(parse_gnome_copied_files("copy\nhttp://example.com/a"), None);
    }

    #[test]
    fn uri_list_keeps_local_file_uris() {
        let data = "# comment\r\nfile:///a%20b\r\nfile://localhost/c\r\nhttps://x/y\r\nfile://otherhost/d\r\n\r\n";
        assert_eq!(parse_uri_list(data), vec!["/a b".to_string(), "/c".to_string()]);
    }

    #[test]
    fn percent_decoding_rejects_truncated_escapes() {
        assert_eq!(file_uri_to_path("file:///a%2"), None);
        assert_eq!(file_uri_to_path("file:///a%zz"), None);
    }

    #[test]
    fn set_files_with_no_paths_fails() {
        assert!(!set_files(&[], false));
    }

    #[cfg(not(windows))]
    #[test]
    fn missing_tool_is_none() {
        assert_eq!(run_tool("wade-no-such-tool", &[], None), None);
        assert!(!run_with_input(&["wade-no-such-tool"], b"x"));
    }

    #[cfg(windows)]
    #[test]
    fn windows_file_clipboard_round_trips_when_available() {
        let paths = vec![r"C:\temp\a b.txt".to_string(), r"C:\temp\ü.md".to_string()];

        if !set_files(&paths, true) {
            eprintln!("clipboard unavailable; skipping");
            return;
        }

        assert_eq!(get_files(), Some((paths, true)));
        assert!(set_text("wade"));
    }
}
