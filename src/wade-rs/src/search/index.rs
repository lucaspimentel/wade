//! Port of `SearchIndex` and `ActiveQuery`: a thread-safe path index whose
//! queries stream scored results over a channel. A query scans a snapshot
//! of the index on a background thread, and paths added while it is active
//! are pushed live; per-query dedup makes the two paths overlap safely
//! (the C# version iterates a weakly-consistent ConcurrentDictionary).
//! Consumers sort by score descending.

use std::collections::HashSet;
use std::sync::atomic::{AtomicUsize, Ordering};
use std::sync::mpsc::{Receiver, Sender};
use std::sync::{Arc, Condvar, Mutex};

use super::query::{QueryMode, SearchQuery};
use super::scorer::{self, NO_MATCH};
use super::{is_separator, SearchResult};
use crate::input::CancelToken;

/// Port of `SearchOptions`.
#[derive(Clone, Copy, Debug)]
pub struct SearchOptions {
    /// Maximum number of results to stream (default: no limit).
    pub max_results: usize,
}

impl Default for SearchOptions {
    fn default() -> Self {
        Self { max_results: usize::MAX }
    }
}

/// Port of `PathEntry`: precomputed scoring data for an indexed path.
struct PathEntry {
    absolute: Arc<str>,
    relative: Vec<char>,
    file_name_start: usize,
}

#[derive(Default)]
struct IndexState {
    // Ordinal (case-sensitive) to keep distinct paths distinct on Linux
    paths: HashSet<Arc<str>>,
    entries: Vec<Arc<PathEntry>>,
}

/// Port of `SearchIndex`.
pub struct SearchIndex {
    base_path: String,
    state: Mutex<IndexState>,
    active: Mutex<Option<Arc<ActiveQuery>>>,
}

impl SearchIndex {
    /// Paths added later are scored against their path relative to `base_path`.
    #[must_use]
    pub fn new(base_path: &str) -> Self {
        Self {
            base_path: base_path.to_string(),
            state: Mutex::new(IndexState::default()),
            active: Mutex::new(None),
        }
    }

    /// Number of distinct paths in the index.
    #[must_use]
    pub fn count(&self) -> usize {
        self.state.lock().unwrap().paths.len()
    }

    /// Port of `Add`: indexes a path (ignoring duplicates) and, when a query
    /// is active, scores it immediately and pushes a match to its channel.
    pub fn add(&self, path: &str) {
        let entry = {
            let mut state = self.state.lock().unwrap();

            if state.paths.contains(path) {
                return; // Already indexed
            }

            let absolute: Arc<str> = Arc::from(path);
            state.paths.insert(Arc::clone(&absolute));

            let relative: Vec<char> = relative_path(&self.base_path, path).chars().collect();
            let file_name_start = relative.iter().rposition(|&c| is_separator(c)).map_or(0, |i| i + 1);
            let entry = Arc::new(PathEntry {
                absolute,
                relative,
                file_name_start,
            });
            state.entries.push(Arc::clone(&entry));
            entry
        };

        // Live push: check the new path against the active query
        let active = self.active.lock().unwrap();

        if let Some(query) = active.as_ref() {
            query.try_match(&entry);
        }
    }

    /// Port of `Search`: replaces (and completes) any active query and
    /// returns a receiver of results. The channel closes when the query is
    /// cancelled or replaced; an empty query returns an already-closed one.
    pub fn search(&self, raw_query: &str, options: SearchOptions) -> Receiver<SearchResult> {
        let parsed = SearchQuery::parse(raw_query);

        if parsed.is_empty() {
            self.cancel_search();
            let (_, receiver) = std::sync::mpsc::channel();
            return receiver;
        }

        let (sender, receiver) = std::sync::mpsc::channel();
        let query = Arc::new(ActiveQuery::new(parsed, options, sender));

        let previous = self.active.lock().unwrap().replace(Arc::clone(&query));

        if let Some(previous) = previous {
            previous.complete();
        }

        // Snapshot after the query is active: a concurrent add() either lands
        // in the snapshot or sees the active query and pushes live.
        let snapshot: Vec<Arc<PathEntry>> = self.state.lock().unwrap().entries.clone();

        std::thread::spawn(move || {
            for entry in &snapshot {
                if query.cancel.is_cancelled() || query.is_max_results_reached() {
                    break;
                }

                query.try_match(entry);
            }

            query.mark_snapshot_complete();
        });

        receiver
    }

