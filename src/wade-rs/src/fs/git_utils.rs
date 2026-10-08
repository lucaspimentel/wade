//! Port of `src/Wade/FileSystem/GitUtils.cs` (git status subset; the action
//! commands and GetDiff land in Phase 4b / Phase 7).

use std::collections::HashMap;
use std::process::{Command, Stdio};
use std::time::{Duration, Instant};

use super::directory_contents::GitFileStatus;
use crate::input::CancelToken;

const LOCAL_TIMEOUT_MS: u128 = 10_000;
const NETWORK_TIMEOUT_MS: u128 = 30_000;

/// Case-insensitive status lookup mirroring the C# dictionary's
/// `StringComparer.OrdinalIgnoreCase`: exact match first, then a
/// case-insensitive scan on Windows only.
#[must_use]
pub fn statuses_get(map: &HashMap<String, GitFileStatus>, path: &str) -> Option<GitFileStatus> {
    if let Some(status) = map.get(path) {
        return Some(*status);
    }

    #[cfg(windows)]
    {
        map.iter().find(|(key, _)| key.eq_ignore_ascii_case(path)).map(|(_, status)| *status)
    }

    #[cfg(not(windows))]
    None
}

/// Port of `GitUtils.FindRepoRoot`: walks up from `path` looking for a
/// directory containing a `.git` folder (or worktree `.git` file).
#[must_use]
pub fn find_repo_root(path: &str) -> Option<String> {
    if path.is_empty() {
        return None;
    }

    let dir_path = if std::path::Path::new(path).is_dir() {
        std::path::PathBuf::from(path)
    } else {
        std::path::Path::new(path).parent()?.to_path_buf()
    };

    let mut dir = dir_path;
    loop {
        let git_path = dir.join(".git");
        if git_path.is_dir() || git_path.is_file() {
            return Some(dir.to_string_lossy().to_string());
        }

        dir = dir.parent()?.to_path_buf();
    }
}

/// Port of `GitUtils.ReadBranchName`: reads the branch from `.git/HEAD`;
/// supports worktrees (`.git` as a file with `gitdir: <path>`). Returns None
/// for detached HEAD, missing files, or errors.
#[must_use]
pub fn read_branch_name(repo_root: &str) -> Option<String> {
    let git_path = std::path::Path::new(repo_root).join(".git");
    let head_path = if git_path.is_file() && !git_path.is_dir() {
        // Worktree: .git is a file containing "gitdir: <path>"
        let content = std::fs::read_to_string(&git_path).ok()?.trim().to_string();
        let git_dir = content.strip_prefix("gitdir: ")?.trim().to_string();
        let git_dir = if std::path::Path::new(&git_dir).is_absolute() {
            std::path::PathBuf::from(git_dir)
        } else {
            std::path::Path::new(repo_root).join(git_dir)
        };
        git_dir.join("HEAD")
    } else {
        git_path.join("HEAD")
    };

    let head_content = std::fs::read_to_string(head_path).ok()?.trim().to_string();
    head_content.strip_prefix("ref: refs/heads/").map(str::to_string)
}

/// Port of `GitUtils.RunGitCommand` (success/error shape); used by the
/// Phase 4b git actions. Reads stderr before stdout (C# behavior), then
/// enforces the timeout via a try_wait poll loop (kill on expiry).
/// Port of `GitUtils.RunGitCommand` shape: success plus an optional error
/// message. `args` are the argv words after "git" (the C# version joins them
/// into a command line and lets the process launcher re-split it, which
/// unquotes; passing argv directly is equivalent).
pub fn run_git_result(
    repo_root: &str,
    args: &[&str],
    cancel: &CancelToken,
    timeout_ms: u128,
) -> (bool, Option<String>) {
    match run_git_capturing(repo_root, args, cancel, timeout_ms) {
        Ok(_) => (true, None),
        Err(message) => (false, Some(message)),
    }
}

/// Runs git and returns stdout on success.
fn run_git_capturing(repo_root: &str, args: &[&str], cancel: &CancelToken, timeout_ms: u128) -> Result<String, String> {
    if cancel.is_cancelled() {
        return Err("Cancelled".to_string());
    }

    let mut child = spawn_git(repo_root, args).map_err(|err| err.to_string())?;

    // C# reads stderr first, then drains stdout; blocking reads clone the
    // C# semantics (git commands here emit small output)
    let stderr = read_stream(child.stderr.take());
    let stdout = read_stream(child.stdout.take());

    let Some(status) = wait_with_timeout(&mut child, timeout_ms) else {
        return Err("Git command timed out".to_string());
    };

    if cancel.is_cancelled() {
        return Err("Cancelled".to_string());
    }

    if !status.success() {
        let error = stderr.trim();
        if error.is_empty() {
            return Err(format!("git exited with code {}", status.code().unwrap_or(-1)));
        }

        return Err(error.to_string());
    }

    Ok(stdout)
}

fn spawn_git(repo_root: &str, args: &[&str]) -> std::io::Result<std::process::Child> {
    Command::new("git")
        .args(args)
        .current_dir(repo_root)
        .stdout(Stdio::piped())
        .stderr(Stdio::piped())
        .spawn()
}

/// Read a ChildStdout/ChildStderr to end.
fn read_stream<S: std::io::Read>(stream: Option<S>) -> String {
    match stream {
        Some(mut stream) => {
            // Lossy: a file name that is not valid UTF-8 must not blank the
            // whole output
            let mut bytes = Vec::new();
            let _ = std::io::Read::read_to_end(&mut stream, &mut bytes);
            String::from_utf8_lossy(&bytes).into_owned()
        }
        None => String::new(),
    }
}

/// Port of `WaitForExit(timeoutMs)`: poll `try_wait` and kill on expiry.
/// Returns the exit status on success, None on timeout/error.
fn wait_with_timeout(child: &mut std::process::Child, timeout_ms: u128) -> Option<std::process::ExitStatus> {
    let deadline = Instant::now() + Duration::from_millis(timeout_ms as u64);
    loop {
        match child.try_wait() {
            Ok(Some(status)) => return Some(status),
            Ok(None) => {
                if Instant::now() >= deadline {
                    let _ = child.kill();
                    let _ = child.wait();
                    return None;
                }

                std::thread::sleep(Duration::from_millis(20));
            }
            Err(_) => return None,
        }
    }
}

