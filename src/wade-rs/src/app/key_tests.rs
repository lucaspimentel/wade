//! Drives the App through key, mouse and paste events the way the main loop
//! does (`App::handle_event`), so key mapping, mode routing and the effect
//! are tested together. Assertions read the rendered frame, the file
//! system or App state.

use std::path::Path;

use super::test_support::{
    app_at, ch, ctrl, fixture, frame, frame_has, key, press, pump_until, row_with, select, selected_name, shift,
    status_bar, type_text, visible_names,
};
use super::{App, AppConfig};
use crate::console_key::ConsoleKey as K;
use crate::input::{InputEvent, InputMode, MouseButton, MouseEvent};

fn app() -> (App, std::path::PathBuf, std::path::PathBuf) {
    let (parent, root) = fixture();
    let app = app_at(AppConfig { git_status_enabled: false, ..AppConfig::default() }, &root);
    (app, parent, root)
}

fn path_of(root: &Path, name: &str) -> String {
    root.join(name).to_string_lossy().into_owned()
}

/// Waits for the running file operation to finish.
fn finish_file_operation(app: &mut App) {
    pump_until(app, "the file operation", |app| app.input_mode != InputMode::FileOperation);
}

fn click(app: &mut App, button: MouseButton, row: i32, col: i32) {
    app.handle_event(InputEvent::Mouse(MouseEvent { button, row, col, is_release: false }));
}

/// The screen row the center pane draws `name` on.
fn center_row_of(app: &mut App, name: &str) -> i32 {
    let pane = app.layout.center_pane;
    let rows = frame(app);
    (pane.top..pane.top + pane.height)
        .find(|&row| {
            rows[row as usize].chars().skip(pane.left as usize).take(pane.width as usize).collect::<String>().contains(name)
        })
        .unwrap_or_else(|| panic!("{name} not drawn in the center pane"))
}

// --- Navigation -----------------------------------------------------------

#[test]
fn arrows_and_vim_keys_move_and_wrap_the_selection() {
    let (mut app, parent, _) = app();
    // sub, a.txt, b.txt, empty.zip, readme.md
    assert_eq!(selected_name(&mut app), "sub");
    press(&mut app, key(K::DownArrow));
    assert_eq!(selected_name(&mut app), "a.txt");
    press(&mut app, ch('j'));
    assert_eq!(selected_name(&mut app), "b.txt");
    press(&mut app, ch('k'));
    press(&mut app, key(K::UpArrow));
    assert_eq!(selected_name(&mut app), "sub");
    // Like C#, Up on the first entry wraps to the last and Down wraps back
    press(&mut app, key(K::UpArrow));
    assert_eq!(selected_name(&mut app), "readme.md", "wraps to the last entry");
    press(&mut app, key(K::DownArrow));
    assert_eq!(selected_name(&mut app), "sub", "wraps to the first entry");

    press(&mut app, key(K::End));
    assert_eq!(selected_name(&mut app), "readme.md");
    press(&mut app, key(K::Home));
    assert_eq!(selected_name(&mut app), "sub");
    press(&mut app, key(K::PageDown));
    assert_eq!(selected_name(&mut app), "readme.md", "page down clamps to the end");
    press(&mut app, key(K::PageUp));
    assert_eq!(selected_name(&mut app), "sub");
    let _ = std::fs::remove_dir_all(&parent);
}

#[test]
fn page_keys_move_by_the_visible_height() {
    let (mut app, parent, root) = app();
    for i in 0..100 {
        std::fs::write(root.join(format!("f{i:03}.txt")), "x").unwrap();
    }
    app.directory_contents.invalidate_all();
    let page = app.visible_file_list_height(&[]);

    press(&mut app, key(K::PageDown));
    assert_eq!(app.selected_index, page);
    press(&mut app, key(K::PageDown));
    assert_eq!(app.selected_index, page * 2);
    press(&mut app, key(K::PageUp));
    assert_eq!(app.selected_index, page);
    let _ = std::fs::remove_dir_all(&parent);
}

