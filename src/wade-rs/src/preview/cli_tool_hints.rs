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