    /// Port of `CancelSearch`: completes the active query's channel. No-op
    /// when no query is active.
    pub fn cancel_search(&self) {
        let query = self.active.lock().unwrap().take();

        if let Some(query) = query {
            query.complete();
        }
    }

    /// Port of `Clear`.
    pub fn clear(&self) {
        self.cancel_search();
        let mut state = self.state.lock().unwrap();
        state.paths.clear();
        state.entries.clear();
    }

    /// Port of `SnapshotCompleteTask`: waits for the active query's initial
    /// scan. Returns false on timeout; true when it finished or no query is
    /// active.
    pub fn wait_for_snapshot(&self, timeout: std::time::Duration) -> bool {
        let query = self.active.lock().unwrap().clone();
        query.is_none_or(|query| query.wait_for_snapshot(timeout))
    }
}

impl Drop for SearchIndex {
    fn drop(&mut self) {
        self.cancel_search();
    }
}

/// `Path.GetRelativePath(basePath, path)` for paths under `base_path` (the
/// finder only indexes descendants); other paths are returned unchanged.
pub(crate) fn relative_path<'a>(base_path: &str, path: &'a str) -> &'a str {
    let base = base_path.trim_end_matches(is_separator);

    let under_base = path.len() > base.len()
        && path.is_char_boundary(base.len())
        && if cfg!(windows) {
            path[..base.len()].eq_ignore_ascii_case(base)
        } else {
            &path[..base.len()] == base
        };

    if !under_base {
        return path;
    }

    let rest = &path[base.len()..];

    match rest.strip_prefix(is_separator) {
        Some(relative) => relative,
        None if base.is_empty() => rest,
        None => path, // A sibling sharing the prefix ("/a/bc" under "/a/b")
    }
}

#[derive(Default)]
struct EmitState {
    sender: Option<Sender<SearchResult>>,
    emitted: HashSet<Arc<str>>,
}

/// Port of `ActiveQuery`: one query's channel, cancellation, dedup set and
/// matching. `try_match` is safe to call from several threads at once.
struct ActiveQuery {
    mode: QueryMode,
    text: Vec<char>,
    case_sensitive: bool,
    max_results: usize,
    cancel: CancelToken,
    result_count: AtomicUsize,
    emit: Mutex<EmitState>,
    snapshot_complete: (Mutex<bool>, Condvar),
}

impl ActiveQuery {
    fn new(query: SearchQuery, options: SearchOptions, sender: Sender<SearchResult>) -> Self {
        Self {
            mode: query.mode,
            text: query.text.chars().collect(),
            case_sensitive: query.case_sensitive,
            max_results: options.max_results,
            cancel: CancelToken::new(),
            result_count: AtomicUsize::new(0),
            emit: Mutex::new(EmitState {
                sender: Some(sender),
                emitted: HashSet::new(),
            }),
            snapshot_complete: (Mutex::new(false), Condvar::new()),
        }
    }

    fn is_max_results_reached(&self) -> bool {
        self.result_count.load(Ordering::SeqCst) >= self.max_results
    }

    /// Port of `TryMatch`: scores the entry and, on a match not yet emitted,
    /// sends it.
    fn try_match(&self, entry: &PathEntry) -> bool {
        if self.cancel.is_cancelled() || self.is_max_results_reached() {
            return false;
        }

        let (score, match_positions) = match self.mode {
            QueryMode::ExactSubstring => scorer::exact_score_with_file_name_priority_positions(
                &self.text,
                &entry.relative,
                entry.file_name_start,
                self.case_sensitive,
            ),
            QueryMode::Fuzzy => {
                scorer::score_with_file_name_priority_positions(&self.text, &entry.relative, entry.file_name_start)
            }
        };

        if score == NO_MATCH {
            return false;
        }

        let mut emit = self.emit.lock().unwrap();

        if self.cancel.is_cancelled() || self.is_max_results_reached() {
            return false;
        }

        if !emit.emitted.insert(Arc::clone(&entry.absolute)) {
            return false; // Already emitted
        }

        self.result_count.fetch_add(1, Ordering::SeqCst);

        if let Some(sender) = &emit.sender {
            let _ = sender.send(SearchResult {
                path: entry.absolute.to_string(),
                score,
                match_positions,
            });
        }

        true
    }

