//! Ports of `CliToolHints` and `CliTool`: an install hint when a PDF or
//! media file has no tool to preview it, the cached availability probe,
//! and `Run` (a tool's stdout on success).

use std::collections::HashMap;
use std::sync::{Mutex, OnceLock};

const MEDIA_EXTENSIONS: &[&str] = &[
    ".mp3", ".flac", ".wav", ".ogg", ".aac", ".wma", ".m4a", ".opus", ".aiff", ".mp4", ".mkv", ".avi", ".mov", ".wmv",
    ".webm", ".flv", ".m4v", ".ts", ".mpg", ".mpeg",
];

/// Port of `CliToolHints.GetHint`.
#[must_use]
pub fn get_hint(path: &str) -> Option<&'static str> {
    let name = path.rsplit(crate::search::is_separator).next().unwrap_or(path);
    let ext = match name.rfind('.') {
        Some(index) if index + 1 < name.len() => &name[index..],
        _ => "",
    };

    if ext.eq_ignore_ascii_case(".pdf") {
        let has_pdftopng = is_available("pdftopng", None, false);
        let has_pdfinfo = is_available("pdfinfo", Some("-v"), false);

        match (has_pdftopng, has_pdfinfo) {
            (false, false) => return Some("Install xpdf CLI tools for PDF preview and metadata (pdftopng, pdfinfo)"),
            (false, true) => return Some("Install pdftopng for PDF image preview (xpdf)"),
            (true, false) => return Some("Install pdfinfo for PDF metadata (xpdf or poppler-utils)"),
            (true, true) => {}
        }
    }

    if MEDIA_EXTENSIONS.iter().any(|media| media.eq_ignore_ascii_case(ext)) {
        let has_ffprobe = is_available("ffprobe", Some("-version"), true);
        let has_mediainfo = is_available("mediainfo", Some("--version"), false);

        if !has_ffprobe && !has_mediainfo {
            return Some("Install ffprobe or mediainfo for media metadata");
        }
    }

    None
}

/// Port of `CliTool.IsAvailable`: runs the tool once (3 s timeout) and
/// caches the answer by file name.
#[must_use]
pub fn is_available(file_name: &str, argument: Option<&str>, require_zero_exit_code: bool) -> bool {
    static CACHE: OnceLock<Mutex<HashMap<String, bool>>> = OnceLock::new();
    let cache = CACHE.get_or_init(|| Mutex::new(HashMap::new()));

    if let Some(&known) = cache.lock().unwrap().get(file_name) {
        return known;
    }

    let available = probe(file_name, argument, require_zero_exit_code);
    cache.lock().unwrap().insert(file_name.to_string(), available);
    available
}

fn probe(file_name: &str, argument: Option<&str>, require_zero_exit_code: bool) -> bool {
    let mut command = std::process::Command::new(file_name);
    command
        .args(argument)
        .stdin(std::process::Stdio::null())
        .stdout(std::process::Stdio::null())
        .stderr(std::process::Stdio::null());

    let Ok(mut child) = command.spawn() else {
        return false;
    };

    let deadline = std::time::Instant::now() + std::time::Duration::from_millis(3000);

    loop {
        match child.try_wait() {
            Ok(Some(status)) => return !require_zero_exit_code || status.success(),
            Ok(None) if std::time::Instant::now() < deadline => {
                std::thread::sleep(std::time::Duration::from_millis(10));
            }
            _ => {
                let _ = child.kill();
                let _ = child.wait();
                return false;
            }
        }
    }
}

/// Port of `CliTool.Run`: the tool's stdout when it exits 0 within
/// `timeout_ms`; `None` on failure, timeout or cancellation (the process is
/// killed).
#[must_use]
pub fn run(file_name: &str, args: &[&str], timeout_ms: u64, cancel: &crate::input::CancelToken) -> Option<String> {
    use std::io::Read;

    let mut command = std::process::Command::new(file_name);
    command
        .args(args)
        .stdin(std::process::Stdio::null())
        .stdout(std::process::Stdio::piped())
        .stderr(std::process::Stdio::null());

    #[cfg(windows)]
    {
        use std::os::windows::process::CommandExt;
        command.creation_flags(0x0800_0000); // CREATE_NO_WINDOW
    }

    let mut child = command.spawn().ok()?;
    let mut stdout = child.stdout.take()?;
    let reader = std::thread::spawn(move || {
        let mut output = Vec::new();
        let _ = stdout.read_to_end(&mut output);
        output
    });

    let deadline = std::time::Instant::now() + std::time::Duration::from_millis(timeout_ms);
    let status = loop {
        match child.try_wait() {
            Ok(Some(status)) => break Some(status),
            Ok(None) if !cancel.is_cancelled() && std::time::Instant::now() < deadline => {
                std::thread::sleep(std::time::Duration::from_millis(10));
            }
            _ => {
                let _ = child.kill();
                let _ = child.wait();
                break None;
            }
        }
    };

    let output = reader.join().ok()?;
    status.filter(std::process::ExitStatus::success)?;
    Some(String::from_utf8_lossy(&output).into_owned())
}

#[cfg(test)]
mod tests {
    //! Ports of CliToolHintsTests.cs and CliToolTests.cs.

    use super::{get_hint, is_available, run};
    use crate::input::CancelToken;

    #[test]
    fn files_without_tool_needs_get_no_hint() {
        for name in ["document.txt", "Program.cs", "file.xyz123", "README.md"] {
            assert_eq!(get_hint(name), None, "{name}");
        }
    }

    #[test]
    fn pdf_and_media_hints_follow_tool_availability() {
        let pdf_tools = is_available("pdftopng", None, false) && is_available("pdfinfo", Some("-v"), false);
        assert_eq!(get_hint("document.pdf").is_none(), pdf_tools);

        let media_tools =
            is_available("ffprobe", Some("-version"), true) || is_available("mediainfo", Some("--version"), false);
        assert_eq!(get_hint("video.mp4").is_none(), media_tools);
        assert_eq!(get_hint("VIDEO.MP4").is_none(), media_tools, "extension case is ignored");
    }

    #[test]
    fn availability_is_cached_and_missing_tools_are_unavailable() {
        assert_eq!(is_available("git", Some("--version"), true), is_available("git", Some("--version"), true));
        assert!(!is_available("wade_nonexistent_tool_xyz", None, false));
    }

    #[test]
    fn run_returns_output_or_none() {
        let cancel = CancelToken::new();
        assert_eq!(run("wade_nonexistent_tool_xyz", &["--version"], 5000, &cancel), None);

        if is_available("git", Some("--version"), true) {
            let output = run("git", &["--version"], 5000, &cancel).expect("git output");
            assert!(output.contains("git version"));

            let cancelled = CancelToken::new();
            cancelled.cancel();
            assert_eq!(run("git", &["--version"], 5000, &cancelled), None, "already cancelled");
        }
    }

    #[test]
    fn cancelling_a_running_tool_returns_quickly() {
        let (file_name, args): (&str, &[&str]) = if cfg!(windows) {
            ("ping", &["-n", "100", "localhost"])
        } else {
            ("sleep", &["60"])
        };
        let cancel = CancelToken::new();
        let canceller = cancel.clone();
        let timer = std::thread::spawn(move || {
            std::thread::sleep(std::time::Duration::from_millis(500));
            canceller.cancel();
        });

        let started = std::time::Instant::now();
        let result = run(file_name, args, 60_000, &cancel);
        timer.join().unwrap();
        assert_eq!(result, None);
        assert!(started.elapsed() < std::time::Duration::from_secs(5), "took {:?}", started.elapsed());
    }
}