/// Port of `GitUtils.GetDiff`: `git diff [--staged] -- <path>` in the repo
/// root, split on '\n' (a trailing empty element and '\r' kept, as in C#).
/// None on failure, empty output, a non-zero exit, timeout (10 s) or
/// cancellation.
#[must_use]
pub fn get_diff(repo_root: &str, file_path: &str, staged: bool, cancel: &CancelToken) -> Option<Vec<String>> {
    let relative = relative_path(repo_root, file_path).replace('\\', "/");
    let mut args = vec!["diff"];

    if staged {
        args.push("--staged");
    }

    args.extend(["--", relative.as_str()]);

    // Only stdout is redirected (C# leaves stderr alone), so a large diff
    // cannot block on a full stderr pipe
    let mut child = Command::new("git")
        .args(&args)
        .current_dir(repo_root)
        .stdout(Stdio::piped())
        .stderr(Stdio::null())
        .spawn()
        .ok()?;

    let output = read_stream(child.stdout.take());
    let status = wait_with_timeout(&mut child, 10_000)?;

    if cancel.is_cancelled() || !status.success() || output.is_empty() {
        return None;
    }

    Some(output.split('\n').map(str::to_string).collect())
}

/// Port of `GitUtils.QueryStatus`: runs `git status --porcelain=v1` in the
/// repo root; returns the parsed status map or None on error/cancellation.
#[must_use]
pub fn query_status(repo_root: &str, cancel: &CancelToken) -> Option<HashMap<String, GitFileStatus>> {
    if cancel.is_cancelled() {
        return None;
    }

    match run_git_capturing(
        repo_root,
        // --no-optional-locks: a background status must not take
        // .git/index.lock, or a stage/commit started meanwhile fails
        &["--no-optional-locks", "-c", "core.quotepath=false", "status", "--porcelain=v1"],
        cancel,
        LOCAL_TIMEOUT_MS,
    ) {
        // macOS: git reports the index's spelling, which can differ from the
        // disk's (a case-only rename without `git mv`); key by the disk's
        Ok(stdout) => Some(parse_porcelain_output_with(&stdout, repo_root, crate::fs::file_operations::on_disk_case)),
        Err(_) => None,
    }
}

/// Port of `GitUtils.GetAheadBehind`: commit counts ahead/behind the
/// upstream; None when there is no upstream or on error.
#[must_use]
pub fn get_ahead_behind(repo_root: &str, cancel: &CancelToken) -> Option<(u32, u32)> {
    if cancel.is_cancelled() {
        return None;
    }

    match run_git_capturing(
        repo_root,
        &["rev-list", "--count", "--left-right", "@{upstream}...HEAD"],
        cancel,
        LOCAL_TIMEOUT_MS,
    ) {
        Ok(stdout) => parse_ahead_behind(&stdout),
        Err(_) => None,
    }
}

/// Port of `GitUtils.GetRelativePath` usage in `BuildPathArgs`: the path
/// relative to the repo root, with backslashes converted to forward slashes.
#[must_use]
pub fn relative_path(repo_root: &str, path: &str) -> String {
    let root_norm = repo_root.trim_end_matches(['/', '\\']);
    let path_norm = path.trim_end_matches(['/', '\\']);

    let under_root = if cfg!(windows) {
        path_norm.len() >= root_norm.len() && path_norm[..root_norm.len()].eq_ignore_ascii_case(root_norm)
    } else {
        path_norm.len() >= root_norm.len() && path_norm.starts_with(root_norm)
    };

    let relative = if under_root {
        &path_norm[root_norm.len()..]
    } else {
        // Not under the root: return the path unchanged (stage paths always
        // live under the root; C# GetRelativePath would produce ../ segments)
        path_norm
    };

    // Strip only the separator that separated root from the relative part;
    // a not-under-root path keeps its leading separator (e.g. "/other")
    let relative = if under_root && relative.starts_with(['/', '\\']) {
        &relative[1..]
    } else {
        relative
    };

    let relative = if relative.is_empty() { "." } else { relative };
    relative.replace('\\', "/")
}

/// Port of `GitUtils.BuildPathArgs`: the command followed by `--` and the
/// relative, forward-slashed paths as separate argv words (the C# version
/// builds a quoted command line that the launcher re-splits, which unquotes;
/// passing argv words directly is equivalent).
#[must_use]
pub fn build_path_args(command: &str, repo_root: &str, paths: &[String]) -> Vec<String> {
    // The C# command may be multi-word ("reset -q"); the argv model
    // splits it
    let mut args: Vec<String> = command.split(' ').map(str::to_string).collect();
    args.push("--".to_string());
    for path in paths {
        args.push(relative_path(repo_root, path));
    }

    args
}

/// Port of `GitUtils.Stage`: `git add -- <paths>`.
pub fn stage(repo_root: &str, paths: &[String], cancel: &CancelToken) -> (bool, Option<String>) {
    run_git_args_owned(repo_root, &build_path_args("add", repo_root, paths), cancel, LOCAL_TIMEOUT_MS)
}

/// Port of `GitUtils.Unstage`: `git reset -q -- <paths>` (unlike
/// `git restore --staged`, it also works before the first commit).
pub fn unstage(repo_root: &str, paths: &[String], cancel: &CancelToken) -> (bool, Option<String>) {
    run_git_args_owned(repo_root, &build_path_args("reset -q", repo_root, paths), cancel, LOCAL_TIMEOUT_MS)
}

/// Port of `GitUtils.StageAll`: `git add -A`.
pub fn stage_all(repo_root: &str, cancel: &CancelToken) -> (bool, Option<String>) {
    run_git_args_owned(repo_root, &["add".to_string(), "-A".to_string()], cancel, LOCAL_TIMEOUT_MS)
}

/// Port of `GitUtils.UnstageAll`: `git reset -q` (no `HEAD` argument, so it
/// also works before the first commit).
pub fn unstage_all(repo_root: &str, cancel: &CancelToken) -> (bool, Option<String>) {
    run_git_args_owned(repo_root, &["reset".to_string(), "-q".to_string()], cancel, LOCAL_TIMEOUT_MS)
}

/// Port of `GitUtils.Commit`: `git commit -m <message>`. The C# version
/// escapes the message for its command-line string, which the process
/// launcher then unescapes; passing the raw message as one argv word is
/// equivalent.
pub fn commit(repo_root: &str, message: &str, cancel: &CancelToken) -> (bool, Option<String>) {
    run_git_args_owned(
        repo_root,
        &["commit".to_string(), "-m".to_string(), message.to_string()],
        cancel,
        LOCAL_TIMEOUT_MS,
    )
}

/// Port of `GitUtils.Push`: `git push` (30s timeout).
pub fn push(repo_root: &str, cancel: &CancelToken) -> (bool, Option<String>) {
    run_git_args_owned(repo_root, &["push".to_string()], cancel, NETWORK_TIMEOUT_MS)
}