#[test]
fn enter_opens_a_directory_and_back_returns_with_it_selected() {
    let (mut app, parent, root) = app();
    select(&mut app, "sub");
    press(&mut app, key(K::Enter));
    assert_eq!(app.current_path, path_of(&root, "sub"));
    assert_eq!(visible_names(&mut app), ["inner.txt"]);
    assert!(frame_has(&mut app, "inner.txt"));

    press(&mut app, key(K::LeftArrow));
    assert_eq!(app.current_path, root.to_string_lossy());
    assert_eq!(selected_name(&mut app), "sub", "the directory we came from is selected");

    // A directory that is not first in the listing, so selection 0 differs
    std::fs::create_dir(root.join("zdir")).unwrap();
    app.directory_contents.invalidate_all();
    select(&mut app, "zdir");
    press(&mut app, key(K::Enter));
    press(&mut app, key(K::LeftArrow));
    assert_eq!(selected_name(&mut app), "zdir", "the directory we came from is selected");

    // Right and l open too; Backspace and h go back
    select(&mut app, "sub");
    press(&mut app, key(K::RightArrow));
    assert_eq!(app.current_path, path_of(&root, "sub"));
    press(&mut app, key(K::Backspace));
    assert_eq!(app.current_path, root.to_string_lossy(), "Backspace goes back");
    select(&mut app, "sub");
    press(&mut app, ch('l'));
    assert_eq!(app.current_path, path_of(&root, "sub"));
    press(&mut app, ch('h'));
    assert_eq!(app.current_path, root.to_string_lossy());
    let _ = std::fs::remove_dir_all(&parent);
}

#[test]
fn selection_is_remembered_per_directory() {
    let (mut app, parent, root) = app();
    select(&mut app, "sub");
    press(&mut app, key(K::Enter));
    press(&mut app, key(K::LeftArrow));
    select(&mut app, "b.txt");
    press(&mut app, key(K::LeftArrow));
    assert_eq!(selected_name(&mut app), crate::app::test_support::CURRENT_DIR);
    press(&mut app, key(K::Enter));
    assert_eq!(app.current_path, root.to_string_lossy());
    assert_eq!(selected_name(&mut app), "b.txt");
    let _ = std::fs::remove_dir_all(&parent);
}

#[test]
fn q_quits_and_shift_q_quits_without_writing_the_cwd() {
    let (mut app, parent, _) = app();
    press(&mut app, ch('q'));
    assert!(app.quit);
    assert!(app.write_cwd);

    let (mut app2, parent2, _) = app_pair();
    press(&mut app2, ch('Q'));
    assert!(app2.quit);
    assert!(!app2.write_cwd);
    let _ = std::fs::remove_dir_all(&parent);
    let _ = std::fs::remove_dir_all(&parent2);
}

fn app_pair() -> (App, std::path::PathBuf, std::path::PathBuf) {
    app()
}

// --- Sort and marks ---------------------------------------------------------

#[test]
fn s_and_shift_s_reorder_the_listing() {
    let (mut app, parent, root) = app();
    std::fs::write(root.join("big.bin"), vec![0u8; 50_000]).unwrap();
    app.directory_contents.invalidate_all();
    let files = |app: &mut App| -> Vec<String> {
        visible_names(app).into_iter().filter(|n| n != "sub").collect()
    };

    press(&mut app, ch('s')); // Modified
    press(&mut app, ch('s')); // Size
    let by_size = files(&mut app);
    assert_eq!(by_size.first().map(String::as_str), Some("b.txt"), "{by_size:?}");
    assert_eq!(by_size.last().map(String::as_str), Some("big.bin"), "{by_size:?}");

    press(&mut app, ch('S'));
    let descending = files(&mut app);
    assert_eq!(descending.first().map(String::as_str), Some("big.bin"), "{descending:?}");

    press(&mut app, ch('s')); // Extension
    press(&mut app, ch('s')); // Name
    press(&mut app, ch('S')); // back to ascending
    assert_eq!(files(&mut app), ["a.txt", "b.txt", "big.bin", "empty.zip", "readme.md"]);
    let _ = std::fs::remove_dir_all(&parent);
}

