//! Port of the modal overlay handling in `src/Wade/App.cs`: Confirm and
//! TextInput dialogs, GoToPath, the search bar, the action palette, and the
//! Help overlay. Rendering and key handling mirror the C# `Handle*Key` and
//! `Render*Dialog` methods.

use crate::app::input_reader::AppAction;
use crate::app::{App, InputMode, KeyEvent};
use crate::fs::directory_contents::capitalize_drive_letter;
use crate::screen::{CellStyle, Color, ScreenBuffer};
use crate::ui::action_palette::{ActionMenuItem, ActionMenuLevel};
use crate::ui::config_dialog::ConfigDialogState;
use crate::ui::dialog_box::{self, BG_COLOR};
use crate::ui::help_overlay;
use crate::ui::layout::Rect;
use crate::ui::notification::NotificationKind;
use crate::ui::text_input::TextInput;

/// Purpose of the active text-input dialog (what C# stores as an
/// `Action<string>` completion callback). The file-operation consumers land
/// in a later phase; for now Enter with a purpose set reports not-yet-ported.
/// What the confirm dialog's Yes executes: C# stores an `Action` closure;
/// the Rust port enumerates the two uses (git-style action dispatch and the
/// file deletion that carries its target list).
#[derive(Clone, Debug, PartialEq, Eq)]
pub enum ConfirmAction {
    Dispatch(AppAction),
    DeleteFiles {
        targets: Vec<String>,
        permanent: bool,
    },
    /// `() => ExecutePaste(overwrite: true)`.
    Paste {
        overwrite: bool,
    },
}

#[derive(Clone, Copy, Debug, PartialEq, Eq)]
pub enum TextInputPurpose {
    Rename,
    NewFile,
    NewDirectory,
    /// Port of the C# `ShowTextInputDialog("Commit message", ...)` callback.
    Commit,
    /// Port of the `Create Symlink` dialog.
    CreateSymlink,
}

#[derive(Default)]
pub(crate) struct ModalState {
    pub confirm_title: Option<String>,
    pub confirm_message: Option<String>,
    /// C# stores an `Action`; the only producers dispatch a single action.
    pub confirm_yes_action: Option<ConfirmAction>,
    pub text_input_title: Option<String>,
    pub active_text_input: Option<TextInput>,
    pub text_input_purpose: Option<TextInputPurpose>,
    pub go_to_path_input: Option<TextInput>,
    pub go_to_path_suggestion: Option<String>,
    pub action_menu_stack: Vec<ActionMenuLevel>,
    pub search_input: Option<TextInput>,
    pub config_state: Option<ConfigDialogState>,
    pub bookmark_selected_index: usize,
    pub bookmark_scroll_offset: usize,
    pub bookmark_input: Option<TextInput>,
    pub context_menu: Option<crate::ui::context_menu::ContextMenuState>,
}

/// Port of `Path.GetInvalidFileNameChars`: the platform's invalid file-name
/// characters. C# returns the full Windows set on Windows; on unix, the
/// separator plus control characters.
#[must_use]
pub fn is_invalid_file_name(name: &str) -> bool {
    if name.is_empty() {
        return false;
    }

    #[cfg(windows)]
    {
        const WINDOWS_INVALID: [char; 9] = ['"', '<', '>', '|', ':', '*', '?', '\\', '/'];
        name.chars().any(|c| WINDOWS_INVALID.contains(&c) || c.is_ascii_control())
    }

    #[cfg(not(windows))]
    {
        name.chars().any(|c| c == '/' || c == '\\' || c.is_control())
    }
}

#[cfg(test)]
mod invalid_name_tests {
    use super::is_invalid_file_name;

    #[test]
    fn rejects_separators_and_reserved() {
        assert!(!is_invalid_file_name("readme.txt"));
        assert!(!is_invalid_file_name(".hidden"));
        assert!(is_invalid_file_name("a/b.txt"));
        assert!(is_invalid_file_name("a\\b.txt"));
        assert!(is_invalid_file_name("a\u{0}b"));

        #[cfg(windows)]
        {
            assert!(is_invalid_file_name("a<b"));
            assert!(is_invalid_file_name("a|b"));
            assert!(is_invalid_file_name("a?b"));
            assert!(is_invalid_file_name("a:b"));
            assert!(is_invalid_file_name("a*b"));
            assert!(is_invalid_file_name("\"a\""));
            assert!(!is_invalid_file_name("a\u{00e9}b.txt"));
        }
    }
}

impl App {
    /// Port of `RenderModals`' modal tail entry points: FileOperation overlay
    /// and progress reset helpers.
    pub fn enter_file_operation_mode(&mut self, label: &str) {
        self.input_mode = InputMode::FileOperation;
        self.file_op_label = label.to_string();
    }

    pub fn exit_file_operation_mode(&mut self) {
        self.input_mode = InputMode::Normal;
        self.file_op_progress = None;
    }
}

impl App {
    /// Port of the `_inputMode` switch at the top of the C# input loop.
    /// Returns true when the event was consumed by a modal mode.
    pub fn handle_modal_key(&mut self, key: KeyEvent) -> bool {
        match self.input_mode {
            InputMode::Help => {
                if !key.is_modifier_only() {
                    self.input_mode = InputMode::Normal;
                }

                true
            }
            InputMode::Search => {
                self.handle_search_key(key);
                true
            }
            InputMode::TextInput => {
                self.handle_text_input_key(key);
                true
            }
            InputMode::Confirm => {
                self.handle_confirm_key(key);
                true
            }
            InputMode::GoToPath => {
                self.handle_go_to_path_key(key);
                true
            }
            InputMode::ActionPalette => {
                self.handle_action_palette_key(key);
                true
            }
            InputMode::Config => {
                self.handle_config_key(key);
                true
            }
            InputMode::FileOperation => {
                // Escape cancels the running operation (App.cs:660-671)
                if key.key == crate::console_key::ConsoleKey::Escape {
                    self.file_operation_runner.cancel();
                    self.input_mode = InputMode::Normal;
                    self.directory_contents.invalidate(&self.current_path);
                    self.invalidate_filtered_entries();
                    self.marked_paths.clear();
                    self.refresh_git_status();
                    self.show_notification("Operation cancelled", NotificationKind::Info);
                }

                true
            }
            InputMode::Properties => {
                // Modifier-only keys fall through (App.cs:602-608)
                if !key.is_modifier_only() {
                    self.handle_properties_key(&key);
                }

                true
            }
            InputMode::Bookmarks => {
                self.handle_bookmark_key(key);
                true
            }
            InputMode::ContextMenu => {
                self.handle_context_menu_key(key);
                true
            }
            InputMode::FileFinder => {
                self.handle_file_finder_key(key);
                true
            }
            InputMode::ExpandedPreview => {
                self.handle_expanded_preview_key(&key);
                true
            }
            _ => false,
        }
    }

    /// Port of `HandleSearchKey` (App.cs:2790).
    fn handle_search_key(&mut self, key: KeyEvent) {
        if key.control {
            match key.key {
                crate::console_key::ConsoleKey::LeftArrow => {
                    if let Some(input) = &mut self.modal.search_input {
                        input.move_cursor_word_left();
                    }
                    return;
                }
                crate::console_key::ConsoleKey::RightArrow => {
                    if let Some(input) = &mut self.modal.search_input {
                        input.move_cursor_word_right();
                    }
                    return;
                }
                crate::console_key::ConsoleKey::Backspace => {
                    if let Some(input) = &mut self.modal.search_input {
                        input.delete_word_backward();
                    }
                    self.sync_search_filter();
                    return;
                }
                // Rust only: Ctrl+C clears the filter
                crate::console_key::ConsoleKey::C => {
                    if let Some(input) = &mut self.modal.search_input {
                        input.clear();
                    }
                    self.sync_search_filter();
                    return;
                }
                _ => {}
            }
        }

        match key.key {
            crate::console_key::ConsoleKey::Escape => {
                self.clear_search_filter();
                self.selected_index = 0;
                self.scroll_offset = 0;
            }
            crate::console_key::ConsoleKey::Enter => {
                self.input_mode = InputMode::Normal;
                self.modal.search_input = None;
            }
            crate::console_key::ConsoleKey::UpArrow => {
                let count = self.get_visible_entries().len();
                self.selected_index = if self.selected_index > 0 {
                    self.selected_index - 1
                } else {
                    count.saturating_sub(1)
                };
            }
            crate::console_key::ConsoleKey::DownArrow => {
                let count = self.get_visible_entries().len();
                self.selected_index = if count != 0 && self.selected_index < count - 1 {
                    self.selected_index + 1
                } else {
                    0
                };
            }
            crate::console_key::ConsoleKey::LeftArrow => {
                if let Some(input) = &mut self.modal.search_input {
                    input.move_cursor_left();
                }
            }
            crate::console_key::ConsoleKey::RightArrow => {
                if let Some(input) = &mut self.modal.search_input {
                    input.move_cursor_right();
                }
            }
            crate::console_key::ConsoleKey::Home => {
                if let Some(input) = &mut self.modal.search_input {
                    input.move_cursor_home();
                }
            }
            crate::console_key::ConsoleKey::End => {
                if let Some(input) = &mut self.modal.search_input {
                    input.move_cursor_end();
                }
            }
            crate::console_key::ConsoleKey::Backspace => {
                if let Some(input) = &mut self.modal.search_input {
                    input.delete_backward();
                }
                self.sync_search_filter();
            }
            crate::console_key::ConsoleKey::Delete => {
                if let Some(input) = &mut self.modal.search_input {
                    input.delete_forward();
                }
                self.sync_search_filter();
            }
            _ => {
                if key.key_char >= 0x20
                    && let Some(ch) = char::from_u32(u32::from(key.key_char))
                    && let Some(input) = &mut self.modal.search_input
                {
                    input.insert_char(ch);
                    self.sync_search_filter();
                }
            }
        }
    }

    /// Shared tail of the C# search edit cases: publish the filter value,
    /// invalidate the cache, reset selection and scroll.
    fn sync_search_filter(&mut self) {
        self.search_filter = self.modal.search_input.as_ref().map(|i| i.value().to_string()).unwrap_or_default();
        self.filtered_entries = None;
        self.selected_index = 0;
        self.scroll_offset = 0;
    }

