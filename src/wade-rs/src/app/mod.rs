//! Port of the app spine: main loop, navigation, pane rendering.
//! Subsets of src/Wade/App.cs not yet portable (git, previews, file
//! operations, dialogs) dispatch a status-bar notification instead;
//! tracked as a temporary deviation in KNOWN_DEVIATIONS.md.

pub mod config_io;
pub mod git_action_runner;
pub mod git_menu_items;
pub mod git_status_loader;
pub mod dialogs;
pub mod input_reader;

use std::collections::HashMap;
use std::path::Path;

use crate::fs::directory_contents::{DirectoryContents, FileSystemEntry, GitFileStatus, DRIVES_PATH};
pub use crate::input::InputMode;
use crate::input::{InputEvent, KeyEvent, ResizeEvent};
use crate::screen::ScreenBuffer;
use crate::ui::notification::{Notification, NotificationKind};
use crate::ui::pane_renderer::PaneRenderer;
use crate::fs::directory_contents::SortMode;
use crate::ui::Layout;

use input_reader::{map_key, AppAction};

const NOTIFICATION_DURATION_MS: i64 = 4000;

/// Port of the 3a subset of `WadeConfig` (same config.toml keys).
/// 3c adds the remaining `WadeConfig` settings so the config dialog can
/// round-trip all 27 of them.
#[derive(Clone, Debug)]
pub struct AppConfig {
    pub start_path: String,
    pub show_icons_enabled: bool,
    pub image_previews_enabled: bool,
    pub show_hidden_files: bool,
    pub show_system_files: bool,
    pub sort_mode: SortMode,
    pub sort_ascending: bool,
    pub parent_pane_enabled: bool,
    pub preview_pane_enabled: bool,
    pub size_column_enabled: bool,
    pub date_column_enabled: bool,
    pub column_headers_enabled: bool,
    pub confirm_delete_enabled: bool,
    pub copy_symlinks_as_links_enabled: bool,
    pub zip_preview_enabled: bool,
    pub terminal_title_enabled: bool,
    pub git_status_enabled: bool,
    pub file_metadata_enabled: bool,
    pub file_previews_enabled: bool,
    pub archive_metadata_enabled: bool,
    pub dir_size_ssd_enabled: bool,
    pub dir_size_hdd_enabled: bool,
    pub dir_size_network_enabled: bool,
    pub pdf_preview_enabled: bool,
    pub pdf_metadata_enabled: bool,
    pub markdown_preview_enabled: bool,
    pub ffprobe_enabled: bool,
    pub mediainfo_enabled: bool,
    /// C# `WadeConfig.ConfigFilePath`: where `Save()` writes. `None` means
    /// the default `~/.config/wade/config.toml`.
    pub config_file_path: Option<String>,
}

impl Default for AppConfig {
    fn default() -> Self {
        Self {
            start_path: String::new(),
            show_icons_enabled: true,
            image_previews_enabled: true,
            show_hidden_files: false,
            show_system_files: false,
            sort_mode: SortMode::Name,
            sort_ascending: true,
            parent_pane_enabled: true,
            preview_pane_enabled: true,
            size_column_enabled: true,
            date_column_enabled: true,
            column_headers_enabled: true,
            confirm_delete_enabled: true,
            copy_symlinks_as_links_enabled: true,
            zip_preview_enabled: true,
            terminal_title_enabled: true,
            git_status_enabled: true,
            file_metadata_enabled: true,
            file_previews_enabled: true,
            archive_metadata_enabled: true,
            dir_size_ssd_enabled: true,
            dir_size_hdd_enabled: false,
            dir_size_network_enabled: false,
            pdf_preview_enabled: true,
            pdf_metadata_enabled: true,
            markdown_preview_enabled: true,
            ffprobe_enabled: true,
            mediainfo_enabled: true,
            config_file_path: None,
        }
    }
}

/// The app state machine plus main loop.
pub struct App {
    pub config: AppConfig,
    pub directory_contents: DirectoryContents,
    pub layout: Layout,

    current_path: String,
    selected_index: usize,
    scroll_offset: usize,
    selected_index_per_dir: HashMap<String, usize>,
    marked_paths: std::collections::HashSet<String>,
    notification: Option<Notification>,
    pub(crate) git_status_loader: crate::app::git_status_loader::GitStatusLoader,
    pub(crate) git_action_runner: crate::app::git_action_runner::GitActionRunner,
    pub(crate) git_queries: std::sync::Arc<dyn crate::app::git_status_loader::GitQueries>,
    current_repo_root: Option<String>,
    current_branch_name: Option<String>,
    git_statuses: Option<HashMap<String, GitFileStatus>>,
    ahead_behind_text: Option<String>,
    /// Public so the bin crate can hand the pump thread a sender clone.
    pub pipeline: crate::input::input_pipeline::InputPipeline,
    bookmark_store: crate::fs::bookmark_store::BookmarkStore,
    parent_pane_enabled: bool,
    preview_pane_enabled: bool,
    input_mode: crate::input::InputMode,
    search_filter: String,
    filtered_entries: Option<Vec<FileSystemEntry>>,
    quit: bool,
    write_cwd: bool,
    last_width: i32,
    last_height: i32,
    pub(crate) modal: dialogs::ModalState,
}