#[test]
fn space_marks_and_moves_down_and_delete_prompts_for_all_marks() {
    let (mut app, parent, root) = app();
    select(&mut app, "a.txt");
    press(&mut app, key(K::Spacebar));
    assert_eq!(selected_name(&mut app), "b.txt");
    press(&mut app, key(K::Spacebar));
    assert!(app.marked_paths.contains(&path_of(&root, "a.txt")));
    assert!(app.marked_paths.contains(&path_of(&root, "b.txt")));
    assert!(status_bar(&mut app).contains('2'), "status bar shows the mark count");

    press(&mut app, key(K::Delete));
    assert_eq!(app.input_mode, InputMode::Confirm);
    let message = app.modal.confirm_message.clone().unwrap_or_default();
    assert!(message.contains('2'), "prompt counts the marked items: {message}");

    // Space again unmarks
    press(&mut app, key(K::Escape));
    select(&mut app, "a.txt");
    press(&mut app, key(K::Spacebar));
    assert!(!app.marked_paths.contains(&path_of(&root, "a.txt")));
    let _ = std::fs::remove_dir_all(&parent);
}

// --- Filter -------------------------------------------------------------------

#[test]
fn slash_filters_the_listing_and_escape_clears_it() {
    let (mut app, parent, _) = app();
    press(&mut app, ch('/'));
    assert_eq!(app.input_mode, InputMode::Search);
    type_text(&mut app, "txt");
    assert_eq!(visible_names(&mut app), ["a.txt", "b.txt"]);
    assert!(!frame_has(&mut app, "readme.md"));

    // Enter keeps the filter; q clears it before quitting
    press(&mut app, key(K::Enter));
    assert_eq!(app.input_mode, InputMode::Normal);
    assert_eq!(visible_names(&mut app), ["a.txt", "b.txt"]);
    press(&mut app, ch('q'));
    assert!(!app.quit, "q first clears the filter");
    assert!(visible_names(&mut app).contains(&"readme.md".to_string()));

    press(&mut app, ch('/'));
    type_text(&mut app, "zip");
    press(&mut app, key(K::Escape));
    assert!(visible_names(&mut app).contains(&"readme.md".to_string()), "Escape clears the filter");
    let _ = std::fs::remove_dir_all(&parent);
}

// --- Dialogs ------------------------------------------------------------------

#[test]
fn ctrl_g_goes_to_a_typed_path() {
    let (mut app, parent, root) = app();
    press(&mut app, ctrl(K::G));
    assert_eq!(app.input_mode, InputMode::GoToPath);
    let target = path_of(&root, "sub");
    if let Some(input) = app.modal.go_to_path_input.as_mut() {
        input.clear();
    }
    app.handle_event(InputEvent::Paste(target.clone()));
    press(&mut app, key(K::Enter));
    assert_eq!(app.input_mode, InputMode::Normal);
    assert_eq!(app.current_path, target);
    let _ = std::fs::remove_dir_all(&parent);
}

#[test]
fn question_mark_shows_help_until_a_key() {
    let (mut app, parent, _) = app();
    press(&mut app, ch('?'));
    assert_eq!(app.input_mode, InputMode::Help);
    assert!(frame_has(&mut app, "Help"));
    press(&mut app, ch('x'));
    assert_eq!(app.input_mode, InputMode::Normal);
    let _ = std::fs::remove_dir_all(&parent);
}

#[test]
fn comma_opens_the_config_dialog_and_enter_saves_a_toggled_item() {
    let (mut app, parent, root) = app();
    press(&mut app, ch(','));
    assert_eq!(app.input_mode, InputMode::Config);
    assert!(frame_has(&mut app, "Show Hidden Files"));

    let hidden_item = app.modal.config_state.as_ref().unwrap().items.iter().position(|item| item.label == "Show Hidden Files").unwrap();
    for _ in 0..hidden_item {
        press(&mut app, key(K::DownArrow));
    }
    press(&mut app, key(K::Spacebar));
    press(&mut app, key(K::Enter));
    assert_eq!(app.input_mode, InputMode::Normal);
    assert!(app.config.show_hidden_files);
    assert!(visible_names(&mut app).contains(&".hidden".to_string()));
    let saved = std::fs::read_to_string(root.join("config.toml")).unwrap();
    assert!(saved.contains("show_hidden_files = true"), "{saved}");

    // Escape closes without applying
    press(&mut app, ch(','));
    for _ in 0..hidden_item {
        press(&mut app, key(K::DownArrow));
    }
    press(&mut app, key(K::Spacebar));
    press(&mut app, key(K::Escape));
    assert!(app.config.show_hidden_files);
    let _ = std::fs::remove_dir_all(&parent);
}