    /// Port of `BuildContextMenuItems` (App.cs:3866).
    pub fn build_context_menu_items(&mut self) -> Vec<ActionMenuItem> {
        let mut items = vec![
            ActionMenuItem::new("Open with default app", "o", AppAction::OpenExternal),
            ActionMenuItem::new("Rename", "F2", AppAction::Rename),
            ActionMenuItem::new("Delete", "Del", AppAction::Delete),
            ActionMenuItem::new("Copy", "c", AppAction::Copy),
            ActionMenuItem::new("Cut", "x", AppAction::Cut),
        ];

        if !self.clipboard_paths.is_empty() {
            items.push(ActionMenuItem::new("Paste", "v", AppAction::Paste));
        }

        items.extend([
            ActionMenuItem::new("Copy path", "y", AppAction::CopyAbsolutePath),
            ActionMenuItem::new("Properties", "i", AppAction::ShowProperties),
        ]);

        // Git stage/unstage when applicable (App.cs:3916-3927)
        let entries = self.get_visible_entries();
        let selected_path = entries.get(self.selected_index).map(|entry| entry.full_path.clone());
        let git_ctx = crate::app::git_menu_items::GitMenuContext {
            repo_root: self.current_repo_root.as_deref(),
            statuses: self.git_statuses.as_ref(),
            selected_path: selected_path.as_deref(),
            marked_paths: &self.marked_paths,
        };
        items.extend(crate::app::git_menu_items::build_git_context_menu_items(&git_ctx));

        items
    }

    /// Port of `HandleContextMenuKey` (App.cs:3934).
    pub fn handle_context_menu_key(&mut self, key: KeyEvent) {
        use crate::console_key::ConsoleKey;

        let Some(state) = &mut self.modal.context_menu else {
            self.input_mode = InputMode::Normal;
            return;
        };

        match key.key {
            ConsoleKey::Escape => {
                self.input_mode = InputMode::Normal;
                self.modal.context_menu = None;
            }
            ConsoleKey::Enter => {
                let selected = state.items[state.selected_index].clone();
                self.input_mode = InputMode::Normal;
                self.modal.context_menu = None;
                self.dispatch(selected.action);
            }
            ConsoleKey::UpArrow => state.move_up(),
            ConsoleKey::DownArrow => state.move_down(),
            _ => {
                // Vim-style navigation
                if key.key == ConsoleKey::K && !key.control && !key.alt {
                    state.move_up();
                } else if key.key == ConsoleKey::J && !key.control && !key.alt {
                    state.move_down();
                }
            }
        }
    }

    /// Port of `HandleContextMenuMouse` (App.cs:3979): any click dismisses
    /// the menu; clicks inside the item rows also execute the item.
    pub fn handle_context_menu_mouse(&mut self, mouse: crate::input::MouseEvent) {
        use crate::input::MouseButton;

        if self.modal.context_menu.is_none() {
            self.input_mode = InputMode::Normal;
            return;
        }

        if mouse.is_release || mouse.button == MouseButton::ScrollUp || mouse.button == MouseButton::ScrollDown {
            return;
        }

        let screen_width = self.layout.status_bar.width;
        let screen_height = self.layout.status_bar.top + self.layout.status_bar.height;
        let menu_rect = crate::ui::context_menu::get_menu_rect(
            screen_width,
            screen_height,
            self.modal.context_menu.as_ref().expect("checked"),
        );

        // Content area is inside the border (top border = row 0, items start
        // at row 1)
        let content_top = menu_rect.top + 1;
        let content_bottom = menu_rect.top + 1 + self.modal.context_menu.as_ref().expect("checked").items.len() as i32;

        let inside = mouse.row >= content_top
            && mouse.row < content_bottom
            && mouse.col >= menu_rect.left
            && mouse.col < menu_rect.left + menu_rect.width;

        if inside {
            let item_index = (mouse.row - content_top) as usize;
            let state = self.modal.context_menu.as_ref().expect("checked");
            if item_index < state.items.len() {
                let item = state.items[item_index].clone();
                self.input_mode = InputMode::Normal;
                self.modal.context_menu = None;
                self.dispatch(item.action);
                return;
            }
        }

        // Click outside the menu: dismiss
        self.input_mode = InputMode::Normal;
        self.modal.context_menu = None;
    }

    /// Port of `HandlePasteEvent` (App.cs:2251): paste into the active text
    /// input by mode.
    pub fn handle_paste_event(&mut self, text: &str) {
        // Filter to printable characters only
        let text: String = text.chars().filter(|c| *c >= ' ').collect();
        if text.is_empty() {
            return;
        }

        match self.input_mode {
            InputMode::TextInput => {
                if let Some(input) = &mut self.modal.active_text_input {
                    input.insert_string(&text);
                }
            }
            InputMode::Search => {
                if let Some(input) = &mut self.modal.search_input {
                    input.insert_string(&text);
                    self.sync_search_filter();
                }
            }
            InputMode::GoToPath => {
                if let Some(input) = &mut self.modal.go_to_path_input {
                    input.insert_string(&text);
                    self.modal.go_to_path_suggestion = None;
                }
            }
            InputMode::FileFinder => self.paste_into_file_finder(&text),
            _ => {}
        }
    }

    /// Port of the Rename callback (App.cs:3153-3207): blank or unchanged
    /// name returns silently; existing destination errors like C#'s catch;
    /// success re-selects the renamed entry.
    fn complete_rename(&mut self, new_name: &str) {
        let Some(target) = self.text_input_target.clone() else {
            return;
        };

        if new_name.trim().is_empty() || new_name == crate::app::dialogs::file_name_of(&target) {
            return;
        }

        let Some(parent) = crate::app::dialogs::parent_of(&target) else {
            return;
        };
        let new_path = std::path::Path::new(&parent).join(new_name).to_string_lossy().to_string();

        let result = if std::path::Path::new(&new_path).symlink_metadata().is_ok() {
            Err(std::io::Error::other("cannot rename to an existing path"))
        } else {
            crate::fs::file_operations::move_path(&target, &new_path)
        };

        match result {
            Ok(()) => {
                self.directory_contents.invalidate(&parent);
                self.invalidate_filtered_entries();
                self.refresh_git_status();
                self.show_notification(&format!("Renamed to '{new_name}'"), NotificationKind::Success);
                self.select_entry_by_name(new_name);
            }
            Err(err) => {
                self.show_notification(&format!("Rename failed: {err}"), NotificationKind::Error);
            }
        }
    }

    /// Port of the NewFile callback (App.cs:3372-3414).
    fn complete_new_file(&mut self, name: &str) {
        if name.trim().is_empty() || is_invalid_file_name(name) {
            if is_invalid_file_name(name) {
                self.show_notification("Invalid file name", NotificationKind::Error);
            }

            return;
        }

        let dest_path = std::path::Path::new(&self.current_path).join(name).to_string_lossy().to_string();
        if std::path::Path::new(&dest_path).symlink_metadata().is_ok() {
            self.show_notification(&format!("'{name}' already exists"), NotificationKind::Error);
            return;
        }

        match std::fs::File::create(&dest_path) {
            Ok(file) => {
                drop(file);
                self.directory_contents.invalidate(&self.current_path);
                self.invalidate_filtered_entries();
                self.refresh_git_status();
                self.show_notification(&format!("Created '{name}'"), NotificationKind::Success);
                self.select_entry_by_name(name);
            }
            Err(err) => {
                self.show_notification(&format!("Create failed: {err}"), NotificationKind::Error);
            }
        }
    }

    /// Port of the NewDirectory callback (App.cs:3415-3457).
    fn complete_new_directory(&mut self, name: &str) {
        if name.trim().is_empty() || is_invalid_file_name(name) {
            if is_invalid_file_name(name) {
                self.show_notification("Invalid directory name", NotificationKind::Error);
            }

            return;
        }

        let dest_path = std::path::Path::new(&self.current_path).join(name).to_string_lossy().to_string();
        if std::path::Path::new(&dest_path).symlink_metadata().is_ok() {
            self.show_notification(&format!("'{name}' already exists"), NotificationKind::Error);
            return;
        }

        match std::fs::create_dir(&dest_path) {
            Ok(()) => {
                self.directory_contents.invalidate(&self.current_path);
                self.invalidate_filtered_entries();
                self.refresh_git_status();
                self.show_notification(&format!("Created '{name}'"), NotificationKind::Success);
                self.select_entry_by_name(name);
            }
            Err(err) => {
                self.show_notification(&format!("Create failed: {err}"), NotificationKind::Error);
            }
        }
    }

    /// Port of the CreateSymlink callback (App.cs:3458-3530): creates a link
    /// to the stored target; permission errors get the C# message.
    fn complete_create_symlink(&mut self, link_name: &str) {
        let Some(target) = self.text_input_target.clone() else {
            return;
        };

        if link_name.trim().is_empty() || is_invalid_file_name(link_name) {
            if is_invalid_file_name(link_name) {
                self.show_notification("Invalid link name", NotificationKind::Error);
            }

            return;
        }

        let link_path = std::path::Path::new(&self.current_path).join(link_name).to_string_lossy().to_string();
        if std::path::Path::new(&link_path).symlink_metadata().is_ok() {
            self.show_notification(&format!("'{link_name}' already exists"), NotificationKind::Error);
            return;
        }

        #[cfg(windows)]
        let result = if std::path::Path::new(&target).is_dir() {
            std::os::windows::fs::symlink_dir(&target, &link_path)
        } else {
            std::os::windows::fs::symlink_file(&target, &link_path)
        };

        #[cfg(not(windows))]
        let result = std::os::unix::fs::symlink(&target, &link_path);

        match result {
            Ok(()) => {
                self.directory_contents.invalidate(&self.current_path);
                self.invalidate_filtered_entries();
                self.refresh_git_status();
                self.show_notification(&format!("Created symlink '{link_name}'"), NotificationKind::Success);
                self.select_entry_by_name(link_name);
            }
            Err(err) if err.kind() == std::io::ErrorKind::PermissionDenied => {
                self.show_notification("Insufficient privileges to create symlink", NotificationKind::Error);
            }
            Err(err) => {
                self.show_notification(&format!("Create symlink failed: {err}"), NotificationKind::Error);
            }
        }
    }

    /// The re-select-by-name shared by the create/rename callbacks
    /// (OrdinalIgnoreCase on Windows, matching C#).
    fn select_entry_by_name(&mut self, name: &str) {
        let entries = self.get_visible_entries();
        if let Some(index) = entries.iter().position(|entry| entry.name.eq_ignore_ascii_case(name)) {
            self.selected_index = index;
        }
    }