impl App {
    #[must_use]
    pub fn new(config: AppConfig) -> Self {
        Self {
            config,
            directory_contents: DirectoryContents::new(),
            layout: Layout::default(),
            current_path: String::new(),
            selected_index: 0,
            scroll_offset: 0,
            selected_index_per_dir: HashMap::new(),
            marked_paths: std::collections::HashSet::new(),
            notification: None,
            parent_pane_enabled: true,
            preview_pane_enabled: true,
            input_mode: crate::input::InputMode::Normal,
            search_filter: String::new(),
            filtered_entries: None,
            quit: false,
            write_cwd: true,
            last_width: 0,
            last_height: 0,
            modal: dialogs::ModalState::default(),
            bookmark_store: crate::fs::bookmark_store::BookmarkStore::new(None),
            pipeline: crate::input::input_pipeline::InputPipeline::new(),
            git_status_loader: crate::app::git_status_loader::GitStatusLoader::new(),
            git_action_runner: crate::app::git_action_runner::GitActionRunner::new(),
            git_queries: std::sync::Arc::new(crate::app::git_status_loader::GitUtilsQueries),
            current_repo_root: None,
            current_branch_name: None,
            git_statuses: None,
            ahead_behind_text: None,
        }
    }

    #[must_use]
    pub fn current_path(&self) -> &str {
        &self.current_path
    }

    #[must_use]
    pub fn selected_index(&self) -> usize {
        self.selected_index
    }

    #[must_use]
    pub fn scroll_offset(&self) -> usize {
        self.scroll_offset
    }

    /// Port of `App.Run`'s setup and main loop. Events arrive over the
    /// app-owned pipeline (console pump + async loaders), mirroring the C#
    /// `InputPipeline`.
    pub fn run(&mut self, cancel: &crate::input::CancelToken) -> Option<String> {
        let start = if self.config.start_path.is_empty() { App::default_start_path() } else { self.config.start_path.clone() };
        self.current_path = capitalize_drive_letter(&std::fs::canonicalize(&start).unwrap_or_else(|_| start.clone().into()).to_string_lossy());
        self.directory_contents.show_hidden_files = self.config.show_hidden_files;
        self.directory_contents.show_system_files = self.config.show_system_files;
        self.directory_contents.sort_mode = self.config.sort_mode;
        self.directory_contents.sort_ascending = self.config.sort_ascending;
        self.parent_pane_enabled = self.config.parent_pane_enabled;
        self.preview_pane_enabled = self.config.preview_pane_enabled;
        self.bookmark_store.load();
        self.refresh_git_status();

        let (mut width, mut height) = terminal_size().unwrap_or((80, 25));
        self.last_width = width;
        self.last_height = height;

        let mut buffer = ScreenBuffer::new(width, height);
        self.layout.calculate(width, height, self.preview_pane_enabled, self.parent_pane_enabled);

        while !self.quit {
            // Auto-clear expired notifications
            if let Some(notif) = &self.notification
                && notif.is_expired(now_ms(), NOTIFICATION_DURATION_MS) {
                    self.notification = None;
                }

            // Render
            buffer.clear();
            self.render(&mut buffer);
            flush_buffer(&mut buffer);

            // Wait for next input event (pump thread feeds the queue; loader
            // threads send into the same queue)
            let Some(event) = self.pipeline.wait_next(cancel) else {
                break;
            };

            // Drain queued events like the C# loop, keeping the last key/
            // mouse/paste/resize; git status ready events are handled inline
            // exactly as C# processes them in its extra-event loop
            let mut current = event;
            while let Some(extra) = self.pipeline.try_take() {
                match extra {
                    InputEvent::Resize(_) | InputEvent::Key(_) | InputEvent::Mouse(_) | InputEvent::Paste(_) => {
                        current = extra;
                    }
                    InputEvent::GitStatusReady(ready) => self.handle_git_status_ready(ready),
                    InputEvent::GitActionComplete(event) => self.handle_git_action_complete(event),
                }
            }

            match current {
                InputEvent::Resize(resize) => self.handle_resize(resize, &mut buffer, &mut width, &mut height),
                InputEvent::Key(key) => self.handle_key(key),
                InputEvent::Mouse(mouse) => self.handle_mouse(mouse),
                InputEvent::Paste(text) => self.handle_paste_event(&text),
                InputEvent::GitStatusReady(event) => self.handle_git_status_ready(event),
                InputEvent::GitActionComplete(event) => self.handle_git_action_complete(event),
            }

            // Clamp selection and adjust scroll
            let entries = self.get_visible_entries();
            if !entries.is_empty() {
                self.selected_index = self.selected_index.min(entries.len() - 1);
            } else {
                self.selected_index = 0;
            }
            self.adjust_scroll(self.visible_file_list_height(&entries));
        }

        if self.write_cwd {
            Some(self.current_path.clone())
        } else {
            None
        }
    }

    fn default_start_path() -> String {
        std::env::current_dir()
            .map(|p| p.to_string_lossy().to_string())
            .unwrap_or_else(|_| ".".to_string())
    }

    fn handle_resize(&mut self, resize: ResizeEvent, buffer: &mut ScreenBuffer, width: &mut i32, height: &mut i32) {
        if resize.width != *width || resize.height != *height {
            *width = resize.width;
            *height = resize.height;
            self.last_width = resize.width;
            self.last_height = resize.height;
            buffer.resize(resize.width, resize.height);
            self.layout.calculate(resize.width, resize.height, self.preview_pane_enabled, self.parent_pane_enabled);
            clear_screen();
        }
    }