#[test]
fn shift_b_bookmarks_the_directory_and_b_lists_it() {
    let (mut app, parent, root) = app();
    press(&mut app, ch('B'));
    let current = root.to_string_lossy().into_owned();
    assert!(app.bookmark_store.contains(&current));
    assert!(root.join("bookmarks.txt").exists(), "saved to the fixture store");

    select(&mut app, "sub");
    press(&mut app, key(K::Enter));
    press(&mut app, ch('b'));
    assert_eq!(app.input_mode, InputMode::Bookmarks);
    assert!(frame_has(&mut app, crate::app::test_support::CURRENT_DIR));
    press(&mut app, key(K::Enter));
    assert_eq!(app.current_path, current, "Enter navigates to the bookmark");

    press(&mut app, ch('B'));
    assert!(!app.bookmark_store.contains(&current), "B again removes it");
    let _ = std::fs::remove_dir_all(&parent);
}

#[test]
fn ctrl_p_palette_runs_the_filtered_action() {
    let (mut app, parent, _) = app();
    press(&mut app, ctrl(K::P));
    assert_eq!(app.input_mode, InputMode::ActionPalette);
    type_text(&mut app, "hidden");
    press(&mut app, key(K::Enter));
    assert_eq!(app.input_mode, InputMode::Normal);
    assert!(app.config.show_hidden_files, "Toggle hidden files ran");
    let _ = std::fs::remove_dir_all(&parent);
}

// --- File creation and changes ---------------------------------------------

#[test]
fn n_and_shift_n_create_a_file_and_a_directory() {
    let (mut app, parent, root) = app();
    press(&mut app, ch('n'));
    assert_eq!(app.input_mode, InputMode::TextInput);
    type_text(&mut app, "new.txt");
    press(&mut app, key(K::Enter));
    assert!(root.join("new.txt").is_file());
    assert_eq!(selected_name(&mut app), "new.txt");

    press(&mut app, ch('N'));
    type_text(&mut app, "newdir");
    press(&mut app, key(K::Enter));
    assert!(root.join("newdir").is_dir());
    let _ = std::fs::remove_dir_all(&parent);
}

#[test]
fn f2_renames_the_selected_entry() {
    let (mut app, parent, root) = app();
    select(&mut app, "b.txt");
    press(&mut app, key(K::F2));
    assert_eq!(app.input_mode, InputMode::TextInput);
    assert_eq!(app.modal.active_text_input.as_ref().map(|i| i.value().to_string()).as_deref(), Some("b.txt"));
    for _ in 0.."b.txt".len() {
        press(&mut app, key(K::Backspace));
    }
    type_text(&mut app, "c.txt");
    press(&mut app, key(K::Enter));
    assert!(!root.join("b.txt").exists());
    assert!(root.join("c.txt").is_file());
    let _ = std::fs::remove_dir_all(&parent);
}

#[test]
fn escape_cancels_a_text_input_dialog() {
    let (mut app, parent, root) = app();
    press(&mut app, ch('n'));
    type_text(&mut app, "never.txt");
    press(&mut app, key(K::Escape));
    assert_eq!(app.input_mode, InputMode::Normal);
    assert!(!root.join("never.txt").exists());
    let _ = std::fs::remove_dir_all(&parent);
}

#[test]
fn paste_inserts_into_a_text_input() {
    let (mut app, parent, root) = app();
    press(&mut app, ch('n'));
    app.handle_event(InputEvent::Paste("pasted.txt".to_string()));
    press(&mut app, key(K::Enter));
    assert!(root.join("pasted.txt").is_file());
    let _ = std::fs::remove_dir_all(&parent);
}

#[cfg(unix)]
#[test]
fn ctrl_l_creates_a_symlink_to_the_selected_entry() {
    let (mut app, parent, root) = app();
    select(&mut app, "a.txt");
    press(&mut app, ctrl(K::L));
    assert_eq!(app.input_mode, InputMode::TextInput);
    assert_eq!(app.modal.active_text_input.as_ref().map(|i| i.value().to_string()).as_deref(), Some("a.txt_link"));
    press(&mut app, key(K::Enter));
    let link = root.join("a.txt_link");
    assert!(std::fs::symlink_metadata(&link).unwrap().file_type().is_symlink());
    assert_eq!(std::fs::read_link(&link).unwrap(), root.join("a.txt"));
    let _ = std::fs::remove_dir_all(&parent);
}

