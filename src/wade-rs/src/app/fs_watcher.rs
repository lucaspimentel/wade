//! Port of `src/Wade/FileSystemWatcherManager.cs`: watches the current
//! directory for changes and injects a debounced `FileSystemChangedEvent`.
//!
//! The C# version uses `System.IO.FileSystemWatcher`; this port is
//! hand-rolled on `ReadDirectoryChangesW` (windows-sys only). On unix the
//! watcher is a silent no-op until Phase 9 (inotify deferred) — documented
//! in KNOWN_DEVIATIONS.md.

use std::sync::mpsc::Sender;

use crate::input::{FileSystemChangedEvent, InputEvent};

/// Port of `FileSystemWatcherManager.DebounceMs`.
pub const DEBOUNCE_MS: u64 = 300;

/// Port of `FileSystemWatcherManager`. `Watch` no-ops when the path is
/// unchanged; failures silently degrade (manual refresh still works).
pub struct FileSystemWatcherManager {
    sender: Sender<InputEvent>,
    watched_path: Option<String>,
    shutdown: Option<Sender<()>>,
    #[allow(dead_code)] // kept alive for the Windows thread handles
    worker: Option<std::thread::JoinHandle<()>>,
}

impl FileSystemWatcherManager {
    #[must_use]
    pub fn new(sender: Sender<InputEvent>) -> Self {
        Self {
            sender,
            watched_path: None,
            shutdown: None,
            worker: None,
        }
    }

    /// Port of `Watch`: already-watching check (OrdinalIgnoreCase, matching
    /// C#), stop-then-restart on change, silent degradation on failure.
    pub fn watch(&mut self, directory_path: &str) {
        if self.watched_path.as_ref().is_some_and(|current| paths_equal(current, directory_path)) {
            return;
        }

        self.stop();

        let (shutdown_tx, shutdown_rx) = std::sync::mpsc::channel();
        let (event_tx, event_rx) = std::sync::mpsc::channel::<bool>();
        let sender = self.sender.clone();
        let watch_path = directory_path.to_string();

        let spawn_result = spawn_watch_thread(&watch_path, event_tx);

        match spawn_result {
            Ok(worker) => {
                self.watched_path = Some(watch_path.clone());
                self.shutdown = Some(shutdown_tx);
                self.worker = Some(worker);

                // Debounce pump: resets the 300ms window on every FS event
                // (matching C#'s per-event timer reset); `true` = full refresh.
                std::thread::spawn(move || {
                    let mut pending_full_refresh = false;

                    while let Ok(full_refresh) = event_rx.recv() {
                        pending_full_refresh |= full_refresh;

                        // Drain any events that arrive within the window,
                        // extending it each time (timer reset semantics).
                        loop {
                            match event_rx.recv_timeout(std::time::Duration::from_millis(DEBOUNCE_MS)) {
                                Ok(full_refresh) => pending_full_refresh |= full_refresh,
                                Err(std::sync::mpsc::RecvTimeoutError::Timeout) => break,
                                Err(std::sync::mpsc::RecvTimeoutError::Disconnected) => return,
                            }
                        }

                        if shutdown_rx.try_recv().is_ok() {
                            return;
                        }

                        let send_result = sender.send(InputEvent::FileSystemChanged(FileSystemChangedEvent {
                            directory_path: watch_path.clone(),
                            full_refresh: pending_full_refresh,
                        }));

                        if send_result.is_err() {
                            return;
                        }

                        pending_full_refresh = false;
                    }
                });
            }
            Err(()) => {
                // Directory may not exist, be inaccessible, or the watcher
                // may fail to start. Silently degrade.
                drop(shutdown_tx);
            }
        }
    }

    /// Port of `Stop`: tears down the watcher and debounce timer.
    pub fn stop(&mut self) {
        if let Some(shutdown) = self.shutdown.take() {
            let _ = shutdown.send(());
        }

        self.watched_path = None;
        self.worker = None;
    }
}

impl Drop for FileSystemWatcherManager {
    fn drop(&mut self) {
        self.stop();
    }
}

/// Case-insensitive path equality on Windows (C# OrdinalIgnoreCase),
/// case-sensitive elsewhere.
#[must_use]
fn paths_equal(a: &str, b: &str) -> bool {
    #[cfg(windows)]
    {
        a.eq_ignore_ascii_case(b)
    }

    #[cfg(not(windows))]
    {
        a == b
    }
}