    fn handle_key(&mut self, key: KeyEvent) {
        if self.handle_modal_key(key) {
            return;
        }

        let action = map_key(&key);
        self.dispatch(action);
    }

    /// Port of the AppAction dispatch switch (3a subset).
    pub fn dispatch(&mut self, action: AppAction) {
        use AppAction as A;
        let entries = self.get_visible_entries();

        match action {
            A::NavigateUp => {
                self.selected_index = if self.selected_index > 0 {
                    self.selected_index - 1
                } else {
                    entries.len().saturating_sub(1)
                };
            }
            A::NavigateDown => {
                self.selected_index = if !entries.is_empty() && self.selected_index < entries.len() - 1 {
                    self.selected_index + 1
                } else {
                    0
                };
            }
            A::Open => {
                if !entries.is_empty() && self.selected_index < entries.len() {
                    let entry = &entries[self.selected_index];
                    if entry.is_directory {
                        if entry.is_broken_symlink {
                            self.show_notification("Cannot open: broken symlink", NotificationKind::Error);
                        } else {
                            self.selected_index_per_dir.insert(self.current_path.clone(), self.selected_index);
                            self.current_path = capitalize_drive_letter(&entry.full_path);
                            self.selected_index = *self.selected_index_per_dir.get(&self.current_path).unwrap_or(&0);
                            self.scroll_offset = 0;
                            self.notification = None;
                            self.marked_paths.clear();
                            self.clear_search_filter();
                        }
                    } else {
                        self.show_notification("Open: previews not yet ported", NotificationKind::Info);
                    }
                }
            }
            A::Back => self.navigate_back(),
            A::PageUp => {
                self.selected_index = self.selected_index.saturating_sub(self.visible_file_list_height(&entries));
            }
            A::PageDown => {
                self.selected_index = (self.selected_index + self.visible_file_list_height(&entries)).min(entries.len().saturating_sub(1));
            }
            A::Home => self.selected_index = 0,
            A::End => self.selected_index = entries.len().saturating_sub(1),
            A::Quit => {
                if !self.search_filter.is_empty() {
                    self.clear_search_filter();
                    self.selected_index = 0;
                    self.scroll_offset = 0;
                } else {
                    self.quit = true;
                }
            }
            A::QuitNoCd => {
                self.quit = true;
                self.write_cwd = false;
            }
            A::Refresh => {
                self.notification = None;
                self.marked_paths.clear();
                self.clear_search_filter();
                self.directory_contents.invalidate_all();
                self.refresh_git_status();
            }
            A::ToggleMark => {
                if !entries.is_empty() && self.selected_index < entries.len() {
                    let path = entries[self.selected_index].full_path.clone();
                    if !self.marked_paths.remove(&path) {
                        self.marked_paths.insert(path);
                    }

                    if self.selected_index < entries.len() - 1 {
                        self.selected_index += 1;
                    }
                }
            }
            A::ToggleHiddenFiles => {
                self.config.show_hidden_files = !self.config.show_hidden_files;
                self.directory_contents.show_hidden_files = self.config.show_hidden_files;
                self.directory_contents.invalidate_all();
            }
            A::ToggleParentPane => {
                self.parent_pane_enabled = !self.parent_pane_enabled;
                self.layout.calculate(self.last_width, self.last_height, self.preview_pane_enabled, self.parent_pane_enabled);
                clear_screen();
            }
            A::TogglePreviewPane => {
                self.preview_pane_enabled = !self.preview_pane_enabled;
                self.layout.calculate(self.last_width, self.last_height, self.preview_pane_enabled, self.parent_pane_enabled);
                clear_screen();
            }
            A::CycleSortMode => {
                self.config.sort_mode = match self.config.sort_mode {
                    SortMode::Name => SortMode::Modified,
                    SortMode::Modified => SortMode::Size,
                    SortMode::Size => SortMode::Extension,
                    other => other,
                };
                self.directory_contents.sort_mode = self.config.sort_mode;
                self.directory_contents.invalidate_all();
            }
            A::ToggleSortDirection => {
                self.config.sort_ascending = !self.config.sort_ascending;
                self.directory_contents.sort_ascending = self.config.sort_ascending;
                self.directory_contents.invalidate_all();
            }
            A::ShowHelp => {
                self.input_mode = InputMode::Help;
            }
            A::Search => {
                self.input_mode = InputMode::Search;
                self.modal.search_input = Some(crate::ui::text_input::TextInput::new(&self.search_filter));
            }
            A::GoToPath => {
                self.input_mode = InputMode::GoToPath;
                self.modal.go_to_path_input = Some(crate::ui::text_input::TextInput::default());
            }
            A::ShowActionPalette => self.show_action_palette(),
            A::ShowConfig => self.show_config_dialog(),
            A::ShowBookmarks => self.show_bookmarks(),
            A::ToggleBookmark => {
                let path = self.current_path.clone();
                self.bookmark_store.toggle(&path);
                let message = if self.bookmark_store.contains(&path) {
                    "Bookmarked"
                } else {
                    "Bookmark removed"
                };
                self.show_notification(message, NotificationKind::Success);
            }
            A::StageFile => {
                if let Some(root) = self.current_repo_root.clone() {
                    let stage_paths = self.get_selected_or_marked_paths(&entries);
                    if !stage_paths.is_empty() {
                        let sender = self.pipeline.sender();
                        self.git_action_runner.start_action(
                            Box::new(move |cancel| crate::fs::git_utils::stage(&root, &stage_paths, cancel)),
                            sender,
                        );
                    }
                }
            }
            A::UnstageFile => {
                if let Some(root) = self.current_repo_root.clone() {
                    let unstage_paths = self.get_selected_or_marked_paths(&entries);
                    if !unstage_paths.is_empty() {
                        let sender = self.pipeline.sender();
                        self.git_action_runner.start_action(
                            Box::new(move |cancel| crate::fs::git_utils::unstage(&root, &unstage_paths, cancel)),
                            sender,
                        );
                    }
                }
            }
            A::StageAll => {
                if let Some(root) = self.current_repo_root.clone() {
                    let sender = self.pipeline.sender();
                    self.git_action_runner.start_action(
                        Box::new(move |cancel| crate::fs::git_utils::stage_all(&root, cancel)),
                        sender,
                    );
                }
            }
            A::UnstageAll => {
                if let Some(root) = self.current_repo_root.clone() {
                    let sender = self.pipeline.sender();
                    self.git_action_runner.start_action(
                        Box::new(move |cancel| crate::fs::git_utils::unstage_all(&root, cancel)),
                        sender,
                    );
                }
            }
            A::GitCommit => {
                if self.current_repo_root.is_some() {
                    self.show_text_input_dialog("Commit message", "", Some(dialogs::TextInputPurpose::Commit));
                }
            }
            A::GitPush => self.run_simple_git_action(crate::fs::git_utils::push),
            A::GitPushForceWithLease => self.run_simple_git_action(crate::fs::git_utils::push_force_with_lease),
            A::GitPull => self.run_simple_git_action(crate::fs::git_utils::pull),
            A::GitPullRebase => self.run_simple_git_action(crate::fs::git_utils::pull_rebase),
            A::GitFetch => self.run_simple_git_action(crate::fs::git_utils::fetch),
            other => {
                // Unported in 3a: navigation to the subsystem lands in later phases.
                self.show_notification("Not yet ported", NotificationKind::Info);
                let _ = other;
            }
        }
    }

