//! Port of `src/Wade/FileSystemWatcherManager.cs`: watches the current
//! directory for changes and injects a debounced `FileSystemChangedEvent`.
//!
//! The C# version uses `System.IO.FileSystemWatcher`. On Windows this port
//! is hand-rolled on `ReadDirectoryChangesW`, which reports what
//! `FileSystemWatcher` does (buffer overflow and a deleted watched
//! directory request a full refresh); the `notify` crate's Windows backend
//! drops both. Linux and macOS use `notify` (inotify, FSEvents/kqueue).

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
    /// Dropping the worker stops and joins the watch thread.
    #[allow(dead_code)] // held only for its Drop
    worker: Option<WatchWorker>,
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
                        // extending it each time (timer reset semantics). A
                        // disconnect means the watch thread ended: after
                        // stop() the shutdown check below discards the event;
                        // after a watcher error it is still delivered.
                        let mut disconnected = false;

                        loop {
                            match event_rx.recv_timeout(std::time::Duration::from_millis(DEBOUNCE_MS)) {
                                Ok(full_refresh) => pending_full_refresh |= full_refresh,
                                Err(std::sync::mpsc::RecvTimeoutError::Timeout) => break,
                                Err(std::sync::mpsc::RecvTimeoutError::Disconnected) => {
                                    disconnected = true;
                                    break;
                                }
                            }
                        }

                        if shutdown_rx.try_recv().is_ok() {
                            return;
                        }

                        let send_result = sender.send(InputEvent::FileSystemChanged(FileSystemChangedEvent {
                            directory_path: watch_path.clone(),
                            full_refresh: pending_full_refresh,
                        }));

                        if send_result.is_err() || disconnected {
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

        // Joins the watch thread; its event sender drops with it, which
        // ends the debounce pump without delivering a pending event.
        self.worker = None;
        self.watched_path = None;
    }
}

impl Drop for FileSystemWatcherManager {
    fn drop(&mut self) {
        self.stop();
    }
}

/// Case-insensitive path equality on every platform (C# compares with
/// `StringComparison.OrdinalIgnoreCase` regardless of OS).
#[must_use]
fn paths_equal(a: &str, b: &str) -> bool {
    a.eq_ignore_ascii_case(b)
}

/// Port of `OnFileSystemEvent`'s `.git` filter: ignore changes to the .git
/// directory (git operations modify it constantly, creating a feedback loop
/// with RefreshGitStatus).
#[must_use]
pub fn is_git_internal_event(file_name: &str) -> bool {
    file_name.eq_ignore_ascii_case(".git")
}

/// The running watch thread. Dropping it signals the stop event and joins
/// the thread, so the directory handle is closed before `stop` returns.
#[cfg(windows)]
struct WatchWorker {
    stop_event: std::os::windows::io::OwnedHandle,
    thread: Option<std::thread::JoinHandle<()>>,
}

#[cfg(windows)]
impl Drop for WatchWorker {
    fn drop(&mut self) {
        use std::os::windows::io::AsRawHandle;

        unsafe {
            windows_sys::Win32::System::Threading::SetEvent(self.stop_event.as_raw_handle() as _);
        }

        if let Some(thread) = self.thread.take() {
            let _ = thread.join();
        }
    }
}

/// Elsewhere the `notify` watcher; dropping it stops watching.
#[cfg(not(windows))]
type WatchWorker = notify::RecommendedWatcher;

/// `FileSystemWatcher.NotifyFilter` in the C# version.
#[cfg(windows)]
const NOTIFY_FILTER: u32 = windows_sys::Win32::Storage::FileSystem::FILE_NOTIFY_CHANGE_FILE_NAME
    | windows_sys::Win32::Storage::FileSystem::FILE_NOTIFY_CHANGE_DIR_NAME
    | windows_sys::Win32::Storage::FileSystem::FILE_NOTIFY_CHANGE_LAST_WRITE
    | windows_sys::Win32::Storage::FileSystem::FILE_NOTIFY_CHANGE_SIZE;

/// Buffer and OVERLAPPED for the pending read, heap-pinned so moving the
/// boxes into the watch thread does not move what the kernel writes to.
/// The buffer is DWORD-aligned, as overlapped ReadDirectoryChangesW
/// requires.
#[cfg(windows)]
struct PendingRead {
    buffer: Box<[u32]>,
    overlapped: Box<windows_sys::Win32::System::IO::OVERLAPPED>,
}

// SAFETY: only the raw event handle inside OVERLAPPED is !Send; it is owned
// by the watch thread's OwnedHandle and outlives every use.
#[cfg(windows)]
unsafe impl Send for PendingRead {}