#[test]
fn shift_delete_then_y_permanently_deletes() {
    let (mut app, parent, root) = app();
    select(&mut app, "b.txt");
    press(&mut app, shift(K::Delete));
    assert_eq!(app.input_mode, InputMode::Confirm);
    assert_eq!(app.modal.confirm_title.as_deref(), Some("Permanently Delete"));
    assert!(app.modal.confirm_message.as_deref().unwrap_or_default().contains("cannot be undone"));

    press(&mut app, ch('y'));
    finish_file_operation(&mut app);
    assert!(!root.join("b.txt").exists());
    assert!(!visible_names(&mut app).contains(&"b.txt".to_string()));
    let _ = std::fs::remove_dir_all(&parent);
}

#[test]
fn n_in_the_confirm_dialog_keeps_the_file() {
    let (mut app, parent, root) = app();
    select(&mut app, "b.txt");
    press(&mut app, key(K::Delete));
    press(&mut app, ch('n'));
    assert_eq!(app.input_mode, InputMode::Normal);
    assert!(root.join("b.txt").exists());
    let _ = std::fs::remove_dir_all(&parent);
}

#[test]
fn escape_during_a_file_operation_cancels_it() {
    let (mut app, parent, root) = app();
    app.clipboard_paths = vec![path_of(&root, "sub")];
    app.clipboard_is_cut = false;
    select(&mut app, "sub");
    press(&mut app, key(K::Enter));
    // Pasting "sub" into itself would conflict; paste into the parent of sub instead
    press(&mut app, key(K::LeftArrow));
    app.execute_paste_internal(vec![path_of(&root, "a.txt")], false, true);
    assert_eq!(app.input_mode, InputMode::FileOperation);
    press(&mut app, key(K::Escape));
    assert_eq!(app.input_mode, InputMode::Normal);
    assert_eq!(app.notification.as_ref().map(|n| n.message.as_str()), Some("Operation cancelled"));
    let _ = std::fs::remove_dir_all(&parent);
}

// --- Properties, context menu, mouse ---------------------------------------

#[test]
fn i_shows_properties_and_fills_in_the_directory_size() {
    let (mut app, parent, _) = app();
    select(&mut app, "sub");
    press(&mut app, ch('i'));
    assert_eq!(app.input_mode, InputMode::Properties);
    assert!(frame_has(&mut app, "Properties"));
    pump_until(&mut app, "the directory size", |app| {
        app.properties_dir_size_text.as_deref().is_some_and(|text| !text.starts_with("Calculating"))
    });
    assert!(frame_has(&mut app, "5 B"), "sub holds a 5-byte file");
    press(&mut app, key(K::Escape));
    assert_eq!(app.input_mode, InputMode::Normal);
    let _ = std::fs::remove_dir_all(&parent);
}

#[test]
fn right_click_selects_the_entry_and_the_menu_copies_it() {
    let (mut app, parent, root) = app();
    let row = center_row_of(&mut app, "b.txt");
    let col = app.layout.center_pane.left + 3;
    click(&mut app, MouseButton::Right, row, col);
    assert_eq!(selected_name(&mut app), "b.txt");
    assert_eq!(app.input_mode, InputMode::ContextMenu);

    let items: Vec<String> = app.modal.context_menu.as_ref().unwrap().items.iter().map(|i| i.label.clone()).collect();
    let copy = items.iter().position(|label| label == "Copy").expect("Copy in the context menu");
    for _ in 0..copy {
        press(&mut app, key(K::DownArrow));
    }
    press(&mut app, key(K::Enter));
    assert_eq!(app.input_mode, InputMode::Normal);
    assert_eq!(app.clipboard_paths, [path_of(&root, "b.txt")]);
    assert!(!app.clipboard_is_cut);
    let _ = std::fs::remove_dir_all(&parent);
}

#[test]
fn left_click_selects_and_the_wheel_moves_the_selection() {
    let (mut app, parent, _) = app();
    let row = center_row_of(&mut app, "readme.md");
    let col = app.layout.center_pane.left + 3;
    let header_row = app.layout.center_pane.top;
    click(&mut app, MouseButton::Left, row, col);
    assert_eq!(selected_name(&mut app), "readme.md");

    click(&mut app, MouseButton::ScrollUp, row, col);
    assert_eq!(selected_name(&mut app), "empty.zip");
    click(&mut app, MouseButton::ScrollDown, row, col);
    assert_eq!(selected_name(&mut app), "readme.md");

    // A click on the header rows selects nothing new
    click(&mut app, MouseButton::Left, header_row, col);
    assert_eq!(selected_name(&mut app), "readme.md");
    let _ = std::fs::remove_dir_all(&parent);
}