    fn navigate_back(&mut self) {
        if self.current_path == DRIVES_PATH {
            return; // already at the top level
        }

        self.notification = None;
        self.marked_paths.clear();
        self.clear_search_filter();
        self.selected_index_per_dir.insert(self.current_path.clone(), self.selected_index);
        let old_path = self.current_path.clone();

        if DirectoryContents::is_drive_root(&self.current_path) {
            // Go up to the drives list
            self.current_path = DRIVES_PATH.to_string();
            let drive_entries = self.directory_contents.get_entries(DRIVES_PATH);
            let root = drive_root(&old_path)
                .map(|r| r.trim_end_matches(['\\', '/']).to_string())
                .unwrap_or_default();
            let idx = drive_entries.iter().position(|e| e.name.eq_ignore_ascii_case(&root));
            self.selected_index = idx.unwrap_or(0);
        } else if let Some(parent) = Path::new(&self.current_path).parent() {
            let parent_path = capitalize_drive_letter(&parent.to_string_lossy());
            self.current_path = parent_path;
            let parent_entries = self.directory_contents.get_entries(&self.current_path);
            let old_name = Path::new(&old_path)
                .file_name()
                .map(|n| n.to_string_lossy().to_string())
                .unwrap_or_default();
            let idx = parent_entries.iter().position(|e| e.name.eq_ignore_ascii_case(&old_name));
            self.selected_index = idx.unwrap_or_else(|| *self.selected_index_per_dir.get(&self.current_path).unwrap_or(&0));
        }

        self.scroll_offset = 0;
    }

    /// Port of `GetVisibleEntries`.
    #[must_use]
    pub fn get_visible_entries(&mut self) -> Vec<FileSystemEntry> {
        let all = self.directory_contents.get_entries(&self.current_path);
        if self.search_filter.is_empty() {
            self.filtered_entries = None;
            return all;
        }

        if self.filtered_entries.is_none() {
            let filter = self.search_filter.to_lowercase();
            self.filtered_entries = Some(
                all.into_iter()
                    .filter(|e| e.name.to_lowercase().contains(&filter))
                    .collect(),
            );
        }

        self.filtered_entries.clone().unwrap_or_default()
    }

    /// Port of `VisibleFileListHeight`.
    #[must_use]
    pub fn visible_file_list_height(&self, _entries: &[FileSystemEntry]) -> usize {
        let mut height = self.layout.center_pane.height;
        if self.input_mode == crate::input::InputMode::Search || !self.search_filter.is_empty() {
            height -= 1;
        }
        if self.config.column_headers_enabled && height > 2 {
            height -= 2;
        }
        height.max(0) as usize
    }

    /// Port of `AdjustScroll`.
    pub fn adjust_scroll(&mut self, visible_height: usize) {
        let selected = self.selected_index;
        if selected < self.scroll_offset {
            self.scroll_offset = selected;
        } else if selected >= self.scroll_offset.saturating_add(visible_height) {
            self.scroll_offset = (selected + 1).saturating_sub(visible_height);
        }
    }