/// Port of `GitUtils.PushForceWithLease`: `git push --force-with-lease` (30s).
pub fn push_force_with_lease(repo_root: &str, cancel: &CancelToken) -> (bool, Option<String>) {
    run_git_args_owned(repo_root, &["push".to_string(), "--force-with-lease".to_string()], cancel, NETWORK_TIMEOUT_MS)
}

/// Port of `GitUtils.Pull`: `git pull` (30s).
pub fn pull(repo_root: &str, cancel: &CancelToken) -> (bool, Option<String>) {
    run_git_args_owned(repo_root, &["pull".to_string()], cancel, NETWORK_TIMEOUT_MS)
}

/// Port of `GitUtils.PullRebase`: `git pull --rebase` (30s).
pub fn pull_rebase(repo_root: &str, cancel: &CancelToken) -> (bool, Option<String>) {
    run_git_args_owned(repo_root, &["pull".to_string(), "--rebase".to_string()], cancel, NETWORK_TIMEOUT_MS)
}

/// Port of `GitUtils.Fetch`: `git fetch` (30s).
pub fn fetch(repo_root: &str, cancel: &CancelToken) -> (bool, Option<String>) {
    run_git_args_owned(repo_root, &["fetch".to_string()], cancel, NETWORK_TIMEOUT_MS)
}

fn run_git_args_owned(
    repo_root: &str,
    args: &[String],
    cancel: &CancelToken,
    timeout_ms: u128,
) -> (bool, Option<String>) {
    let refs: Vec<&str> = args.iter().map(String::as_str).collect();
    run_git_result(repo_root, &refs, cancel, timeout_ms)
}

/// Port of `GitUtils.ParsePorcelainOutput` (internal static, testable):
/// parses `git status --porcelain=v1` into per-path statuses, then
/// aggregates into parent directories.
#[must_use]
pub fn parse_porcelain_output(output: &str, repo_root: &str) -> HashMap<String, GitFileStatus> {
    parse_porcelain_output_with(output, repo_root, str::to_string)
}

/// `parse_porcelain_output` with each file's full path passed through
/// `respell` before the directories are aggregated (macOS: on-disk case).
#[must_use]
pub fn parse_porcelain_output_with(
    output: &str,
    repo_root: &str,
    respell: impl Fn(&str) -> String,
) -> HashMap<String, GitFileStatus> {
    let mut statuses: HashMap<String, GitFileStatus> = HashMap::new();

    for line in output.lines() {
        if line.chars().count() < 4 {
            continue;
        }

        let chars: Vec<char> = line.chars().collect();
        let index_status = chars[0];
        let work_tree_status = chars[1];
        // Path starts at index 3 (after "XY ")
        let mut relative_path: String = chars[3..].iter().collect();

        // Handle renames: "R  old -> new" - use the new path
        if let Some(arrow_idx) = relative_path.find(" -> ") {
            relative_path = relative_path[arrow_idx + 4..].to_string();
        }

        // Strip surrounding quotes if present (git quotes paths with spaces
        // and special chars) and decode the C-style escapes inside
        if relative_path.chars().count() >= 2 && relative_path.starts_with('"') && relative_path.ends_with('"') {
            relative_path = unquote_git_path(&relative_path[1..relative_path.len() - 1]);
        }

        let mut status = GitFileStatus::NONE;

        // Conflict markers
        if index_status == 'U'
            || work_tree_status == 'U'
            || (index_status == 'A' && work_tree_status == 'A')
            || (index_status == 'D' && work_tree_status == 'D')
        {
            status |= GitFileStatus::CONFLICT;
        } else {
            // Untracked
            if index_status == '?' && work_tree_status == '?' {
                status |= GitFileStatus::UNTRACKED;
            }
            // Ignored
            else if index_status == '!' && work_tree_status == '!' {
                status |= GitFileStatus::IGNORED;
            } else {
                // Index (staged) status
                if matches!(index_status, 'A' | 'M' | 'D' | 'R' | 'C') {
                    status |= GitFileStatus::STAGED;
                }

                // Working tree (modified) status
                if matches!(work_tree_status, 'M' | 'D') {
                    status |= GitFileStatus::MODIFIED;
                }
            }
        }

        if status == GitFileStatus::NONE {
            continue;
        }

        // Convert relative path (using /) to full path with platform
        // separators
        let full_path = std::path::Path::new(repo_root).join(relative_path.replace('/', std::path::MAIN_SEPARATOR_STR));
        let full_path = respell(&normalize_full_path(&full_path.to_string_lossy()));

        match statuses.get(&full_path) {
            Some(existing) => {
                statuses.insert(full_path, *existing | status);
            }
            None => {
                statuses.insert(full_path, status);
            }
        }
    }

    // Aggregate into parent directories (exclude Ignored from propagation)
    aggregate_directory_statuses(&mut statuses, repo_root);

    statuses
}

/// Decodes the C-style escapes git puts inside a quoted path: backslash,
/// double quote, `\n`, `\t` and the like, and octal `\NNN` bytes (UTF-8
/// sequences when `core.quotepath` is on).
fn unquote_git_path(inner: &str) -> String {
    let bytes = inner.as_bytes();
    let mut out = Vec::with_capacity(bytes.len());
    let mut i = 0;

    while i < bytes.len() {
        let b = bytes[i];
        i += 1;

        if b != b'\\' || i >= bytes.len() {
            out.push(b);
            continue;
        }

        let escaped = bytes[i];
        i += 1;

        match escaped {
            b'0'..=b'7' => {
                let mut value = u32::from(escaped - b'0');
                let mut digits = 1;

                while digits < 3 && i < bytes.len() && matches!(bytes[i], b'0'..=b'7') {
                    value = value * 8 + u32::from(bytes[i] - b'0');
                    i += 1;
                    digits += 1;
                }

                out.push(value as u8);
            }
            b'a' => out.push(0x07),
            b'b' => out.push(0x08),
            b'f' => out.push(0x0c),
            b'n' => out.push(b'\n'),
            b'r' => out.push(b'\r'),
            b't' => out.push(b'\t'),
            b'v' => out.push(0x0b),
            other => out.push(other),
        }
    }

    String::from_utf8_lossy(&out).into_owned()
}

/// Path.GetFullPath normalizes separators and collapses `.`/`..` segments;
/// reuse the dialogs path helpers for the same effect.
fn normalize_full_path(path: &str) -> String {
    crate::app::dialogs::get_full_path(path)
}