    /// Port of `HandleTextInputKey` (App.cs:2289).
    fn handle_text_input_key(&mut self, key: KeyEvent) {
        if key.control {
            match key.key {
                crate::console_key::ConsoleKey::LeftArrow => {
                    if let Some(input) = &mut self.modal.active_text_input {
                        input.move_cursor_word_left();
                    }
                    return;
                }
                crate::console_key::ConsoleKey::RightArrow => {
                    if let Some(input) = &mut self.modal.active_text_input {
                        input.move_cursor_word_right();
                    }
                    return;
                }
                crate::console_key::ConsoleKey::Backspace => {
                    if let Some(input) = &mut self.modal.active_text_input {
                        input.delete_word_backward();
                    }
                    return;
                }
                _ => {}
            }
        }

        match key.key {
            crate::console_key::ConsoleKey::Escape => {
                self.input_mode = InputMode::Normal;
                self.modal.active_text_input = None;
                self.modal.text_input_title = None;
                self.modal.text_input_purpose = None;
            }
            crate::console_key::ConsoleKey::Enter => {
                let value = self.modal.active_text_input.as_ref().map(|i| i.value().to_string()).unwrap_or_default();
                let purpose = self.modal.text_input_purpose.take();
                self.input_mode = InputMode::Normal;
                self.modal.active_text_input = None;
                self.modal.text_input_title = None;
                // C# clears the dialog state before invoking the completion
                // action, so the empty-message notification shows with the
                // dialog already closed (App.cs:2323-2335).
                match purpose {
                    Some(TextInputPurpose::Commit) => {
                        let trimmed = value.trim().to_string();
                        if trimmed.is_empty() {
                            self.show_notification(
                                "Commit message cannot be empty",
                                crate::ui::NotificationKind::Error,
                            );
                        } else if let Some(root) = self.current_repo_root.clone() {
                            let sender = self.pipeline.sender();
                            self.git_action_runner.start_action(
                                Box::new(move |cancel| crate::fs::git_utils::commit(&root, &trimmed, cancel)),
                                sender,
                            );
                        }
                    }
                    // File-operation consumers: Rename/CreateSymlink carry a
                    // target path in text_input_target; New* create in the
                    // current directory
                    Some(TextInputPurpose::Rename) => self.complete_rename(&value),
                    Some(TextInputPurpose::NewFile) => self.complete_new_file(&value),
                    Some(TextInputPurpose::NewDirectory) => self.complete_new_directory(&value),
                    Some(TextInputPurpose::CreateSymlink) => self.complete_create_symlink(&value),
                    None => {}
                }
            }
            crate::console_key::ConsoleKey::Backspace => {
                if let Some(input) = &mut self.modal.active_text_input {
                    input.delete_backward();
                }
            }
            crate::console_key::ConsoleKey::Delete => {
                if let Some(input) = &mut self.modal.active_text_input {
                    input.delete_forward();
                }
            }
            crate::console_key::ConsoleKey::LeftArrow => {
                if let Some(input) = &mut self.modal.active_text_input {
                    input.move_cursor_left();
                }
            }
            crate::console_key::ConsoleKey::RightArrow => {
                if let Some(input) = &mut self.modal.active_text_input {
                    input.move_cursor_right();
                }
            }
            crate::console_key::ConsoleKey::Home => {
                if let Some(input) = &mut self.modal.active_text_input {
                    input.move_cursor_home();
                }
            }
            crate::console_key::ConsoleKey::End => {
                if let Some(input) = &mut self.modal.active_text_input {
                    input.move_cursor_end();
                }
            }
            _ => {
                if key.key_char >= 0x20
                    && let Some(ch) = char::from_u32(u32::from(key.key_char))
                    && let Some(input) = &mut self.modal.active_text_input
                {
                    input.insert_char(ch);
                }
            }
        }
    }

    /// Port of `HandleConfirmKey` (App.cs:2360).
    fn handle_confirm_key(&mut self, key: KeyEvent) {
        match key.key {
            crate::console_key::ConsoleKey::Y | crate::console_key::ConsoleKey::Enter => {
                let yes_action = self.modal.confirm_yes_action.take();
                self.input_mode = InputMode::Normal;
                self.modal.confirm_title = None;
                self.modal.confirm_message = None;
                match yes_action {
                    Some(ConfirmAction::Dispatch(action)) => self.dispatch(action),
                    Some(ConfirmAction::Paste { overwrite }) => self.execute_paste(overwrite),
                    Some(ConfirmAction::DeleteFiles { targets, permanent }) => {
                        self.execute_delete(targets, permanent);
                    }
                    None => {}
                }
            }
            crate::console_key::ConsoleKey::N | crate::console_key::ConsoleKey::Escape => {
                self.input_mode = InputMode::Normal;
                self.modal.confirm_title = None;
                self.modal.confirm_message = None;
                self.modal.confirm_yes_action = None;
            }
            // All other keys are consumed but ignored
            _ => {}
        }
    }

    /// Port of `HandleGoToPathKey` (App.cs:2387), minus the ghost-suggestion
    /// completion, which is deferred to Phase 3c with PathCompletion.
    fn handle_go_to_path_key(&mut self, key: KeyEvent) {
        use crate::console_key::ConsoleKey;

        if key.control {
            match key.key {
                ConsoleKey::LeftArrow => {
                    if let Some(input) = &mut self.modal.go_to_path_input {
                        input.move_cursor_word_left();
                    }
                }
                ConsoleKey::RightArrow => {
                    if let Some(input) = &mut self.modal.go_to_path_input {
                        input.move_cursor_word_right();
                    }
                }
                ConsoleKey::Backspace => {
                    if let Some(input) = &mut self.modal.go_to_path_input {
                        input.delete_word_backward();
                    }
                    self.modal.go_to_path_suggestion = None;
                }
                _ => {}
            }

            return;
        }

        match key.key {
            ConsoleKey::Escape => {
                let has_value = self.modal.go_to_path_input.as_ref().is_some_and(|i| !i.value().is_empty());
                if has_value {
                    if let Some(input) = &mut self.modal.go_to_path_input {
                        input.clear();
                    }
                    self.modal.go_to_path_suggestion = None;
                } else {
                    self.input_mode = InputMode::Normal;
                    self.modal.go_to_path_input = None;
                    self.modal.go_to_path_suggestion = None;
                }
            }
            ConsoleKey::Enter => {
                let raw_path = self.modal.go_to_path_input.as_ref().map(|i| i.value().to_string()).unwrap_or_default();
                let path = if raw_path.chars().count() > 1 {
                    raw_path.trim_end_matches(['/', '\\']).to_string()
                } else {
                    raw_path
                };
                self.input_mode = InputMode::Normal;
                self.modal.go_to_path_input = None;
                self.modal.go_to_path_suggestion = None;
                self.navigate_to_path(&path);
            }
            ConsoleKey::Tab => {
                let accepted = self.modal.go_to_path_suggestion.clone().map(|accepted| {
                    // Completing a directory appends a separator so the
                    // next completion round descends into it
                    if std::path::Path::new(&accepted).is_dir() {
                        format!("{accepted}{}", std::path::MAIN_SEPARATOR)
                    } else {
                        accepted
                    }
                });

                if let Some(accepted) = accepted {
                    self.modal.go_to_path_input = Some(TextInput::new(&accepted));
                    self.modal.go_to_path_suggestion = self.get_path_suggestion(&accepted);
                }
            }
            ConsoleKey::Backspace => {
                if let Some(input) = &mut self.modal.go_to_path_input {
                    input.delete_backward();
                    let value = input.value().to_string();
                    self.modal.go_to_path_suggestion = self.get_path_suggestion(&value);
                }
            }
            ConsoleKey::Delete => {
                if let Some(input) = &mut self.modal.go_to_path_input {
                    input.delete_forward();
                    let value = input.value().to_string();
                    self.modal.go_to_path_suggestion = self.get_path_suggestion(&value);
                }
            }
            ConsoleKey::LeftArrow => {
                if let Some(input) = &mut self.modal.go_to_path_input {
                    input.move_cursor_left();
                }
            }
            ConsoleKey::RightArrow => {
                let accept = self.modal.go_to_path_suggestion.is_some()
                    && self
                        .modal
                        .go_to_path_input
                        .as_ref()
                        .is_some_and(|i| i.cursor_position() == i.value().chars().count());

                if accept {
                    let accepted = self
                        .modal
                        .go_to_path_suggestion
                        .clone()
                        .map(|accepted| {
                            if std::path::Path::new(&accepted).is_dir() {
                                format!("{accepted}{}", std::path::MAIN_SEPARATOR)
                            } else {
                                accepted
                            }
                        })
                        .expect("checked above");

                    self.modal.go_to_path_input = Some(TextInput::new(&accepted));
                    self.modal.go_to_path_suggestion = self.get_path_suggestion(&accepted);
                } else if let Some(input) = &mut self.modal.go_to_path_input {
                    input.move_cursor_right();
                }
            }
            ConsoleKey::Home => {
                if let Some(input) = &mut self.modal.go_to_path_input {
                    input.move_cursor_home();
                }
            }
            ConsoleKey::End => {
                if let Some(input) = &mut self.modal.go_to_path_input {
                    input.move_cursor_end();
                }
            }
            ConsoleKey::UpArrow => {
                let value = self.modal.go_to_path_input.as_ref().map(|i| i.value().to_string()).unwrap_or_default();
                if !value.is_empty() {
                    let trimmed = value.trim_end_matches(['/', '\\']).to_string();
                    if let Some(last_sep) = trimmed.rfind(['/', '\\']) {
                        let parent = &trimmed[..last_sep + 1];
                        self.modal.go_to_path_input = Some(TextInput::new(parent));
                        self.modal.go_to_path_suggestion = self.get_path_suggestion(parent);
                    }
                }
            }
            _ => {
                if key.key_char >= 0x20
                    && let Some(ch) = char::from_u32(u32::from(key.key_char))
                    && let Some(input) = &mut self.modal.go_to_path_input
                {
                    input.insert_char(ch);
                    let value = input.value().to_string();
                    self.modal.go_to_path_suggestion = self.get_path_suggestion(&value);
                }
            }
        }
    }

    /// Port of `GetPathSuggestion` (App.cs:2660).
    fn get_path_suggestion(&self, input: &str) -> Option<String> {
        crate::fs::path_completion::get_suggestion(
            input,
            self.directory_contents.show_hidden_files,
            self.directory_contents.show_system_files,
        )
    }