#[test]
fn resize_relayouts_the_panes() {
    let (mut app, parent, _) = app();
    let mut buffer = crate::screen::ScreenBuffer::new(super::test_support::WIDTH, super::test_support::HEIGHT);
    let (mut width, mut height) = (super::test_support::WIDTH, super::test_support::HEIGHT);
    let before = app.layout.center_pane.width;
    app.handle_resize(crate::input::ResizeEvent { width: 60, height: 20 }, &mut buffer, &mut width, &mut height);
    assert_eq!((width, height), (60, 20));
    assert!(app.layout.center_pane.width < before);
    assert_eq!(app.layout.center_pane.height, 19, "one row for the status bar");
    let _ = std::fs::remove_dir_all(&parent);
}

// --- Launch guards ------------------------------------------------------------

#[test]
fn download_cloud_file_is_a_no_op_for_local_files() {
    let (mut app, parent, _) = app();
    select(&mut app, "a.txt");
    app.dispatch(super::AppAction::DownloadCloudFile);
    assert_eq!(app.input_mode, InputMode::Normal);
    assert!(row_with(&mut app, "a.txt").contains("a.txt"));
    let _ = std::fs::remove_dir_all(&parent);
}

// --- Git ------------------------------------------------------------------------

fn git(dir: &Path, args: &[&str]) -> Option<String> {
    let output = std::process::Command::new("git").arg("-C").arg(dir).args(args).output().ok()?;
    output.status.success().then(|| String::from_utf8_lossy(&output.stdout).into_owned())
}

/// The fixture as a fresh repo on branch "main", with an App watching git
/// status; `None` when git is not installed.
fn git_app() -> Option<(App, std::path::PathBuf, std::path::PathBuf)> {
    let (parent, root) = fixture();
    git(&root, &["init", "-q"])?;
    git(&root, &["symbolic-ref", "HEAD", "refs/heads/main"])?;
    git(&root, &["config", "user.name", "wade test"])?;
    git(&root, &["config", "user.email", "wade@example.invalid"])?;
    git(&root, &["config", "commit.gpgsign", "false"])?;
    let mut app = app_at(AppConfig::default(), &root);
    app.refresh_git_status();
    pump_until(&mut app, "git status", |app| app.git_statuses.is_some());
    Some((app, parent, root))
}

fn wait_for_git_action(app: &mut App) {
    pump_until(app, "the git action", |app| {
        app.notification.as_ref().is_some_and(|n| n.message.starts_with("Git action completed") || n.message.starts_with("Git error"))
    });
    let message = app.notification.take().map(|n| n.message).unwrap_or_default();
    assert_eq!(message, "Git action completed");
}

fn staged_files(root: &Path) -> Vec<String> {
    git(root, &["diff", "--cached", "--name-only"]).unwrap_or_default().lines().map(str::to_string).collect()
}

#[test]
fn git_status_shows_branch_and_untracked_files() {
    let Some((mut app, parent, root)) = git_app() else { return };
    assert_eq!(app.current_repo_root.as_deref(), Some(root.to_string_lossy().as_ref()));
    assert_eq!(app.current_branch_name.as_deref(), Some("main"));
    assert!(status_bar(&mut app).contains("main"));
    let status = app.git_statuses.as_ref().unwrap().get(&path_of(&root, "a.txt")).copied();
    assert_eq!(status, Some(crate::fs::GitFileStatus::UNTRACKED));
    let _ = std::fs::remove_dir_all(&parent);
}

#[test]
fn stage_unstage_and_stage_all_change_the_index() {
    let Some((mut app, parent, root)) = git_app() else { return };
    // `git restore --staged` needs a HEAD (in C# too), so start from a commit
    git(&root, &["add", "readme.md"]).unwrap();
    git(&root, &["commit", "-q", "-m", "base"]).unwrap();
    select(&mut app, "a.txt");
    app.dispatch(super::AppAction::StageFile);
    wait_for_git_action(&mut app);
    assert_eq!(staged_files(&root), ["a.txt"]);

    app.dispatch(super::AppAction::UnstageFile);
    wait_for_git_action(&mut app);
    assert!(staged_files(&root).is_empty());

    app.dispatch(super::AppAction::StageAll);
    wait_for_git_action(&mut app);
    assert!(staged_files(&root).contains(&"b.txt".to_string()));
    let _ = std::fs::remove_dir_all(&parent);
}

