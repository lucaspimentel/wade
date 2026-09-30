//! Port of the git-entry gating shared by the action palette and the context
//! menu (App.cs:2958-3018 and App.cs:3916-3927). The C# port of this module
//! is src/Wade/UI/GitMenuItems.cs; both the app and the renderer fixture
//! runners call it so fixtures exercise the same code as the app.

use std::collections::{HashMap, HashSet};

use crate::app::input_reader::AppAction;
use crate::fs::directory_contents::GitFileStatus;
use crate::fs::git_utils::statuses_get;
use crate::ui::action_palette::ActionMenuItem;

/// Inputs to the git-menu builders. `selected_path` corresponds to C#'s
/// `_selectedIndex < entries.Count` check plus the entry's FullPath.
pub struct GitMenuContext<'a> {
    pub repo_root: Option<&'a str>,
    pub statuses: Option<&'a HashMap<String, GitFileStatus>>,
    pub selected_path: Option<&'a str>,
    pub marked_paths: &'a HashSet<String>,
}

/// Port of `HasStatusInSelection` (App.cs:1489): marked paths take
/// precedence over the selected entry.
#[must_use]
fn has_status_in_selection(ctx: &GitMenuContext, mask: GitFileStatus) -> bool {
    let Some(statuses) = ctx.statuses else {
        return false;
    };

    if !ctx.marked_paths.is_empty() {
        return ctx
            .marked_paths
            .iter()
            .any(|path| statuses_get(statuses, path).is_some_and(|status| status.intersects(mask)));
    }

    ctx.selected_path
        .is_some_and(|path| statuses_get(statuses, path).is_some_and(|status| status.intersects(mask)))
}

/// Port of the palette's git block (App.cs:2958-3018). "Git: Copy relative
/// path" is omitted: it needs the OS clipboard, deferred to Phase 9.
#[must_use]
pub fn build_git_menu_items(ctx: &GitMenuContext) -> Vec<ActionMenuItem> {
    let Some(_repo_root) = ctx.repo_root else {
        return Vec::new();
    };

    let mut items = Vec::new();

    if let Some(statuses) = ctx.statuses {
        if ctx.selected_path.is_some() {
            if has_status_in_selection(ctx, GitFileStatus::MODIFIED | GitFileStatus::UNTRACKED) {
                items.push(ActionMenuItem::new("Git: Stage", "", AppAction::StageFile));
            }

            if has_status_in_selection(ctx, GitFileStatus::STAGED) {
                items.push(ActionMenuItem::new("Git: Unstage", "", AppAction::UnstageFile));
            }
        }

        let has_any_changes = statuses.values().any(|status| {
            status.contains(GitFileStatus::MODIFIED) || status.contains(GitFileStatus::UNTRACKED)
        });

        if has_any_changes {
            items.push(ActionMenuItem::new("Git: Stage all changes", "", AppAction::StageAll));
        }

        let has_any_staged_changes = statuses.values().any(|status| status.contains(GitFileStatus::STAGED));

        if has_any_staged_changes {
            items.push(ActionMenuItem::new("Git: Unstage all", "", AppAction::UnstageAll));
            items.push(ActionMenuItem::new("Git: Commit", "", AppAction::GitCommit));
        }
    }

    items.push(ActionMenuItem::new("Git: Push", "", AppAction::GitPush));
    items.push(ActionMenuItem::new(
        "Git: Push (force with lease)",
        "",
        AppAction::GitPushForceWithLease,
    ));
    items.push(ActionMenuItem::new("Git: Pull", "", AppAction::GitPull));
    items.push(ActionMenuItem::new("Git: Pull (rebase)", "", AppAction::GitPullRebase));
    items.push(ActionMenuItem::new("Git: Fetch", "", AppAction::GitFetch));

    items
}

/// Port of the context menu's git additions (App.cs:3916-3927): Stage/Unstage
/// only, gated on repo + statuses + a selected entry.
#[must_use]
pub fn build_git_context_menu_items(ctx: &GitMenuContext) -> Vec<ActionMenuItem> {
    let Some(_repo_root) = ctx.repo_root else {
        return Vec::new();
    };

    let Some(_statuses) = ctx.statuses else {
        return Vec::new();
    };

    let Some(_selected) = ctx.selected_path else {
        return Vec::new();
    };

    let mut items = Vec::new();

    if has_status_in_selection(ctx, GitFileStatus::MODIFIED | GitFileStatus::UNTRACKED) {
        items.push(ActionMenuItem::new("Git: Stage", "", AppAction::StageFile));
    }

    if has_status_in_selection(ctx, GitFileStatus::STAGED) {
        items.push(ActionMenuItem::new("Git: Unstage", "", AppAction::UnstageFile));
    }

    items
}

#[cfg(test)]
mod tests {
    use super::*;

    fn statuses() -> HashMap<String, GitFileStatus> {
        HashMap::new()
    }