    /// Port of `HandleActionPaletteKey` (App.cs:3099).
    pub fn handle_action_palette_key(&mut self, key: KeyEvent) {
        if self.modal.action_menu_stack.is_empty() {
            return;
        }

        match key.key {
            crate::console_key::ConsoleKey::Escape => {
                self.modal.action_menu_stack.pop();
                if self.modal.action_menu_stack.is_empty() {
                    self.input_mode = InputMode::Normal;
                }
            }
            crate::console_key::ConsoleKey::Enter => {
                if let Some((action, data)) = self.action_palette_activate() {
                    self.dispatch_action_palette_action(action, data);
                }
            }
            _ => self.handle_action_palette_navigation_key(key),
        }

        if self.modal.action_menu_stack.is_empty() {
            return;
        }

        // Adjust scroll offset to keep selection visible
        let level = self.modal.action_menu_stack.last_mut().expect("checked above");
        let filtered_count = level.get_filtered_items().len();

        if filtered_count > 0 {
            level.selected_index = level.selected_index.min(filtered_count - 1);
        } else {
            level.selected_index = 0;
        }

        let max_visible = 18usize;
        if level.selected_index < level.scroll_offset {
            level.scroll_offset = level.selected_index;
        } else if level.selected_index >= level.scroll_offset + max_visible {
            level.scroll_offset = level.selected_index - max_visible + 1;
        }
    }

    /// Port of `DispatchActionPaletteAction`: the main dispatch, plus the
    /// item data for `SelectPreviewProvider`.
    pub fn dispatch_action_palette_action(&mut self, action: AppAction, data: i32) {
        if action == AppAction::SelectPreviewProvider {
            self.handle_select_preview_provider(data);
        } else {
            self.dispatch(action);
        }
    }

    /// The navigation and filter-editing arms of `HandleActionPaletteKey`.
    fn handle_action_palette_navigation_key(&mut self, key: KeyEvent) {
        {
            let level = self.modal.action_menu_stack.last_mut().expect("checked above");
            let filtered_count = level.get_filtered_items().len();

            match key.key {
                crate::console_key::ConsoleKey::UpArrow => {
                    if level.selected_index > 0 {
                        level.selected_index -= 1;
                    }
                }
                crate::console_key::ConsoleKey::DownArrow => {
                    if level.selected_index + 1 < filtered_count {
                        level.selected_index += 1;
                    }
                }
                crate::console_key::ConsoleKey::PageUp => {
                    let visible_count = filtered_count.min(18);
                    level.selected_index = level.selected_index.saturating_sub(visible_count);
                }
                crate::console_key::ConsoleKey::PageDown => {
                    let visible_count = filtered_count.min(18);
                    level.selected_index = (level.selected_index + visible_count).min(filtered_count.saturating_sub(1));
                }
                crate::console_key::ConsoleKey::Home => level.selected_index = 0,
                crate::console_key::ConsoleKey::End => {
                    level.selected_index = filtered_count.saturating_sub(1);
                }
                // Rust only: Ctrl+C clears the filter
                crate::console_key::ConsoleKey::C if key.control => {
                    level.filter.clear();
                    level.selected_index = 0;
                    level.scroll_offset = 0;
                }
                crate::console_key::ConsoleKey::Backspace => {
                    level.filter.delete_backward();
                    level.selected_index = 0;
                    level.scroll_offset = 0;
                }
                crate::console_key::ConsoleKey::Delete => {
                    level.filter.delete_forward();
                    level.selected_index = 0;
                    level.scroll_offset = 0;
                }
                crate::console_key::ConsoleKey::LeftArrow => level.filter.move_cursor_left(),
                crate::console_key::ConsoleKey::RightArrow => level.filter.move_cursor_right(),
                _ => {
                    if key.key == crate::console_key::ConsoleKey::K && key.control {
                        if level.selected_index > 0 {
                            level.selected_index -= 1;
                        }
                    } else if key.key == crate::console_key::ConsoleKey::J && key.control {
                        if level.selected_index + 1 < filtered_count {
                            level.selected_index += 1;
                        }
                    } else if key.key_char >= 0x20
                        && let Some(ch) = char::from_u32(u32::from(key.key_char))
                    {
                        level.filter.insert_char(ch);
                        level.selected_index = 0;
                        level.scroll_offset = 0;
                    }
                }
            }
        }
    }

    /// Port of the palette Enter handling (a separate step in C# so the
    /// dispatch happens after the selection clamp). Returns Some(action)
    /// when a leaf item was selected.
    pub fn action_palette_activate(&mut self) -> Option<(AppAction, i32)> {
        let level = self.modal.action_menu_stack.last()?;
        let filtered = level.get_filtered_items();
        let selected = filtered.get(level.selected_index)?;

        if selected.is_submenu() {
            let sub_items = selected.sub_items.clone().expect("submenu");
            let title = selected.label.clone();
            self.modal.action_menu_stack.push(ActionMenuLevel::new(&title, sub_items));
            None
        } else {
            let (action, data) = (selected.action, selected.data);
            self.modal.action_menu_stack.clear();
            self.input_mode = InputMode::Normal;
            Some((action, data))
        }
    }

    /// Port of `ShowActionPalette`, restricted to actions whose subsystems
    /// are already ported (see KNOWN_DEVIATIONS.md).
    pub fn show_action_palette(&mut self) {
        self.input_mode = InputMode::ActionPalette;
        self.modal.action_menu_stack.clear();
        let items = self.build_action_palette_items();
        self.modal.action_menu_stack.push(ActionMenuLevel::new("Action Palette", items));
    }

    pub(crate) fn build_action_palette_items(&mut self) -> Vec<ActionMenuItem> {
        // Port of BuildActionPaletteItems (App.cs:2935), C# order
        let mut items = vec![
            ActionMenuItem::new("Open with default app", "o", AppAction::OpenExternal),
            ActionMenuItem::new("Rename", "F2", AppAction::Rename),
            ActionMenuItem::new("Delete", "Del", AppAction::Delete),
            ActionMenuItem::new("Copy", "c", AppAction::Copy),
            ActionMenuItem::new("Cut", "x", AppAction::Cut),
        ];

        if !self.clipboard_paths.is_empty() {
            items.push(ActionMenuItem::new("Paste", "v", AppAction::Paste));
        }

        items.extend([
            ActionMenuItem::new("Copy absolute path", "y", AppAction::CopyAbsolutePath),
            ActionMenuItem::new("New file", "n", AppAction::NewFile),
            ActionMenuItem::new("New directory", "Shift+N", AppAction::NewDirectory),
            ActionMenuItem::new("Create symlink", "Ctrl+L", AppAction::CreateSymlink),
            ActionMenuItem::new("Properties", "i", AppAction::ShowProperties),
        ]);

        // Cloud file download, only for cloud placeholders (Windows)
        if cfg!(windows)
            && self.get_visible_entries().get(self.selected_index).is_some_and(|entry| entry.is_cloud_placeholder)
        {
            items.push(ActionMenuItem::new("Download cloud file", "", AppAction::DownloadCloudFile));
        }

        // Preview provider submenu, when multiple providers are available
        if let Some(preview_items) = self.build_preview_menu_items() {
            items.push(ActionMenuItem::submenu("Change preview", "p", preview_items));
        }

        items.extend([
            ActionMenuItem::new("Toggle hidden files", ".", AppAction::ToggleHiddenFiles),
            ActionMenuItem::new("Toggle left pane", "[", AppAction::ToggleParentPane),
            ActionMenuItem::new("Toggle right pane", "]", AppAction::TogglePreviewPane),
            ActionMenuItem::new("New tab", "t", AppAction::NewTab),
        ]);

        if self.tab_count() > 1 {
            items.extend([
                ActionMenuItem::new("Next tab", "}", AppAction::NextTab),
                ActionMenuItem::new("Previous tab", "{", AppAction::PrevTab),
                ActionMenuItem::new("Close tab", "w", AppAction::CloseTab),
            ]);
        }

        items.extend([
            ActionMenuItem::new("Cycle sort mode", "s", AppAction::CycleSortMode),
            ActionMenuItem::new("Reverse sort direction", "S", AppAction::ToggleSortDirection),
        ]);

        if self.directory_contents.sort_for(&self.current_path).2 {
            items.push(ActionMenuItem::new("Reset sort for this directory", "", AppAction::ResetDirectorySort));
        }

        items.extend([
            ActionMenuItem::new("Bookmarks", "b", AppAction::ShowBookmarks),
            ActionMenuItem::new("Toggle bookmark", "B", AppAction::ToggleBookmark),
            ActionMenuItem::new("Go to path", "Ctrl+G", AppAction::GoToPath),
            ActionMenuItem::new("Find files (Search)", "Ctrl+F", AppAction::ShowFileFinder),
            ActionMenuItem::new("Filter", "/", AppAction::Search),
            ActionMenuItem::new("Open terminal here", "Ctrl+T", AppAction::OpenTerminal),
            ActionMenuItem::new("Settings (Configuration)", ",", AppAction::ShowConfig),
            ActionMenuItem::new("Help", "?", AppAction::ShowHelp),
            ActionMenuItem::new("Refresh", "Ctrl+R", AppAction::Refresh),
        ]);

        // Git block, gated on the repo root (App.cs:2958-3018)
        let entries = self.get_visible_entries();
        let selected_path = entries.get(self.selected_index).map(|entry| entry.full_path.clone());
        let git_ctx = crate::app::git_menu_items::GitMenuContext {
            repo_root: self.current_repo_root.as_deref(),
            statuses: self.git_statuses.as_ref(),
            selected_path: selected_path.as_deref(),
            marked_paths: &self.marked_paths,
        };
        items.extend(crate::app::git_menu_items::build_git_menu_items(&git_ctx));

        items
    }

    /// Port of `ShowConfirmDialog`.
    pub fn show_confirm_dialog(&mut self, title: &str, message: &str, on_yes: ConfirmAction) {
        self.input_mode = InputMode::Confirm;
        self.modal.confirm_title = Some(title.to_string());
        self.modal.confirm_message = Some(message.to_string());
        self.modal.confirm_yes_action = Some(on_yes);
    }

    /// Port of `ShowTextInputDialog`.
    pub fn show_text_input_dialog(
        &mut self,
        title: &str,
        initial_value: &str,
        purpose: Option<TextInputPurpose>,
        target: Option<String>,
    ) {
        self.input_mode = InputMode::TextInput;
        self.modal.text_input_title = Some(title.to_string());
        self.modal.active_text_input = Some(TextInput::new(initial_value));
        self.modal.text_input_purpose = purpose;
        self.text_input_target = target;
    }

