//! Port of `src/Wade/FileOperationRunner.cs`: copy/move/delete on a
//! background thread with progress + completion events into the pipeline.

use std::sync::mpsc::Sender;

use crate::fs::file_operations;
use crate::input::{CancelToken, FileOperationCompleteEvent, InputEvent};

/// One file operation for the runner thread: the C# runner closes over
/// `RunPaste`/`RunDelete`; the Rust runner receives it as a closure and the
/// App builds the appropriate one.
pub type FileOperation = Box<dyn FnOnce(&CancelToken, &Sender<InputEvent>) -> (usize, usize, bool) + Send>;

/// Port of `FileOperationRunner`.
pub struct FileOperationRunner {
    cancel: Option<CancelToken>,
}

impl Default for FileOperationRunner {
    fn default() -> Self {
        Self::new()
    }
}

impl FileOperationRunner {
    #[must_use]
    pub fn new() -> Self {
        Self { cancel: None }
    }

    /// Port of `BeginPaste`/`BeginDelete`'s `StartAction` shape: cancels any
    /// in-flight operation, runs on a background thread. The closure emits
    /// `FileOperationProgress` events per item and the runner emits the
    /// `FileOperationComplete` event when not cancelled.
    pub fn begin(&mut self, operation: FileOperation, out: Sender<InputEvent>) {
        if let Some(previous) = self.cancel.take() {
            previous.cancel();
        }

        let cancel = CancelToken::new();
        self.cancel = Some(cancel.clone());

        std::thread::spawn(move || {
            let (success_count, error_count, was_cut) = operation(&cancel, &out);

            if cancel.is_cancelled() {
                return;
            }

            let _ = out.send(InputEvent::FileOperationComplete(FileOperationCompleteEvent {
                success_count,
                error_count,
                was_cut,
            }));
        });
    }

    /// Port of `Cancel` (also the Esc-to-cancel path).
    pub fn cancel(&mut self) {
        if let Some(cancel) = self.cancel.take() {
            cancel.cancel();
        }
    }
}

/// Builds the paste operation (copy or move per item) with the C# runner's
/// exact per-item logic, emitting progress events. Never emits the
/// completion event itself, the runner does.
pub fn paste_operation(
    sources: Vec<String>,
    destination: String,
    is_cut: bool,
    overwrite: bool,
    copy_symlinks_as_links: bool,
) -> FileOperation {
    Box::new(move |cancel, progress| {
        let total = sources.len();
        let mut success = 0;
        let mut errors = 0;

        for (i, source_path) in sources.iter().enumerate() {
            if cancel.is_cancelled() {
                break;
            }

            let current_name = crate::app::dialogs::file_name_of(source_path);
            let _ = progress.send(InputEvent::FileOperationProgress(crate::input::FileOperationProgressEvent {
                index: i + 1,
                total,
                current_name: current_name.clone(),
            }));

            let dest_path = std::path::Path::new(&destination).join(&current_name).to_string_lossy().to_string();

            if std::path::Path::new(&dest_path).symlink_metadata().is_ok() {
                if !overwrite {
                    errors += 1;
                    continue;
                }

                if file_operations::delete_existing(&dest_path).is_err() {
                    errors += 1;
                    continue;
                }
            }

            let result = if is_cut {
                file_operations::move_path(source_path, &dest_path)
            } else {
                file_operations::copy_path(source_path, &dest_path, copy_symlinks_as_links)
            };

            match result {
                Ok(()) => success += 1,
                Err(_) => errors += 1,
            }
        }

        (success, errors, is_cut)
    })
}

/// Builds the delete operation with the C# runner's exact per-item loop.
pub fn delete_operation(paths: Vec<String>, permanent: bool) -> FileOperation {
    Box::new(move |cancel, progress| {
        let total = paths.len();
        let mut success = 0;
        let mut errors = 0;

        for (i, path) in paths.iter().enumerate() {
            if cancel.is_cancelled() {
                break;
            }

            let _ = progress.send(InputEvent::FileOperationProgress(crate::input::FileOperationProgressEvent {
                index: i + 1,
                total,
                current_name: crate::app::dialogs::file_name_of(path),
            }));

            let (item_success, item_errors) =
                file_operations::delete_paths(std::slice::from_ref(path), permanent, cancel);
            success += item_success;
            errors += item_errors;
        }

        (success, errors, false)
    })
}

#[cfg(test)]
mod tests {
    use super::*;
    use crate::input::input_pipeline::InputPipeline;
    use std::path::Path;

    fn temp_dir(name: &str) -> std::path::PathBuf {
        let dir = std::env::temp_dir().join(format!("wade-filerun-test-{}-{name}", std::process::id()));
        let _ = std::fs::remove_dir_all(&dir);
        std::fs::create_dir_all(&dir).expect("mkdir");
        dir
    }

    fn p(path: &Path) -> String {
        path.to_string_lossy().to_string()
    }

    fn collect_events(pipeline: &InputPipeline, count: usize) -> Vec<InputEvent> {
        let cancel = CancelToken::new();
        let mut events = Vec::new();
        while events.len() < count {
            match pipeline.wait_next(&cancel) {
                Some(event) => events.push(event),
                None => break,
            }
        }

        events
    }