    /// Port of `ShowNotification`.
    pub fn show_notification(&mut self, message: &str, kind: NotificationKind) {
        self.notification = Some(Notification {
            message: message.to_string(),
            kind,
            timestamp_ms: now_ms(),
        });
    }


    /// Port of `UpdateTerminalTitle` (App.cs): OSC 0 title set/clear.
    pub fn update_terminal_title(&self) {
        use std::io::Write;

        let sequence = if self.config.terminal_title_enabled {
            crate::ansi::set_title(&format!("wade - {}", self.current_path))
        } else {
            crate::ansi::set_title("")
        };

        let mut out = std::io::stdout();
        let _ = out.write_all(sequence.as_bytes());
        let _ = out.flush();
    }

    /// Port of the mouse-event dispatch (App.cs:541-573) and
    /// `HandleMouseEvent` (App.cs:1731): scroll wheel, pane click navigation,
    /// right-click context menu.
    pub fn handle_mouse(&mut self, mouse: crate::input::MouseEvent) {
        use crate::input::MouseButton;

        if self.input_mode == InputMode::ExpandedPreview {
            // Expanded-preview scrolling lands with previews in Phase 7.
            return;
        }

        if self.input_mode == InputMode::ContextMenu {
            self.handle_context_menu_mouse(mouse);
            return;
        }

        // Discard mouse events while a modal dialog is open
        if matches!(
            self.input_mode,
            InputMode::Help
                | InputMode::GoToPath
                | InputMode::TextInput
                | InputMode::Confirm
                | InputMode::Config
                | InputMode::Properties
                | InputMode::ActionPalette
                | InputMode::Bookmarks
                | InputMode::FileFinder
        ) {
            return;
        }

        // Scroll wheel moves the selection in the center pane
        let entries = self.get_visible_entries();
        if mouse.button == MouseButton::ScrollUp {
            if self.selected_index > 0 {
                self.selected_index -= 1;
            }
            return;
        }

        if mouse.button == MouseButton::ScrollDown {
            if !entries.is_empty() && self.selected_index < entries.len() - 1 {
                self.selected_index += 1;
            }
            return;
        }

        // Ignore releases and non-left/right clicks
        if mouse.is_release || (mouse.button != MouseButton::Left && mouse.button != MouseButton::Right) {
            return;
        }

        let row = mouse.row;
        let col = mouse.col;
        let header_offset = i32::from(self.config.column_headers_enabled) * 2;

        if Self::hit_test_pane(self.layout.center_pane, row, col) {
            // Center pane click: select the entry (same as arrow keys)
            let entry_index = self.scroll_offset as i32 + (row - self.layout.center_pane.top - header_offset);
            let entry_index = usize::try_from(entry_index).unwrap_or(usize::MAX);
            if entry_index < entries.len() {
                self.selected_index = entry_index;
            }

            // Right-click opens the context menu
            if mouse.button == MouseButton::Right && entry_index < entries.len() {
                self.modal.context_menu = Some(crate::ui::context_menu::ContextMenuState::new(
                    self.build_context_menu_items(),
                    row,
                    col,
                ));
                self.input_mode = InputMode::ContextMenu;
            }
        } else if Self::hit_test_pane(self.layout.left_pane, row, col) {
            self.handle_left_pane_click(row, col, header_offset);
        } else if Self::hit_test_pane(self.layout.right_pane, row, col) {
            self.handle_right_pane_click(row, col, header_offset);
        }

        // Clamp after mouse handling
        let entries = self.get_visible_entries();
        self.selected_index = if entries.is_empty() {
            0
        } else {
            self.selected_index.min(entries.len() - 1)
        };
    }

    /// Port of `HitTestPane`.
    fn hit_test_pane(pane: crate::ui::layout::Rect, row: i32, col: i32) -> bool {
        row >= pane.top && row < pane.top + pane.height && col >= pane.left && col < pane.left + pane.width
    }

    /// The navigate-into-directory housekeeping shared by mouse handlers
    /// (per-dir selection save/restore, scroll reset, mark/search clearing).
    /// Preview-cache and git-status refreshes land with those subsystems.
    fn navigate_to_directory(&mut self, path: &str) {
        self.selected_index_per_dir.insert(self.current_path.clone(), self.selected_index);
        self.current_path = path.to_string();
        self.selected_index = *self.selected_index_per_dir.get(&self.current_path).unwrap_or(&0);
        self.scroll_offset = 0;
        self.marked_paths.clear();
        self.clear_search_filter();
        self.update_terminal_title();
        self.refresh_git_status();
    }

    /// Port of `RefreshGitStatus` (App.cs:2571).
    pub fn refresh_git_status(&mut self) {
        if !self.config.git_status_enabled {
            self.current_repo_root = None;
            self.current_branch_name = None;
            self.git_statuses = None;
            self.git_status_loader.cancel();
        } else {
            let repo_root = crate::fs::git_utils::find_repo_root(&self.current_path);
            self.current_repo_root = repo_root.clone();

            if let Some(root) = repo_root {
                // Like C#: branch/statuses are NOT cleared here, so the
                // previous listing stays visible until the new event lands
                let sender = self.pipeline.sender();
                let queries = std::sync::Arc::clone(&self.git_queries);
                self.git_status_loader.begin_load(&root, queries, sender);
            } else {
                self.current_branch_name = None;
                self.git_statuses = None;
            }
        }

        // C# also calls RefreshInlineDirSizes here (Phase 4d).
    }