    /// Port of `NavigateToPath` (App.cs:2514).
    pub fn navigate_to_path(&mut self, path: &str) {
        if path.trim().is_empty() {
            return;
        }

        // C# Path.GetFullPath throws for an embedded NUL
        if path.contains('\0') {
            self.show_notification("Invalid path", NotificationKind::Error);
            return;
        }

        // C# Path.GetFullPath resolves relative paths against the process
        // working directory, not the pane's current path.
        let full = get_full_path(path);

        if is_directory(&full) {
            self.selected_index_per_dir.insert(self.current_path.clone(), self.selected_index);
            self.current_path = capitalize_drive_letter(&full);
            self.selected_index = 0;
            self.scroll_offset = 0;
            self.marked_paths.clear();
            self.clear_search_filter();
            self.notification = None;
            self.clear_preview_cache();
            self.refresh_git_status();
        } else if is_file(&full) {
            let parent = match parent_of(&full) {
                Some(p) => p,
                None => return,
            };
            let file_name = file_name_of(&full);
            self.selected_index_per_dir.insert(self.current_path.clone(), self.selected_index);
            self.current_path = capitalize_drive_letter(&parent);
            let entries = self.directory_contents.get_entries(&self.current_path);
            let idx = entries.iter().position(|e| e.name.eq_ignore_ascii_case(&file_name));
            self.selected_index = idx.unwrap_or(0);
            self.scroll_offset = 0;
            self.marked_paths.clear();
            self.clear_search_filter();
            self.notification = None;
            self.clear_preview_cache();
            self.refresh_git_status();
        } else {
            self.show_notification("Path not found", crate::ui::NotificationKind::Error);
        }
    }
    pub fn set_screen_size(&mut self, width: i32, height: i32) {
        self.last_width = width;
        self.last_height = height;
    }

    pub fn enter_search_mode(&mut self, filter: &str) {
        self.input_mode = InputMode::Search;
        self.search_filter = filter.to_string();
        self.modal.search_input = Some(TextInput::new(filter));
    }

    pub fn enter_go_to_path_mode(&mut self, initial: &str) {
        self.input_mode = InputMode::GoToPath;
        self.modal.go_to_path_input = Some(TextInput::new(initial));
    }

    pub fn enter_help_mode(&mut self) {
        self.input_mode = InputMode::Help;
    }

    pub fn push_palette_level(&mut self, title: &str, items: Vec<ActionMenuItem>, selected: usize) {
        let mut level = ActionMenuLevel::new(title, items);
        level.selected_index = selected;
        self.modal.action_menu_stack.push(level);
        self.input_mode = InputMode::ActionPalette;
    }

    /// Port of the modal tail of `App.Render` (App.cs:1254-1300): search bar,
    /// help overlay, then the modal dialogs, rendered last on top.
    pub fn render_modals(&mut self, buffer: &mut ScreenBuffer) {
        let width = self.last_width;
        let height = self.last_height;

        // Search bar at bottom of center pane
        let show_search_bar = self.input_mode == InputMode::Search || !self.search_filter.is_empty();
        if show_search_bar {
            self.render_search_bar(buffer);
        }

        // Help overlay
        if self.input_mode == InputMode::Help {
            help_overlay::render(buffer, width, height);
        }

        // Modal overlays (render last, on top)
        match self.input_mode {
            InputMode::Confirm => self.render_confirm_dialog(buffer, width, height),
            InputMode::TextInput => self.render_text_input_dialog(buffer, width, height),
            InputMode::GoToPath => self.render_go_to_path_dialog(buffer, width, height),
            InputMode::ActionPalette => self.render_action_palette(buffer, width, height),
            InputMode::Config => {
                if let Some(state) = &self.modal.config_state {
                    render_config_dialog(buffer, width, height, state);
                }
            }
            InputMode::Bookmarks => self.render_bookmarks(buffer, width, height),
            InputMode::FileFinder => self.render_file_finder(buffer, width, height),
            InputMode::ContextMenu => {
                if let Some(state) = &mut self.modal.context_menu {
                    crate::ui::context_menu::render(buffer, width, height, state);
                }
            }
            InputMode::FileOperation => {
                crate::ui::progress_overlay::render(
                    buffer,
                    width,
                    height,
                    &self.file_op_label,
                    self.file_op_progress.as_ref(),
                );
            }
            InputMode::Properties => {
                let entries = self.get_visible_entries();

                if self.selected_index < entries.len() {
                    let entry = &entries[self.selected_index];
                    let git_status = self
                        .git_statuses
                        .as_ref()
                        .and_then(|statuses| crate::fs::git_utils::statuses_get(statuses, &entry.full_path));
                    let dir_size_text = self.properties_dir_size_text.clone();
                    let metadata = self.properties_metadata_sections(&entry.name);

                    self.properties_content_height = crate::ui::properties_overlay::render(
                        buffer,
                        width,
                        height,
                        entry,
                        dir_size_text.as_deref(),
                        git_status,
                        metadata.as_deref(),
                        self.properties_scroll_offset,
                    );

                    // Clamp scroll offset in case content changed
                    // (App.cs:1286-1291)
                    let visible_rows = (height - 8).max(1) as usize;
                    let max_scroll = self.properties_content_height.saturating_sub(visible_rows);
                    self.properties_scroll_offset = self.properties_scroll_offset.min(max_scroll);
                }
            }
            _ => {}
        }
    }

    fn render_search_bar(&mut self, buffer: &mut ScreenBuffer) {
        let active = self.input_mode == InputMode::Search;
        let pane = self.layout.center_pane;
        let filter = self.search_filter.clone();
        let input = self.modal.search_input.as_mut();
        render_search_bar(buffer, pane, active, &filter, input);
    }

    fn render_confirm_dialog(&mut self, buffer: &mut ScreenBuffer, width: i32, height: i32) {
        let title = self.modal.confirm_title.clone();
        let message = self.modal.confirm_message.clone().unwrap_or_default();
        render_confirm_dialog(buffer, width, height, title.as_deref(), &message);
    }

    fn render_text_input_dialog(&mut self, buffer: &mut ScreenBuffer, width: i32, height: i32) {
        let title = self.modal.text_input_title.clone();
        let input = self.modal.active_text_input.as_mut();
        render_text_input_dialog(buffer, width, height, title.as_deref(), input);
    }

    /// Port of `RenderGoToPathDialog` (App.cs) / `GoToPathDialog.Render`.
    fn render_go_to_path_dialog(&mut self, buffer: &mut ScreenBuffer, width: i32, height: i32) {
        let input = self.modal.go_to_path_input.as_mut();
        let suggestion = self.modal.go_to_path_suggestion.as_deref();
        render_go_to_path_dialog(buffer, width, height, input, suggestion);
    }

    fn render_action_palette(&mut self, buffer: &mut ScreenBuffer, width: i32, height: i32) {
        let depth = self.modal.action_menu_stack.len() as i32;
        if let Some(level) = self.modal.action_menu_stack.last_mut() {
            render_action_palette(buffer, width, height, level, depth);
        }
    }

    /// Port of `ShowConfigDialog` (App.cs).
    pub fn show_config_dialog(&mut self) {
        self.input_mode = InputMode::Config;
        self.modal.config_state = Some(ConfigDialogState::from_app_config(&self.config));
    }

    /// Port of `ShowBookmarks` (App.cs:4023).
    pub fn show_bookmarks(&mut self) {
        self.input_mode = InputMode::Bookmarks;
        self.modal.bookmark_selected_index = 0;
        self.modal.bookmark_scroll_offset = 0;
        self.modal.bookmark_input = Some(TextInput::default());
    }

    /// Port of `GetFilteredBookmarks` (App.cs:4031).
    #[must_use]
    pub fn get_filtered_bookmarks(&self) -> Vec<String> {
        let filter = self.modal.bookmark_input.as_ref().map_or(String::new(), |input| input.value().to_string());

        if filter.is_empty() {
            return self.bookmark_store.bookmarks().to_vec();
        }

        self.bookmark_store
            .bookmarks()
            .iter()
            .filter(|bookmark| bookmark.to_ascii_lowercase().contains(&filter.to_ascii_lowercase()))
            .cloned()
            .collect()
    }

    /// Port of `HandleBookmarkKey` (App.cs:4053).
    pub fn handle_bookmark_key(&mut self, key: KeyEvent) {
        use crate::console_key::ConsoleKey;

        let filtered = self.get_filtered_bookmarks();

        match key.key {
            ConsoleKey::Escape => {
                self.input_mode = InputMode::Normal;
                self.modal.bookmark_input = None;
            }
            ConsoleKey::Enter => {
                if !filtered.is_empty() && self.modal.bookmark_selected_index < filtered.len() {
                    let path = filtered[self.modal.bookmark_selected_index].clone();
                    self.input_mode = InputMode::Normal;
                    self.modal.bookmark_input = None;
                    self.navigate_to_path(&path);
                }
            }
            ConsoleKey::UpArrow => {
                if self.modal.bookmark_selected_index > 0 {
                    self.modal.bookmark_selected_index -= 1;
                }
            }
            ConsoleKey::DownArrow => {
                if self.modal.bookmark_selected_index < filtered.len().saturating_sub(1) {
                    self.modal.bookmark_selected_index += 1;
                }
            }
            ConsoleKey::PageUp => {
                let visible_count = filtered.len().min(18);
                self.modal.bookmark_selected_index = self.modal.bookmark_selected_index.saturating_sub(visible_count);
            }
            ConsoleKey::PageDown => {
                let visible_count = filtered.len().min(18);
                self.modal.bookmark_selected_index =
                    (self.modal.bookmark_selected_index + visible_count).min(filtered.len().saturating_sub(1));
            }
            ConsoleKey::Home => self.modal.bookmark_selected_index = 0,
            ConsoleKey::End => {
                self.modal.bookmark_selected_index = filtered.len().saturating_sub(1);
            }
            // Rust only: Ctrl+C clears the filter
            ConsoleKey::C if key.control => {
                if let Some(input) = &mut self.modal.bookmark_input {
                    input.clear();
                }
                self.modal.bookmark_selected_index = 0;
                self.modal.bookmark_scroll_offset = 0;
            }
            ConsoleKey::Backspace => {
                if let Some(input) = &mut self.modal.bookmark_input {
                    input.delete_backward();
                }
                self.modal.bookmark_selected_index = 0;
                self.modal.bookmark_scroll_offset = 0;
            }
            ConsoleKey::Delete => {
                if !filtered.is_empty() && self.modal.bookmark_selected_index < filtered.len() {
                    let path = filtered[self.modal.bookmark_selected_index].clone();
                    self.bookmark_store.remove(&path);
                }
            }
            ConsoleKey::LeftArrow => {
                if let Some(input) = &mut self.modal.bookmark_input {
                    input.move_cursor_left();
                }
            }
            ConsoleKey::RightArrow => {
                if let Some(input) = &mut self.modal.bookmark_input {
                    input.move_cursor_right();
                }
            }
            _ => {
                if key.key == ConsoleKey::K && key.control {
                    if self.modal.bookmark_selected_index > 0 {
                        self.modal.bookmark_selected_index -= 1;
                    }
                } else if key.key == ConsoleKey::J && key.control {
                    if self.modal.bookmark_selected_index < filtered.len().saturating_sub(1) {
                        self.modal.bookmark_selected_index += 1;
                    }
                } else if let Some(ch) = char::from_u32(u32::from(key.key_char)) {
                    if ch == 'd' {
                        // 'd' also removes the selected bookmark
                        if !filtered.is_empty() && self.modal.bookmark_selected_index < filtered.len() {
                            let path = filtered[self.modal.bookmark_selected_index].clone();
                            self.bookmark_store.remove(&path);
                        }
                    } else if ch == 'B' {
                        // Toggle current directory as bookmark from within the dialog
                        let path = self.current_path.clone();
                        self.bookmark_store.toggle(&path);
                    } else if ch.is_ascii_digit() && ('1'..='9').contains(&ch) {
                        let index = (ch as u8 - b'1') as usize;
                        if index < filtered.len() {
                            let path = filtered[index].clone();
                            self.input_mode = InputMode::Normal;
                            self.modal.bookmark_input = None;
                            self.navigate_to_path(&path);
                            return;
                        }
                    } else if ch >= ' ' {
                        if let Some(input) = &mut self.modal.bookmark_input {
                            input.insert_char(ch);
                        }
                        self.modal.bookmark_selected_index = 0;
                        self.modal.bookmark_scroll_offset = 0;
                    }
                }
            }
        }

        // Adjust scroll offset to keep the selection visible
        let filtered = self.get_filtered_bookmarks();

        if !filtered.is_empty() {
            self.modal.bookmark_selected_index = self.modal.bookmark_selected_index.min(filtered.len() - 1);
        } else {
            self.modal.bookmark_selected_index = 0;
        }

        const MAX_VISIBLE: usize = 18;

        if self.modal.bookmark_selected_index < self.modal.bookmark_scroll_offset {
            self.modal.bookmark_scroll_offset = self.modal.bookmark_selected_index;
        } else if self.modal.bookmark_selected_index >= self.modal.bookmark_scroll_offset + MAX_VISIBLE {
            self.modal.bookmark_scroll_offset = self.modal.bookmark_selected_index - MAX_VISIBLE + 1;
        }
    }