/// Issues one overlapped read; false means the directory is gone or
/// inaccessible.
#[cfg(windows)]
fn issue_read(
    directory: windows_sys::Win32::Foundation::HANDLE,
    io_event: windows_sys::Win32::Foundation::HANDLE,
    read: &mut PendingRead,
) -> bool {
    use windows_sys::Win32::Storage::FileSystem::ReadDirectoryChangesW;
    use windows_sys::Win32::System::Threading::ResetEvent;

    *read.overlapped = unsafe { std::mem::zeroed() };
    read.overlapped.hEvent = io_event;
    let buffer_bytes = std::mem::size_of_val(&*read.buffer) as u32;

    unsafe {
        ResetEvent(io_event);
        ReadDirectoryChangesW(
            directory,
            read.buffer.as_mut_ptr().cast(),
            buffer_bytes,
            0, // IncludeSubdirectories = false
            NOTIFY_FILTER,
            std::ptr::null_mut(),
            &mut *read.overlapped,
            None,
        ) != 0
    }
}

/// Spawns the overlapped ReadDirectoryChangesW watch thread.
///
/// The thread waits on the I/O event and the worker's stop event, so
/// `WatchWorker`'s drop can cancel the pending read and join. Mirrors
/// `FileSystemWatcher` error semantics: a buffer overflow (0 bytes
/// returned) requests a full refresh and keeps watching; any other failure
/// requests one full refresh and stops watching.
#[cfg(windows)]
fn spawn_watch_thread(directory_path: &str, event_tx: std::sync::mpsc::Sender<bool>) -> Result<WatchWorker, ()> {
    use std::os::windows::ffi::OsStrExt;
    use std::os::windows::io::{AsRawHandle, FromRawHandle, OwnedHandle};

    use windows_sys::Win32::Foundation::{ERROR_OPERATION_ABORTED, GetLastError, INVALID_HANDLE_VALUE, WAIT_OBJECT_0};
    use windows_sys::Win32::Storage::FileSystem::{
        CreateFileW, FILE_FLAG_BACKUP_SEMANTICS, FILE_FLAG_OVERLAPPED, FILE_LIST_DIRECTORY, FILE_SHARE_DELETE,
        FILE_SHARE_READ, FILE_SHARE_WRITE, OPEN_EXISTING,
    };
    use windows_sys::Win32::System::IO::{CancelIoEx, GetOverlappedResult};
    use windows_sys::Win32::System::Threading::{CreateEventW, INFINITE, WaitForMultipleObjects};

    let wide_path: Vec<u16> = std::ffi::OsStr::new(directory_path).encode_wide().chain(std::iter::once(0)).collect();

    let raw_directory = unsafe {
        CreateFileW(
            wide_path.as_ptr(),
            FILE_LIST_DIRECTORY,
            FILE_SHARE_READ | FILE_SHARE_WRITE | FILE_SHARE_DELETE,
            std::ptr::null(),
            OPEN_EXISTING,
            FILE_FLAG_BACKUP_SEMANTICS | FILE_FLAG_OVERLAPPED,
            std::ptr::null_mut(),
        )
    };

    if raw_directory == INVALID_HANDLE_VALUE || raw_directory.is_null() {
        return Err(());
    }

    let directory = unsafe { OwnedHandle::from_raw_handle(raw_directory as _) };

    // Manual-reset events: one for I/O completion, one for stop
    let raw_io_event = unsafe { CreateEventW(std::ptr::null(), 1, 0, std::ptr::null()) };
    let raw_stop_event = unsafe { CreateEventW(std::ptr::null(), 1, 0, std::ptr::null()) };

    if raw_io_event.is_null() || raw_stop_event.is_null() {
        unsafe {
            if !raw_io_event.is_null() {
                drop(OwnedHandle::from_raw_handle(raw_io_event as _));
            }

            if !raw_stop_event.is_null() {
                drop(OwnedHandle::from_raw_handle(raw_stop_event as _));
            }
        }

        return Err(());
    }

    let io_event = unsafe { OwnedHandle::from_raw_handle(raw_io_event as _) };
    let stop_event = unsafe { OwnedHandle::from_raw_handle(raw_stop_event as _) };

    let dir_raw = directory.as_raw_handle() as _;
    let io_raw = io_event.as_raw_handle() as _;

    let mut read = PendingRead {
        buffer: vec![0u32; 16 * 1024].into_boxed_slice(),
        overlapped: Box::new(unsafe { std::mem::zeroed() }),
    };

    // The first read is issued before watch() returns, like C#'s
    // EnableRaisingEvents: Windows only buffers changes once a read is
    // pending, so issuing it on the thread would drop changes made right
    // after watch().
    if !issue_read(dir_raw, io_raw, &mut read) {
        return Err(());
    }

    // The thread borrows the stop event by raw value; WatchWorker owns it
    // and joins the thread before closing it. Raw handles are not Send, so
    // they cross as integers.
    let stop_raw = stop_event.as_raw_handle() as usize;
    let dir_raw_value = dir_raw as usize;
    let io_raw_value = io_raw as usize;

    let thread = std::thread::spawn(move || {
        // Keep the handles alive (and closed on exit) on this thread
        let _directory = directory;
        let _io_event = io_event;
        let mut read = read;
        let dir_raw = dir_raw_value as _;
        let handles = [io_raw_value as _, stop_raw as _];

        loop {
            let wait = unsafe { WaitForMultipleObjects(2, handles.as_ptr(), 0, INFINITE) };
            let mut bytes_returned: u32 = 0;

            if wait != WAIT_OBJECT_0 {
                // Stop requested (or the wait failed): cancel the pending read
                // and wait for it to drain before the buffer is freed.
                unsafe {
                    CancelIoEx(dir_raw, &*read.overlapped);
                    GetOverlappedResult(dir_raw, &*read.overlapped, &mut bytes_returned, 1);
                }

                break;
            }

            if unsafe { GetOverlappedResult(dir_raw, &*read.overlapped, &mut bytes_returned, 0) } == 0 {
                if unsafe { GetLastError() } != ERROR_OPERATION_ABORTED {
                    let _ = event_tx.send(true);
                }

                break;
            }

            if bytes_returned == 0 {
                // Buffer overflow: changes were lost, request a full refresh
                let _ = event_tx.send(true);
            } else {
                let bytes =
                    unsafe { std::slice::from_raw_parts(read.buffer.as_ptr().cast::<u8>(), bytes_returned as usize) };
                parse_notify_buffer(bytes, &event_tx);
            }

            if !issue_read(dir_raw, handles[0], &mut read) {
                // Directory gone or inaccessible: report once (OnError) and stop
                let _ = event_tx.send(true);
                break;
            }
        }

        // _directory and _io_event close here (OwnedHandle drop)
    });

    Ok(WatchWorker { stop_event, thread: Some(thread) })
}