#[test]
fn git_commit_dialog_commits_the_index() {
    let Some((mut app, parent, root)) = git_app() else { return };
    app.dispatch(super::AppAction::StageAll);
    wait_for_git_action(&mut app);

    app.dispatch(super::AppAction::GitCommit);
    assert_eq!(app.input_mode, InputMode::TextInput);
    type_text(&mut app, "First commit");
    press(&mut app, key(K::Enter));
    wait_for_git_action(&mut app);
    let log = git(&root, &["log", "--format=%s"]).unwrap_or_default();
    assert_eq!(log.trim(), "First commit");
    let _ = std::fs::remove_dir_all(&parent);
}

// --- Ports of SearchFilterTests.cs and ModalInputTests.cs ------------------------

fn char_only(c: char) -> crate::input::KeyEvent {
    crate::input::KeyEvent { key: K::None, key_char: c as u16, shift: false, alt: false, control: false }
}

#[test]
fn filter_is_case_insensitive_navigates_and_shows_all_when_emptied() {
    let (mut app, parent, _) = app();
    press(&mut app, ch('/'));
    assert!(app.modal.search_input.is_some());
    for c in "TXT".chars() {
        press(&mut app, char_only(c));
    }
    assert_eq!(visible_names(&mut app), ["a.txt", "b.txt"], "case-insensitive");

    press(&mut app, key(K::DownArrow));
    assert_eq!(selected_name(&mut app), "b.txt");
    press(&mut app, key(K::UpArrow));
    assert_eq!(selected_name(&mut app), "a.txt");

    for _ in 0..3 {
        press(&mut app, key(K::Backspace));
    }
    assert!(visible_names(&mut app).contains(&"readme.md".to_string()), "empty filter shows everything");
    let _ = std::fs::remove_dir_all(&parent);
}

#[test]
fn vim_and_quit_keys_are_typed_into_the_filter() {
    for c in ['j', 'k', 'q'] {
        let (mut app, parent, _) = app();
        press(&mut app, ch('/'));
        press(&mut app, ch(c));
        assert_eq!(app.input_mode, InputMode::Search, "{c}");
        assert!(!app.quit, "{c}");
        assert_eq!(app.search_filter, c.to_string(), "{c}");
        let _ = std::fs::remove_dir_all(&parent);
    }
}

#[test]
fn the_search_bar_shows_the_slash_and_the_filter() {
    let (mut app, parent, _) = app();
    press(&mut app, ch('/'));
    type_text(&mut app, "read");
    assert!(frame_has(&mut app, "/read"));
    let _ = std::fs::remove_dir_all(&parent);
}

fn confirm_toggle_hidden(app: &mut App) {
    app.show_confirm_dialog("Test", "Test?", super::dialogs::ConfirmAction::Dispatch(super::AppAction::ToggleHiddenFiles));
}

#[test]
fn confirm_dialog_keys() {
    for yes in [ch('y'), key(K::Enter)] {
        let (mut app, parent, _) = app();
        confirm_toggle_hidden(&mut app);
        press(&mut app, yes);
        assert_eq!(app.input_mode, InputMode::Normal);
        assert!(app.config.show_hidden_files, "the action ran");
        let _ = std::fs::remove_dir_all(&parent);
    }

    for no in [ch('n'), key(K::Escape)] {
        let (mut app, parent, _) = app();
        confirm_toggle_hidden(&mut app);
        press(&mut app, no);
        assert_eq!(app.input_mode, InputMode::Normal);
        assert!(!app.config.show_hidden_files, "dismissed without the action");
        let _ = std::fs::remove_dir_all(&parent);
    }

    // Other keys, including navigation and quit, are consumed
    for other in [ch('a'), ch('j'), ch('k'), ch('q'), key(K::UpArrow), key(K::DownArrow)] {
        let (mut app, parent, _) = app();
        confirm_toggle_hidden(&mut app);
        press(&mut app, other);
        assert_eq!(app.input_mode, InputMode::Confirm, "{other:?}");
        assert_eq!(selected_name(&mut app), "sub", "{other:?}");
        assert!(!app.quit, "{other:?}");
        let _ = std::fs::remove_dir_all(&parent);
    }
}