/// Port of `AggregateDirectoryStatuses` (internal static, testable): ORs
/// each entry's status (minus Ignored) into every parent directory up to the
/// repo root.
pub fn aggregate_directory_statuses(statuses: &mut HashMap<String, GitFileStatus>, repo_root: &str) {
    // Snapshot keys to avoid modifying during enumeration
    let file_paths: Vec<String> = statuses.keys().cloned().collect();

    for file_path in file_paths {
        let status = statuses.get(&file_path).copied().unwrap_or(GitFileStatus::NONE) & !GitFileStatus::IGNORED;
        if status == GitFileStatus::NONE {
            continue;
        }

        let mut parent_dir = parent_of(&file_path);
        while let Some(dir) = parent_dir {
            if dir.chars().count() < repo_root.chars().count() {
                break;
            }

            if dir.eq_ignore_ascii_case(&file_path) {
                break;
            }

            match statuses.get(&dir) {
                Some(existing) => {
                    statuses.insert(dir.clone(), *existing | status);
                }
                None => {
                    statuses.insert(dir.clone(), status);
                }
            }

            if dir.eq_ignore_ascii_case(repo_root) {
                break;
            }

            parent_dir = parent_of(&dir);
        }
    }
}

fn parent_of(path: &str) -> Option<String> {
    std::path::Path::new(path).parent().map(|p| p.to_string_lossy().to_string())
}

/// Pure parse of `GetAheadBehind` stdout: format is
/// `"<behind>\t<ahead>\n"` (behind first). Returns (ahead, behind) or None
/// on malformed output.
#[must_use]
pub fn parse_ahead_behind(stdout: &str) -> Option<(u32, u32)> {
    let trimmed = stdout.trim();
    let mut parts = trimmed.split('\t');
    let behind = parts.next()?;
    let ahead = parts.next()?;
    if parts.next().is_some() {
        return None;
    }

    Some((ahead.parse().ok()?, behind.parse().ok()?))
}

#[cfg(test)]
mod tests {
    use super::*;
    use std::path::PathBuf;

    pub(super) fn temp_dir(name: &str) -> PathBuf {
        let dir = std::env::temp_dir().join(format!("wade-git-test-{}-{name}", std::process::id()));
        let _ = std::fs::remove_dir_all(&dir);
        std::fs::create_dir_all(&dir).expect("mkdir");
        dir
    }

    #[test]
    fn repo_root_walk() {
        let root = temp_dir("root");
        std::fs::create_dir_all(root.join(".git")).expect("git dir");
        let nested = root.join("a").join("b");
        std::fs::create_dir_all(&nested).expect("nested");

        let found = find_repo_root(&nested.to_string_lossy());
        assert_eq!(
            found.map(|p| std::path::PathBuf::from(p).canonicalize().unwrap()),
            std::path::PathBuf::from(&root.to_string_lossy().to_string()).canonicalize().ok()
        );

        // Outside any repo: walk up to the temp root and fail
        let orphan = temp_dir("orphan");
        assert!(find_repo_root(&orphan.to_string_lossy()).is_none());
    }

    #[test]
    fn repo_root_worktree_git_file() {
        let main_root = temp_dir("main");
        std::fs::create_dir_all(main_root.join(".git")).expect("git dir");
        let wt_dir = temp_dir("wt");
        let gitdir = wt_dir.join(".git").join("worktrees").join("wt1");
        std::fs::create_dir_all(&gitdir).expect("gitdir");
        std::fs::write(gitdir.join("HEAD"), "ref: refs/heads/feature").expect("head");

        let wt_root = wt_dir.join("checkout");
        std::fs::create_dir_all(&wt_root).expect("checkout");
        std::fs::write(wt_root.join(".git"), format!("gitdir: {}", gitdir.to_string_lossy())).expect("git file");

        assert_eq!(read_branch_name(&wt_root.to_string_lossy()).as_deref(), Some("feature"));
    }

    #[test]
    fn branch_name_cases() {
        let root = temp_dir("branch");
        std::fs::create_dir_all(root.join(".git")).expect("git dir");

        std::fs::write(root.join(".git").join("HEAD"), "ref: refs/heads/main\n").expect("head");
        assert_eq!(read_branch_name(&root.to_string_lossy()).as_deref(), Some("main"));

        // Detached HEAD (raw SHA)
        std::fs::write(root.join(".git").join("HEAD"), "0123456789abcdef\n").expect("head");
        assert!(read_branch_name(&root.to_string_lossy()).is_none());

        // Missing HEAD
        std::fs::remove_file(root.join(".git").join("HEAD")).expect("rm");
        assert!(read_branch_name(&root.to_string_lossy()).is_none());
    }

    /// Platform-native root for parser tests: a windows-style root produces
    /// CWD-resolved keys on unix (faithful C# `Path.GetFullPath` behavior),
    /// so tests use the platform's native form and build expected keys with
    /// the same join the parser uses.
    fn test_root() -> String {
        if cfg!(windows) { String::from("C:\\repo") } else { String::from("/repo") }
    }

    fn test_key(root: &str, name: &str) -> String {
        std::path::Path::new(root)
            .join(name.replace('/', std::path::MAIN_SEPARATOR_STR))
            .to_string_lossy()
            .to_string()
    }

    #[test]
    fn porcelain_parse_basic_statuses() {
        let root = test_root();
        let output = "?? new.txt\n M mod.txt\nM  staged.txt\nAM both.txt\n!! ignored.txt\nUU conflict.txt\nAA both-added.txt\nDD both-deleted.txt\nR  old.txt -> renamed.txt\n?? \"quoted path.txt\"\nx\n";

        let statuses = parse_porcelain_output(output, &root);

        let get = |name: &str| statuses.get(&test_key(&root, name)).copied();

        assert_eq!(get("new.txt"), Some(GitFileStatus::UNTRACKED));
        assert_eq!(get("mod.txt"), Some(GitFileStatus::MODIFIED));
        assert_eq!(get("staged.txt"), Some(GitFileStatus::STAGED));
        assert_eq!(get("both.txt"), Some(GitFileStatus::STAGED | GitFileStatus::MODIFIED));
        assert_eq!(get("ignored.txt"), Some(GitFileStatus::IGNORED));
        assert_eq!(get("conflict.txt"), Some(GitFileStatus::CONFLICT));
        assert_eq!(get("both-added.txt"), Some(GitFileStatus::CONFLICT));
        assert_eq!(get("both-deleted.txt"), Some(GitFileStatus::CONFLICT));
        // Rename: new path gets Staged
        assert_eq!(get("renamed.txt"), Some(GitFileStatus::STAGED));
        assert!(get("old.txt").is_none());
        // Quoted path is stripped of quotes
        assert_eq!(get("quoted path.txt"), Some(GitFileStatus::UNTRACKED));
        // Short line skipped; 10 files + 1 repo-root aggregate
        assert_eq!(statuses.len(), 11);
    }

