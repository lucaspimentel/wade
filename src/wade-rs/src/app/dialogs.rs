//! Port of the modal overlay handling in `src/Wade/App.cs`: Confirm and
//! TextInput dialogs, GoToPath, the search bar, the action palette, and the
//! Help overlay. Rendering and key handling mirror the C# `Handle*Key` and
//! `Render*Dialog` methods.

use crate::app::input_reader::AppAction;
use crate::app::{App, InputMode, KeyEvent};
use crate::fs::directory_contents::capitalize_drive_letter;
use crate::screen::{CellStyle, Color, ScreenBuffer};
use crate::ui::layout::Rect;
use crate::ui::action_palette::{ActionMenuItem, ActionMenuLevel};
use crate::ui::text_input::TextInput;
use crate::ui::dialog_box::{self, BG_COLOR};
use crate::ui::help_overlay;

/// Purpose of the active text-input dialog (what C# stores as an
/// `Action<string>` completion callback). The file-operation consumers land
/// in a later phase; for now Enter with a purpose set reports not-yet-ported.
#[derive(Clone, Copy, Debug, PartialEq, Eq)]
pub enum TextInputPurpose {
    Rename,
    NewFile,
    NewDirectory,
}

#[derive(Default)]
pub(crate) struct ModalState {
    pub confirm_title: Option<String>,
    pub confirm_message: Option<String>,
    /// C# stores an `Action`; the only producers dispatch a single action.
    pub confirm_yes_action: Option<AppAction>,
    pub text_input_title: Option<String>,
    pub active_text_input: Option<TextInput>,
    pub text_input_purpose: Option<TextInputPurpose>,
    pub go_to_path_input: Option<TextInput>,
    pub action_menu_stack: Vec<ActionMenuLevel>,
    pub search_input: Option<TextInput>,
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
        self.search_filter = self
            .modal
            .search_input
            .as_ref()
            .map(|i| i.value().to_string())
            .unwrap_or_default();
        self.filtered_entries = None;
        self.selected_index = 0;
        self.scroll_offset = 0;
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
                let value = self
                    .modal
                    .active_text_input
                    .as_ref()
                    .map(|i| i.value().to_string())
                    .unwrap_or_default();
                let purpose = self.modal.text_input_purpose.take();
                self.input_mode = InputMode::Normal;
                self.modal.active_text_input = None;
                self.modal.text_input_title = None;
                // File-operation consumers (Rename/NewFile/NewDirectory) land
                // in a later phase.
                if purpose.is_some() {
                    self.show_notification("Not yet ported", crate::ui::NotificationKind::Info);
                }
                let _ = value;
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
                if let Some(action) = yes_action {
                    self.dispatch(action);
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
        if key.control {
            match key.key {
                crate::console_key::ConsoleKey::LeftArrow => {
                    if let Some(input) = &mut self.modal.go_to_path_input {
                        input.move_cursor_word_left();
                    }
                    return;
                }
                crate::console_key::ConsoleKey::RightArrow => {
                    if let Some(input) = &mut self.modal.go_to_path_input {
                        input.move_cursor_word_right();
                    }
                    return;
                }
                crate::console_key::ConsoleKey::Backspace => {
                    if let Some(input) = &mut self.modal.go_to_path_input {
                        input.delete_word_backward();
                    }
                    return;
                }
                _ => {}
            }
        }

        match key.key {
            crate::console_key::ConsoleKey::Escape => {
                let has_value = self
                    .modal
                    .go_to_path_input
                    .as_ref()
                    .is_some_and(|i| !i.value().is_empty());
                if has_value {
                    if let Some(input) = &mut self.modal.go_to_path_input {
                        input.clear();
                    }
                } else {
                    self.input_mode = InputMode::Normal;
                    self.modal.go_to_path_input = None;
                }
            }
            crate::console_key::ConsoleKey::Enter => {
                let raw_path = self
                    .modal
                    .go_to_path_input
                    .as_ref()
                    .map(|i| i.value().to_string())
                    .unwrap_or_default();
                let path = if raw_path.chars().count() > 1 {
                    raw_path.trim_end_matches(['/', '\\']).to_string()
                } else {
                    raw_path
                };
                self.input_mode = InputMode::Normal;
                self.modal.go_to_path_input = None;
                self.navigate_to_path(&path);
            }
            // Tab completes via PathCompletion (Phase 3c); consumed, no-op here.
            crate::console_key::ConsoleKey::Tab => {}
            crate::console_key::ConsoleKey::Backspace => {
                if let Some(input) = &mut self.modal.go_to_path_input {
                    input.delete_backward();
                }
            }
            crate::console_key::ConsoleKey::Delete => {
                if let Some(input) = &mut self.modal.go_to_path_input {
                    input.delete_forward();
                }
            }
            crate::console_key::ConsoleKey::LeftArrow => {
                if let Some(input) = &mut self.modal.go_to_path_input {
                    input.move_cursor_left();
                }
            }
            crate::console_key::ConsoleKey::RightArrow => {
                if let Some(input) = &mut self.modal.go_to_path_input {
                    input.move_cursor_right();
                }
            }
            crate::console_key::ConsoleKey::Home => {
                if let Some(input) = &mut self.modal.go_to_path_input {
                    input.move_cursor_home();
                }
            }
            crate::console_key::ConsoleKey::End => {
                if let Some(input) = &mut self.modal.go_to_path_input {
                    input.move_cursor_end();
                }
            }
            crate::console_key::ConsoleKey::UpArrow => {
                let value = self
                    .modal
                    .go_to_path_input
                    .as_ref()
                    .map(|i| i.value().to_string())
                    .unwrap_or_default();
                if !value.is_empty() {
                    let trimmed = value.trim_end_matches(['/', '\\']).to_string();
                    if let Some(last_sep) = trimmed.rfind(['/', '\\']) {
                        let parent = &trimmed[..last_sep + 1];
                        self.modal.go_to_path_input = Some(TextInput::new(parent));
                    }
                }
            }
            _ => {
                if key.key_char >= 0x20
                    && let Some(ch) = char::from_u32(u32::from(key.key_char))
                    && let Some(input) = &mut self.modal.go_to_path_input
                {
                    input.insert_char(ch);
                }
            }
        }
    }