    /// The dispatch arms for the network git commands (push/pull/fetch and
    /// variants): all share the repo-root guard and the 30s timeout
    /// (App.cs:3789-3826).
    fn run_simple_git_action(&mut self, action: fn(&str, &crate::input::CancelToken) -> (bool, Option<String>)) {
        if let Some(root) = self.current_repo_root.clone() {
            let sender = self.pipeline.sender();
            self.git_action_runner.start_action(Box::new(move |cancel| action(&root, cancel)), sender);
        }
    }

    /// Port of `HandleGitActionComplete` (App.cs:1517).
    pub fn handle_git_action_complete(&mut self, event: crate::input::GitActionCompleteEvent) {
        if event.success {
            self.show_notification("Git action completed", NotificationKind::Success);
        } else {
            // C# interpolation renders null as empty: "Git error: "
            let message = format!("Git error: {}", event.error_message.unwrap_or_default());
            self.show_notification(&message, NotificationKind::Error);
        }

        self.refresh_git_status();
    }

    /// Port of `GetSelectedOrMarkedPaths` (App.cs:3867): marked paths first,
    /// else the selected entry's full path.
    #[must_use]
    pub fn get_selected_or_marked_paths(&self, entries: &[FileSystemEntry]) -> Vec<String> {
        if !self.marked_paths.is_empty() {
            return self.marked_paths.iter().cloned().collect();
        }

        if self.selected_index < entries.len() {
            return vec![entries[self.selected_index].full_path.clone()];
        }

        Vec::new()
    }

    /// Port of `HasStatusInSelection` (App.cs:1489): marked paths take
    /// precedence over the selected entry.
    #[must_use]
    pub fn has_status_in_selection(&self, status_mask: GitFileStatus, entries: &[FileSystemEntry]) -> bool {
        let Some(statuses) = &self.git_statuses else {
            return false;
        };

        if !self.marked_paths.is_empty() {
            return self
                .marked_paths
                .iter()
                .any(|path| crate::fs::git_utils::statuses_get(statuses, path).is_some_and(|s| s.intersects(status_mask)));
        }

        if self.selected_index < entries.len() {
            let path = &entries[self.selected_index].full_path;
            return crate::fs::git_utils::statuses_get(statuses, path).is_some_and(|s| s.intersects(status_mask));
        }

        false
    }

    /// Port of `HandleGitStatusReady` (App.cs:1455).
    pub fn handle_git_status_ready(&mut self, event: crate::input::GitStatusReadyEvent) {
        if Some(event.repo_root.clone()) != self.current_repo_root {
            return;
        }

        self.current_branch_name = event.branch_name;
        self.git_statuses = event.statuses;
        self.ahead_behind_text = Self::format_ahead_behind(event.ahead, event.behind);
    }

    /// Port of `FormatAheadBehind` (App.cs:1461).
    #[must_use]
    pub fn format_ahead_behind(ahead: u32, behind: u32) -> Option<String> {
        if ahead == 0 && behind == 0 {
            return None;
        }

        if ahead > 0 && behind > 0 {
            return Some(format!(" \u{2191}{ahead} \u{2193}{behind}"));
        }

        if ahead > 0 {
            return Some(format!(" \u{2191}{ahead}"));
        }

        Some(format!(" \u{2193}{behind}"))
    }

    /// Left-pane click: navigate into the clicked directory, or navigate to
    /// the parent and select the clicked file (App.cs:1795-1830).
    fn handle_left_pane_click(&mut self, row: i32, _col: i32, header_offset: i32) {
        if self.current_path == DRIVES_PATH {
            return;
        }

        // The left pane shows the parent directory (see render_left_pane)
        let (parent_key, _) = if DirectoryContents::is_drive_root(&self.current_path) {
            (
                DRIVES_PATH.to_string(),
                drive_root(&self.current_path)
                    .map(|r| r.trim_end_matches(['\\', '/']).to_string())
                    .unwrap_or_default(),
            )
        } else {
            match Path::new(&self.current_path).parent() {
                Some(parent) => (parent.to_string_lossy().to_string(), file_name_of(&self.current_path)),
                None => (DRIVES_PATH.to_string(), file_name_of(&self.current_path)),
            }
        };

        let left_entries = self.directory_contents.get_entries(&parent_key);
        let entry_index = self.mouse_left_pane_scroll(&left_entries) + (row - self.layout.left_pane.top - header_offset);
        let entry_index = usize::try_from(entry_index).unwrap_or(usize::MAX);
        if entry_index >= left_entries.len() {
            return;
        }

        let clicked = left_entries[entry_index].clone();
        if clicked.is_directory {
            self.navigate_to_directory(&crate::fs::directory_contents::capitalize_drive_letter(&clicked.full_path));
        } else {
            // File in parent dir: navigate to parent, select the file
            self.selected_index_per_dir.insert(self.current_path.clone(), self.selected_index);
            self.current_path = crate::fs::directory_contents::capitalize_drive_letter(&parent_key);
            self.update_terminal_title();
            self.refresh_git_status();
            let parent_entries = self.directory_contents.get_entries(&self.current_path);
            self.selected_index = parent_entries
                .iter()
                .position(|e| e.name.eq_ignore_ascii_case(&clicked.name))
                .unwrap_or(0);
            self.scroll_offset = 0;
            self.marked_paths.clear();
            self.clear_search_filter();
        }
    }