    /// Port of `MarkSnapshotComplete`: the initial scan finished; the channel
    /// stays open for live pushes.
    fn mark_snapshot_complete(&self) {
        let (done, condvar) = &self.snapshot_complete;
        *done.lock().unwrap() = true;
        condvar.notify_all();
    }

    fn wait_for_snapshot(&self, timeout: std::time::Duration) -> bool {
        let (done, condvar) = &self.snapshot_complete;
        let guard = done.lock().unwrap();
        let (guard, _) = condvar.wait_timeout_while(guard, timeout, |done| !*done).unwrap();
        *guard
    }

    /// Port of `Complete`: cancels outstanding work and closes the channel
    /// (receivers drain what was sent, then see the disconnect).
    fn complete(&self) {
        self.cancel.cancel();
        self.emit.lock().unwrap().sender = None;
    }
}

#[cfg(test)]
mod tests {
    use std::sync::mpsc::Receiver;
    use std::time::Duration;

    use super::{relative_path, SearchIndex, SearchOptions};
    use crate::search::SearchResult;

    const SEP: char = std::path::MAIN_SEPARATOR;

    fn base_path() -> String {
        std::env::temp_dir().to_string_lossy().into_owned()
    }

    fn abs(parts: &[&str]) -> String {
        std::path::Path::new(&base_path())
            .join(parts.join(&SEP.to_string()))
            .to_string_lossy()
            .into_owned()
    }

    fn search(index: &SearchIndex, query: &str) -> Receiver<SearchResult> {
        index.search(query, SearchOptions::default())
    }

    /// Waits for the snapshot scan, cancels, then drains everything sent.
    fn drain_after_snapshot(index: &SearchIndex, receiver: &Receiver<SearchResult>) -> Vec<SearchResult> {
        assert!(index.wait_for_snapshot(Duration::from_secs(5)), "snapshot scan timed out");
        index.cancel_search();
        drain(receiver)
    }

    /// Drains a receiver whose channel is already closed.
    fn drain(receiver: &Receiver<SearchResult>) -> Vec<SearchResult> {
        let mut results = Vec::new();

        while let Ok(result) = receiver.recv_timeout(Duration::from_secs(5)) {
            results.push(result);
        }

        results
    }

    #[test]
    fn add_increments_count() {
        let index = SearchIndex::new(&base_path());
        index.add(&abs(&["src", "Wade", "App.cs"]));
        index.add(&abs(&["src", "Wade", "Program.cs"]));
        assert_eq!(index.count(), 2);
    }

    #[test]
    fn add_duplicate_path_not_counted() {
        let index = SearchIndex::new(&base_path());
        let path = abs(&["src", "Wade", "App.cs"]);
        index.add(&path);
        index.add(&path);
        assert_eq!(index.count(), 1);
    }

    #[test]
    fn search_subsequence_match_finds_results() {
        let index = SearchIndex::new(&base_path());
        index.add(&abs(&["src", "Wade", "App.cs"]));
        index.add(&abs(&["src", "Wade", "Program.cs"]));
        index.add(&abs(&["tests", "Wade.Tests", "Test.cs"]));

        let receiver = search(&index, "App");
        let results = drain_after_snapshot(&index, &receiver);
        assert!(results.iter().any(|r| r.path == abs(&["src", "Wade", "App.cs"])));
    }

    #[test]
    fn search_case_insensitive() {
        let index = SearchIndex::new(&base_path());
        index.add(&abs(&["src", "Wade", "App.cs"]));

        let receiver = search(&index, "app");
        let results = drain_after_snapshot(&index, &receiver);
        assert_eq!(results.len(), 1);
        assert!(results[0].score > i32::MIN);
    }

    #[test]
    fn search_substring_in_file_name_finds_results() {
        let index = SearchIndex::new(&base_path());
        index.add(&abs(&["Documents", "report.pdf"]));
        index.add(&abs(&["Documents", "notes.txt"]));

        let receiver = search(&index, "pdf");
        let results = drain_after_snapshot(&index, &receiver);
        assert_eq!(results.len(), 1);
        assert_eq!(results[0].path, abs(&["Documents", "report.pdf"]));
    }