/// Parses a `FILE_NOTIFY_INFORMATION` sequence, forwarding one debounce
/// signal per batch (unless every entry is a .git internal event).
#[cfg(windows)]
fn parse_notify_buffer(buffer: &[u8], event_tx: &std::sync::mpsc::Sender<bool>) {
    let mut offset = 0usize;
    let mut has_reportable = false;

    while offset + 12 <= buffer.len() {
        let next_entry_offset = u32::from_ne_bytes(buffer[offset..offset + 4].try_into().unwrap());
        let file_name_length = u32::from_ne_bytes(buffer[offset + 8..offset + 12].try_into().unwrap()) as usize;

        if offset + 12 + file_name_length > buffer.len() {
            break;
        }

        let name_bytes = &buffer[offset + 12..offset + 12 + file_name_length];
        let name_utf16: Vec<u16> = name_bytes.as_chunks::<2>().0.iter().map(|pair| u16::from_ne_bytes(*pair)).collect();

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
}

/// Starts a non-recursive watch. Each reportable change sends `false`;
/// a watcher error, a rescan request (queue overflow) or the removal of
/// the watched directory sends `true` (C# `OnError`, full refresh). Mirrors `FileSystemWatcher.NotifyFilter`
/// (FileName | DirectoryName | LastWrite | Size): creations, removals,
/// renames and modifications count; accesses do not.
#[cfg(not(windows))]
fn spawn_watch_thread(
    directory_path: &str,
    event_tx: std::sync::mpsc::Sender<bool>,
) -> Result<notify::RecommendedWatcher, ()> {
    use notify::{EventKind, Watcher};

    let path = std::path::Path::new(directory_path);

    if !path.is_dir() {
        return Err(());
    }

    let watched = path.to_path_buf();
    let mut watcher = notify::recommended_watcher(move |result: notify::Result<notify::Event>| {
        let signal = match result {
            Err(_) => Some(true),
            Ok(event) if event.need_rescan() => Some(true),
            // The watched directory itself went away (C# raises Error)
            Ok(event) if matches!(event.kind, EventKind::Remove(_)) && event.paths.contains(&watched) => Some(true),
            Ok(event) => {
                let relevant = matches!(
                    event.kind,
                    EventKind::Create(_) | EventKind::Remove(_) | EventKind::Modify(_) | EventKind::Any
                );
                let only_git = !event.paths.is_empty()
                    && event
                        .paths
                        .iter()
                        .all(|p| p.file_name().is_some_and(|name| is_git_internal_event(&name.to_string_lossy())));
                (relevant && !only_git).then_some(false)
            }
        };

        if let Some(full_refresh) = signal {
            let _ = event_tx.send(full_refresh);
        }
    })
    .map_err(|_| ())?;

    watcher.watch(path, notify::RecursiveMode::NonRecursive).map_err(|_| ())?;
    Ok(watcher)
}

#[cfg(test)]
mod tests {
    use super::{DEBOUNCE_MS, FileSystemWatcherManager, is_git_internal_event, paths_equal};
    use crate::input::InputEvent;
    use crate::input::input_pipeline::InputPipeline;

    #[test]
    fn debounce_window_is_300ms() {
        assert_eq!(DEBOUNCE_MS, 300);
    }

    #[test]
    fn git_filter_matches_case_insensitively() {
        assert!(is_git_internal_event(".git"));
        assert!(is_git_internal_event(".GIT"));
        assert!(!is_git_internal_event(".github"));
        assert!(!is_git_internal_event("src.txt"));
    }

    #[test]
    fn path_equality_ignores_case() {
        assert!(paths_equal("C:\\Data", "c:\\data"));
        assert!(!paths_equal("C:\\Data", "C:\\Data2"));
    }

    #[test]
    fn watch_failure_degrades_silently() {
        let pipeline = InputPipeline::new();
        let mut watcher = FileSystemWatcherManager::new(pipeline.sender());
        // A path that cannot exist as a directory
        let missing = std::env::temp_dir().join("wade-definitely-not-a-real-dir").join("wade-test");
        watcher.watch(missing.to_str().unwrap());
        assert!(pipeline.try_take().is_none());
        assert!(watcher.watched_path.is_none());
    }

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

    fn live_dir(name: &str) -> std::path::PathBuf {
        let dir = std::env::temp_dir().join(format!("wade-watch-{}-{}", std::process::id(), name));
        let _ = std::fs::remove_dir_all(&dir);
        std::fs::create_dir_all(&dir).unwrap();
        dir
    }

    /// Waits for the next FileSystemChangedEvent. Generous deadline: CI
    /// runners can stall well past the 300ms debounce.
    fn wait_for_change(pipeline: &InputPipeline) -> Option<crate::input::FileSystemChangedEvent> {
        let deadline = std::time::Instant::now() + std::time::Duration::from_secs(15);

        while std::time::Instant::now() < deadline {
            if let Some(InputEvent::FileSystemChanged(event)) = pipeline.try_take() {
                return Some(event);
            }

            std::thread::sleep(std::time::Duration::from_millis(50));
        }

        None
    }

    /// Counts FileSystemChangedEvents arriving within `window`.
    fn count_changes(pipeline: &InputPipeline, window: std::time::Duration) -> usize {
        let deadline = std::time::Instant::now() + window;
        let mut count = 0;

        while std::time::Instant::now() < deadline {
            if let Some(InputEvent::FileSystemChanged(_)) = pipeline.try_take() {
                count += 1;
            }

            std::thread::sleep(std::time::Duration::from_millis(20));
        }

        count
    }

    #[test]
    fn file_deletion_fires_event() {
        let dir = live_dir("delete");
        let file = dir.join("doomed.txt");
        std::fs::write(&file, b"x").unwrap();

        let pipeline = InputPipeline::new();
        let mut watcher = FileSystemWatcherManager::new(pipeline.sender());
        watcher.watch(dir.to_str().unwrap());
        std::fs::remove_file(&file).unwrap();

        let event = wait_for_change(&pipeline).expect("expected event for delete");
        watcher.stop();
        assert!(!event.full_refresh);
        let _ = std::fs::remove_dir_all(&dir);
    }

    #[test]
    fn file_rename_fires_event() {
        let dir = live_dir("rename");
        std::fs::write(dir.join("old.txt"), b"x").unwrap();

        let pipeline = InputPipeline::new();
        let mut watcher = FileSystemWatcherManager::new(pipeline.sender());
        watcher.watch(dir.to_str().unwrap());
        std::fs::rename(dir.join("old.txt"), dir.join("new.txt")).unwrap();

        assert!(wait_for_change(&pipeline).is_some(), "expected event for rename");
        watcher.stop();
        let _ = std::fs::remove_dir_all(&dir);
    }

    #[test]
    fn watching_new_directory_stops_old_one() {
        let dir1 = live_dir("switch-1");
        let dir2 = live_dir("switch-2");

        let pipeline = InputPipeline::new();
        let mut watcher = FileSystemWatcherManager::new(pipeline.sender());
        watcher.watch(dir1.to_str().unwrap());
        watcher.watch(dir2.to_str().unwrap());

        std::fs::write(dir1.join("ignored.txt"), b"x").unwrap();
        std::fs::write(dir2.join("seen.txt"), b"x").unwrap();

        let event = wait_for_change(&pipeline).expect("expected event for dir2");
        assert!(event.directory_path.eq_ignore_ascii_case(dir2.to_str().unwrap()));
        // Nothing for dir1 follows
        assert_eq!(count_changes(&pipeline, std::time::Duration::from_millis(800)), 0);

        watcher.stop();
        let _ = std::fs::remove_dir_all(&dir1);
        let _ = std::fs::remove_dir_all(&dir2);
    }

    #[test]
    fn stop_delivers_no_further_events_and_releases_directory() {
        let dir = live_dir("stop");

        let pipeline = InputPipeline::new();
        let mut watcher = FileSystemWatcherManager::new(pipeline.sender());
        watcher.watch(dir.to_str().unwrap());
        watcher.stop();

        std::fs::write(dir.join("after-stop.txt"), b"x").unwrap();
        assert_eq!(count_changes(&pipeline, std::time::Duration::from_millis(800)), 0);

        // The watch thread was joined and its handle closed: the directory
        // can be removed and recreated at once (an open handle would leave
        // it delete-pending and fail the create).
        std::fs::remove_dir_all(&dir).unwrap();
        std::fs::create_dir(&dir).unwrap();
        let _ = std::fs::remove_dir_all(&dir);
    }

    #[test]
    fn rapid_changes_coalesce_into_single_event() {
        let dir = live_dir("debounce");

        let pipeline = InputPipeline::new();
        let mut watcher = FileSystemWatcherManager::new(pipeline.sender());
        watcher.watch(dir.to_str().unwrap());

        for i in 0..10 {
            std::fs::write(dir.join(format!("file{i}.txt")), format!("content{i}")).unwrap();
        }

        assert!(wait_for_change(&pipeline).is_some(), "expected a debounced event");
        let extra = count_changes(&pipeline, std::time::Duration::from_millis(800));
        assert!(extra < 5, "expected fewer than 5 extra events but got {extra}");

        watcher.stop();
        let _ = std::fs::remove_dir_all(&dir);
    }

    #[test]
    fn deleted_watched_directory_requests_full_refresh_then_stops() {
        let dir = live_dir("vanish");

        let pipeline = InputPipeline::new();
        let mut watcher = FileSystemWatcherManager::new(pipeline.sender());
        watcher.watch(dir.to_str().unwrap());
        std::fs::remove_dir_all(&dir).unwrap();

        // Collect everything for a window comfortably past the debounce
        let deadline = std::time::Instant::now() + std::time::Duration::from_millis(2500);
        let mut events = Vec::new();

        while std::time::Instant::now() < deadline {
            if let Some(InputEvent::FileSystemChanged(event)) = pipeline.try_take() {
                events.push(event);
            }

            std::thread::sleep(std::time::Duration::from_millis(20));
        }

        watcher.stop();

        // The pending read fails once the directory is gone; like
        // FileSystemWatcher.Error that requests a full refresh, and the
        // watcher then stops instead of re-issuing the failing read.
        assert!(events.len() <= 2, "watcher kept firing after its directory vanished: {}", events.len());

        // Wine's ReadDirectoryChangesW never completes for a deleted
        // directory, so only the upper bound holds there.
        if !running_under_wine() {
            assert!(events.iter().any(|event| event.full_refresh), "expected a full-refresh event, got {events:?}");
        }
    }

    /// Wine exports `wine_get_version` from ntdll; Windows does not.
    #[cfg(windows)]
    fn running_under_wine() -> bool {
        use windows_sys::Win32::System::LibraryLoader::{GetModuleHandleA, GetProcAddress};

        unsafe {
            let ntdll = GetModuleHandleA(c"ntdll.dll".as_ptr().cast());
            !ntdll.is_null() && GetProcAddress(ntdll, c"wine_get_version".as_ptr().cast()).is_some()
        }
    }

    #[cfg(not(windows))]
    fn running_under_wine() -> bool {
        false
    }
}