    /// The scroll offset the left pane is rendered with (mirrors
    /// `calculate_scroll(parent_selected, ...)` in `render_left_pane`).
    fn mouse_left_pane_scroll(&self, parent_entries: &[FileSystemEntry]) -> i32 {
        let (_, current_name) = if DirectoryContents::is_drive_root(&self.current_path) {
            (
                DRIVES_PATH.to_string(),
                drive_root(&self.current_path)
                    .map(|r| r.trim_end_matches(['\\', '/']).to_string())
                    .unwrap_or_default(),
            )
        } else {
            match Path::new(&self.current_path).parent() {
                Some(parent) => (parent.to_string_lossy().to_string(), file_name_of(&self.current_path)),
                None => (DRIVES_PATH.to_string(), file_name_of(&self.current_path)),
            }
        };

        let parent_selected = parent_entries
            .iter()
            .position(|e| e.name.eq_ignore_ascii_case(&current_name))
            .unwrap_or(0);

        let mut left_pane = self.layout.left_pane;
        if self.config.column_headers_enabled && left_pane.height > 2 {
            left_pane.top += 2;
            left_pane.height -= 2;
        }

        crate::app::calculate_scroll(parent_selected, left_pane.height, parent_entries.len())
    }

    /// Right-pane click: navigate into the previewed directory (only when the
    /// selected entry is a directory) (App.cs:1843-1900).
    fn handle_right_pane_click(&mut self, row: i32, _col: i32, header_offset: i32) {
        let entries = self.get_visible_entries();
        if entries.is_empty() || self.selected_index >= entries.len() {
            return;
        }

        let selected = entries[self.selected_index].clone();
        if !selected.is_directory {
            return;
        }

        let preview_entries = self.directory_contents.get_entries(&selected.full_path);
        let entry_index = row - self.layout.right_pane.top - header_offset; // scroll is always 0 for preview
        let entry_index = usize::try_from(entry_index).unwrap_or(usize::MAX);
        if entry_index >= preview_entries.len() {
            return;
        }

        let clicked = preview_entries[entry_index].clone();
        if clicked.is_directory {
            self.navigate_to_directory(&crate::fs::directory_contents::capitalize_drive_letter(&clicked.full_path));
        } else {
            // File in previewed directory: navigate there, select the file
            self.navigate_to_directory(&crate::fs::directory_contents::capitalize_drive_letter(&selected.full_path));
            let dir_entries = self.directory_contents.get_entries(&self.current_path);
            self.selected_index = dir_entries
                .iter()
                .position(|e| e.name.eq_ignore_ascii_case(&clicked.name))
                .unwrap_or(0);
        }
    }

    /// Port of `ClearSearchFilter`.
    pub fn clear_search_filter(&mut self) {
        self.search_filter.clear();
        self.filtered_entries = None;
        if self.input_mode == crate::input::InputMode::Search {
            self.input_mode = crate::input::InputMode::Normal;
        }
    }

    /// Port of the 3a subset of `App.Render`.
    pub fn render(&mut self, buffer: &mut ScreenBuffer) {
        let entries = self.get_visible_entries();
        let show_search_bar = self.input_mode == crate::input::InputMode::Search || !self.search_filter.is_empty();
        let mut file_list_pane = self.layout.center_pane;
        if show_search_bar {
            file_list_pane.height -= 1;
        }

        let is_drive_view = self.current_path == DRIVES_PATH;

        // Column headers + separator line
        if self.config.column_headers_enabled && file_list_pane.height > 2 {
            let header_rect = Rect2::new(file_list_pane.left, file_list_pane.top, file_list_pane.width, 2);
            PaneRenderer::render_column_headers(
                buffer,
                header_rect,
                self.config.show_icons_enabled,
                self.config.size_column_enabled,
                self.config.date_column_enabled,
                is_drive_view,
                false,
            );
            file_list_pane.top += 2;
            file_list_pane.height -= 2;
        }

        let scroll = calculate_scroll(self.selected_index, file_list_pane.height, entries.len());
        PaneRenderer::render_file_list(
            buffer,
            file_list_pane,
            &entries,
            i32::try_from(self.selected_index).unwrap_or(0),
            scroll,
            true,
            self.config.show_icons_enabled,
            self.config.size_column_enabled,
            self.config.date_column_enabled,
            &self.marked_paths,
            self.git_statuses.as_ref(),
            None,
            is_drive_view,
        );

        self.render_left_pane(buffer);
        self.render_right_pane(buffer, &entries);

        // Borders
        PaneRenderer::render_borders(buffer, &self.layout, self.last_height, self.preview_pane_enabled, self.parent_pane_enabled);

        // Status bar
        let selected_entry = entries.get(self.selected_index);
        let display_path = if self.current_path == DRIVES_PATH {
            "Drives".to_string()
        } else {
            self.current_path.clone()
        };
        crate::ui::status_bar::render(
            buffer,
            self.layout.status_bar,
            &display_path,
            entries.len(),
            self.selected_index,
            selected_entry,
            None,
            None,
            None,
            self.notification.clone(),
            self.marked_paths.len(),
            self.config.sort_mode,
            self.config.sort_ascending,
            0,
            false,
            self.current_branch_name.as_deref(),
            self.ahead_behind_text.as_deref(),
        );

        // Search bar, help overlay, and modal dialogs render last, on top.
        self.render_modals(buffer);
    }