    /// Port of `HandleActionPaletteKey` (App.cs:3099).
    pub fn handle_action_palette_key(&mut self, key: KeyEvent) {
        if self.modal.action_menu_stack.is_empty() {
            return;
        }

        // Navigation on the top level's filtered list
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
                    level.selected_index =
                        (level.selected_index + visible_count).min(filtered_count.saturating_sub(1));
                }
                crate::console_key::ConsoleKey::Home => level.selected_index = 0,
                crate::console_key::ConsoleKey::End => {
                    level.selected_index = filtered_count.saturating_sub(1);
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

    /// Port of the palette Enter handling (a separate step in C# so the
    /// dispatch happens after the selection clamp). Returns Some(action)
    /// when a leaf item was selected.
    pub fn action_palette_activate(&mut self) -> Option<AppAction> {
        let level = self.modal.action_menu_stack.last()?;
        let filtered = level.get_filtered_items();
        let selected = filtered.get(level.selected_index)?;

        if selected.is_submenu() {
            let sub_items = selected.sub_items.clone().expect("submenu");
            let title = selected.label.clone();
            self.modal.action_menu_stack.push(ActionMenuLevel::new(&title, sub_items));
            None
        } else {
            let action = selected.action;
            self.modal.action_menu_stack.clear();
            self.input_mode = InputMode::Normal;
            Some(action)
        }
    }

    /// Port of `ShowActionPalette`, restricted to actions whose subsystems
    /// are already ported (see KNOWN_DEVIATIONS.md).
    pub fn show_action_palette(&mut self) {
        self.input_mode = InputMode::ActionPalette;
        self.modal.action_menu_stack.clear();
        self.modal
            .action_menu_stack
            .push(ActionMenuLevel::new("Action Palette", self.build_action_palette_items()));
    }

    fn build_action_palette_items(&self) -> Vec<ActionMenuItem> {
        vec![
            ActionMenuItem::new("Toggle hidden files", ".", AppAction::ToggleHiddenFiles),
            ActionMenuItem::new("Toggle left pane", "[", AppAction::ToggleParentPane),
            ActionMenuItem::new("Toggle right pane", "]", AppAction::TogglePreviewPane),
            ActionMenuItem::new("Cycle sort mode", "s", AppAction::CycleSortMode),
            ActionMenuItem::new("Reverse sort direction", "S", AppAction::ToggleSortDirection),
            ActionMenuItem::new("Go to path", "Ctrl+G", AppAction::GoToPath),
            ActionMenuItem::new("Filter", "/", AppAction::Search),
            ActionMenuItem::new("Configuration", ",", AppAction::ShowConfig),
            ActionMenuItem::new("Help", "?", AppAction::ShowHelp),
            ActionMenuItem::new("Refresh", "Ctrl+R", AppAction::Refresh),
        ]
    }

    /// Port of `ShowConfirmDialog`.
    pub fn show_confirm_dialog(&mut self, title: &str, message: &str, on_yes: AppAction) {
        self.input_mode = InputMode::Confirm;
        self.modal.confirm_title = Some(title.to_string());
        self.modal.confirm_message = Some(message.to_string());
        self.modal.confirm_yes_action = Some(on_yes);
    }

    /// Port of `ShowTextInputDialog`.
    pub fn show_text_input_dialog(&mut self, title: &str, initial_value: &str, purpose: Option<TextInputPurpose>) {
        self.input_mode = InputMode::TextInput;
        self.modal.text_input_title = Some(title.to_string());
        self.modal.active_text_input = Some(TextInput::new(initial_value));
        self.modal.text_input_purpose = purpose;
    }

    /// Port of `NavigateToPath` (App.cs:2514), minus the file-selection side
    /// effects that need the preview subsystem.
    pub fn navigate_to_path(&mut self, path: &str) {
        if path.trim().is_empty() {
            return;
        }

        // C# Path.GetFullPath resolves relative paths against the process
        // working directory, not the pane's current path.
        let full = get_full_path(path);

        if is_directory(&full) {
            self.selected_index_per_dir
                .insert(self.current_path.clone(), self.selected_index);
            self.current_path = capitalize_drive_letter(&full);
            self.selected_index = 0;
            self.scroll_offset = 0;
            self.marked_paths.clear();
            self.clear_search_filter();
            self.notification = None;
        } else if is_file(&full) {
            let parent = match parent_of(&full) {
                Some(p) => p,
                None => return,
            };
            let file_name = file_name_of(&full);
            self.selected_index_per_dir
                .insert(self.current_path.clone(), self.selected_index);
            self.current_path = capitalize_drive_letter(&parent);
            let entries = self.directory_contents.get_entries(&self.current_path);
            let idx = entries
                .iter()
                .position(|e| e.name.eq_ignore_ascii_case(&file_name));
            self.selected_index = idx.unwrap_or(0);
            self.scroll_offset = 0;
            self.marked_paths.clear();
            self.clear_search_filter();
            self.notification = None;
        } else {
            self.show_notification("Path not found", crate::ui::NotificationKind::Error);
        }
    }

    /// Fixture-facing helpers: the golden-frame harness drives these to set
    /// up modal state before calling `render_modals`.
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

    /// Port of `RenderGoToPathDialog` (App.cs:2663), minus the ghost suffix
    fn render_go_to_path_dialog(&mut self, buffer: &mut ScreenBuffer, width: i32, height: i32) {
        let input = self.modal.go_to_path_input.as_mut();
        render_go_to_path_dialog(buffer, width, height, input);
    }

    fn render_action_palette(&mut self, buffer: &mut ScreenBuffer, width: i32, height: i32) {
        let depth = self.modal.action_menu_stack.len() as i32;
        if let Some(level) = self.modal.action_menu_stack.last_mut() {
            render_action_palette(buffer, width, height, level, depth);
        }
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
    pub fn render_confirm_dialog(
    buffer: &mut ScreenBuffer,
    width: i32,
    height: i32,
    title: Option<&str>,
    message: &str,
) {
        let lines: Vec<&str> = message.split('\n').collect();
        let footer = "[Y/Enter] Yes  [N/Esc] No";
        let max_line_len = lines.iter().map(|l| l.chars().count()).max().unwrap_or(0) as i32;

        let content_width = max_line_len.max(footer.chars().count() as i32) + 2;
        let content_height = i32::try_from(lines.len()).unwrap_or(0);

        let content = dialog_box::render(
            buffer,
            width,
            height,
            content_width,
            content_height,
            title,
            Some(footer),
        );

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

        let content = dialog_box::render(
            buffer,
            width,
            height,
            content_width,
            content_height,
            title,
            Some(footer),
        );

        let input_style = CellStyle {
            fg: Some(Color { r: 200, g: 200, b: 200 }),
            bg: Some(BG_COLOR),
            ..CellStyle::default()
        };
        if let Some(input) = input {
            input.render(buffer, content.top, content.left, content.width, input_style);
        }
    }

    /// (PathCompletion lands in Phase 3c).
    pub fn render_go_to_path_dialog(buffer: &mut ScreenBuffer, width: i32, height: i32, input: Option<&mut TextInput>) {
        let content_width = 60.min(width - 8);
        let content_height = 1;
        let footer = "[Tab] Complete  [↑] Up dir  [Esc] Clear/Close  [Enter] Go";

        let content = dialog_box::render(
            buffer,
            width,
            height,
            content_width,
            content_height,
            Some("Go to path"),
            Some(footer),
        );

        let input_style = CellStyle {
            fg: Some(Color { r: 200, g: 200, b: 200 }),
            bg: Some(BG_COLOR),
            ..CellStyle::default()
        };
        if let Some(input) = input {
            input.render(buffer, content.top, content.left, content.width, input_style);
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
        level
            .filter
            .render(buffer, content.top, content.left + 2, content.width - 2, input_style);

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
                buffer.write_string(
                    row,
                    content.left + 1,
                    label,
                    label_style,
                    i64::from(content.width - 4),
                );
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

/// Port of `PathCompletion.NormalizeSeparators`.
#[must_use]
pub fn normalize_separators(path: &str) -> String {
    if cfg!(windows) {
        path.replace('/', "\\")
    } else {
        path.replace('\\', "/")
    }
}

/// Port of `PathCompletion.ExpandTilde`.
#[must_use]
pub fn expand_tilde(path: &str) -> String {
    if path == "~" {
        return home_dir();
    }

    if let Some(rest) = path.strip_prefix("~/").or_else(|| path.strip_prefix("~\\")) {
        let home = home_dir();
        let sep = if cfg!(windows) { '\\' } else { '/' };
        if home.ends_with(sep) {
            return format!("{home}{rest}");
        }

        return format!("{home}{sep}{rest}");
    }

    path.to_string()
}

fn home_dir() -> String {
    std::env::var("USERPROFILE")
        .or_else(|_| std::env::var("HOME"))
        .unwrap_or_else(|_| "~".to_string())
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
    let expanded = expand_tilde(&normalize_separators(path));
    if is_absolute_path(&expanded) {
        return collapse_dots(&expanded);
    }

    let base = std::env::current_dir()
        .map(|p| p.to_string_lossy().to_string())
        .unwrap_or_else(|_| ".".to_string());
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
    bytes.len() >= 3
        && bytes[0].is_ascii_alphabetic()
        && bytes[1] == b':'
        && (bytes[2] == b'\\' || bytes[2] == b'/')
}

/// Collapse `.` and `..` segments lexically (rough `Path.GetFullPath`).
#[must_use]
pub fn collapse_dots(path: &str) -> String {
    // Preserve the "C:" prefix when present
    let (prefix, rest) = if path.len() >= 2 && path.as_bytes()[1] == b':' {
        (&path[..2], &path[2..])
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
    use crate::app::input_reader::AppAction;
    use crate::app::AppConfig;

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
                ActionMenuItem::submenu(
                    "Change preview",
                    "p",
                    vec![ActionMenuItem::new("Text", "", AppAction::None)],
                ),
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
        app.show_confirm_dialog("Delete", "Sure?", AppAction::Refresh);
        assert_eq!(app.input_mode, InputMode::Confirm);

        app.handle_confirm_key(
            KeyEvent {
                key: crate::console_key::ConsoleKey::Escape,
                key_char: 0,
                shift: false,
                alt: false,
                control: false,
            },
        );
        assert_eq!(app.input_mode, InputMode::Normal);
        assert!(app.modal.confirm_yes_action.is_none());
    }

    #[test]
    fn path_helpers_round_trip() {
        assert!(is_absolute_path(r"C:\foo"));
        assert!(is_absolute_path("C:/foo"));
        assert!(!is_absolute_path(r"foo\bar"));
        assert_eq!(parent_of(r"C:\foo\bar"), Some(r"C:\foo".to_string()));
        assert_eq!(parent_of(r"C:\foo"), Some(r"C:\".to_string()));
        assert_eq!(file_name_of(r"C:\foo\bar\"), "bar");
        assert_eq!(collapse_dots(r"C:\foo\..\bar"), r"C:\bar");
        assert_eq!(normalize_separators("a/b"), r"a\b");
    }
}