/// Port of `OnFileSystemEvent`'s `.git` filter: ignore changes to the .git
/// directory (git operations modify it constantly, creating a feedback loop
/// with RefreshGitStatus).
#[must_use]
pub fn is_git_internal_event(file_name: &str) -> bool {
    #[cfg(windows)]
    {
        file_name.eq_ignore_ascii_case(".git")
    }

    #[cfg(not(windows))]
    {
        file_name == ".git"
    }
}

/// Spawns the ReadDirectoryChangesW watch thread (Windows) or fails
/// immediately (unix, deferred to Phase 9).
#[cfg(windows)]
fn spawn_watch_thread(
    directory_path: &str,
    event_tx: std::sync::mpsc::Sender<bool>,
) -> Result<std::thread::JoinHandle<()>, ()> {
    use std::os::windows::ffi::OsStrExt;
    use std::os::windows::io::{FromRawHandle, OwnedHandle};

    use windows_sys::Win32::Foundation::{
        CloseHandle, ERROR_IO_PENDING, GetLastError, INVALID_HANDLE_VALUE,
    };
    use windows_sys::Win32::Storage::FileSystem::{
        CreateFileW, ReadDirectoryChangesW, FILE_FLAG_BACKUP_SEMANTICS,
        FILE_LIST_DIRECTORY, FILE_NOTIFY_CHANGE_DIR_NAME, FILE_NOTIFY_CHANGE_FILE_NAME,
        FILE_NOTIFY_CHANGE_LAST_WRITE, FILE_NOTIFY_CHANGE_SIZE, FILE_SHARE_READ,
        FILE_SHARE_WRITE, FILE_SHARE_DELETE, OPEN_EXISTING,
    };

    let wide_path: Vec<u16> = std::ffi::OsStr::new(directory_path)
        .encode_wide()
        .chain(std::iter::once(0))
        .collect();

    let handle = unsafe {
        CreateFileW(
            wide_path.as_ptr(),
            FILE_LIST_DIRECTORY,
            FILE_SHARE_READ | FILE_SHARE_WRITE | FILE_SHARE_DELETE,
            std::ptr::null(),
            OPEN_EXISTING,
            FILE_FLAG_BACKUP_SEMANTICS,
            std::ptr::null_mut() as _,
        )
    };

    if handle == INVALID_HANDLE_VALUE || handle.is_null() {
        return Err(());
    }

    let handle = unsafe { OwnedHandle::from_raw_handle(handle as _) };

    Ok(std::thread::spawn(move || {
        use std::os::windows::io::AsRawHandle;

        let raw = handle.as_raw_handle() as _;
        let mut buffer = [0u8; 64 * 1024];
        let mut bytes_returned: u32 = 0;

        loop {
            let ok = unsafe {
                ReadDirectoryChangesW(
                    raw,
                    buffer.as_mut_ptr().cast(),
                    buffer.len() as u32,
                    0, // IncludeSubdirectories = false
                    FILE_NOTIFY_CHANGE_FILE_NAME
                        | FILE_NOTIFY_CHANGE_DIR_NAME
                        | FILE_NOTIFY_CHANGE_LAST_WRITE
                        | FILE_NOTIFY_CHANGE_SIZE,
                    &mut bytes_returned,
                    std::ptr::null_mut(),
                    None,
                )
            };

            if ok == 0 {
                let err = unsafe { GetLastError() };

                if err != ERROR_IO_PENDING {
                    // Buffer overflow or watcher failure — request a full
                    // refresh (port of OnError).
                    let _ = event_tx.send(true);
                    continue;
                }
            }

            if parse_notify_buffer(&buffer[..bytes_returned as usize], &event_tx) {
                break;
            }
        }

        drop(handle);
        unsafe {
            let _ = CloseHandle(raw);
        }
    }))
}

/// Parses a `FILE_NOTIFY_INFORMATION` sequence, forwarding one debounce
/// signal per batch (unless every entry is a .git internal event). Returns
/// true when the thread should stop.
#[cfg(windows)]
fn parse_notify_buffer(buffer: &[u8], event_tx: &std::sync::mpsc::Sender<bool>) -> bool {
    let mut offset = 0usize;
    let mut has_reportable = false;

    while offset + 12 <= buffer.len() {
        let next_entry_offset = u32::from_ne_bytes(buffer[offset..offset + 4].try_into().unwrap());
        let file_name_length =
            u32::from_ne_bytes(buffer[offset + 8..offset + 12].try_into().unwrap()) as usize;

        if offset + 12 + file_name_length > buffer.len() {
            break;
        }

        let name_bytes = &buffer[offset + 12..offset + 12 + file_name_length];
        let name_utf16: Vec<u16> = name_bytes
            .as_chunks::<2>()
            .0
            .iter()
            .map(|pair| u16::from_ne_bytes(*pair))
            .collect();

        if let Ok(name) = String::from_utf16(&name_utf16)
            && !is_git_internal_event(&name)
        {
            has_reportable = true;
        }

        if next_entry_offset == 0 {
            break;
        }

        offset += next_entry_offset as usize;
    }

    if has_reportable {
        let _ = event_tx.send(false);
    }

    false
}