    fn render_left_pane(&mut self, buffer: &mut ScreenBuffer) {
        if !self.parent_pane_enabled || self.current_path == DRIVES_PATH {
            return;
        }

        let (parent_key, current_name) = if DirectoryContents::is_drive_root(&self.current_path) {
            (
                DRIVES_PATH.to_string(),
                drive_root(&self.current_path)
                    .map(|r| r.trim_end_matches(['\\', '/']).to_string())
                    .unwrap_or_default(),
            )
        } else {
            match Path::new(&self.current_path).parent() {
                Some(parent) => (parent.to_string_lossy().to_string(), file_name_of(&self.current_path)),
                None => (DRIVES_PATH.to_string(), file_name_of(&self.current_path)),
            }
        };

        let parent_entries = self.directory_contents.get_entries(&parent_key);
        let mut parent_selected = parent_entries
            .iter()
            .position(|e| e.name.eq_ignore_ascii_case(&current_name))
            .map(|i| i as i32)
            .unwrap_or(-1);

        if parent_selected < 0 {
            parent_selected = 0;
        }

        let mut left_pane = self.layout.left_pane;

        if self.config.column_headers_enabled && left_pane.height > 2 {
            let header_rect = Rect2::new(left_pane.left, left_pane.top, left_pane.width, 2);
            PaneRenderer::render_column_headers(buffer, header_rect, self.config.show_icons_enabled, false, false, false, false);
            left_pane.top += 2;
            left_pane.height -= 2;
        }

        let parent_scroll = calculate_scroll(parent_selected as usize, left_pane.height, parent_entries.len());
        PaneRenderer::render_file_list(
            buffer,
            left_pane,
            &parent_entries,
            parent_selected,
            parent_scroll,
            false,
            self.config.show_icons_enabled,
            false,
            false,
            &self.marked_paths,
            None,
            None,
            false,
        );
    }

    fn render_right_pane(&mut self, buffer: &mut ScreenBuffer, entries: &[FileSystemEntry]) {
        if !self.preview_pane_enabled || entries.is_empty() || self.selected_index >= entries.len() {
            return;
        }

        let selected = &entries[self.selected_index];
        if selected.is_directory {
            let preview_entries = self.directory_contents.get_entries(&selected.full_path);
            if !preview_entries.is_empty() {
                let mut right_list_pane = self.layout.right_pane;

                if self.config.column_headers_enabled && right_list_pane.height > 2 {
                    let header_rect = Rect2::new(right_list_pane.left, right_list_pane.top, right_list_pane.width, 2);
                    PaneRenderer::render_column_headers(buffer, header_rect, self.config.show_icons_enabled, false, false, false, false);
                    right_list_pane.top += 2;
                    right_list_pane.height -= 2;
                }

                PaneRenderer::render_file_list(
                    buffer,
                    right_list_pane,
                    &preview_entries,
                    -1,
                    0,
                    false,
                    self.config.show_icons_enabled,
                    false,
                    false,
                    &self.marked_paths,
                    None,
                    None,
                    false,
                );
            } else {
                PaneRenderer::render_message(buffer, self.layout.right_pane, "[empty directory]");
            }
        } else {
            PaneRenderer::render_message(buffer, self.layout.right_pane, "[no preview available]");
        }
    }
}

use crate::fs::directory_contents::{capitalize_drive_letter, drive_root};
use crate::ui::layout::Rect as Rect2;

/// Port of `CalculateScroll`.
#[must_use]
pub fn calculate_scroll(selected_index: usize, visible_height: i32, total_count: usize) -> i32 {
    let visible = visible_height.max(0);
    if total_count <= visible as usize {
        return 0;
    }

    let scroll = i32::try_from(selected_index).unwrap_or(i32::MAX) - visible / 2;
    scroll.clamp(0, i32::try_from(total_count).unwrap_or(i32::MAX) - visible)
}

fn file_name_of(path: &str) -> String {
    Path::new(path)
        .file_name()
        .map(|n| n.to_string_lossy().to_string())
        .unwrap_or_else(|| path.to_string())
}

fn now_ms() -> i64 {
    std::time::SystemTime::now()
        .duration_since(std::time::UNIX_EPOCH)
        .map(|d| i64::try_from(d.as_millis()).unwrap_or(0))
        .unwrap_or(0)
}

fn terminal_size() -> Option<(i32, i32)> {
    // Real size comes from the console API on Windows; Unix input (Phase 9)
    // does not implement window queries yet.
    #[cfg(windows)]
    {
        crate::input::windows::window_size_pub()
    }
    #[cfg(not(windows))]
    {
        None
    }
}

fn clear_screen() {
    print!("{}", crate::ansi::CLEAR_SCREEN);
}

fn flush_buffer(buffer: &mut ScreenBuffer) {
    use std::io::Write;
    let mut out = String::new();
    buffer.serialize(&mut out);
    if !out.is_empty() {
        let stdout = std::io::stdout();
        let mut lock = stdout.lock();
        let _ = lock.write_all(out.as_bytes());
        let _ = lock.flush();
    }
}