    #[test]
    fn search_no_match_returns_empty() {
        let index = SearchIndex::new(&base_path());
        index.add(&abs(&["src", "Wade", "App.cs"]));

        let receiver = search(&index, "xyz");
        assert!(drain_after_snapshot(&index, &receiver).is_empty());
    }

    #[test]
    fn search_distinct_results() {
        let index = SearchIndex::new(&base_path());
        // Path with duplicate segment names: still appears only once
        index.add(&abs(&["src", "src", "file.cs"]));

        let receiver = search(&index, "src");
        assert_eq!(drain_after_snapshot(&index, &receiver).len(), 1);
    }

    #[test]
    fn search_empty_query_no_results() {
        let index = SearchIndex::new(&base_path());
        index.add(&abs(&["src", "Wade", "App.cs"]));

        let receiver = search(&index, "");
        assert!(drain_after_snapshot(&index, &receiver).is_empty());
    }

    #[test]
    fn search_max_results_limits_output() {
        let index = SearchIndex::new(&base_path());

        for i in 0..100 {
            index.add(&abs(&["src", &format!("File{i}.cs")]));
        }

        let receiver = index.search("File", SearchOptions { max_results: 5 });
        assert!(drain_after_snapshot(&index, &receiver).len() <= 5);
    }

    #[test]
    fn search_new_query_cancels_previous() {
        let index = SearchIndex::new(&base_path());
        index.add(&abs(&["src", "Wade", "App.cs"]));

        let receiver1 = search(&index, "App");
        let receiver2 = search(&index, "Wade");

        // receiver1 was completed by the second search: draining terminates
        let _ = drain(&receiver1);
        let results2 = drain_after_snapshot(&index, &receiver2);
        assert!(results2.iter().any(|r| r.path == abs(&["src", "Wade", "App.cs"])));
    }

    #[test]
    fn cancel_search_completes_channel() {
        let index = SearchIndex::new(&base_path());
        index.add(&abs(&["src", "Wade", "App.cs"]));

        let receiver = search(&index, "App");
        index.cancel_search();
        // Wait for the scan thread to release its handle on the query
        assert!(index.wait_for_snapshot(Duration::from_secs(5)));
        let _ = drain(&receiver);
        assert!(matches!(receiver.try_recv(), Err(std::sync::mpsc::TryRecvError::Disconnected)));
    }

    #[test]
    fn cancel_search_no_active_query_is_noop() {
        let index = SearchIndex::new(&base_path());
        index.cancel_search();
    }

    #[test]
    fn live_push_add_after_search_pushes_new_match() {
        let index = SearchIndex::new(&base_path());
        index.add(&abs(&["src", "existing.cs"]));

        let receiver = search(&index, "newfile");
        assert!(index.wait_for_snapshot(Duration::from_secs(5)));

        // Live push emits the new match immediately
        index.add(&abs(&["src", "newfile.cs"]));
        index.cancel_search();

        let results = drain(&receiver);
        assert!(results.iter().any(|r| r.path == abs(&["src", "newfile.cs"])));
    }

    #[test]
    fn clear_empties_index() {
        let index = SearchIndex::new(&base_path());
        index.add(&abs(&["src", "file.cs"]));
        assert_eq!(index.count(), 1);
        index.clear();
        assert_eq!(index.count(), 0);
    }

    #[test]
    fn exact_match_ranked_above_fuzzy() {
        let index = SearchIndex::new(&base_path());
        index.add(&abs(&["src", "Wade"])); // consecutive
        index.add(&abs(&["src", "W_a_d_e"])); // spread out

        let receiver = search(&index, "Wade");
        let results = drain_after_snapshot(&index, &receiver);
        assert!(results.len() >= 2);

        let exact = results.iter().find(|r| r.path == abs(&["src", "Wade"])).unwrap();
        let spread = results.iter().find(|r| r.path == abs(&["src", "W_a_d_e"])).unwrap();
        assert!(exact.score > spread.score);
    }