    /// Port of `RenderBookmarks` (App.cs).
    pub fn render_bookmarks(&mut self, buffer: &mut ScreenBuffer, width: i32, height: i32) {
        let filtered = self.get_filtered_bookmarks();
        let selected = self.modal.bookmark_selected_index;
        let scroll = self.modal.bookmark_scroll_offset;
        let input = self.modal.bookmark_input.as_mut();
        render_bookmarks(buffer, width, height, &filtered, selected, scroll, input);
    }

    /// Port of `HandleConfigKey` (App.cs).
    pub fn handle_config_key(&mut self, key: KeyEvent) {
        use crate::console_key::ConsoleKey;

        let Some(state) = &mut self.modal.config_state else {
            self.input_mode = InputMode::Normal;
            return;
        };

        match key.key {
            ConsoleKey::UpArrow | ConsoleKey::K => state.move_up(),
            ConsoleKey::DownArrow | ConsoleKey::J => state.move_down(),
            ConsoleKey::Spacebar => state.toggle_selected(),
            ConsoleKey::Enter => self.apply_config_changes(),
            ConsoleKey::Escape => self.input_mode = InputMode::Normal,
            ConsoleKey::LeftArrow | ConsoleKey::H => state.cycle_prev_selected(),
            ConsoleKey::RightArrow | ConsoleKey::L => state.cycle_next_selected(),
            _ => {}
        }
    }

    /// Port of `ApplyConfigChanges` (App.cs:4196).
    pub(crate) fn apply_config_changes(&mut self) {
        let Some(mut state) = self.modal.config_state.take() else {
            return;
        };

        state.apply_to(&mut self.config);

        self.directory_contents.show_hidden_files = self.config.show_hidden_files;
        self.directory_contents.show_system_files = self.config.show_system_files;
        self.directory_contents.sort_mode = self.config.sort_mode;
        self.directory_contents.sort_ascending = self.config.sort_ascending;
        self.update_image_protocol();

        self.parent_pane_enabled = self.config.parent_pane_enabled;
        self.preview_pane_enabled = self.config.preview_pane_enabled;

        self.directory_contents.invalidate_all();
        self.clear_preview_cache();
        self.layout
            .calculate(self.last_width, self.last_height, self.preview_pane_enabled, self.parent_pane_enabled);
        self.update_terminal_title();
        self.refresh_git_status();

        match crate::app::config_io::save_config(&self.config) {
            Ok(()) => self.show_notification("Configuration saved", NotificationKind::Success),
            Err(err) => {
                self.show_notification(&format!("Save failed: {err}"), NotificationKind::Error);
            }
        }

        self.input_mode = InputMode::Normal;
    }
}

/// Port of `RenderBookmarks` / `BookmarksDialog.Render` (App.cs).
pub fn render_bookmarks(
    buffer: &mut ScreenBuffer,
    width: i32,
    height: i32,
    filtered: &[String],
    selected_index: usize,
    scroll_offset: usize,
    mut input: Option<&mut TextInput>,
) {
    let content_width = 70.min(width - 8);
    let item_rows = filtered.len().min(18) as i32;
    let content_height = item_rows + 2; // 1 row for text input + 1 separator + item rows
    const FOOTER: &str = "[\u{2191}\u{2193}] Navigate [Enter] Open [d] Remove [1-9] Jump  [B] Add/Remove  [Esc] Close";

    let content = dialog_box::render(
        buffer,
        width,
        height,
        content_width.max(FOOTER.chars().count() as i32),
        content_height.max(3),
        Some("Bookmarks"),
        Some(FOOTER),
    );

    // Row 0: text input with "> " prefix
    let prefix_style = CellStyle {
        fg: Some(Color { r: 220, g: 220, b: 100 }),
        bg: Some(BG_COLOR),
        ..CellStyle::default()
    };
    let input_style = CellStyle {
        fg: Some(Color { r: 200, g: 200, b: 200 }),
        bg: Some(BG_COLOR),
        ..CellStyle::default()
    };
    buffer.write_string(content.top, content.left, "> ", prefix_style, i64::from(i32::MAX));
    if let Some(input) = &mut input {
        input.render(buffer, content.top, content.left + 2, content.width - 2, input_style);
    }

    // Row 1: separator
    let separator_style = CellStyle {
        fg: Some(dialog_box::BORDER_COLOR),
        bg: Some(BG_COLOR),
        dim: true,
        ..CellStyle::default()
    };
    for c in 0..content.width {
        buffer.put(content.top + 1, content.left + c, '\u{2500}', separator_style);
    }

    if filtered.is_empty() {
        let empty_style = CellStyle {
            fg: Some(Color { r: 120, g: 120, b: 140 }),
            bg: Some(BG_COLOR),
            ..CellStyle::default()
        };
        buffer.write_string(content.top + 2, content.left + 1, "No bookmarks", empty_style, i64::from(i32::MAX));
        return;
    }

    // Rows 2+: bookmark items
    let normal_style = CellStyle {
        fg: Some(Color { r: 200, g: 200, b: 200 }),
        bg: Some(BG_COLOR),
        ..CellStyle::default()
    };
    let selected_style = CellStyle {
        fg: Some(Color { r: 20, g: 20, b: 35 }),
        bg: Some(Color { r: 200, g: 200, b: 200 }),
        ..CellStyle::default()
    };
    let number_style = CellStyle {
        fg: Some(Color { r: 220, g: 220, b: 100 }),
        bg: Some(BG_COLOR),
        ..CellStyle::default()
    };
    let number_selected_style = CellStyle {
        fg: Some(Color { r: 20, g: 20, b: 35 }),
        bg: Some(Color { r: 200, g: 200, b: 200 }),
        ..CellStyle::default()
    };
    let dim_style = CellStyle {
        fg: Some(Color { r: 120, g: 120, b: 140 }),
        bg: Some(BG_COLOR),
        ..CellStyle::default()
    };
    let dim_selected_style = CellStyle {
        fg: Some(Color { r: 80, g: 80, b: 100 }),
        bg: Some(Color { r: 200, g: 200, b: 200 }),
        ..CellStyle::default()
    };

    let visible_count = content.height - 2;

    for i in 0..visible_count {
        let item_index = scroll_offset + i as usize;

        if item_index >= filtered.len() {
            break;
        }

        let path = &filtered[item_index];
        let is_selected = item_index == selected_index;
        let exists = std::path::Path::new(path).is_dir() || std::path::Path::new(path).is_file();
        let row = content.top + 2 + i;

        let label_style = if is_selected {
            if exists { selected_style } else { dim_selected_style }
        } else if exists {
            normal_style
        } else {
            dim_style
        };

        let num_style = if is_selected { number_selected_style } else { number_style };

        if is_selected {
            buffer.fill_row(row, content.left, content.width, ' ', selected_style);
        }

        // Number prefix [1]-[9] for the first 9 items
        let mut col = content.left + 1;

        if item_index < 9 {
            let num = format!("[{}] ", item_index + 1);
            buffer.write_string(row, col, &num, num_style, i64::from(i32::MAX));
            col += 4;
        } else {
            col += 4; // align with numbered items
        }

        buffer.write_string(row, col, path, label_style, i64::from(content.width - (col - content.left) - 1));
    }
}

