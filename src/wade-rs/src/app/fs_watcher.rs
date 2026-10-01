//! Port of `src/Wade/FileSystemWatcherManager.cs`: watches the current
//! directory for changes and injects a debounced `FileSystemChangedEvent`.
//!
//! The C# version uses `System.IO.FileSystemWatcher`; this port uses the
//! `notify` crate (ReadDirectoryChangesW, inotify, FSEvents/kqueue), with
//! the same non-recursive scope, `.git` filter, 300ms debounce and
//! full-refresh-on-error semantics.

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
    /// Dropping the watcher stops watching.
    worker: Option<notify::RecommendedWatcher>,
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

        // Drops the watcher; its event sender drops with it, which ends the
        // debounce pump (the shutdown signal discards a pending event).
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

/// Starts a non-recursive watch. Each reportable change sends `false`;
/// a watcher error, a rescan request (queue overflow) or the removal of
/// the watched directory sends `true` (C# `OnError`, full refresh). Mirrors `FileSystemWatcher.NotifyFilter`
/// (FileName | DirectoryName | LastWrite | Size): creations, removals,
/// renames and modifications count; accesses do not.
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
            Ok(event) if matches!(event.kind, EventKind::Remove(_)) && event.paths.contains(&watched) => {
                Some(true)
            }
            Ok(event) => {
                let relevant = matches!(
                    event.kind,
                    EventKind::Create(_) | EventKind::Remove(_) | EventKind::Modify(_) | EventKind::Any
                );
                let only_git = !event.paths.is_empty()
                    && event.paths.iter().all(|p| {
                        p.file_name().is_some_and(|name| is_git_internal_event(&name.to_string_lossy()))
                    });
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
    use super::{is_git_internal_event, paths_equal, FileSystemWatcherManager, DEBOUNCE_MS};
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
            assert!(
                events.iter().any(|event| event.full_refresh),
                "expected a full-refresh event, got {events:?}"
            );
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
