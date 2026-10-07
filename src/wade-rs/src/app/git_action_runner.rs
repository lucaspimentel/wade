//! Port of `src/Wade/GitActionRunner.cs`: runs a git action on a background
//! thread and injects a `GitActionCompleteEvent` into the pipeline.

use std::sync::mpsc::Sender;

use crate::input::{CancelToken, GitActionCompleteEvent, InputEvent};

/// One git action: the C# runner closes over `GitUtils.<Action>`; the Rust
/// runner receives it as a closure.
pub type GitAction = Box<dyn FnOnce(&CancelToken) -> (bool, Option<String>) + Send>;

/// Port of `GitActionRunner`.
pub struct GitActionRunner {
    cancel: Option<CancelToken>,
}

impl Default for GitActionRunner {
    fn default() -> Self {
        Self::new()
    }
}

impl GitActionRunner {
    #[must_use]
    pub fn new() -> Self {
        Self { cancel: None }
    }

    /// Port of `StartAction`: cancels any in-flight action, runs the action
    /// on a background thread, injects the completion event (unless
    /// cancelled, matching the C# token check).
    pub fn start_action(&mut self, action: GitAction, out: Sender<InputEvent>) {
        if let Some(previous) = self.cancel.take() {
            previous.cancel();
        }

        let cancel = CancelToken::new();
        self.cancel = Some(cancel.clone());

        std::thread::spawn(move || {
            let (success, error) = action(&cancel);

            if cancel.is_cancelled() {
                return;
            }

            let _ = out.send(InputEvent::GitActionComplete(GitActionCompleteEvent { success, error_message: error }));
        });
    }

    /// Port of `Cancel`.
    pub fn cancel(&mut self) {
        if let Some(cancel) = self.cancel.take() {
            cancel.cancel();
        }
    }
}

#[cfg(test)]
mod tests {
    use super::*;
    use crate::input::input_pipeline::InputPipeline;

    #[test]
    fn injects_completion_event() {
        let mut runner = GitActionRunner::new();
        let pipeline = InputPipeline::new();
        let sender = pipeline.sender();

        runner.start_action(Box::new(|_cancel| (true, None)), sender);

        let cancel = CancelToken::new();
        match pipeline.wait_next(&cancel).expect("event") {
            InputEvent::GitActionComplete(event) => {
                assert!(event.success);
                assert!(event.error_message.is_none());
            }
            other => panic!("unexpected event: {other:?}"),
        }
    }

    #[test]
    fn error_message_propagates() {
        let mut runner = GitActionRunner::new();
        let pipeline = InputPipeline::new();

        runner.start_action(Box::new(|_cancel| (false, Some("boom".to_string()))), pipeline.sender());

        let cancel = CancelToken::new();
        match pipeline.wait_next(&cancel).expect("event") {
            InputEvent::GitActionComplete(event) => {
                assert!(!event.success);
                assert_eq!(event.error_message.as_deref(), Some("boom"));
            }
            other => panic!("unexpected event: {other:?}"),
        }
    }

    #[test]
    fn cancel_prevents_injection() {
        let mut runner = GitActionRunner::new();
        let pipeline = InputPipeline::new();

        runner.start_action(
            Box::new(|cancel| {
                // Hold the "action" until cancelled or a short delay passes;
                // the runner's Cancel() flips the token before completion
                for _ in 0..100 {
                    if cancel.is_cancelled() {
                        return (false, None);
                    }

                    std::thread::sleep(std::time::Duration::from_millis(5));
                }

                (true, None)
            }),
            pipeline.sender(),
        );
        runner.cancel();

        std::thread::sleep(std::time::Duration::from_millis(100));
        assert!(pipeline.try_take().is_none(), "cancelled action must not inject");
    }
}