    #[test]
    fn concurrent_add_and_search_no_panics() {
        let index = SearchIndex::new(&base_path());
        let receiver = search(&index, "test");

        std::thread::scope(|scope| {
            for i in 0..100 {
                let index = &index;
                scope.spawn(move || index.add(&abs(&["dir", &format!("test{i}.cs")])));
            }
        });

        index.cancel_search();
        // Every path contains "test" as a subsequence
        assert!(!drain(&receiver).is_empty());
    }

    #[test]
    fn file_name_priority_ranks_file_name_match_higher() {
        let index = SearchIndex::new(&base_path());
        index.add(&abs(&["src", "Wade", "App.cs"]));
        index.add(&abs(&["src", "Applications", "Config.cs"]));

        let receiver = search(&index, "App");
        let results = drain_after_snapshot(&index, &receiver);
        assert!(results.len() >= 2);

        let app = results.iter().find(|r| r.path == abs(&["src", "Wade", "App.cs"])).unwrap();
        let config = results
            .iter()
            .find(|r| r.path == abs(&["src", "Applications", "Config.cs"]))
            .unwrap();
        assert!(app.score > config.score);
    }

    #[test]
    fn search_exact_prefix_only_matches_contiguous_substring() {
        let index = SearchIndex::new(&base_path());
        index.add(&abs(&["src", "abcfile.cs"]));
        index.add(&abs(&["src", "aXbXc.cs"]));

        let receiver = search(&index, "'abc");
        let results = drain_after_snapshot(&index, &receiver);
        assert_eq!(results.len(), 1);
        assert_eq!(results[0].path, abs(&["src", "abcfile.cs"]));
    }

    #[test]
    fn search_exact_prefix_smart_case_lowercase_is_case_insensitive() {
        let index = SearchIndex::new(&base_path());
        index.add(&abs(&["src", "App.cs"]));

        let receiver = search(&index, "'app");
        assert_eq!(drain_after_snapshot(&index, &receiver).len(), 1);
    }

    #[test]
    fn search_exact_prefix_smart_case_uppercase_is_case_sensitive() {
        let index = SearchIndex::new(&base_path());
        index.add(&abs(&["src", "App.cs"]));
        index.add(&abs(&["src", "app.cs"]));

        let receiver = search(&index, "'App");
        let results = drain_after_snapshot(&index, &receiver);
        assert_eq!(results.len(), 1);
        assert_eq!(results[0].path, abs(&["src", "App.cs"]));
    }

    #[test]
    fn search_quote_only_no_results() {
        let index = SearchIndex::new(&base_path());
        index.add(&abs(&["src", "App.cs"]));

        let receiver = search(&index, "'");
        assert!(drain_after_snapshot(&index, &receiver).is_empty());
    }

    #[test]
    fn search_exact_prefix_match_positions_are_contiguous() {
        let index = SearchIndex::new(&base_path());
        index.add(&abs(&["Documents", "report.pdf"]));

        let receiver = search(&index, "'pdf");
        let results = drain_after_snapshot(&index, &receiver);
        assert_eq!(results.len(), 1);
        let positions = &results[0].match_positions;
        assert_eq!(positions.len(), 3);
        assert_eq!(positions[0] + 1, positions[1]);
        assert_eq!(positions[1] + 1, positions[2]);
    }

    #[test]
    fn positions_index_the_relative_path() {
        let index = SearchIndex::new(&base_path());
        index.add(&abs(&["src", "Wade", "App.cs"]));

        let receiver = search(&index, "App");
        let results = drain_after_snapshot(&index, &receiver);
        // "src" + SEP + "Wade" + SEP = 9 chars before the filename
        assert_eq!(results[0].match_positions, [9, 10, 11]);
    }

    #[test]
    fn relative_path_strips_base_and_separator() {
        let base = format!("{SEP}tmp{SEP}");
        assert_eq!(relative_path(&base, &format!("{SEP}tmp{SEP}src{SEP}a.cs")), format!("src{SEP}a.cs"));
        assert_eq!(
            relative_path(&format!("{SEP}tmp"), &format!("{SEP}tmp{SEP}a.cs")),
            "a.cs"
        );
        // Prefix-sharing sibling is not under the base
        let sibling = format!("{SEP}tmpx{SEP}a.cs");
        assert_eq!(relative_path(&format!("{SEP}tmp"), &sibling), sibling);
    }
}