    #[test]
    fn porcelain_parse_decodes_quoted_path_escapes() {
        let root = test_root();
        let output = concat!(
            "?? \"\\303\\274n\\303\\257code.txt\"\n",
            "?? \"with \\\"quote\\\" and \\\\ and \\ttab.txt\"\n",
            "?? \"caf\u{e9} dir/x.txt\"\n",
        );
        let statuses = parse_porcelain_output(output, &root);

        assert!(statuses.contains_key(&test_key(&root, "\u{fc}n\u{ef}code.txt")), "{statuses:?}");
        assert!(statuses.contains_key(&test_key(&root, "with \"quote\" and \\ and \ttab.txt")), "{statuses:?}");
        assert!(statuses.contains_key(&test_key(&root, "caf\u{e9} dir/x.txt")), "{statuses:?}");
    }

    #[test]
    fn porcelain_parse_slash_paths_normalized() {
        let root = test_root();
        let statuses = parse_porcelain_output("?? src/deep/new.txt\n", &root);

        let file_key = test_key(&root, "src/deep/new.txt");
        let deep_key = test_key(&root, "src/deep");
        let src_key = test_key(&root, "src");

        // The relative path separator is normalized to the platform
        // separator (backslash on windows, unchanged on unix)
        if cfg!(windows) {
            assert!(statuses.contains_key(r"C:\repo\src\deep\new.txt"));
        } else {
            assert!(statuses.contains_key(&file_key));
        }

        // Aggregation created directory entries
        assert!(statuses.contains_key(&deep_key));
        assert!(statuses.contains_key(&src_key));
        assert!(statuses.contains_key(&root));
    }

    #[test]
    fn aggregation_excludes_ignored() {
        let root = test_root();
        let statuses = parse_porcelain_output("!! ignored.txt\n M mod.txt\n", &root);

        let dir_status = statuses.get(&root).copied().expect("root aggregate");
        // Ignored does not propagate; Modified does
        assert!(dir_status.contains(GitFileStatus::MODIFIED));
        assert!(!dir_status.contains(GitFileStatus::IGNORED));
        // But the ignored file itself keeps its flag
        let ignored_key = test_key(&root, "ignored.txt");
        assert!(statuses.get(&ignored_key).copied().expect("file").contains(GitFileStatus::IGNORED));
    }

    #[test]
    fn aggregation_or_merges() {
        let root = test_root();
        let statuses = parse_porcelain_output("?? a/one.txt\n M a/two.txt\n", &root);
        let dir_status = statuses.get(&test_key(&root, "a")).copied().expect("dir");
        assert!(dir_status.contains(GitFileStatus::UNTRACKED));
        assert!(dir_status.contains(GitFileStatus::MODIFIED));
    }

    #[test]
    fn relative_path_strips_root() {
        #[cfg(windows)]
        {
            assert_eq!(relative_path(r"C:\repo", r"C:\repo\src\a.txt"), "src/a.txt");
            assert_eq!(relative_path(r"c:\REPO", r"C:\repo\b.txt"), "b.txt");
            // Outside the root: slashes normalized, unchanged otherwise. C#
            // GetRelativePath would produce ../ segments, but stage paths
            // always live under the repo root, so this branch never fires.
            assert_eq!(relative_path(r"C:\repo", r"C:\other\c.txt"), "C:/other/c.txt");
        }
        #[cfg(not(windows))]
        {
            assert_eq!(relative_path("/repo", "/repo/src/a.txt"), "src/a.txt");
            assert_eq!(relative_path("/repo", "/other/c.txt"), "/other/c.txt");
        }
    }

    #[test]
    fn build_path_args_quotes_via_argv() {
        let root = if cfg!(windows) { r"C:\repo" } else { "/repo" };
        let paths = vec![format!("{root}{}a.txt", std::path::MAIN_SEPARATOR)];
        let args = build_path_args("add", root, &paths);
        assert_eq!(args[0], "add");
        assert_eq!(args[1], "--");
        assert_eq!(args[2], "a.txt");
    }

    #[test]
    fn ahead_behind_parse() {
        assert_eq!(parse_ahead_behind("3\t1\n"), Some((1, 3)));
        assert_eq!(parse_ahead_behind("0\t0"), Some((0, 0)));
        assert_eq!(parse_ahead_behind("34\t12"), Some((12, 34)));
        assert_eq!(parse_ahead_behind(""), None);
        assert_eq!(parse_ahead_behind("x\ty"), None);
        assert_eq!(parse_ahead_behind("1\t2\t3"), None);
    }

    #[test]
    fn porcelain_single_file_cases() {
        let root = test_root();
        for (line, name, expected) in [
            ("MM file.txt", "file.txt", GitFileStatus::STAGED | GitFileStatus::MODIFIED),
            ("A  file.txt", "file.txt", GitFileStatus::STAGED),
            ("D  file.txt", "file.txt", GitFileStatus::STAGED),
            (" D file.txt", "file.txt", GitFileStatus::MODIFIED),
            (" M file.txt", "file.txt", GitFileStatus::MODIFIED),
            ("R  old.txt -> new.txt", "new.txt", GitFileStatus::STAGED),
        ] {
            let statuses = parse_porcelain_output(line, &root);
            let files: Vec<_> = statuses.keys().filter(|k| **k != root).collect();
            assert_eq!(files, [&test_key(&root, name)], "{line:?}");
            assert_eq!(statuses.get(&test_key(&root, name)).copied(), Some(expected), "{line:?}");
        }
    }

    #[test]
    fn porcelain_empty_output_is_empty() {
        assert!(parse_porcelain_output("", &test_root()).is_empty());
    }

    #[test]
    fn porcelain_quoted_path_in_a_subdirectory() {
        let root = test_root();
        let statuses = parse_porcelain_output(" M \"src/special file.txt\"\n", &root);
        assert_eq!(statuses.get(&test_key(&root, "src/special file.txt")).copied(), Some(GitFileStatus::MODIFIED));
        assert_eq!(statuses.get(&test_key(&root, "src")).copied(), Some(GitFileStatus::MODIFIED));
    }

    #[test]
    fn aggregation_merges_staged_and_modified() {
        let root = test_root();
        let statuses = parse_porcelain_output(" M src/foo/bar.cs\nA  src/baz.cs\n", &root);
        let src = statuses.get(&test_key(&root, "src")).copied().expect("src aggregate");
        assert!(src.contains(GitFileStatus::MODIFIED) && src.contains(GitFileStatus::STAGED), "{src:?}");
    }

    #[test]
    fn ignored_files_create_no_directory_entries() {
        let root = test_root();
        let statuses = parse_porcelain_output("!! build/output.dll\n", &root);
        assert_eq!(statuses.get(&test_key(&root, "build/output.dll")).copied(), Some(GitFileStatus::IGNORED));
        assert!(!statuses.contains_key(&test_key(&root, "build")), "{statuses:?}");
    }

