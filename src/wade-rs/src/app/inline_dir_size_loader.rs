//! Port of `src/Wade/InlineDirSizeLoader.cs`: computes sizes for a list of
//! directories on a background thread, injecting one `InlineDirSizeReadyEvent`
//! per directory and a final `InlineDirSizeCompleteEvent`.

use std::sync::mpsc::Sender;

use crate::input::{CancelToken, InlineDirSizeCompleteEvent, InlineDirSizeReadyEvent, InputEvent};

/// Port of `InlineDirSizeLoader`.
pub struct InlineDirSizeLoader {
    cancel: Option<CancelToken>,
}

impl Default for InlineDirSizeLoader {
    fn default() -> Self {
        Self::new()
    }
}

impl InlineDirSizeLoader {
    #[must_use]
    pub fn new() -> Self {
        Self { cancel: None }
    }

    /// Port of `BeginLoad`: cancels any in-flight load and spawns a fresh
    /// one on a background thread.
    pub fn begin_load(&mut self, parent_path: &str, directory_paths: &[String], out: Sender<InputEvent>) {
        if let Some(previous) = self.cancel.take() {
            previous.cancel();
        }

        let cancel = CancelToken::new();
        self.cancel = Some(cancel.clone());
        let parent_path = parent_path.to_string();
        let directory_paths = directory_paths.to_vec();

        std::thread::spawn(move || {
            load(&parent_path, &directory_paths, &cancel, &out);
        });
    }

    /// Port of `Cancel`.
    pub fn cancel(&mut self) {
        if let Some(cancel) = self.cancel.take() {
            cancel.cancel();
        }
    }
}

/// Recursive walk summing one directory's file lengths (shared with
/// `DirectorySizeLoader`; cancellation checked every 500 files).
pub(crate) fn sum_directory(directory_path: &str, cancel: &CancelToken) -> i64 {
    let mut total_bytes: i64 = 0;
    let mut file_count: usize = 0;
    let mut stack = vec![std::path::PathBuf::from(directory_path)];

    while let Some(dir) = stack.pop() {
        let Ok(read_dir) = std::fs::read_dir(&dir) else {
            continue; // IgnoreInaccessible
        };

        for entry in read_dir.flatten() {
            if file_count > 0 && file_count.is_multiple_of(500) && cancel.is_cancelled() {
                return total_bytes;
            }

            let Ok(file_type) = entry.file_type() else {
                continue;
            };

            if file_type.is_dir() {
                stack.push(entry.path());
                continue;
            }

            // A symlink/junction to a directory is neither listed by .NET's
            // EnumerateFiles nor recursed into
            if file_type.is_symlink() && std::fs::metadata(entry.path()).is_ok_and(|target| target.is_dir()) {
                continue;
            }

            file_count += 1;

            let Ok(metadata) = entry.metadata() else {
                continue;
            };

            let len = metadata.len();
            total_bytes += i64::try_from(len).unwrap_or(i64::MAX);
        }

        if cancel.is_cancelled() {
            return total_bytes;
        }
    }

    total_bytes
}

/// Core of `InlineDirSizeLoader.Load`.
pub fn load(parent_path: &str, directory_paths: &[String], cancel: &CancelToken, out: &Sender<InputEvent>) {
    for directory_path in directory_paths {
        if cancel.is_cancelled() {
            return;
        }

        let total_bytes = sum_directory(directory_path, cancel);

        if cancel.is_cancelled() {
            return;
        }

        let _ = out.send(InputEvent::InlineDirSizeReady(InlineDirSizeReadyEvent {
            parent_path: parent_path.to_string(),
            directory_path: directory_path.clone(),
            total_bytes,
        }));
    }

    if !cancel.is_cancelled() {
        let _ = out.send(InputEvent::InlineDirSizeComplete(InlineDirSizeCompleteEvent {
            parent_path: parent_path.to_string(),
        }));
    }
}

#[cfg(test)]
mod tests {
    use super::load;
    use crate::input::CancelToken;
    use crate::input::InputEvent;
    use crate::input::input_pipeline::InputPipeline;

    fn test_root(name: &str) -> std::path::PathBuf {
        let dir = std::env::temp_dir().join(format!("wade-idl-{}-{}", std::process::id(), name));
        let _ = std::fs::remove_dir_all(&dir);
        std::fs::create_dir_all(&dir).unwrap();
        dir
    }