    fn ctx<'a>(
        repo_root: &'a str,
        statuses: &'a HashMap<String, GitFileStatus>,
        selected: &'a str,
    ) -> GitMenuContext<'a> {
        GitMenuContext {
            repo_root: Some(repo_root),
            statuses: Some(statuses),
            selected_path: Some(selected),
            marked_paths: empty_marked(),
        }
    }

    fn empty_marked() -> &'static HashSet<String> {
        static EMPTY: std::sync::OnceLock<HashSet<String>> = std::sync::OnceLock::new();
        EMPTY.get_or_init(HashSet::new)
    }

    #[test]
    fn outside_repo_yields_nothing() {
        let map = statuses();
        let none_ctx = GitMenuContext {
            repo_root: None,
            statuses: Some(&map),
            selected_path: Some("/x"),
            marked_paths: empty_marked(),
        };
        assert!(build_git_menu_items(&none_ctx).is_empty());
        assert!(build_git_context_menu_items(&none_ctx).is_empty());
    }

    #[test]
    fn clean_repo_shows_only_network_actions() {
        let root = if cfg!(windows) { r"C:\repo" } else { "/repo" };
        let map = statuses();
        let items = build_git_menu_items(&ctx(root, &map, ""));
        let labels: Vec<&str> = items.iter().map(|item| item.label.as_str()).collect();
        assert_eq!(
            labels,
            vec![
                "Git: Push",
                "Git: Push (force with lease)",
                "Git: Pull",
                "Git: Pull (rebase)",
                "Git: Fetch",
            ]
        );
    }

    #[test]
    fn staged_and_modified_selection_shows_stage_unstage_commit() {
        let root = if cfg!(windows) { r"C:\repo" } else { "/repo" };
        let sep = std::path::MAIN_SEPARATOR;
        let mut map = statuses();
        let file = format!("{root}{sep}file.txt");
        map.insert(file.clone(), GitFileStatus::STAGED | GitFileStatus::MODIFIED);

        let items = build_git_menu_items(&ctx(root, &map, &file));
        let labels: Vec<&str> = items.iter().map(|item| item.label.as_str()).collect();
        assert_eq!(
            labels,
            vec![
                "Git: Stage",
                "Git: Unstage",
                "Git: Stage all changes",
                "Git: Unstage all",
                "Git: Commit",
                "Git: Push",
                "Git: Push (force with lease)",
                "Git: Pull",
                "Git: Pull (rebase)",
                "Git: Fetch",
            ]
        );

        // Context menu: both entries apply
        let ctx_items = build_git_context_menu_items(&ctx(root, &map, &file));
        let labels: Vec<&str> = ctx_items.iter().map(|item| item.label.as_str()).collect();
        assert_eq!(labels, vec!["Git: Stage", "Git: Unstage"]);
    }

    #[test]
    fn ignored_only_repo_has_no_stage_all() {
        let root = if cfg!(windows) { r"C:\repo" } else { "/repo" };
        let sep = std::path::MAIN_SEPARATOR;
        let mut map = statuses();
        map.insert(format!("{root}{sep}log.txt"), GitFileStatus::IGNORED);

        let items = build_git_menu_items(&ctx(root, &map, ""));
        let labels: Vec<&str> = items.iter().map(|item| item.label.as_str()).collect();
        assert_eq!(labels, vec!["Git: Push", "Git: Push (force with lease)", "Git: Pull", "Git: Pull (rebase)", "Git: Fetch"]);
    }

    #[test]
    fn marked_paths_take_precedence() {
        let root = if cfg!(windows) { r"C:\repo" } else { "/repo" };
        let sep = std::path::MAIN_SEPARATOR;
        let mut map = statuses();
        map.insert(format!("{root}{sep}clean.txt"), GitFileStatus::NONE);
        map.insert(format!("{root}{sep}dirty.txt"), GitFileStatus::MODIFIED);

        let mut marked = HashSet::new();
        marked.insert(format!("{root}{sep}clean.txt"));

        let other = format!("{root}{sep}other.txt");        // Marked path is clean: no Stage entry despite the dirty selection
        let marked_ctx = GitMenuContext {
            repo_root: Some(root),
            statuses: Some(&map),
            selected_path: Some(other.as_str()),
            marked_paths: &marked,
        };
        let items = build_git_menu_items(&marked_ctx);
        assert!(!items.iter().any(|item| item.label == "Git: Stage"));
        assert!(items.iter().any(|item| item.label == "Git: Stage all changes"));

        // Add the dirty path to the marks: Stage now applies (marked paths
        // take precedence over the selected entry)
        marked.insert(format!("{root}{sep}dirty.txt"));
        let marked_ctx = GitMenuContext {
            repo_root: Some(root),
            statuses: Some(&map),
            selected_path: Some(other.as_str()),
            marked_paths: &marked,
        };
        let items = build_git_menu_items(&marked_ctx);
        assert!(items.iter().any(|item| item.label == "Git: Stage"));
    }
}