    #[test]
    fn find_repo_root_of_an_empty_path_is_none() {
        assert!(find_repo_root("").is_none());
    }

    #[test]
    fn branch_name_with_a_slash() {
        let root = temp_dir("feature");
        std::fs::create_dir_all(root.join(".git")).expect("git dir");
        std::fs::write(root.join(".git").join("HEAD"), "ref: refs/heads/feature/my-branch\n").expect("head");
        assert_eq!(read_branch_name(&root.to_string_lossy()).as_deref(), Some("feature/my-branch"));
    }

    #[test]
    fn cancelled_token_stops_before_running_git() {
        // A directory that is not a repo: only the up-front cancel check can
        // produce these results without spawning git
        let dir = temp_dir("cancelled");
        let root = dir.to_string_lossy().to_string();
        let cancel = CancelToken::new();
        cancel.cancel();
        assert!(query_status(&root, &cancel).is_none());
        assert_eq!(
            stage(&root, &[format!("{root}{}a.txt", std::path::MAIN_SEPARATOR)], &cancel),
            (false, Some("Cancelled".to_string()))
        );
        assert_eq!(
            run_git_result(&root, &["status"], &cancel, LOCAL_TIMEOUT_MS),
            (false, Some("Cancelled".to_string()))
        );
    }

    #[test]
    fn respelled_paths_key_files_and_their_directories() {
        let root = test_root();
        let respell = |path: &str| path.replace("lower", "Lower");
        let statuses = parse_porcelain_output_with(" M lower/file.txt\n", &root, respell);
        let full = |tail: &str| normalize_full_path(&std::path::Path::new(&root).join(tail).to_string_lossy());
        assert_eq!(statuses.get(&full("Lower/file.txt")), Some(&GitFileStatus::MODIFIED));
        assert_eq!(statuses.get(&full("Lower")), Some(&GitFileStatus::MODIFIED));
        assert!(!statuses.contains_key(&full("lower/file.txt")));
    }

    #[test]
    fn statuses_get_case_insensitive_on_windows() {
        let mut map = HashMap::new();
        map.insert(r"C:\Repo\File.txt".to_string(), GitFileStatus::MODIFIED);
        assert_eq!(statuses_get(&map, r"C:\Repo\File.txt"), Some(GitFileStatus::MODIFIED));
        #[cfg(windows)]
        assert_eq!(statuses_get(&map, r"c:\repo\file.txt"), Some(GitFileStatus::MODIFIED));
        #[cfg(not(windows))]
        assert_eq!(statuses_get(&map, r"c:\repo\file.txt"), None);
    }
}

#[cfg(test)]
mod integration {
    use super::tests::temp_dir;
    use super::*;
    use std::path::Path;

    fn run_git(dir: &std::path::Path, args: &[&str]) {
        let output = Command::new("git")
            // Hermetic: ignore the user global/system config (commit
            // signing, hooks) and set the identity explicitly
            .env("GIT_CONFIG_GLOBAL", "/dev/null")
            .env("GIT_CONFIG_SYSTEM", "/dev/null")
            .env("GIT_AUTHOR_NAME", "wade-test")
            .env("GIT_COMMITTER_NAME", "wade-test")
            .env("GIT_AUTHOR_EMAIL", "[EMAIL]")
            .env("GIT_COMMITTER_EMAIL", "[EMAIL]")
            .args(args)
            .current_dir(dir)
            .output()
            .expect("git available");
        assert!(output.status.success(), "git {args:?} failed: {}", String::from_utf8_lossy(&output.stderr));
    }

    fn git_installed() -> bool {
        Command::new("git").arg("--version").output().is_ok()
    }

    /// A fresh repo on branch "main", or None when git is not installed.
    /// Production code (spawn_git) runs git with this process's environment,
    /// so the identity and signing settings go in the repo's local config:
    /// the user's global commit signing would break non-interactive commits.
    fn init_repo(name: &str) -> Option<std::path::PathBuf> {
        if !git_installed() {
            return None;
        }
        let repo = temp_dir(name);
        run_git(&repo, &["init", "-q", "-b", "main"]);
        run_git(&repo, &["config", "user.name", "wade-test"]);
        run_git(&repo, &["config", "user.email", "wade@example.invalid"]);
        run_git(&repo, &["config", "commit.gpgsign", "false"]);
        Some(repo)
    }

    fn git_stdout(repo: &Path, args: &[&str]) -> String {
        let output = Command::new("git").args(args).current_dir(repo).output().expect("git");
        String::from_utf8_lossy(&output.stdout).into_owned()
    }

    fn p(path: &Path) -> String {
        path.to_string_lossy().to_string()
    }

    /// A repo with `test.txt` ("line1\nline2\n") committed.
    fn committed_repo(name: &str) -> Option<std::path::PathBuf> {
        let repo = init_repo(name)?;
        std::fs::write(repo.join("test.txt"), "line1\nline2\n").expect("write");
        run_git(&repo, &["add", "test.txt"]);
        run_git(&repo, &["commit", "-q", "-m", "initial"]);
        Some(repo)
    }

    /// macOS: after a case-only rename without `git mv`, git still reports
    /// the index's spelling; the status is keyed by the disk's.
    #[cfg(target_os = "macos")]
    #[test]
    fn status_follows_a_case_only_rename_on_disk() {
        let Some(repo) = committed_repo("case-rename") else { return };
        std::fs::rename(repo.join("test.txt"), repo.join("Test.txt")).expect("rename");
        std::fs::write(repo.join("Test.txt"), "changed\n").expect("write");

        let statuses = query_status(&p(&repo), &CancelToken::new()).expect("status");
        assert_eq!(statuses_get(&statuses, &p(&repo.join("Test.txt"))), Some(GitFileStatus::MODIFIED), "{statuses:?}");
        let _ = std::fs::remove_dir_all(&repo);
    }

    #[test]
    fn get_diff_for_modified_staged_and_clean_files() {
        let Some(repo) = committed_repo("diff") else { return };
        let root = p(&repo);
        let file = p(&repo.join("test.txt"));
        let cancel = CancelToken::new();
        assert!(get_diff(&root, &file, false, &cancel).is_none(), "clean file has no diff");

        std::fs::write(repo.join("test.txt"), "line1\nchanged\n").expect("write");
        let diff = get_diff(&root, &file, false, &cancel).expect("worktree diff");
        assert!(diff.iter().any(|l| l == "-line2") && diff.iter().any(|l| l == "+changed"), "{diff:?}");
        assert!(get_diff(&root, &file, true, &cancel).is_none(), "nothing staged yet");

        run_git(&repo, &["add", "test.txt"]);
        let staged = get_diff(&root, &file, true, &cancel).expect("staged diff");
        assert!(staged.iter().any(|l| l == "+changed"), "{staged:?}");

        let cancelled = CancelToken::new();
        cancelled.cancel();
        assert!(get_diff(&root, &file, true, &cancelled).is_none());
        let _ = std::fs::remove_dir_all(&repo);
    }

