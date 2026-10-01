//! Port of `src/Wade/DirectorySizeLoader.cs`: computes a directory's total
//! size on a background thread and injects a `DirectorySizeReadyEvent`.

use std::sync::mpsc::Sender;

use crate::input::{CancelToken, DirectorySizeReadyEvent, InputEvent};

/// Port of `DirectorySizeLoader`.
pub struct DirectorySizeLoader {
    cancel: Option<CancelToken>,
}

impl Default for DirectorySizeLoader {
    fn default() -> Self {
        Self::new()
    }
}

impl DirectorySizeLoader {
    #[must_use]
    pub fn new() -> Self {
        Self { cancel: None }
    }

    /// Port of `BeginCalculation`: cancels any in-flight calculation and
    /// spawns a fresh one on a background thread.
    pub fn begin_calculation(&mut self, directory_path: &str, out: Sender<InputEvent>) {
        if let Some(previous) = self.cancel.take() {
            previous.cancel();
        }

        let cancel = CancelToken::new();
        self.cancel = Some(cancel.clone());
        let directory_path = directory_path.to_string();

        std::thread::spawn(move || {
            calculate(&directory_path, &cancel, &out);
        });
    }

    /// Port of `Cancel`.
    pub fn cancel(&mut self) {
        if let Some(cancel) = self.cancel.take() {
            cancel.cancel();
        }
    }
}

/// Core of `DirectorySizeLoader.Calculate`: recursive walk summing file
/// lengths (IgnoreInaccessible semantics: unreadable entries are skipped),
/// checking cancellation every 500 files, injecting on success.
pub fn calculate(directory_path: &str, cancel: &CancelToken, out: &Sender<InputEvent>) {
    let total_bytes = crate::app::inline_dir_size_loader::sum_directory(directory_path, cancel);

    if cancel.is_cancelled() {
        return;
    }

    let _ = out.send(InputEvent::DirectorySizeReady(DirectorySizeReadyEvent {
        path: directory_path.to_string(),
        total_bytes,
    }));
}

#[cfg(test)]
mod tests {
    use super::calculate;
    use crate::input::InputEvent;
    use crate::input::input_pipeline::InputPipeline;
    use crate::input::CancelToken;
    use std::sync::mpsc::channel;

    fn test_root(name: &str) -> std::path::PathBuf {
        let dir = std::env::temp_dir().join(format!("wade-dsl-{}-{}", std::process::id(), name));
        let _ = std::fs::remove_dir_all(&dir);
        std::fs::create_dir_all(&dir).unwrap();
        dir
    }

    #[test]
    fn sums_nested_files() {
        let root = test_root("nested");
        std::fs::write(root.join("a.txt"), [0u8; 100]).unwrap();
        let sub = root.join("sub");
        std::fs::create_dir(&sub).unwrap();
        std::fs::write(sub.join("b.txt"), [0u8; 250]).unwrap();

        let pipeline = InputPipeline::new();
        calculate(root.to_str().unwrap(), &CancelToken::new(), &pipeline.sender());

        match pipeline.try_take() {
            Some(InputEvent::DirectorySizeReady(event)) => {
                assert_eq!(event.total_bytes, 350);
                assert_eq!(event.path, root.to_str().unwrap());
            }
            other => panic!("expected DirectorySizeReady, got {other:?}"),
        }
    }

    #[test]
    fn empty_directory_is_zero() {
        let root = test_root("empty");

        let pipeline = InputPipeline::new();
        calculate(root.to_str().unwrap(), &CancelToken::new(), &pipeline.sender());

        match pipeline.try_take() {
            Some(InputEvent::DirectorySizeReady(event)) => assert_eq!(event.total_bytes, 0),
            other => panic!("expected DirectorySizeReady, got {other:?}"),
        }
    }

    #[test]
    fn missing_directory_is_zero() {
        let root = test_root("missing");
        std::fs::remove_dir(&root).unwrap();

        let pipeline = InputPipeline::new();
        calculate(root.to_str().unwrap(), &CancelToken::new(), &pipeline.sender());

        match pipeline.try_take() {
            Some(InputEvent::DirectorySizeReady(event)) => assert_eq!(event.total_bytes, 0),
            other => panic!("expected DirectorySizeReady, got {other:?}"),
        }
    }

    #[test]
    fn cancelled_walk_injects_nothing() {
        let root = test_root("cancelled");
        std::fs::write(root.join("a.txt"), [0u8; 10]).unwrap();

        let cancel = CancelToken::new();
        cancel.cancel();

        let (sender, receiver) = channel();
        calculate(root.to_str().unwrap(), &cancel, &sender);

        assert!(receiver.try_recv().is_err());
    }
}