    #[test]
    fn delete_operation_reports_counts_and_completion() {
        let dir = temp_dir("delrun");
        let a = dir.join("a.txt");
        let b = dir.join("b.txt");
        std::fs::write(&a, b"x").expect("write");
        std::fs::write(&b, b"y").expect("write");

        let mut runner = FileOperationRunner::new();
        let pipeline = InputPipeline::new();
        runner.begin(delete_operation(vec![p(&a), p(&b), p(&dir.join("missing.txt"))], true), pipeline.sender());

        let events = collect_events(&pipeline, 4); // 3 progress + 1 completion
        let progress_count = events.iter().filter(|e| matches!(e, InputEvent::FileOperationProgress(_))).count();
        assert_eq!(progress_count, 3);

        let Some(InputEvent::FileOperationComplete(completion)) = events.last() else {
            panic!("expected completion, got {:?}", events.last());
        };
        assert_eq!(completion.success_count, 2);
        assert_eq!(completion.error_count, 1);
        assert!(!completion.was_cut);
        assert!(!a.exists());
        assert!(!b.exists());
    }

    #[test]
    fn paste_operation_copies_with_progress() {
        let dir = temp_dir("pasterun");
        let src_dir = dir.join("src");
        std::fs::create_dir_all(&src_dir).expect("mkdir");
        std::fs::write(src_dir.join("f.txt"), b"data").expect("write");

        let dest_dir = dir.join("dest");
        std::fs::create_dir_all(&dest_dir).expect("mkdir");

        let mut runner = FileOperationRunner::new();
        let pipeline = InputPipeline::new();
        runner.begin(
            paste_operation(vec![p(&src_dir.join("f.txt"))], p(&dest_dir), false, false, true),
            pipeline.sender(),
        );

        let events = collect_events(&pipeline, 2);
        let Some(InputEvent::FileOperationComplete(completion)) = events.last() else {
            panic!("expected completion, got {:?}", events.last());
        };
        assert_eq!((completion.success_count, completion.error_count), (1, 0));
        assert!(!completion.was_cut);
        assert_eq!(std::fs::read(dest_dir.join("f.txt")).expect("read"), b"data");
    }

    #[test]
    fn paste_operation_moves_on_cut() {
        let dir = temp_dir("cutrun");
        let src = dir.join("m.txt");
        std::fs::write(&src, b"data").expect("write");
        let dest_dir = dir.join("dest");
        std::fs::create_dir_all(&dest_dir).expect("mkdir");

        let mut runner = FileOperationRunner::new();
        let pipeline = InputPipeline::new();
        runner.begin(paste_operation(vec![p(&src)], p(&dest_dir), true, false, true), pipeline.sender());

        let _ = collect_events(&pipeline, 2);
        assert!(!src.exists());
        assert_eq!(std::fs::read(dest_dir.join("m.txt")).expect("read"), b"data");
    }

    #[test]
    fn paste_without_overwrite_counts_existing_as_error() {
        let dir = temp_dir("conflict");
        let src = dir.join("f.txt");
        std::fs::write(&src, b"new").expect("write");
        let dest = dir.join("f.txt");
        std::fs::write(&dest, b"old").expect("write");

        let mut runner = FileOperationRunner::new();
        let pipeline = InputPipeline::new();
        runner.begin(paste_operation(vec![p(&src)], p(&dir), false, false, true), pipeline.sender());

        let events = collect_events(&pipeline, 2);
        let Some(InputEvent::FileOperationComplete(completion)) = events.last() else {
            panic!("expected completion");
        };
        assert_eq!((completion.success_count, completion.error_count), (0, 1));
        assert_eq!(std::fs::read(&dest).expect("read"), b"old");
    }

    #[test]
    fn cancel_midway_suppresses_completion() {
        let dir = temp_dir("cancelrun");
        let sources: Vec<String> = (0..8)
            .map(|i| {
                let f = dir.join(format!("f{i}.txt"));
                std::fs::write(&f, b"x").expect("write");
                p(&f)
            })
            .collect();
        let dest_dir = dir.join("dest");
        std::fs::create_dir_all(&dest_dir).expect("mkdir");

        let mut runner = FileOperationRunner::new();
        let pipeline = InputPipeline::new();
        runner.begin(paste_operation(sources, p(&dest_dir), false, false, true), pipeline.sender());

        // Wait for the first progress event, then cancel
        let cancel = CancelToken::new();
        let mut saw_first = false;
        while let Some(event) = pipeline.wait_next(&cancel) {
            if matches!(event, InputEvent::FileOperationProgress(_)) {
                saw_first = true;
                runner.cancel();
                break;
            }
        }
        assert!(saw_first, "no progress event seen");

        std::thread::sleep(std::time::Duration::from_millis(150));
        assert!(
            !pipeline.try_take().is_some_and(|e| matches!(e, InputEvent::FileOperationComplete(_))),
            "cancelled operation must not complete"
        );
    }
}