    #[test]
    fn stage_several_paths_and_unstage_all_after_a_commit() {
        let Some(repo) = committed_repo("multi") else { return };
        let root = p(&repo);
        let cancel = CancelToken::new();
        std::fs::write(repo.join("test.txt"), "changed\n").expect("write");
        std::fs::write(repo.join("new.txt"), "new\n").expect("write");
        std::fs::write(repo.join("other.txt"), "other\n").expect("write");

        let (ok, err) = stage(&root, &[p(&repo.join("test.txt")), p(&repo.join("new.txt"))], &cancel);
        assert!(ok, "stage failed: {err:?}");
        assert_eq!(git_stdout(&repo, &["diff", "--cached", "--name-only"]), "new.txt\ntest.txt\n");

        let (ok, err) = unstage_all(&root, &cancel);
        assert!(ok, "unstage_all failed: {err:?}");
        let statuses = query_status(&root, &cancel).expect("statuses");
        let status = |name: &str| statuses_get(&statuses, &p(&repo.join(name))).unwrap_or_default();
        assert_eq!(status("test.txt"), GitFileStatus::MODIFIED);
        assert_eq!(status("new.txt"), GitFileStatus::UNTRACKED);
        let _ = std::fs::remove_dir_all(&repo);
    }

    #[test]
    fn commit_keeps_quotes_and_fails_with_nothing_staged() {
        let Some(repo) = init_repo("commitmsg") else { return };
        let root = p(&repo);
        let cancel = CancelToken::new();
        std::fs::write(repo.join("test.txt"), "content\n").expect("write");
        run_git(&repo, &["add", "test.txt"]);

        let message = "Fix \"broken\" thing and it's 'quoted'";
        let (ok, err) = commit(&root, message, &cancel);
        assert!(ok, "commit failed: {err:?}");
        assert_eq!(git_stdout(&repo, &["log", "-1", "--format=%B"]).trim_end(), message);

        let (ok, err) = commit(&root, "nothing", &cancel);
        assert!(!ok);
        assert!(err.is_some());
        let _ = std::fs::remove_dir_all(&repo);
    }

    #[test]
    fn run_git_result_reports_success_and_failure() {
        let Some(repo) = init_repo("rungit") else { return };
        let root = p(&repo);
        let cancel = CancelToken::new();
        assert_eq!(run_git_result(&root, &["status"], &cancel, LOCAL_TIMEOUT_MS), (true, None));

        let (ok, err) = run_git_result(&root, &["not-a-command"], &cancel, LOCAL_TIMEOUT_MS);
        assert!(!ok);
        assert!(err.is_some_and(|e| e.contains("not-a-command")));
        let _ = std::fs::remove_dir_all(&repo);
    }

    /// A bare `remote.git` with one commit on main, and two clones of it
    /// (`local`, `other`) with the test identity; all local paths, so no
    /// network is involved.
    fn cloned_pair(name: &str) -> Option<(std::path::PathBuf, std::path::PathBuf, std::path::PathBuf)> {
        if !git_installed() {
            return None;
        }
        let dir = temp_dir(name);
        let remote = dir.join("remote.git");
        run_git(&dir, &["init", "-q", "--bare", "-b", "main", "remote.git"]);
        let clone = |name: &str| {
            run_git(&dir, &["clone", "-q", &p(&remote), name]);
            let repo = dir.join(name);
            run_git(&repo, &["config", "user.name", "wade-test"]);
            run_git(&repo, &["config", "user.email", "wade@example.invalid"]);
            run_git(&repo, &["config", "commit.gpgsign", "false"]);
            repo
        };
        let local = clone("local");
        std::fs::write(local.join("base.txt"), "base\n").expect("write");
        run_git(&local, &["add", "base.txt"]);
        run_git(&local, &["commit", "-q", "-m", "base"]);
        run_git(&local, &["push", "-q", "-u", "origin", "main"]);
        let other = clone("other");
        Some((remote, local, other))
    }

    fn commit_file(repo: &Path, name: &str, content: &str) {
        std::fs::write(repo.join(name), content).expect("write");
        run_git(repo, &["add", name]);
        run_git(repo, &["commit", "-q", "-m", name]);
    }

    fn head(repo: &Path, rev: &str) -> String {
        git_stdout(repo, &["rev-parse", rev]).trim().to_string()
    }

    #[test]
    fn network_actions_without_a_remote() {
        let Some(repo) = committed_repo("noremote") else { return };
        let root = p(&repo);
        let cancel = CancelToken::new();
        for (name, action) in [
            ("push", push as fn(&str, &CancelToken) -> (bool, Option<String>)),
            ("push --force-with-lease", push_force_with_lease),
            ("pull", pull),
            ("pull --rebase", pull_rebase),
        ] {
            let (ok, err) = action(&root, &cancel);
            assert!(!ok, "{name} should fail without a remote");
            assert!(err.is_some_and(|e| !e.is_empty()), "{name} error message");
        }
        assert_eq!(fetch(&root, &cancel), (true, None), "fetch with no remote does nothing");
        assert_eq!(get_ahead_behind(&root, &cancel), None, "no upstream");
        let _ = std::fs::remove_dir_all(&repo);
    }

    #[test]
    fn push_fetch_and_pull_against_a_bare_remote() {
        let Some((remote, local, other)) = cloned_pair("remote") else { return };
        let root = p(&local);
        let cancel = CancelToken::new();
        assert_eq!(get_ahead_behind(&root, &cancel), Some((0, 0)));

        commit_file(&local, "mine.txt", "mine\n");
        assert_eq!(get_ahead_behind(&root, &cancel), Some((1, 0)), "one commit ahead");

        let (ok, err) = push(&root, &cancel);
        assert!(ok, "push failed: {err:?}");
        assert_eq!(get_ahead_behind(&root, &cancel), Some((0, 0)));
        assert_eq!(head(&remote, "main"), head(&local, "HEAD"));

        run_git(&other, &["pull", "-q"]);
        commit_file(&other, "theirs.txt", "theirs\n");
        run_git(&other, &["push", "-q"]);
        assert_eq!(get_ahead_behind(&root, &cancel), Some((0, 0)), "not fetched yet");
        assert_eq!(fetch(&root, &cancel), (true, None));
        assert_eq!(get_ahead_behind(&root, &cancel), Some((0, 1)), "one commit behind");

        let (ok, err) = pull(&root, &cancel);
        assert!(ok, "pull failed: {err:?}");
        assert_eq!(get_ahead_behind(&root, &cancel), Some((0, 0)));
        assert!(local.join("theirs.txt").is_file());
        let _ = std::fs::remove_dir_all(remote.parent().unwrap());
    }