#[test]
fn text_input_dialog_editing_keys() {
    let (mut app, parent, _) = app();
    press(&mut app, ch('n'));
    let value = |app: &App| app.modal.active_text_input.as_ref().map(|i| (i.value().to_string(), i.cursor_position())).unwrap();

    type_text(&mut app, "abc");
    assert_eq!(value(&app), ("abc".to_string(), 3));
    press(&mut app, key(K::Backspace));
    assert_eq!(value(&app), ("ab".to_string(), 2));
    press(&mut app, key(K::LeftArrow));
    assert_eq!(value(&app).1, 1);
    press(&mut app, key(K::Delete));
    assert_eq!(value(&app), ("a".to_string(), 1));
    press(&mut app, key(K::Home));
    assert_eq!(value(&app).1, 0);
    press(&mut app, key(K::RightArrow));
    assert_eq!(value(&app).1, 1);
    press(&mut app, key(K::End));
    assert_eq!(value(&app).1, 1);

    press(&mut app, key(K::Escape));
    assert_eq!(app.input_mode, InputMode::Normal);
    assert!(!app.quit, "Escape in a dialog does not quit");
    let _ = std::fs::remove_dir_all(&parent);
}

#[test]
fn help_and_properties_stay_open_for_modifier_keys() {
    for open in [ch('?'), ch('i')] {
        for vk in [16u16, 17, 18] {
            let (mut app, parent, _) = app();
            press(&mut app, open);
            let mode = app.input_mode;
            press(&mut app, crate::input::KeyEvent { key: K(vk), key_char: 0, shift: false, alt: false, control: false });
            assert_eq!(app.input_mode, mode, "modifier {vk} keeps {mode:?} open");
            press(&mut app, ch('x'));
            assert_eq!(app.input_mode, InputMode::Normal, "a real key closes {mode:?}");
            let _ = std::fs::remove_dir_all(&parent);
        }
    }

    for vk in [16u16, 17, 18] {
        assert!(crate::input::KeyEvent { key: K(vk), key_char: 0, shift: false, alt: false, control: false }.is_modifier_only());
    }
    for k in [K::A, K::Escape, K::Enter, K::Spacebar] {
        assert!(!key(k).is_modifier_only(), "{k:?}");
    }
}

#[test]
fn mouse_clicks_are_ignored_while_a_modal_dialog_is_open() {
    type Open = fn(&mut App);
    let modes: [(&str, Open); 4] = [
        ("go to path", |app| press(app, ctrl(K::G))),
        ("text input", |app| press(app, ch('n'))),
        ("confirm", confirm_toggle_hidden),
        ("help", |app| press(app, ch('?'))),
    ];
    for (name, open) in modes {
        let (mut app, parent, _) = app();
        let row = center_row_of(&mut app, "readme.md");
        let col = app.layout.center_pane.left + 3;
        open(&mut app);
        click(&mut app, MouseButton::Left, row, col);
        assert_eq!(selected_name(&mut app), "sub", "{name}");
        let _ = std::fs::remove_dir_all(&parent);
    }

    // The filter bar does not block the mouse
    let (mut app, parent, _) = app();
    let row = center_row_of(&mut app, "readme.md");
    let col = app.layout.center_pane.left + 3;
    press(&mut app, ch('/'));
    click(&mut app, MouseButton::Left, row, col);
    assert_ne!(selected_name(&mut app), "sub", "search mode handles clicks");
    let _ = std::fs::remove_dir_all(&parent);
}

#[test]
fn sixel_output_is_suppressed_in_modal_modes() {
    for (mode, expected) in [
        (InputMode::Normal, true),
        (InputMode::Search, true),
        (InputMode::ExpandedPreview, true),
        (InputMode::GoToPath, false),
        (InputMode::TextInput, false),
        (InputMode::Confirm, false),
        (InputMode::Help, false),
    ] {
        let (mut app, parent, _) = app();
        app.preview.sixel_pending = true;
        app.preview.cached_sixel_data = Some("sixel".to_string());
        app.input_mode = mode;
        assert_eq!(app.take_pending_sixel().is_some(), expected, "{mode:?}");
        let _ = std::fs::remove_dir_all(&parent);
    }
}
