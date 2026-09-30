//! Port of `src/Wade/GitStatusLoader.cs`: loads branch name, statuses, and
//! ahead/behind counts on a background thread and injects a
//! `GitStatusReadyEvent` into the pipeline.

use std::collections::HashMap;
use std::sync::mpsc::Sender;
use std::sync::Arc;

use crate::fs::directory_contents::GitFileStatus;
use crate::fs::git_utils::{self};

/// Concrete queries backed by the real GitUtils implementations.
#[derive(Debug, Default)]
pub struct GitUtilsQueries;
use crate::input::{CancelToken, GitStatusReadyEvent, InputEvent};

/// The three query steps of the load sequence, injectable for tests (C#
/// hardcodes the GitUtils statics).
pub trait GitQueries: Send + Sync {
    fn read_branch_name(&self, repo_root: &str) -> Option<String>;
    fn query_status(&self, repo_root: &str, cancel: &CancelToken) -> Option<HashMap<String, GitFileStatus>>;
    fn get_ahead_behind(&self, repo_root: &str, cancel: &CancelToken) -> Option<(u32, u32)>;
}

impl GitQueries for GitUtilsQueries {
    fn read_branch_name(&self, repo_root: &str) -> Option<String> {
        git_utils::read_branch_name(repo_root)
    }

    fn query_status(&self, repo_root: &str, cancel: &CancelToken) -> Option<HashMap<String, GitFileStatus>> {
        git_utils::query_status(repo_root, cancel)
    }

    fn get_ahead_behind(&self, repo_root: &str, cancel: &CancelToken) -> Option<(u32, u32)> {
        git_utils::get_ahead_behind(repo_root, cancel)
    }
}

/// Port of `GitStatusLoader`.
pub struct GitStatusLoader {
    cancel: Option<CancelToken>,
}

impl Default for GitStatusLoader {
    fn default() -> Self {
        Self::new()
    }
}

impl GitStatusLoader {
    #[must_use]
    pub fn new() -> Self {
        Self { cancel: None }
    }

    /// Port of `BeginLoad`: cancels any in-flight load, spawns the load
    /// sequence on a background thread, injects the ready event.
    pub fn begin_load(&mut self, repo_root: &str, queries: Arc<dyn GitQueries>, out: Sender<InputEvent>) {
        if let Some(previous) = self.cancel.take() {
            previous.cancel();
        }

        let cancel = CancelToken::new();
        self.cancel = Some(cancel.clone());
        let repo_root = repo_root.to_string();

        std::thread::spawn(move || {
            let branch = queries.read_branch_name(&repo_root);

            if cancel.is_cancelled() {
                return;
            }

            let statuses = queries.query_status(&repo_root, &cancel);

            if cancel.is_cancelled() {
                return;
            }

            let (ahead, behind) = queries.get_ahead_behind(&repo_root, &cancel).unwrap_or((0, 0));

            if cancel.is_cancelled() {
                return;
            }

            let _ = out.send(InputEvent::GitStatusReady(GitStatusReadyEvent {
                repo_root,
                branch_name: branch,
                statuses,
                ahead,
                behind,
            }));
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
    struct FakeQueries {
        branch: String,
        query_delay_ms: u64,
    }

    impl GitQueries for FakeQueries {
        fn read_branch_name(&self, _repo_root: &str) -> Option<String> {
            Some(self.branch.clone())
        }

        fn query_status(&self, _repo_root: &str, cancel: &CancelToken) -> Option<HashMap<String, GitFileStatus>> {
            std::thread::sleep(std::time::Duration::from_millis(self.query_delay_ms));
            if cancel.is_cancelled() {
                return None;
            }

            let mut map = HashMap::new();
            map.insert(r"C:\repo\file.txt".to_string(), GitFileStatus::MODIFIED);
            Some(map)
        }

        fn get_ahead_behind(&self, _repo_root: &str, _cancel: &CancelToken) -> Option<(u32, u32)> {
            Some((2, 1))
        }
    }

    #[test]
    fn injects_ready_event() {
        let mut loader = GitStatusLoader::new();
        let pipeline = InputPipeline::new();
        let queries: Arc<dyn GitQueries> = Arc::new(FakeQueries {
            branch: "main".to_string(),
            query_delay_ms: 0,
        });

        loader.begin_load("C:\\repo", queries, pipeline.sender());
        let cancel = CancelToken::new();
        let event = pipeline.wait_next(&cancel).expect("event");

        match event {
            InputEvent::GitStatusReady(ready) => {
                assert_eq!(ready.repo_root, "C:\\repo");
                assert_eq!(ready.branch_name.as_deref(), Some("main"));
                assert_eq!(ready.ahead, 2);
                assert_eq!(ready.behind, 1);
                assert!(ready.statuses.as_ref().is_some_and(|s| s.contains_key(r"C:\repo\file.txt")));
            }
            other => panic!("unexpected event: {other:?}"),
        }
    }

    #[test]
    fn cancel_prevents_send() {
        let mut loader = GitStatusLoader::new();
        let pipeline = InputPipeline::new();
        let queries: Arc<dyn GitQueries> = Arc::new(FakeQueries {
            branch: "main".to_string(),
            query_delay_ms: 100,
        });

        loader.begin_load("C:\\repo", queries, pipeline.sender());
        // Cancel while the fake query is in flight; the thread observes the
        // cancellation at its next check and exits without injecting.
        loader.cancel();

        std::thread::sleep(std::time::Duration::from_millis(200));
        assert!(pipeline.try_take().is_none(), "cancelled load must not inject");
    }
}