    #[test]
    fn pull_rebase_keeps_history_linear() {
        let Some((remote, local, other)) = cloned_pair("rebase") else { return };
        let root = p(&local);
        let cancel = CancelToken::new();
        commit_file(&other, "theirs.txt", "theirs\n");
        run_git(&other, &["push", "-q"]);
        commit_file(&local, "mine.txt", "mine\n");

        let (ok, err) = pull_rebase(&root, &cancel);
        assert!(ok, "pull --rebase failed: {err:?}");
        assert_eq!(git_stdout(&local, &["rev-list", "--merges", "HEAD"]), "", "no merge commit");
        assert_eq!(get_ahead_behind(&root, &cancel), Some((1, 0)), "the local commit replayed on top");
        assert!(local.join("theirs.txt").is_file() && local.join("mine.txt").is_file());
        let _ = std::fs::remove_dir_all(remote.parent().unwrap());
    }

    #[test]
    fn push_force_with_lease_replaces_a_rewritten_commit() {
        let Some((remote, local, _other)) = cloned_pair("lease") else { return };
        let root = p(&local);
        let cancel = CancelToken::new();
        commit_file(&local, "mine.txt", "mine\n");
        assert!(push(&root, &cancel).0);

        run_git(&local, &["commit", "-q", "--amend", "-m", "rewritten"]);
        let (ok, _) = push(&root, &cancel);
        assert!(!ok, "a plain push of rewritten history is rejected");

        let (ok, err) = push_force_with_lease(&root, &cancel);
        assert!(ok, "push --force-with-lease failed: {err:?}");
        assert_eq!(head(&remote, "main"), head(&local, "HEAD"));
        let _ = std::fs::remove_dir_all(remote.parent().unwrap());
    }

    #[test]
    fn real_git_status_round_trip() {
        let Some(repo) = init_repo("realgit") else { return };

        std::fs::write(repo.join("tracked.txt"), "hello").expect("write");
        run_git(&repo, &["add", "tracked.txt"]);
        run_git(&repo, &["commit", "-m", "init"]);

        // Untracked file + modified tracked file
        std::fs::write(repo.join("untracked.txt"), "new").expect("write");
        std::fs::write(repo.join("tracked.txt"), "changed").expect("write");

        let root = repo.to_string_lossy().to_string();
        let cancel = CancelToken::new();
        let statuses = query_status(&root, &cancel).expect("statuses");

        let join = |name: &str| {
            let mut path = repo.clone();
            path.push(name);
            path.to_string_lossy().to_string()
        };

        // Lookup failure dumps the map so CI failures are diagnosable: the
        // keys are platform-shaped by the parser (Path join + GetFullPath),
        // so a mismatch here means the parser's key shaping drifted.
        let lookup = |map: &HashMap<String, GitFileStatus>, name: &str, what: &str| {
            let key = join(name);
            match statuses_get(map, &key) {
                Some(status) => status,
                None => panic!("{what} not found for key {key:?}; map keys: {:?}", {
                    let mut keys: Vec<&String> = map.keys().collect();
                    keys.sort();
                    keys
                }),
            }
        };

        // Modified tracked file (worktree M)
        let tracked = lookup(&statuses, "tracked.txt", "tracked entry");
        assert!(tracked.contains(GitFileStatus::MODIFIED), "tracked: {tracked:?}");

        // Untracked file
        let untracked = lookup(&statuses, "untracked.txt", "untracked entry");
        assert!(untracked.contains(GitFileStatus::UNTRACKED), "untracked: {untracked:?}");

        // Branch name from the real repo
        assert_eq!(read_branch_name(&root).as_deref(), Some("main"));

        // Stage the modified file: it flips to Staged (no longer Modified in
        // the worktree part of the flag set)
        let paths = vec![join("tracked.txt")];
        let (ok, err) = stage(&root, &paths, &cancel);
        assert!(ok, "stage failed: {err:?}");
        let statuses = query_status(&root, &cancel).expect("statuses after stage");
        let staged = lookup(&statuses, "tracked.txt", "staged entry");
        assert!(staged.contains(GitFileStatus::STAGED), "staged: {staged:?}");
        assert!(!staged.contains(GitFileStatus::MODIFIED), "staged: {staged:?}");

        // Unstage: back to worktree-Modified
        let (ok, err) = unstage(&root, &paths, &cancel);
        assert!(ok, "unstage failed: {err:?}");
        let statuses = query_status(&root, &cancel).expect("statuses after unstage");
        let back = lookup(&statuses, "tracked.txt", "unstaged entry");
        assert!(back.contains(GitFileStatus::MODIFIED), "unstaged: {back:?}");
        assert!(!back.contains(GitFileStatus::STAGED), "unstaged: {back:?}");

        // Commit everything: the tree becomes clean
        let (ok, err) = stage_all(&root, &cancel);
        assert!(ok, "stage_all failed: {err:?}");
        let (ok, err) = commit(&root, "wade integration test", &cancel);
        assert!(ok, "commit failed: {err:?}");
        let statuses = query_status(&root, &cancel).expect("statuses after commit");
        assert!(statuses.len() <= 1, "tree not clean after commit: {statuses:?}");

        // Clean up temp pollution
        let _ = std::fs::remove_dir_all(Path::new(&root));
    }

    #[test]
    fn unstage_works_before_the_first_commit() {
        let Some(repo) = init_repo("unborn") else { return };
        std::fs::write(repo.join("one.txt"), "1").expect("write");
        std::fs::write(repo.join("two.txt"), "2").expect("write");
        run_git(&repo, &["add", "-A"]);

        let root = repo.to_string_lossy().to_string();
        let cancel = CancelToken::new();
        let staged = || {
            let output = Command::new("git")
                .args(["diff", "--cached", "--name-only"])
                .current_dir(&repo)
                .output()
                .expect("git");
            String::from_utf8_lossy(&output.stdout).lines().map(str::to_string).collect::<Vec<_>>()
        };
        assert_eq!(staged(), ["one.txt", "two.txt"]);

        let (ok, err) = unstage(&root, &[repo.join("one.txt").to_string_lossy().to_string()], &cancel);
        assert!(ok, "unstage failed: {err:?}");
        assert_eq!(staged(), ["two.txt"]);

        let (ok, err) = unstage_all(&root, &cancel);
        assert!(ok, "unstage_all failed: {err:?}");
        assert!(staged().is_empty());

        let _ = std::fs::remove_dir_all(&repo);
    }
}