/// Port of `RenderConfigDialog` (App.cs).
pub fn render_config_dialog(buffer: &mut ScreenBuffer, width: i32, height: i32, state: &ConfigDialogState) {
    const CONTENT_WIDTH: i32 = 47;
    let content_height = state.items.len() as i32 + 1;
    const FOOTER: &str = "[Space] Toggle [\u{25c4}\u{25ba}] Cycle [Enter] Save [Esc] Cancel";

    let content = dialog_box::render(
        buffer,
        width,
        height,
        CONTENT_WIDTH.max(FOOTER.chars().count() as i32),
        content_height,
        Some("Configuration"),
        Some(FOOTER),
    );

    let normal_style = CellStyle {
        fg: Some(Color { r: 200, g: 200, b: 200 }),
        bg: Some(BG_COLOR),
        ..CellStyle::default()
    };
    let selected_style = CellStyle {
        fg: Some(Color { r: 20, g: 20, b: 35 }),
        bg: Some(Color { r: 200, g: 200, b: 200 }),
        ..CellStyle::default()
    };
    let value_style = CellStyle {
        fg: Some(Color { r: 100, g: 200, b: 255 }),
        bg: Some(BG_COLOR),
        ..CellStyle::default()
    };
    let value_selected_style = CellStyle {
        fg: Some(Color { r: 20, g: 20, b: 35 }),
        bg: Some(Color { r: 200, g: 200, b: 200 }),
        ..CellStyle::default()
    };
    let disabled_style = CellStyle {
        fg: Some(Color { r: 80, g: 80, b: 80 }),
        bg: Some(BG_COLOR),
        ..CellStyle::default()
    };

    for i in 0..state.items.len() {
        let selected = i == state.selected_index;
        let (style, v_style) = if !state.is_enabled(i) {
            (disabled_style, disabled_style)
        } else if selected {
            (selected_style, value_selected_style)
        } else {
            (normal_style, value_style)
        };

        let row = content.top + i as i32;
        let item = &state.items[i];
        let label = if item.indent > 0 {
            format!("{}{}", "  ".repeat(item.indent), item.label)
        } else {
            item.label.to_string()
        };

        const LABEL_WIDTH: i32 = 34;
        buffer.write_string(row, content.left, &label, style, i64::from(LABEL_WIDTH));
        let value = state.format_value(i);
        buffer.write_string(row, content.left + LABEL_WIDTH, &value, v_style, i64::from(content.width - LABEL_WIDTH));
    }
}

/// Port of `RenderSearchBar` / `SearchBar.Render` (App.cs:4558).
pub fn render_search_bar(
    buffer: &mut ScreenBuffer,
    center_pane: Rect,
    active: bool,
    filter: &str,
    input: Option<&mut TextInput>,
) {
    let row = center_pane.top + center_pane.height - 1;
    let col = center_pane.left;
    let width = center_pane.width;

    let label_style = CellStyle {
        fg: Some(Color { r: 220, g: 220, b: 100 }),
        bg: None,
        ..CellStyle::default()
    };
    buffer.put(row, col, '/', label_style);

    let input_col = col + 1;
    let input_width = width - 1;

    if active && let Some(input) = input {
        let input_style = CellStyle {
            fg: Some(Color { r: 200, g: 200, b: 200 }),
            bg: None,
            ..CellStyle::default()
        };
        input.render(buffer, row, input_col, input_width, input_style);
    } else {
        let text_style = CellStyle {
            fg: Some(Color { r: 200, g: 200, b: 200 }),
            bg: None,
            ..CellStyle::default()
        };
        buffer.write_string(row, input_col, filter, text_style, i64::from(input_width));
    }
}

/// Port of `RenderConfirmDialog` / `ConfirmDialog.Render` (App.cs:2888).
pub fn render_confirm_dialog(buffer: &mut ScreenBuffer, width: i32, height: i32, title: Option<&str>, message: &str) {
    let lines: Vec<&str> = message.split('\n').collect();
    let footer = "[Y/Enter] Yes  [N/Esc] No";
    let max_line_len = lines.iter().map(|l| l.chars().count()).max().unwrap_or(0) as i32;

    let content_width = max_line_len.max(footer.chars().count() as i32) + 2;
    let content_height = i32::try_from(lines.len()).unwrap_or(0);

    let content = dialog_box::render(buffer, width, height, content_width, content_height, title, Some(footer));

    let text_style = CellStyle {
        fg: Some(Color { r: 200, g: 200, b: 200 }),
        bg: Some(BG_COLOR),
        ..CellStyle::default()
    };
    let warn_style = CellStyle {
        fg: Some(Color { r: 255, g: 100, b: 100 }),
        bg: Some(BG_COLOR),
        ..CellStyle::default()
    };

    for (i, line) in lines.iter().enumerate() {
        let msg_col = content.left + (content.width - line.chars().count() as i32) / 2;
        let style = if i > 0 { warn_style } else { text_style };
        buffer.write_string(content.top + i as i32, msg_col, line, style, i64::from(i32::MAX));
    }
}

/// Port of `RenderTextInputDialog` / `TextInputDialog.Render` (App.cs:2919).
pub fn render_text_input_dialog(
    buffer: &mut ScreenBuffer,
    width: i32,
    height: i32,
    title: Option<&str>,
    input: Option<&mut TextInput>,
) {
    let content_width = 40.min(width - 8);
    let content_height = 1; // single row for text input
    let footer = "[Enter] Confirm  [Esc] Cancel";

    let content = dialog_box::render(buffer, width, height, content_width, content_height, title, Some(footer));

    let input_style = CellStyle {
        fg: Some(Color { r: 200, g: 200, b: 200 }),
        bg: Some(BG_COLOR),
        ..CellStyle::default()
    };
    if let Some(input) = input {
        input.render(buffer, content.top, content.left, content.width, input_style);
    }
}

/// Port of `RenderGoToPathDialog` / `GoToPathDialog.Render` (App.cs:2663):
/// single-row path input with the inline ghost suggestion suffix.
pub fn render_go_to_path_dialog(
    buffer: &mut ScreenBuffer,
    width: i32,
    height: i32,
    input: Option<&mut TextInput>,
    suggestion: Option<&str>,
) {
    let content_width = 60.min(width - 8);
    let content_height = 1;
    let footer = "[Tab] Complete  [\u{2191}] Up dir  [Esc] Clear/Close  [Enter] Go";

    let content =
        dialog_box::render(buffer, width, height, content_width, content_height, Some("Go to path"), Some(footer));

    let Some(input) = input else {
        return;
    };

    let input_style = CellStyle {
        fg: Some(Color { r: 200, g: 200, b: 200 }),
        bg: Some(BG_COLOR),
        ..CellStyle::default()
    };
    let value = input.value().to_string();
    let cursor_at_end = input.cursor_position() == value.chars().count();
    input.render(buffer, content.top, content.left, content.width, input_style);

    // Inline ghost suffix: show the untyped remainder of the suggestion after
    // the cursor
    let Some(suggestion) = suggestion else {
        return;
    };

    let expanded_input =
        crate::fs::path_completion::normalize_separators(&crate::fs::path_completion::expand_tilde(&value));

    // Only show the ghost when the cursor is at the end and the suggestion
    // extends beyond the expanded input
    if !cursor_at_end
        || suggestion.chars().count() <= expanded_input.chars().count()
        || !suggestion.to_ascii_lowercase().starts_with(&expanded_input.to_ascii_lowercase())
    {
        return;
    }

    let scroll_offset = input.scroll_offset();
    let visual_text_end = value.chars().count() - scroll_offset + 1; // +1 for cursor space
    let ghost_col = content.left + visual_text_end as i32;
    let ghost_max_width = content.width - visual_text_end as i32;

    if ghost_max_width > 0 {
        let ghost: String = suggestion.chars().skip(expanded_input.chars().count()).collect();
        let ghost_style = CellStyle {
            fg: Some(Color { r: 90, g: 90, b: 110 }),
            bg: Some(BG_COLOR),
            ..CellStyle::default()
        };
        buffer.write_string(content.top, ghost_col, &ghost, ghost_style, i64::from(ghost_max_width));
    }
}

/// Port of `RenderActionPalette` / `ActionPaletteDialog.Render` (App.cs:3948).
pub fn render_action_palette(
    buffer: &mut ScreenBuffer,
    width: i32,
    height: i32,
    level: &mut ActionMenuLevel,
    stack_depth: i32,
) {
    let filtered: Vec<(String, String, bool)> = level
        .get_filtered_items()
        .into_iter()
        .map(|item| (item.label.clone(), item.shortcut.clone(), item.is_submenu()))
        .collect();

    let content_width = 60.min(width - 8);
    let item_rows = filtered.len().min(18) as i32;
    let content_height = item_rows + 2; // 1 row for text input + 1 separator + item rows
    let footer = if stack_depth > 1 {
        "[↑↓] Navigate  [Enter] Select  [Esc] Back"
    } else {
        "[↑↓] Navigate  [Enter] Select  [Esc] Cancel"
    };

    let content = dialog_box::render(
        buffer,
        width,
        height,
        content_width.max(footer.chars().count() as i32),
        content_height,
        Some(level.title.as_str()),
        Some(footer),
    );

    // Row 0: text input with "> " prefix
    let prefix_style = CellStyle {
        fg: Some(Color { r: 220, g: 220, b: 100 }),
        bg: Some(BG_COLOR),
        ..CellStyle::default()
    };
    let input_style = CellStyle {
        fg: Some(Color { r: 200, g: 200, b: 200 }),
        bg: Some(BG_COLOR),
        ..CellStyle::default()
    };
    buffer.write_string(content.top, content.left, "> ", prefix_style, i64::from(i32::MAX));
    level.filter.render(buffer, content.top, content.left + 2, content.width - 2, input_style);

    // Row 1: separator
    let separator_style = CellStyle {
        fg: Some(dialog_box::BORDER_COLOR),
        bg: Some(BG_COLOR),
        dim: true,
        ..CellStyle::default()
    };
    for c in 0..content.width {
        buffer.put(content.top + 1, content.left + c, '─', separator_style);
    }

    // Rows 2+: filtered items
    let normal_style = CellStyle {
        fg: Some(Color { r: 200, g: 200, b: 200 }),
        bg: Some(BG_COLOR),
        ..CellStyle::default()
    };
    let selected_style = CellStyle {
        fg: Some(Color { r: 20, g: 20, b: 35 }),
        bg: Some(Color { r: 200, g: 200, b: 200 }),
        ..CellStyle::default()
    };
    let shortcut_style = CellStyle {
        fg: Some(Color { r: 120, g: 120, b: 140 }),
        bg: Some(BG_COLOR),
        ..CellStyle::default()
    };
    let shortcut_selected_style = CellStyle {
        fg: Some(Color { r: 20, g: 20, b: 35 }),
        bg: Some(Color { r: 200, g: 200, b: 200 }),
        ..CellStyle::default()
    };
    let submenu_indicator_style = CellStyle {
        fg: Some(Color { r: 120, g: 120, b: 140 }),
        bg: Some(BG_COLOR),
        ..CellStyle::default()
    };
    let submenu_indicator_selected_style = CellStyle {
        fg: Some(Color { r: 20, g: 20, b: 35 }),
        bg: Some(Color { r: 200, g: 200, b: 200 }),
        ..CellStyle::default()
    };

    let visible_count = content.height - 2;

    for i in 0..visible_count {
        let item_index = level.scroll_offset + i as usize;
        if item_index >= filtered.len() {
            break;
        }

        let (label, shortcut, is_submenu) = &filtered[item_index];
        let is_selected = item_index == level.selected_index;
        let row = content.top + 2 + i;

        let label_style = if is_selected { selected_style } else { normal_style };

        // Fill entire row with selected background if selected
        if is_selected {
            buffer.fill_row(row, content.left, content.width, ' ', selected_style);
        }

        if *is_submenu {
            // Submenu items show a "▸" indicator on the right
            buffer.write_string(row, content.left + 1, label, label_style, i64::from(content.width - 4));
            let ind_style = if is_selected {
                submenu_indicator_selected_style
            } else {
                submenu_indicator_style
            };
            buffer.write_string(row, content.left + content.width - 2, "▸", ind_style, i64::from(i32::MAX));
        } else {
            let sc_style = if is_selected { shortcut_selected_style } else { shortcut_style };
            buffer.write_string(
                row,
                content.left + 1,
                label,
                label_style,
                i64::from(content.width - shortcut.chars().count() as i32 - 3),
            );
            let shortcut_col = content.left + content.width - shortcut.chars().count() as i32 - 1;
            buffer.write_string(row, shortcut_col, shortcut, sc_style, i64::from(i32::MAX));
        }
    }
}