    #[test]
    fn emits_ready_per_directory_then_complete() {
        let root = test_root("multi");
        let dir_a = root.join("a");
        let dir_b = root.join("b");
        std::fs::create_dir(&dir_a).unwrap();
        std::fs::create_dir(&dir_b).unwrap();
        std::fs::write(dir_a.join("x.txt"), [0u8; 40]).unwrap();
        std::fs::write(dir_b.join("y.txt"), [0u8; 60]).unwrap();

        let pipeline = InputPipeline::new();
        let paths = vec![dir_a.to_string_lossy().to_string(), dir_b.to_string_lossy().to_string()];
        load(root.to_str().unwrap(), &paths, &CancelToken::new(), &pipeline.sender());

        let mut sizes = Vec::new();
        let mut completed = false;

        for event in std::iter::from_fn(|| pipeline.try_take()) {
            match event {
                InputEvent::InlineDirSizeReady(ready) => sizes.push((ready.directory_path.clone(), ready.total_bytes)),
                InputEvent::InlineDirSizeComplete(done) => {
                    assert_eq!(done.parent_path, root.to_str().unwrap());
                    completed = true;
                }
                other => panic!("unexpected event {other:?}"),
            }
        }

        assert_eq!(sizes.len(), 2);
        assert!(sizes.contains(&(dir_a.to_string_lossy().to_string(), 40)));
        assert!(sizes.contains(&(dir_b.to_string_lossy().to_string(), 60)));
        assert!(completed);
    }

    #[test]
    fn empty_directory_list_completes_immediately() {
        let pipeline = InputPipeline::new();
        load("C:/ nowhere", &[], &CancelToken::new(), &pipeline.sender());

        match pipeline.try_take() {
            Some(InputEvent::InlineDirSizeComplete(done)) => assert_eq!(done.parent_path, "C:/ nowhere"),
            other => panic!("expected InlineDirSizeComplete, got {other:?}"),
        }
    }

    #[test]
    fn cancelled_before_start_emits_nothing() {
        let root = test_root("cancel");
        std::fs::write(root.join("a.txt"), [0u8; 5]).unwrap();

        let cancel = CancelToken::new();
        cancel.cancel();

        let pipeline = InputPipeline::new();
        let paths = vec![root.to_string_lossy().to_string()];
        load(root.to_str().unwrap(), &paths, &cancel, &pipeline.sender());

        assert!(pipeline.try_take().is_none());
    }

    #[cfg(unix)]
    #[test]
    fn symlinked_directory_is_not_counted_or_followed() {
        let root = test_root("symlink");
        let real = root.join("real");
        std::fs::create_dir_all(&real).unwrap();
        std::fs::write(real.join("data.bin"), [0u8; 100]).unwrap();

        let walked = root.join("walked");
        std::fs::create_dir_all(&walked).unwrap();
        std::fs::write(walked.join("own.bin"), [0u8; 7]).unwrap();
        std::os::unix::fs::symlink(&real, walked.join("link-to-real")).unwrap();

        let total = super::sum_directory(walked.to_str().unwrap(), &CancelToken::new());
        let _ = std::fs::remove_dir_all(&root);
        assert_eq!(total, 7);
    }

    /// Denies Everyone the right to list `dir`; false when icacls is not
    /// usable, so the caller can skip.
    #[cfg(windows)]
    fn deny_listing(dir: &std::path::Path) -> bool {
        std::process::Command::new("icacls")
            .arg(dir)
            .args(["/deny", "*S-1-1-0:(RD)"])
            .output()
            .is_ok_and(|output| output.status.success())
            && std::fs::read_dir(dir).is_err()
    }

    #[cfg(windows)]
    fn allow_listing(dir: &std::path::Path) {
        let _ = std::process::Command::new("icacls").arg(dir).args(["/remove:d", "*S-1-1-0"]).output();
    }

    #[cfg(windows)]
    #[test]
    fn hardlinks_count_once_per_link_and_streams_are_excluded() {
        let root = test_root("links-streams");
        std::fs::write(root.join("a.bin"), [0u8; 100]).unwrap();
        std::fs::hard_link(root.join("a.bin"), root.join("b.bin")).unwrap();
        std::fs::write(root.join("f.txt"), [0u8; 10]).unwrap();
        std::fs::write(root.join("f.txt:extra"), [0u8; 50]).unwrap();

        // Like C# and Explorer: each link counts in full, streams do not
        let total = super::sum_directory(&root.to_string_lossy(), &CancelToken::new());
        let _ = std::fs::remove_dir_all(&root);
        assert_eq!(total, 210);
    }

    #[cfg(windows)]
    #[test]
    fn a_directory_that_cannot_be_listed_is_skipped() {
        let root = test_root("denied");
        std::fs::write(root.join("a.bin"), [0u8; 7]).unwrap();
        let denied = root.join("denied");
        std::fs::create_dir(&denied).unwrap();
        std::fs::write(denied.join("big.bin"), [0u8; 1000]).unwrap();
        if !deny_listing(&denied) {
            allow_listing(&denied);
            return;
        }

        let total = super::sum_directory(&root.to_string_lossy(), &CancelToken::new());
        allow_listing(&denied);
        let _ = std::fs::remove_dir_all(&root);
        assert_eq!(total, 7);
    }
}