#[cfg(not(windows))]
fn spawn_watch_thread(
    _directory_path: &str,
    _event_tx: std::sync::mpsc::Sender<bool>,
) -> Result<std::thread::JoinHandle<()>, ()> {
    Err(())
}

#[cfg(test)]
mod tests {
    use super::{is_git_internal_event, paths_equal, FileSystemWatcherManager, DEBOUNCE_MS};
    use crate::input::InputEvent;
    use crate::input::input_pipeline::InputPipeline;

    #[test]
    fn debounce_window_is_300ms() {
        assert_eq!(DEBOUNCE_MS, 300);
    }

    #[test]
    fn git_filter_matches_case_insensitively_on_windows() {
        assert!(is_git_internal_event(".git"));
        assert!(is_git_internal_event(".GIT"));
        assert!(!is_git_internal_event(".github"));
        assert!(!is_git_internal_event("src.txt"));
    }

    #[test]
    fn path_equality_ignores_case_on_windows() {
        assert!(paths_equal("C:\\Data", "c:\\data"));
        assert!(!paths_equal("C:\\Data", "C:\\Data2"));
    }

    #[cfg(not(windows))]
    #[test]
    fn unix_watch_silently_degrades() {
        let pipeline = InputPipeline::new();
        let mut watcher = FileSystemWatcherManager::new(pipeline.sender());
        watcher.watch("/tmp");
        assert!(pipeline.try_take().is_none());
    }

    #[cfg(windows)]
    #[test]
    fn watch_failure_degrades_silently() {
        let pipeline = InputPipeline::new();
        let mut watcher = FileSystemWatcherManager::new(pipeline.sender());
        // A path that cannot exist as a directory
        watcher.watch("Z:\\definitely-not-a-real-dir\\wade-test");
        assert!(pipeline.try_take().is_none());
        assert!(watcher.watched_path.is_none());
    }

    #[cfg(windows)]
    #[test]
    fn same_path_watch_is_noop() {
        let dir = std::env::temp_dir().join(format!("wade-watch-{}", std::process::id()));
        let _ = std::fs::remove_dir_all(&dir);
        std::fs::create_dir_all(&dir).unwrap();

        let pipeline = InputPipeline::new();
        let mut watcher = FileSystemWatcherManager::new(pipeline.sender());
        watcher.watch(dir.to_str().unwrap());
        let first = watcher.watched_path.clone();
        watcher.watch(dir.to_str().unwrap());
        assert_eq!(watcher.watched_path, first);
        watcher.stop();
        assert!(watcher.watched_path.is_none());
    }

    #[cfg(windows)]
    #[test]
    fn file_creation_fires_debounced_event() {
        let dir = std::env::temp_dir().join(format!("wade-watch-live-{}", std::process::id()));
        let _ = std::fs::remove_dir_all(&dir);
        std::fs::create_dir_all(&dir).unwrap();

        let pipeline = InputPipeline::new();
        let mut watcher = FileSystemWatcherManager::new(pipeline.sender());
        watcher.watch(dir.to_str().unwrap());

        std::fs::write(dir.join("new-file.txt"), b"hello").unwrap();

        // Poll with retries: debounce adds 300ms on top of watcher latency.
        // Generous deadline: CI runners can stall well past the debounce.
        let deadline = std::time::Instant::now() + std::time::Duration::from_secs(15);
        let mut fired = None;

        while std::time::Instant::now() < deadline {
            if let Some(InputEvent::FileSystemChanged(event)) = pipeline.try_take() {
                fired = Some(event);
                break;
            }

            std::thread::sleep(std::time::Duration::from_millis(100));
        }

        watcher.stop();
        let event = fired.expect("expected FileSystemChangedEvent within 15s");
        assert!(event.directory_path.eq_ignore_ascii_case(dir.to_str().unwrap()));
        assert!(!event.full_refresh);
    }
}