fn is_directory(path: &str) -> bool {
    std::fs::metadata(path).map(|m| m.is_dir()).unwrap_or(false)
}

fn is_file(path: &str) -> bool {
    std::fs::metadata(path).map(|m| m.is_file()).unwrap_or(false)
}

fn join_absolute(base: &str, relative: &str) -> String {
    let sep = if cfg!(windows) { '\\' } else { '/' };
    if base.ends_with(sep) {
        format!("{base}{relative}")
    } else {
        format!("{base}{sep}{relative}")
    }
}

/// Minimal stand-in for `Path.GetFullPath`: expand tilde, normalize
/// separators, absolutize against the process working directory, and
/// collapse `.`/`..` segments lexically.
#[must_use]
pub fn get_full_path(path: &str) -> String {
    let expanded = crate::fs::path_completion::expand_tilde(&crate::fs::path_completion::normalize_separators(path));
    if is_absolute_path(&expanded) {
        return collapse_dots(&expanded);
    }

    let base = std::env::current_dir().map(|p| p.to_string_lossy().to_string()).unwrap_or_else(|_| ".".to_string());
    collapse_dots(&join_absolute(&base, &expanded))
}

/// True when the path is rooted (drive letter or leading separator).
#[must_use]
pub fn is_absolute_path(path: &str) -> bool {
    if path.starts_with('/') || path.starts_with('\\') {
        return true;
    }

    // Drive letter (Windows): "C:\..." or "C:/..."
    let bytes = path.as_bytes();
    bytes.len() >= 3 && bytes[0].is_ascii_alphabetic() && bytes[1] == b':' && (bytes[2] == b'\\' || bytes[2] == b'/')
}

/// Collapse `.` and `..` segments lexically (rough `Path.GetFullPath`).
#[must_use]
pub fn collapse_dots(path: &str) -> String {
    // Preserve the "C:" prefix when present, and a leading separator on
    // unix (C# Path.GetFullPath keeps "/foo" rooted)
    let (prefix, rest) = if path.len() >= 2 && path.as_bytes()[1] == b':' {
        (&path[..2], &path[2..])
    } else if !cfg!(windows) && path.starts_with('/') {
        ("/", &path[1..])
    } else {
        ("", path)
    };

    let sep = if cfg!(windows) { '\\' } else { '/' };
    let mut parts: Vec<&str> = Vec::new();
    for segment in rest.split(sep) {
        if segment.is_empty() || segment == "." {
            continue;
        }

        if segment == ".." {
            parts.pop();
            continue;
        }

        parts.push(segment);
    }

    let mut result = prefix.to_string();
    for part in parts {
        if !result.ends_with(sep) && !result.is_empty() {
            result.push(sep);
        }

        result.push_str(part);
    }

    if result.is_empty() {
        result.push(sep);
    }

    result
}

#[cfg(test)]
mod collapse_dots_tests {
    use super::collapse_dots;

    #[test]
    fn preserves_root() {
        // Windows drive prefix
        #[cfg(windows)]
        {
            assert_eq!(collapse_dots(r"C:\foo\..\bar"), r"C:\bar");
            // Bare drive prefix collapses to "C:" (pre-existing behavior;
            // GetFullPath would return "C:\" but nothing feeds it that)
        }
        // Unix leading separator
        #[cfg(not(windows))]
        {
            assert_eq!(collapse_dots("/foo/../bar"), "/bar");
            assert_eq!(collapse_dots("/"), "/");
        }
    }
}

/// Parent directory without a trailing separator; None at a root.
#[must_use]
pub fn parent_of(path: &str) -> Option<String> {
    let trimmed = path.trim_end_matches(['/', '\\']);
    if let Some(idx) = trimmed.rfind(['/', '\\']) {
        if idx == 0 {
            return Some(trimmed[..1].to_string());
        }

        // Keep "C:\" prefix intact
        if idx == 2 && trimmed.as_bytes()[1] == b':' {
            return Some(trimmed[..3].to_string());
        }

        return Some(trimmed[..idx].to_string());
    }

    None
}

/// Final path segment.
#[must_use]
pub fn file_name_of(path: &str) -> String {
    let trimmed = path.trim_end_matches(['/', '\\']);
    match trimmed.rfind(['/', '\\']) {
        Some(idx) => trimmed[idx + 1..].to_string(),
        None => trimmed.to_string(),
    }
}

#[cfg(test)]
mod tests {
    use super::*;
    use crate::app::AppConfig;
    use crate::app::input_reader::AppAction;

    #[test]
    fn text_input_editing_basics() {
        let mut input = TextInput::new("abc");
        assert_eq!(input.value(), "abc");
        assert_eq!(input.cursor_position(), 3);

        input.delete_backward();
        assert_eq!(input.value(), "ab");
        input.move_cursor_left();
        input.insert_char('x');
        assert_eq!(input.value(), "axb");
        input.delete_forward();
        assert_eq!(input.value(), "ax");
        input.move_cursor_home();
        input.delete_backward();
        assert_eq!(input.value(), "ax");
        input.move_cursor_end();
        input.clear();
        assert_eq!(input.value(), "");
        assert_eq!(input.cursor_position(), 0);
    }

    #[test]
    fn text_input_word_motion() {
        let mut input = TextInput::new("foo.bar baz");
        input.move_cursor_end();
        input.move_cursor_word_left();
        assert_eq!(input.cursor_position(), 8);
        input.move_cursor_word_left();
        assert_eq!(input.cursor_position(), 4);
        input.move_cursor_word_right();
        assert_eq!(input.cursor_position(), 8);
        input.move_cursor_word_right();
        assert_eq!(input.cursor_position(), 11);

        input.delete_word_backward();
        assert_eq!(input.value(), "foo.bar ");
    }

    #[test]
    fn palette_filter_is_case_insensitive_substring() {
        let level = ActionMenuLevel::new(
            "Action Palette",
            vec![
                ActionMenuItem::new("Toggle hidden files", ".", AppAction::ToggleHiddenFiles),
                ActionMenuItem::new("Go to path", "Ctrl+G", AppAction::GoToPath),
            ],
        );

        assert_eq!(level.get_filtered_items().len(), 2);

        let mut filtered = ActionMenuLevel::new("t", level.items);
        filtered.filter.insert_string("HIDDEN");
        assert_eq!(filtered.get_filtered_items().len(), 1);
        assert_eq!(filtered.get_filtered_items()[0].label, "Toggle hidden files");

        filtered.filter.clear();
        filtered.filter.insert_string("zzz");
        assert!(filtered.get_filtered_items().is_empty());
    }

    #[test]
    fn palette_activate_pushes_submenu_and_dispatches_leaf() {
        let mut app = App::new(AppConfig::default());
        app.push_palette_level(
            "Root",
            vec![
                ActionMenuItem::new("Refresh", "Ctrl+R", AppAction::Refresh),
                ActionMenuItem::submenu("Change preview", "p", vec![ActionMenuItem::new("Text", "", AppAction::None)]),
            ],
            1,
        );

        // Selecting the submenu pushes a level instead of dispatching
        app.action_palette_activate();
        assert_eq!(app.modal.action_menu_stack.len(), 2);
        assert_eq!(app.modal.action_menu_stack.last().expect("level").title, "Change preview");
        assert_eq!(app.input_mode, InputMode::ActionPalette);

        // Selecting a leaf clears the stack and returns the action
        app.action_palette_activate();
        assert!(app.modal.action_menu_stack.is_empty());
        assert_eq!(app.input_mode, InputMode::Normal);
    }

    #[test]
    fn confirm_dialog_dispatches_yes_action() {
        let mut app = App::new(AppConfig::default());
        app.show_confirm_dialog("Delete", "Sure?", ConfirmAction::Dispatch(AppAction::Refresh));
        assert_eq!(app.input_mode, InputMode::Confirm);

        app.handle_confirm_key(KeyEvent {
            key: crate::console_key::ConsoleKey::Escape,
            key_char: 0,
            shift: false,
            alt: false,
            control: false,
        });
        assert_eq!(app.input_mode, InputMode::Normal);
        assert!(app.modal.confirm_yes_action.is_none());
    }

    #[test]
    fn path_helpers_round_trip() {
        assert_eq!(file_name_of("C:/foo/bar/"), "bar");
        assert_eq!(parent_of("/foo/bar"), Some("/foo".to_string()));
        assert_eq!(parent_of("C:/foo"), Some("C:/".to_string()));
    }

    #[cfg(windows)]
    #[test]
    fn path_helpers_windows_semantics() {
        assert!(is_absolute_path(r"C:\foo"));
        assert!(is_absolute_path("C:/foo"));
        assert!(!is_absolute_path(r"foo\bar"));
        assert_eq!(parent_of(r"C:\foo\bar"), Some(r"C:\foo".to_string()));
        assert_eq!(parent_of(r"C:\foo"), Some(r"C:\".to_string()));
        assert_eq!(file_name_of(r"C:\foo\bar\"), "bar");
        assert_eq!(collapse_dots(r"C:\foo\..\bar"), r"C:\bar");
        assert_eq!(crate::fs::path_completion::normalize_separators("a/b"), r"a\b");
    }
}
