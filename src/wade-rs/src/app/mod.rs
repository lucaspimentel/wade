//! Port of the app spine: main loop, navigation, pane rendering.
//! Subsets of src/Wade/App.cs not yet portable (git, previews, file
//! operations, dialogs) dispatch a status-bar notification instead;
//! tracked as a temporary deviation in KNOWN_DEVIATIONS.md.

pub mod config_io;
pub mod directory_size_loader;
pub mod file_operation_runner;
pub mod file_finder;
pub mod fs_watcher;
pub mod git_action_runner;
pub mod git_menu_items;
pub mod git_status_loader;
pub mod inline_dir_size_loader;
pub mod dialogs;
pub mod input_reader;
pub mod preview;
pub mod preview_loader;
#[cfg(test)]
mod settings_tests;

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
    /// C# `StartFileName`: selected at startup when the start path is a file.
    pub start_file_name: Option<String>,
    /// C# `CwdFilePath` (`--cwd-file=`): receives the final directory.
    pub cwd_file_path: Option<String>,
    pub show_config: bool,
    pub show_help: bool,
    pub show_version: bool,
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
            start_file_name: None,
            cwd_file_path: None,
            show_config: false,
            show_help: false,
            show_version: false,
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
    pub(crate) file_operation_runner: crate::app::file_operation_runner::FileOperationRunner,
    pub(crate) directory_size_loader: crate::app::directory_size_loader::DirectorySizeLoader,
    pub(crate) inline_dir_size_loader: crate::app::inline_dir_size_loader::InlineDirSizeLoader,
    pub(crate) fs_watcher: crate::app::fs_watcher::FileSystemWatcherManager,
    /// Size text for the Properties overlay while the loader works.
    properties_dir_size_path: Option<String>,
    properties_dir_size_text: Option<String>,
    properties_scroll_offset: usize,
    properties_content_height: usize,
    /// Accumulated inline directory sizes for the current listing.
    inline_dir_sizes: Option<HashMap<String, i64>>,
    current_drive_media_type: crate::fs::DriveMediaType,
    /// `drive_media_type::detect`; tests substitute a fixed drive type.
    detect_drive_media_type: fn(&str) -> crate::fs::DriveMediaType,
    /// Port of `_clipboardPaths` / `_clipboardIsCut`: wade's own clipboard.
    pub(crate) clipboard_paths: Vec<String>,
    pub(crate) clipboard_is_cut: bool,
    /// The OS clipboard; tests swap in a recording fake.
    pub(crate) os_clipboard: OsClipboard,
    /// Set by HandleFileSystemChanged: the next frame forces a full redraw
    /// (port of C# buffer.ForceFullRedraw()).
    request_full_redraw: bool,
    /// Ctrl+F file finder state while it is open.
    pub(crate) file_finder: Option<crate::app::file_finder::FileFinderState>,
    file_op_label: String,
    file_op_progress: Option<crate::input::FileOperationProgressEvent>,
    /// C# closes over the entry in the TextInput completion callback; Rust
    /// stores the target path explicitly for Rename/CreateSymlink purposes.
    text_input_target: Option<String>,
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
    /// Preview cache and loader (C# `_cachedPreview*` fields).
    pub(crate) preview: preview::PreviewState,
    /// Terminal Sixel support and cell pixel size (C# `_sixelSupported`,
    /// `_cellPixelWidth`/`Height`).
    pub(crate) capabilities: crate::terminal_caps::TerminalCapabilities,
    /// C# `_imagePreviewsEffective`: the config flag and Sixel support.
    pub(crate) image_previews_effective: bool,
}

impl App {
    #[must_use]
    pub fn new(config: AppConfig) -> Self {
        let mut app = Self {
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
            preview: preview::PreviewState::default(),
            capabilities: crate::terminal_caps::TerminalCapabilities::DEFAULT,
            image_previews_effective: false,
            bookmark_store: crate::fs::bookmark_store::BookmarkStore::new(None),
            pipeline: crate::input::input_pipeline::InputPipeline::new(),
            git_status_loader: crate::app::git_status_loader::GitStatusLoader::new(),
            git_action_runner: crate::app::git_action_runner::GitActionRunner::new(),
            file_operation_runner: crate::app::file_operation_runner::FileOperationRunner::new(),
            directory_size_loader: crate::app::directory_size_loader::DirectorySizeLoader::new(),
            inline_dir_size_loader: crate::app::inline_dir_size_loader::InlineDirSizeLoader::new(),
            // fs_watcher is rewired to the real pipeline below (needs the
            // pipeline built first).
            fs_watcher: crate::app::fs_watcher::FileSystemWatcherManager::new(
                std::sync::mpsc::channel().0,
            ),
            properties_dir_size_path: None,
            properties_dir_size_text: None,
            properties_scroll_offset: 0,
            properties_content_height: 0,
            inline_dir_sizes: None,
            current_drive_media_type: crate::fs::DriveMediaType::Unknown,
            detect_drive_media_type: crate::fs::drive_media_type::detect,
            clipboard_paths: Vec::new(),
            clipboard_is_cut: false,
            os_clipboard: if cfg!(test) { OsClipboard::Fake(FakeClipboard::default()) } else { OsClipboard::System },
            request_full_redraw: false,
            file_finder: None,
            file_op_label: String::new(),
            file_op_progress: None,
            text_input_target: None,
            git_queries: std::sync::Arc::new(crate::app::git_status_loader::GitUtilsQueries),
            current_repo_root: None,
            current_branch_name: None,
            git_statuses: None,
            ahead_behind_text: None,
        };

        // Rewire the watcher to the app's real pipeline (the struct literal
        // had to use a throwaway sender because pipeline is owned by Self).
        app.fs_watcher = crate::app::fs_watcher::FileSystemWatcherManager::new(app.pipeline.sender());

        app
    }

    /// The settings half of `App.Run`'s setup: resolves the start path and
    /// copies the config into the listing and pane state. Split out of
    /// `run` so tests apply settings exactly as startup does.
    pub fn apply_startup_config(&mut self) {
        let start = if self.config.start_path.is_empty() { App::default_start_path() } else { self.config.start_path.clone() };
        self.current_path = capitalize_drive_letter(&dialogs::get_full_path(&start));
        self.directory_contents.show_hidden_files = self.config.show_hidden_files;
        self.directory_contents.show_system_files = self.config.show_system_files;
        self.directory_contents.sort_mode = self.config.sort_mode;
        self.directory_contents.sort_ascending = self.config.sort_ascending;
        self.parent_pane_enabled = self.config.parent_pane_enabled;
        self.preview_pane_enabled = self.config.preview_pane_enabled;

        if let Some(start_file_name) = self.config.start_file_name.clone() {
            let entries = self.directory_contents.get_entries(&self.current_path);

            if let Some(index) = entries.iter().position(|e| e.name.eq_ignore_ascii_case(&start_file_name)) {
                self.selected_index = index;
            }
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
        self.apply_startup_config();
        self.bookmark_store.load();

        // C# `using var terminal = new TerminalSetup()`: console modes and
        // the alternate screen for the lifetime of the loop
        let mut terminal = crate::terminal_setup::TerminalSetup::new();
        self.set_capabilities(terminal.capabilities());

        // C# creates the input source after TerminalSetup, so the capability
        // query's replies are read before the input reader starts
        let pump_cancel = crate::input::CancelToken::new();
        let pump = spawn_input_pump(self.pipeline.sender(), pump_cancel.clone());

        self.update_terminal_title();
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

            // Ensure filesystem watcher tracks the current directory (App.cs:264)
            self.tick_file_system_watcher();

            if self.request_full_redraw {
                self.request_full_redraw = false;
                buffer.force_full_redraw();
            }

            // Render
            buffer.clear();
            self.render(&mut buffer);
            flush_buffer(&mut buffer);

            // Sixel data goes out after the flush, bypassing the cell grid
            if let Some(sixel) = self.take_pending_sixel() {
                write_raw(&sixel);
            }

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
                    InputEvent::FileOperationComplete(event) => self.handle_file_operation_complete(event),
                    InputEvent::FileOperationProgress(event) => self.handle_file_operation_progress(event),
                    InputEvent::DirectorySizeReady(event) => self.handle_directory_size_ready(event),
                    InputEvent::InlineDirSizeReady(event) => self.handle_inline_dir_size_ready(event),
                    InputEvent::InlineDirSizeComplete(event) => self.handle_inline_dir_size_complete(event),
                    InputEvent::FileSystemChanged(event) => self.handle_file_system_changed(event),
                    InputEvent::FileFinderPartialResult(event) => self.handle_file_finder_partial_result(event),
                    InputEvent::FileFinderScanComplete(event) => self.handle_file_finder_scan_complete(event),
                    InputEvent::FileFinderSearchResult(event) => self.handle_file_finder_search_result(event),
                    InputEvent::PreviewReady(event) => self.handle_preview_ready(event),
                    InputEvent::ImagePreviewReady(event) => self.handle_image_preview_ready(event),
                    InputEvent::CombinedPreviewReady(event) => self.handle_combined_preview_ready(event),
                    InputEvent::MetadataReady(event) => self.handle_metadata_ready(event),
                    InputEvent::PreviewLoadingComplete(event) => self.handle_preview_loading_complete(event),
                    InputEvent::CloudDownloadComplete(event) => self.handle_cloud_download_complete(event),
                }
            }

            match current {
                InputEvent::Resize(resize) => self.handle_resize(resize, &mut buffer, &mut width, &mut height),
                InputEvent::Key(key) => self.handle_key(key),
                InputEvent::Mouse(mouse) => self.handle_mouse(mouse),
                InputEvent::Paste(text) => self.handle_paste_event(&text),
                InputEvent::GitStatusReady(event) => self.handle_git_status_ready(event),
                InputEvent::GitActionComplete(event) => self.handle_git_action_complete(event),
                InputEvent::FileOperationComplete(event) => self.handle_file_operation_complete(event),
                InputEvent::FileOperationProgress(event) => self.handle_file_operation_progress(event),
                // Like the C# main loop: loader events are handled whether they
                // arrive first or queued behind another event
                InputEvent::DirectorySizeReady(event) => self.handle_directory_size_ready(event),
                InputEvent::InlineDirSizeReady(event) => self.handle_inline_dir_size_ready(event),
                InputEvent::InlineDirSizeComplete(event) => self.handle_inline_dir_size_complete(event),
                InputEvent::FileSystemChanged(event) => self.handle_file_system_changed(event),
                InputEvent::FileFinderPartialResult(event) => self.handle_file_finder_partial_result(event),
                InputEvent::FileFinderScanComplete(event) => self.handle_file_finder_scan_complete(event),
                InputEvent::FileFinderSearchResult(event) => self.handle_file_finder_search_result(event),
                InputEvent::PreviewReady(event) => self.handle_preview_ready(event),
                InputEvent::ImagePreviewReady(event) => self.handle_image_preview_ready(event),
                InputEvent::CombinedPreviewReady(event) => self.handle_combined_preview_ready(event),
                InputEvent::MetadataReady(event) => self.handle_metadata_ready(event),
                InputEvent::PreviewLoadingComplete(event) => self.handle_preview_loading_complete(event),
                InputEvent::CloudDownloadComplete(event) => self.handle_cloud_download_complete(event),
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

        pump_cancel.cancel();
        let _ = pump.join();
        terminal.restore();

        if self.write_cwd {
            Some(self.current_path.clone())
        } else {
            None
        }
    }

    /// The Sixel write after a flush (App.cs:270-289): only while an image
    /// is pending, the preview is visible, and no modal is open. Returns the
    /// cursor move plus the Sixel data.
    pub(crate) fn take_pending_sixel(&mut self) -> Option<String> {
        let expanded = self.input_mode == InputMode::ExpandedPreview;
        if !self.preview.sixel_pending
            || !(self.preview_pane_enabled || expanded)
            || !matches!(self.input_mode, InputMode::Normal | InputMode::Search | InputMode::ExpandedPreview)
        {
            return None;
        }

        let sixel = self.preview.cached_sixel_data.as_ref()?;
        self.preview.sixel_pending = false;

        let pane = if expanded { self.layout.expanded_pane } else { self.layout.right_pane };
        let mut row = if self.preview.sixel_image_top > 0 { self.preview.sixel_image_top } else { pane.top };
        let mut col = pane.left;

        let (pixel_width, pixel_height) = (self.preview.cached_image_pixel_width, self.preview.cached_image_pixel_height);
        if !self.preview.is_combined_preview && expanded && pixel_width > 0 && pixel_height > 0 {
            (row, col) = pane.center_content(
                pixel_width / self.capabilities.cell_pixel_width.max(1),
                pixel_height / self.capabilities.cell_pixel_height.max(1),
            );
        }

        Some(format!("{}{sixel}", crate::ansi::move_cursor(row, col)))
    }

    /// Applies detected terminal capabilities (App.cs:237-241).
    pub fn set_capabilities(&mut self, capabilities: crate::terminal_caps::TerminalCapabilities) {
        self.capabilities = capabilities;
        self.image_previews_effective = self.config.image_previews_enabled && capabilities.sixel_supported;
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

            // Re-render the preview at the new size
            self.reload_preview_after_resize();
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
                            self.clear_preview_cache();
                            self.update_terminal_title();
                            self.refresh_git_status();
                        }
                    } else if self.active_provider_is_previewable() {
                        self.enter_expanded_preview();
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
                self.clear_preview_cache();
                self.refresh_git_status();
                self.request_full_redraw = true;
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
                self.clear_preview_cache();
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
                self.clear_preview_cache();
                self.request_full_redraw = true;
            }
            A::CycleSortMode => {
                self.config.sort_mode = match self.config.sort_mode {
                    SortMode::Name => SortMode::Modified,
                    SortMode::Modified => SortMode::Size,
                    SortMode::Size => SortMode::Extension,
                    SortMode::Extension => SortMode::Name,
                };
                self.directory_contents.sort_mode = self.config.sort_mode;
                self.directory_contents.invalidate_all();
                self.clear_preview_cache();
            }
            A::ToggleSortDirection => {
                self.config.sort_ascending = !self.config.sort_ascending;
                self.directory_contents.sort_ascending = self.config.sort_ascending;
                self.directory_contents.invalidate_all();
                self.clear_preview_cache();
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
                    self.show_text_input_dialog("Commit message", "", Some(dialogs::TextInputPurpose::Commit), None);
                }
            }
            A::GitPush => self.run_simple_git_action(crate::fs::git_utils::push),
            A::GitPushForceWithLease => self.run_simple_git_action(crate::fs::git_utils::push_force_with_lease),
            A::GitPull => self.run_simple_git_action(crate::fs::git_utils::pull),
            A::GitPullRebase => self.run_simple_git_action(crate::fs::git_utils::pull_rebase),
            A::GitFetch => self.run_simple_git_action(crate::fs::git_utils::fetch),
            A::OpenExternal
            | A::Rename
            | A::Delete
            | A::DeletePermanently
            | A::Copy
            | A::Cut
            | A::Paste
            | A::CopyAbsolutePath
            | A::CopyGitRelativePath
            | A::NewFile
            | A::NewDirectory
            | A::CreateSymlink => self.dispatch_file_action(action),
            A::OpenTerminal => self.open_terminal_here(),
            A::ShowProperties => self.show_properties(),
            A::ShowFileFinder => self.show_file_finder(),
            A::ShowPreviewMenu => self.show_preview_menu(),
            A::DownloadCloudFile => {
                let entries = self.get_visible_entries();

                if let Some(entry) = entries.get(self.selected_index)
                    && entry.is_cloud_placeholder
                {
                    let path = entry.full_path.clone();
                    self.download_cloud_file(path);
                }
            }
            _ => {}
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
            self.update_terminal_title();
            self.refresh_git_status();
            let drive_entries = self.directory_contents.get_entries(DRIVES_PATH);
            let root = drive_root(&old_path)
                .map(|r| r.trim_end_matches(['\\', '/']).to_string())
                .unwrap_or_default();
            let idx = drive_entries.iter().position(|e| e.name.eq_ignore_ascii_case(&root));
            self.selected_index = idx.unwrap_or(0);
        } else if let Some(parent) = Path::new(&self.current_path).parent() {
            let parent_path = capitalize_drive_letter(&parent.to_string_lossy());
            self.current_path = parent_path;
            self.update_terminal_title();
            self.refresh_git_status();
            let parent_entries = self.directory_contents.get_entries(&self.current_path);
            let old_name = Path::new(&old_path)
                .file_name()
                .map(|n| n.to_string_lossy().to_string())
                .unwrap_or_default();
            let idx = parent_entries.iter().position(|e| e.name.eq_ignore_ascii_case(&old_name));
            self.selected_index = idx.unwrap_or_else(|| *self.selected_index_per_dir.get(&self.current_path).unwrap_or(&0));
        }

        self.scroll_offset = 0;
        self.clear_preview_cache();
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


    /// The OSC 0 sequence `update_terminal_title` writes: the current path
    /// when the title setting is on, an empty title otherwise.
    #[must_use]
    pub fn terminal_title_sequence(&self) -> String {
        if self.config.terminal_title_enabled {
            crate::ansi::set_title(&format!("wade - {}", self.current_path))
        } else {
            crate::ansi::set_title("")
        }
    }

    /// Port of `UpdateTerminalTitle` (App.cs): OSC 0 title set/clear.
    pub fn update_terminal_title(&self) {
        use std::io::Write;

        // print! (not a raw stdout write) so test runs capture it
        print!("{}", self.terminal_title_sequence());
        let _ = std::io::stdout().flush();
    }

    /// Port of the mouse-event dispatch (App.cs:541-573) and
    /// `HandleMouseEvent` (App.cs:1731): scroll wheel, pane click navigation,
    /// right-click context menu.
    pub fn handle_mouse(&mut self, mouse: crate::input::MouseEvent) {
        use crate::input::MouseButton;

        if self.input_mode == InputMode::ExpandedPreview {
            self.handle_expanded_preview_mouse(&mouse);
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
        self.clear_preview_cache();
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

        self.refresh_inline_dir_sizes();
    }

    /// Port of `RefreshInlineDirSizes` (App.cs:2625-2675).
    fn refresh_inline_dir_sizes(&mut self) {
        self.inline_dir_sizes = None;
        self.directory_contents.dir_sizes = None;

        if !self.config.size_column_enabled || self.current_path == DRIVES_PATH {
            self.inline_dir_size_loader.cancel();
            return;
        }

        self.current_drive_media_type = crate::fs::directory_contents::drive_root(&self.current_path)
            .map_or(crate::fs::DriveMediaType::Unknown, |root| (self.detect_drive_media_type)(&root));

        if !Self::should_compute_inline_dir_sizes_impl(self.current_drive_media_type, &self.config) {
            self.inline_dir_size_loader.cancel();
            return;
        }

        let entries = self.directory_contents.get_entries(&self.current_path);
        let dir_paths: Vec<String> = entries
            .iter()
            .filter(|entry| entry.is_directory && !entry.is_drive)
            .map(|entry| entry.full_path.clone())
            .collect();

        if !dir_paths.is_empty() {
            self.inline_dir_sizes = Some(HashMap::new());
            let sender = self.pipeline.sender();
            self.inline_dir_size_loader.begin_load(&self.current_path, &dir_paths, sender);
        } else {
            self.inline_dir_size_loader.cancel();
        }
    }

    /// Port of `ShouldComputeInlineDirSizes` (App.cs:2650).
    pub(crate) fn should_compute_inline_dir_sizes_impl(
        drive_type: crate::fs::DriveMediaType,
        config: &AppConfig,
    ) -> bool {
        use crate::fs::DriveMediaType as M;
        match drive_type {
            M::Ssd => config.dir_size_ssd_enabled,
            M::Hdd => config.dir_size_hdd_enabled,
            M::Network => config.dir_size_network_enabled,
            M::Removable => config.dir_size_ssd_enabled,
            M::Unknown => false,
        }
    }

    /// Port of `HandleDirectorySizeReady` (App.cs:1416).
    pub fn handle_directory_size_ready(&mut self, event: crate::input::DirectorySizeReadyEvent) {
        if Some(event.path.as_str()) != self.properties_dir_size_path.as_deref() {
            return;
        }

        let formatted = format_size_buf(event.total_bytes);
        self.properties_dir_size_text =
            Some(format!("{} ({} bytes)", formatted, group_thousands(event.total_bytes)));
    }

    /// Port of `HandleInlineDirSizeReady` (App.cs:1429).
    pub fn handle_inline_dir_size_ready(&mut self, event: crate::input::InlineDirSizeReadyEvent) {
        if event.parent_path != self.current_path {
            return;
        }

        self.inline_dir_sizes
            .get_or_insert_with(HashMap::new)
            .insert(event.directory_path, event.total_bytes);
    }

    /// Port of `HandleInlineDirSizeComplete` (App.cs:1440).
    pub fn handle_inline_dir_size_complete(&mut self, event: crate::input::InlineDirSizeCompleteEvent) {
        if event.parent_path != self.current_path {
            return;
        }

        // C# shares one dictionary between the two; keep the App's copy for rendering
        self.directory_contents.dir_sizes.clone_from(&self.inline_dir_sizes);

        if self.directory_contents.sort_mode == crate::fs::directory_contents::SortMode::Size {
            self.directory_contents.invalidate(&self.current_path);
        }
    }

    /// Port of `HandleFileSystemChanged` (App.cs:1531).
    pub fn handle_file_system_changed(&mut self, event: crate::input::FileSystemChangedEvent) {
        if !paths_equal_ignore_case(&event.directory_path, &self.current_path) {
            return; // Stale event for a directory we've navigated away from
        }

        // Preserve selection by name
        let entries = self.get_visible_entries();
        let selected_name = entries.get(self.selected_index).map(|entry| entry.name.clone());

        // Invalidate cache
        if event.full_refresh {
            self.directory_contents.invalidate_all();
        } else {
            self.directory_contents.invalidate(&self.current_path);
        }

        // Restore selection
        let new_entries = self.get_visible_entries();
        let mut selected_survived = false;

        if let Some(name) = selected_name.as_ref().filter(|_| !new_entries.is_empty()) {
            match new_entries.iter().position(|entry| names_equal_ignore_case(&entry.name, name)) {
                Some(index) => {
                    self.selected_index = index;
                    selected_survived = true;
                }
                None => {
                    self.selected_index = self.selected_index.min(new_entries.len() - 1);
                }
            }
        } else {
            self.selected_index = self.selected_index.min(new_entries.len().saturating_sub(1));
        }

        // Only clear the preview if the selected file was deleted/renamed away
        if !selected_survived {
            self.clear_preview_cache();
        }

        self.refresh_git_status();
        self.request_full_redraw = true;
    }

    /// Port of the watcher tick at the top of the render loop (App.cs:264).
    pub fn tick_file_system_watcher(&mut self) {
        let path = self.current_path.clone();
        self.fs_watcher.watch(&path);
    }

    /// Port of `ShowProperties` dispatch (App.cs:810-828 and the palette
    /// site at App.cs:3546).
    fn show_properties(&mut self) {
        let entries = self.get_visible_entries();

        if entries.is_empty() || self.selected_index >= entries.len() {
            return;
        }

        self.input_mode = InputMode::Properties;
        self.properties_scroll_offset = 0;
        let entry = entries[self.selected_index].clone();

        if entry.is_directory && !entry.is_drive {
            self.properties_dir_size_path = Some(entry.full_path.clone());
            self.properties_dir_size_text = Some("Calculating\u{2026}".to_string());
            let sender = self.pipeline.sender();
            self.directory_size_loader.begin_calculation(&entry.full_path, sender);
        } else {
            self.properties_dir_size_path = None;
            self.properties_dir_size_text = None;
        }
    }

    /// Port of `HandlePropertiesKey` (App.cs:1948). Modifier-only keys are
    /// filtered by the caller.
    pub(crate) fn handle_properties_key(&mut self, key: &KeyEvent) {
        use crate::console_key::ConsoleKey;

        let close = |app: &mut Self| {
            app.input_mode = InputMode::Normal;
            app.directory_size_loader.cancel();
            app.properties_dir_size_path = None;
            app.properties_dir_size_text = None;
        };

        match key.key {
            ConsoleKey::Escape | ConsoleKey::Enter | ConsoleKey::I | ConsoleKey::Q => close(self),
            ConsoleKey::UpArrow | ConsoleKey::K => {
                if self.properties_scroll_offset > 0 {
                    self.properties_scroll_offset -= 1;
                }
            }
            ConsoleKey::DownArrow | ConsoleKey::J => {
                self.properties_scroll_offset += 1;
            }
            ConsoleKey::PageUp => {
                let page_size = (self.properties_content_height / 2).max(1);
                self.properties_scroll_offset = self.properties_scroll_offset.saturating_sub(page_size);
            }
            ConsoleKey::PageDown => {
                let page_size = (self.properties_content_height / 2).max(1);
                self.properties_scroll_offset = self.properties_scroll_offset.saturating_add(page_size);
            }
            ConsoleKey::Home => {
                self.properties_scroll_offset = 0;
            }
            ConsoleKey::End => {
                self.properties_scroll_offset = usize::MAX; // clamped during render
            }
            _ => close(self),
        }
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

    /// Port of `ExecuteDelete` (App.cs:2668) plus the delete-confirm target
    /// building from `DispatchFileAction` (App.cs:3210-3246).
    pub fn execute_delete(&mut self, targets: Vec<String>, permanent: bool) {
        let sender = self.pipeline.sender();
        self.file_operation_runner
            .begin(crate::app::file_operation_runner::delete_operation(targets, permanent), sender);
        self.input_mode = InputMode::FileOperation;
        self.file_op_label = "Deleting".to_string();
    }

    /// Port of `ExecutePaste` (App.cs:1012-1023).
    pub fn execute_paste(&mut self, overwrite: bool) {
        self.execute_paste_internal(self.clipboard_paths.clone(), self.clipboard_is_cut, overwrite);
    }

    /// Port of `DownloadCloudFile` (App.cs): reading the file triggers the
    /// Cloud Files recall; the listing refreshes afterwards.
    fn download_cloud_file(&mut self, path: String) {
        self.show_notification("Downloading\u{2026}", NotificationKind::Info);
        let sender = self.pipeline.sender();

        std::thread::spawn(move || {
            let error = hydrate_file(&path).err().map(|err| err.to_string());
            let _ = sender.send(InputEvent::CloudDownloadComplete(crate::input::CloudDownloadCompleteEvent { error }));
        });
    }

    pub fn handle_cloud_download_complete(&mut self, event: crate::input::CloudDownloadCompleteEvent) {
        self.directory_contents.invalidate_all();

        match event.error {
            None => self.show_notification("Download complete", NotificationKind::Success),
            Some(error) => self.show_notification(&format!("Download failed: {error}"), NotificationKind::Error),
        }
    }

    /// Copies text to the OS clipboard, with C#'s success/failure
    /// notifications.
    pub(crate) fn copy_text_to_clipboard(&mut self, text: &str, success: &str) {
        if self.os_clipboard.set_text(text) {
            self.show_notification(success, NotificationKind::Success);
        } else {
            self.show_notification("Clipboard not available", NotificationKind::Error);
        }
    }

    /// Port of the `CopyGitRelativePath` body: `path` relative to the repo
    /// root of the current directory, with forward slashes.
    pub(crate) fn copy_git_relative_path(&mut self, path: &str) {
        match crate::fs::git_utils::find_repo_root(&self.current_path) {
            None => self.show_notification("Not inside a git repository", NotificationKind::Error),
            Some(repo_root) => {
                let mut relative = crate::fs::git_utils::relative_path(&repo_root, path);

                if relative.is_empty() {
                    relative = ".".to_string();
                }

                self.copy_text_to_clipboard(&relative, "Copied git-relative path to clipboard");
            }
        }
    }

    /// Ports of the Copy/Cut arms: marked paths or the selected entry go to
    /// wade's clipboard and to the OS clipboard.
    fn set_clipboard(&mut self, entries: &[FileSystemEntry], is_cut: bool) {
        let verb = if is_cut { "Cut" } else { "Copied" };

        if !self.marked_paths.is_empty() {
            self.clipboard_paths = self.marked_paths.iter().cloned().collect();
            self.clipboard_is_cut = is_cut;
            self.show_notification(&format!("{verb} {} item(s)", self.marked_paths.len()), NotificationKind::Success);
        } else if let Some(entry) = entries.get(self.selected_index) {
            self.clipboard_paths = vec![entry.full_path.clone()];
            self.clipboard_is_cut = is_cut;
            self.show_notification(&format!("{verb} '{}'", entry.name), NotificationKind::Success);
        }

        let _ = self.os_clipboard.set_files(&self.clipboard_paths, self.clipboard_is_cut);
    }

    /// Port of the Paste arm: OS clipboard files win over wade's own, then
    /// conflicts ask before overwriting.
    fn paste_from_clipboard(&mut self) {
        if let Some((paths, is_cut)) = self.os_clipboard.get_files()
            && !paths.is_empty()
        {
            self.clipboard_paths = paths;
            self.clipboard_is_cut = is_cut;
        }

        if self.clipboard_paths.is_empty() {
            self.show_notification("Clipboard is empty", NotificationKind::Error);
            return;
        }

        let conflicts = self
            .clipboard_paths
            .iter()
            .filter(|path| {
                Path::new(path.as_str())
                    .file_name()
                    .is_some_and(|name| Path::new(&self.current_path).join(name).exists())
            })
            .count();

        if conflicts > 0 {
            self.show_confirm_dialog(
                "Overwrite",
                &format!("{conflicts} item(s) already exist. Overwrite?"),
                dialogs::ConfirmAction::Paste { overwrite: true },
            );
        } else {
            self.execute_paste(false);
        }
    }

    /// Mechanics of `ExecutePaste` for explicit sources.
    pub fn execute_paste_internal(&mut self, sources: Vec<String>, is_cut: bool, overwrite: bool) {
        let sender = self.pipeline.sender();
        self.file_operation_runner.begin(
            crate::app::file_operation_runner::paste_operation(
                sources,
                self.current_path.clone(),
                is_cut,
                overwrite,
                self.config.copy_symlinks_as_links_enabled,
            ),
            sender,
        );
        self.input_mode = InputMode::FileOperation;
        self.file_op_label = if is_cut { "Moving" } else { "Copying" }.to_string();
    }

    /// Port of `HandleFileOperationProgress` (enhancement): stores the live
    /// progress for the overlay.
    pub fn handle_file_operation_progress(&mut self, event: crate::input::FileOperationProgressEvent) {
        self.file_op_progress = Some(event);
    }

    /// Port of `HandleFileOperationComplete` (App.cs:2704).
    pub fn handle_file_operation_complete(&mut self, event: crate::input::FileOperationCompleteEvent) {
        self.input_mode = InputMode::Normal;
        self.file_op_progress = None;
        self.directory_contents.invalidate(&self.current_path);
        self.invalidate_filtered_entries();

        if event.was_cut && event.error_count == 0 {
            self.clipboard_paths.clear();
        }

        self.marked_paths.clear();
        self.refresh_git_status();

        if event.error_count > 0 {
            self.show_notification(
                &format!("{} {}, {} failed", self.file_op_label, event.success_count, event.error_count),
                NotificationKind::Error,
            );
        } else {
            self.show_notification(
                &format!("{} {} item(s)", self.file_op_label, event.success_count),
                NotificationKind::Success,
            );
        }
    }

    /// Port of `HandleGitStatusReady`'s sibling for filtered lists: C# calls
    /// `InvalidateFilteredEntries()` after file operations and config apply.
    pub fn invalidate_filtered_entries(&mut self) {
        self.filtered_entries = None;
    }

    /// Port of `DispatchFileAction` (App.cs:3132-3530).
    pub fn dispatch_file_action(&mut self, action: AppAction) {
        use AppAction as A;
        let entries = self.get_visible_entries();

        match action {
            A::Copy => self.set_clipboard(&entries, false),
            A::Cut => self.set_clipboard(&entries, true),
            A::Paste => self.paste_from_clipboard(),
            A::CopyAbsolutePath => {
                if let Some(entry) = entries.get(self.selected_index) {
                    let path = entry.full_path.clone();
                    self.copy_text_to_clipboard(&path, "Copied path to clipboard");
                }
            }
            A::CopyGitRelativePath => {
                if let Some(entry) = entries.get(self.selected_index) {
                    let path = entry.full_path.clone();
                    self.copy_git_relative_path(&path);
                }
            }
            A::OpenExternal => {
                if !entries.is_empty() && self.selected_index < entries.len() {
                    let entry = entries[self.selected_index].clone();
                    match open_external(&entry.full_path) {
                        Ok(()) => self.show_notification(&format!("Opened '{}'", entry.name), NotificationKind::Success),
                        Err(err) => self.show_notification(&format!("Error: {err}"), NotificationKind::Error),
                    }
                }
            }
            A::Rename => {
                if !entries.is_empty() && self.selected_index < entries.len() {
                    let entry = entries[self.selected_index].clone();
                    self.show_text_input_dialog(
                        "Rename",
                        &entry.name,
                        Some(dialogs::TextInputPurpose::Rename),
                        Some(entry.full_path),
                    );
                }
            }
            A::Delete | A::DeletePermanently => {
                if entries.is_empty() {
                    return;
                }

                let (targets, prompt) = if !self.marked_paths.is_empty() {
                    (self.marked_paths.iter().cloned().collect::<Vec<String>>(), format!("Delete {} item(s)?", self.marked_paths.len()))
                } else if self.selected_index < entries.len() {
                    (
                        vec![entries[self.selected_index].full_path.clone()],
                        format!("Delete '{}'?", entries[self.selected_index].name),
                    )
                } else {
                    return;
                };

                let permanent = action == A::DeletePermanently;
                let is_permanent = permanent || !cfg!(windows);
                let title = if is_permanent { "Permanently Delete" } else { "Delete" };
                let warning = if is_permanent { "\nThis cannot be undone!" } else { "" };

                if self.config.confirm_delete_enabled {
                    self.show_confirm_dialog(
                        title,
                        &format!("{prompt}{warning}"),
                        dialogs::ConfirmAction::DeleteFiles { targets, permanent },
                    );
                } else {
                    self.execute_delete(targets, permanent);
                }
            }
            A::NewFile => {
                self.show_text_input_dialog("New File", "", Some(dialogs::TextInputPurpose::NewFile), None);
            }
            A::NewDirectory => {
                self.show_text_input_dialog("New Directory", "", Some(dialogs::TextInputPurpose::NewDirectory), None);
            }
            A::CreateSymlink
                if !entries.is_empty() && self.selected_index < entries.len() =>
            {
                let entry = entries[self.selected_index].clone();
                let initial = format!("{}_link", entry.name);
                self.show_text_input_dialog(
                    "Create Symlink",
                    &initial,
                    Some(dialogs::TextInputPurpose::CreateSymlink),
                    Some(entry.full_path),
                );
            }
            _ => {}
        }
    }

    /// Port of the OpenTerminal dispatch arm (App.cs:946-957).
    fn open_terminal_here(&mut self) {
        match open_terminal(&self.current_path) {
            Ok(()) => self.show_notification("Opened terminal", NotificationKind::Success),
            Err(err) => self.show_notification(&format!("Error: {err}"), NotificationKind::Error),
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
            self.clear_preview_cache();
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
        if self.input_mode == InputMode::ExpandedPreview {
            self.render_expanded_preview(buffer);
            return;
        }

        let entries = self.get_visible_entries();
        let show_search_bar = self.input_mode == crate::input::InputMode::Search || !self.search_filter.is_empty();
        let mut file_list_pane = self.layout.center_pane;
        if show_search_bar {
            file_list_pane.height -= 1;
        }

        let is_drive_view = self.current_path == DRIVES_PATH;

        // Column headers + separator line
        if self.config.column_headers_enabled && file_list_pane.height > 2 {
            // C#: the rows add a status column for git statuses or cloud
            // placeholders, so the headers must leave room for it too
            let has_status_col = self.git_statuses.is_some() || entries.iter().any(|e| e.is_cloud_placeholder);
            let header_rect = Rect2::new(file_list_pane.left, file_list_pane.top, file_list_pane.width, 2);
            PaneRenderer::render_column_headers(
                buffer,
                header_rect,
                self.config.show_icons_enabled,
                self.config.size_column_enabled,
                self.config.date_column_enabled,
                is_drive_view,
                has_status_col,
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
            self.inline_dir_sizes.as_ref(),
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
            self.preview.cached_file_type_label.as_deref(),
            self.preview.cached_encoding.as_deref(),
            self.preview.cached_line_ending.as_deref(),
            self.notification.clone(),
            self.marked_paths.len(),
            self.config.sort_mode,
            self.config.sort_ascending,
            self.clipboard_paths.len(),
            self.clipboard_is_cut,
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
            let path = selected.full_path.clone();
            self.render_file_preview(buffer, &path);
        }
    }

    /// Port of `RenderExpandedPreview`: the preview fills the screen above
    /// the status bar, which shows the previewed file's path.
    fn render_expanded_preview(&mut self, buffer: &mut ScreenBuffer) {
        self.render_expanded_preview_pane(buffer);

        let entries = self.get_visible_entries();
        let selected_entry = entries.get(self.selected_index);
        let mut display_path = self.preview.cached_path.clone().unwrap_or_else(|| self.current_path.clone());
        if display_path == DRIVES_PATH {
            display_path = "Drives".to_string();
        }

        crate::ui::status_bar::render(
            buffer,
            self.layout.status_bar,
            &display_path,
            entries.len(),
            self.selected_index,
            selected_entry,
            self.preview.cached_file_type_label.as_deref(),
            self.preview.cached_encoding.as_deref(),
            self.preview.cached_line_ending.as_deref(),
            self.notification.clone(),
            self.marked_paths.len(),
            self.config.sort_mode,
            self.config.sort_ascending,
            self.clipboard_paths.len(),
            self.clipboard_is_cut,
            self.current_branch_name.as_deref(),
            self.ahead_behind_text.as_deref(),
        );
    }
}

use crate::fs::directory_contents::{capitalize_drive_letter, drive_root};
use crate::ui::layout::Rect as Rect2;

/// Port of `Process.Start(path) { UseShellExecute = true }` (App.cs:3143).
/// Windows: `ShellExecuteExW` on its own STA thread, as .NET does. Unix
/// (.NET `Process.Unix`): an executable file runs directly (falling back
/// to the opener when exec reports ENOEXEC); anything else goes to the
/// first opener found: xdg-open, gnome-open or kfmclient on Linux,
/// /usr/bin/open on macOS.
fn open_external(path: &str) -> Result<(), std::io::Error> {
    #[cfg(windows)]
    {
        let path = path.to_string();
        std::thread::spawn(move || shell_execute(&path))
            .join()
            .unwrap_or_else(|_| Err(std::io::Error::other("ShellExecuteEx failed")))
    }

    #[cfg(unix)]
    {
        use std::process::{Command, Stdio};

        let quiet = |command: &mut Command| -> std::io::Result<()> {
            command.stdin(Stdio::null()).stdout(Stdio::null()).stderr(Stdio::null()).spawn().map(|_| ())
        };

        if is_executable_file(path) {
            match quiet(&mut Command::new(path)) {
                Err(err) if err.raw_os_error() == Some(libc::ENOEXEC) => {}
                result => return result,
            }
        }

        let openers: &[&str] = if cfg!(target_os = "macos") {
            &["/usr/bin/open"]
        } else {
            &["xdg-open", "gnome-open", "kfmclient"]
        };

        let opener = openers.iter().find(|program| find_program(program).is_some()).ok_or_else(|| {
            std::io::Error::new(std::io::ErrorKind::NotFound, "No program found to open the file")
        })?;
        quiet(Command::new(opener).arg(path))
    }
}

#[cfg(windows)]
fn shell_execute(path: &str) -> std::io::Result<()> {
    use windows_sys::Win32::System::Com::{CoInitializeEx, CoUninitialize, COINIT_APARTMENTTHREADED, COINIT_DISABLE_OLE1DDE};
    use windows_sys::Win32::UI::Shell::{ShellExecuteExW, SEE_MASK_FLAG_DDEWAIT, SEE_MASK_FLAG_NO_UI, SHELLEXECUTEINFOW};

    let file: Vec<u16> = path.encode_utf16().chain(std::iter::once(0)).collect();

    unsafe {
        let com = CoInitializeEx(std::ptr::null(), (COINIT_APARTMENTTHREADED | COINIT_DISABLE_OLE1DDE) as u32);
        let mut info: SHELLEXECUTEINFOW = std::mem::zeroed();
        info.cbSize = std::mem::size_of::<SHELLEXECUTEINFOW>() as u32;
        info.fMask = SEE_MASK_FLAG_DDEWAIT | SEE_MASK_FLAG_NO_UI;
        info.lpFile = file.as_ptr();
        info.nShow = 1; // SW_SHOWNORMAL
        let ok = ShellExecuteExW(&mut info) != 0;
        let error = std::io::Error::last_os_error();

        if com >= 0 {
            CoUninitialize();
        }

        if ok { Ok(()) } else { Err(error) }
    }
}

/// .NET `IsExecutable`: an existing non-directory the user may execute.
#[cfg(unix)]
fn is_executable_file(path: &str) -> bool {
    let Ok(c_path) = std::ffi::CString::new(path) else {
        return false;
    };

    std::fs::metadata(path).is_ok_and(|m| !m.is_dir()) && unsafe { libc::access(c_path.as_ptr(), libc::X_OK) } == 0
}

#[cfg(unix)]
fn find_program(program: &str) -> Option<std::path::PathBuf> {
    if program.contains('/') {
        return is_executable_file(program).then(|| program.into());
    }

    std::env::var_os("PATH").and_then(|paths| {
        std::env::split_paths(&paths)
            .map(|dir| dir.join(program))
            .find(|candidate| is_executable_file(&candidate.to_string_lossy()))
    })
}

/// Port of `OpenTerminalHere` (App.cs:979-1010): wt.exe with cmd fallback on
/// Windows, $SHELL on unix. The child is not waited on.
fn open_terminal(working_directory: &str) -> std::io::Result<()> {
    #[cfg(windows)]
    {
        use std::os::windows::process::CommandExt;
        let wt = std::process::Command::new("wt.exe")
            .args(["-w", "0", "new-tab", "-d", working_directory])
            .creation_flags(0x0800_0000) // CREATE_NO_WINDOW
            .spawn();
        match wt {
            Ok(_) => Ok(()),
            Err(_) => {
                let comspec =
                    std::env::var("COMSPEC").unwrap_or_else(|_| "cmd.exe".to_string());
                std::process::Command::new(comspec)
                    .current_dir(working_directory)
                    .spawn()
                    .map(|_| ())
            }
        }
    }

    #[cfg(not(windows))]
    {
        let shell = std::env::var("SHELL").unwrap_or_else(|_| "/bin/sh".to_string());
        std::process::Command::new(shell)
            .current_dir(working_directory)
            .spawn()
            .map(|_| ())
    }
}
/// Case-insensitive path comparison on every platform (C#
/// `OrdinalIgnoreCase` paths, regardless of OS).
#[must_use]
pub(crate) fn paths_equal_ignore_case(a: &str, b: &str) -> bool {
    a.eq_ignore_ascii_case(b)
}

/// Case-insensitive file-name comparison (C# `OrdinalIgnoreCase` names).
#[must_use]
fn names_equal_ignore_case(a: &str, b: &str) -> bool {
    paths_equal_ignore_case(a, b)
}

/// C# `"{value:N0}"` invariant format: thousands-grouped with commas.
#[must_use]
pub(crate) fn group_thousands(value: i64) -> String {
    let digits = value.abs().to_string();
    let mut grouped = String::new();
    let count = digits.len();

    for (index, ch) in digits.chars().enumerate() {
        grouped.push(ch);
        let remaining = count - index - 1;
        if remaining > 0 && remaining.is_multiple_of(3) {
            grouped.push(',');
        }
    }

    if value < 0 {
        format!("-{grouped}")
    } else {
        grouped
    }
}

/// `FormatHelpers.FormatSize` convenience wrapper returning a String.
#[must_use]
pub(crate) fn format_size_buf(bytes: i64) -> String {
    let mut buf = ['\0'; 32];
    let n = crate::ui::format_helpers::format_size(&mut buf, bytes);
    buf[..n].iter().collect()
}

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

/// Reads the whole file so Windows Cloud Files recalls (downloads) it.
/// OneDrive placeholders are recall-on-data-access: opening the file
/// without reading it does not hydrate it.
pub(crate) fn hydrate_file(path: &str) -> std::io::Result<()> {
    let mut file = std::fs::File::open(path)?;
    std::io::copy(&mut file, &mut std::io::sink())?;
    Ok(())
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
    #[cfg(windows)]
    {
        crate::input::windows::window_size_pub()
    }
    #[cfg(unix)]
    {
        crate::input::unix::window_size()
    }
}

/// The C# `InputPipeline` reader thread: owns the platform input source
/// and forwards its events into the app's queue until cancelled.
fn spawn_input_pump(
    sender: std::sync::mpsc::Sender<InputEvent>,
    cancel: crate::input::CancelToken,
) -> std::thread::JoinHandle<()> {
    use crate::input::InputSource;

    std::thread::spawn(move || {
        #[cfg(windows)]
        let mut source: Box<dyn InputSource> = Box::new(crate::input::windows::WindowsInputSource::new());
        #[cfg(unix)]
        let mut source: Box<dyn InputSource> = match crate::input::unix::UnixInputSource::new() {
            Ok(source) => Box::new(source),
            // No controlling terminal: nothing to read
            Err(_) => return,
        };

        while let Some(event) = source.read_next(&cancel) {
            if sender.send(event).is_err() {
                break; // app loop gone
            }
        }
    })
}

pub(crate) fn clear_screen() {
    print!("{}", crate::ansi::CLEAR_SCREEN);
}

/// `ScreenBuffer.WriteRaw`: straight to stdout, flushed.
fn write_raw(data: &str) {
    use std::io::Write;
    let stdout = std::io::stdout();
    let mut lock = stdout.lock();
    let _ = lock.write_all(data.as_bytes());
    let _ = lock.flush();
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



#[cfg(test)]
mod tests {
    use super::{App, AppAction, AppConfig, InputMode};
    use crate::fs::DriveMediaType;
    use crate::input::FileSystemChangedEvent;

    #[test]
    fn should_compute_inline_dir_sizes_matches_csharp_theory() {
        // (drive type, ssd, hdd, network, expected) from InlineDirSizeTests.cs
        let cases = [
            (DriveMediaType::Ssd, true, false, false, true),
            (DriveMediaType::Ssd, false, false, false, false),
            (DriveMediaType::Hdd, true, true, false, true),
            (DriveMediaType::Hdd, true, false, false, false),
            (DriveMediaType::Network, true, false, true, true),
            (DriveMediaType::Network, true, false, false, false),
            (DriveMediaType::Removable, true, false, false, true), // follows SSD
            (DriveMediaType::Removable, false, false, false, false), // follows SSD
            (DriveMediaType::Unknown, true, true, true, false), // Unknown = disabled
        ];

        for (drive_type, ssd, hdd, network, expected) in cases {
            let config = AppConfig {
                dir_size_ssd_enabled: ssd,
                dir_size_hdd_enabled: hdd,
                dir_size_network_enabled: network,
                ..AppConfig::default()
            };
            assert_eq!(
                App::should_compute_inline_dir_sizes_impl(drive_type, &config),
                expected,
                "{drive_type:?} ssd={ssd} hdd={hdd} network={network}"
            );
        }
    }

    fn test_root(name: &str) -> std::path::PathBuf {
        let dir = std::env::temp_dir().join(format!("wade-app-{}-{}", std::process::id(), name));
        let _ = std::fs::remove_dir_all(&dir);
        std::fs::create_dir_all(&dir).unwrap();
        dir
    }

    /// An App listing `root` with git disabled (no subprocesses).
    fn app_at(root: &std::path::Path, files: &[&str]) -> App {
        for file in files {
            std::fs::write(root.join(file), "x").unwrap();
        }

        let mut app = App::new(AppConfig {
            git_status_enabled: false,
            ..AppConfig::default()
        });
        app.current_path = root.to_string_lossy().into_owned();
        app
    }

    #[test]
    fn cycle_sort_mode_wraps_back_to_name() {
        use crate::fs::directory_contents::SortMode;

        let mut app = App::new(AppConfig { git_status_enabled: false, ..AppConfig::default() });
        let mut seen = Vec::new();
        for _ in 0..4 {
            app.dispatch(AppAction::CycleSortMode);
            seen.push(app.directory_contents.sort_mode);
        }

        assert_eq!(seen, [SortMode::Modified, SortMode::Size, SortMode::Extension, SortMode::Name]);
        app.dispatch(AppAction::ToggleSortDirection);
        assert!(!app.directory_contents.sort_ascending);
    }

    #[test]
    fn inline_dir_sizes_render_in_center_pane_and_survive_completion() {
        use crate::input::{InlineDirSizeCompleteEvent, InlineDirSizeReadyEvent};

        let root = test_root("inline-sizes");
        std::fs::create_dir_all(root.join("sub")).unwrap();
        let mut app = app_at(&root, &[]);
        app.set_screen_size(100, 30);
        app.layout.calculate(100, 30, true, true);
        let sub = root.join("sub").to_string_lossy().into_owned();
        let row_of = |app: &mut App| -> String {
            let mut buffer = crate::screen::ScreenBuffer::new(100, 30);
            app.render(&mut buffer);
            (0..30).map(|row| buffer.row_text(row)).find(|text| text.contains("sub")).unwrap_or_default()
        };

        app.inline_dir_sizes = Some(std::collections::HashMap::new());
        app.handle_inline_dir_size_ready(InlineDirSizeReadyEvent {
            parent_path: app.current_path.clone(),
            directory_path: sub.clone(),
            total_bytes: 123_456_789,
        });
        assert!(row_of(&mut app).contains("117.7 MB"), "size shown while streaming");

        app.handle_inline_dir_size_complete(InlineDirSizeCompleteEvent { parent_path: app.current_path.clone() });
        assert!(row_of(&mut app).contains("117.7 MB"), "size still shown after completion");
        assert_eq!(app.directory_contents.dir_sizes.as_ref().and_then(|sizes| sizes.get(&sub)), Some(&123_456_789));
        std::fs::remove_dir_all(&root).unwrap();
    }

    /// An App over `root` with icons off (so screen columns equal string
    /// indices), laid out at 100x30.
    fn plain_app(root: &std::path::Path) -> App {
        let mut app = App::new(AppConfig { git_status_enabled: false, show_icons_enabled: false, ..AppConfig::default() });
        app.current_path = root.to_string_lossy().into_owned();
        app.set_screen_size(100, 30);
        app.layout.calculate(100, 30, true, true);
        app
    }

    #[test]
    fn column_headers_reserve_the_status_column_when_git_statuses_are_shown() {
        // The status column only narrows the "Name" header, so use a center
        // pane narrow enough that it changes the header text
        let root = test_root("header-status");
        std::fs::write(root.join("a.txt"), "x").unwrap();
        let mut app = plain_app(&root);
        app.set_screen_size(17, 30);
        app.layout.calculate(17, 30, true, true);
        let path = root.join("a.txt").to_string_lossy().into_owned();
        app.git_statuses = Some(std::collections::HashMap::from([(path, crate::fs::GitFileStatus::MODIFIED)]));

        let mut buffer = crate::screen::ScreenBuffer::new(17, 30);
        app.render(&mut buffer);
        let pane = app.layout.center_pane;
        let header_of = |buffer: &crate::screen::ScreenBuffer| -> String {
            buffer.row_text(pane.top).chars().skip(pane.left as usize).take(pane.width as usize).collect()
        };
        let expected = |has_status_col: bool| -> String {
            let mut direct = crate::screen::ScreenBuffer::new(17, 30);
            let rect = crate::ui::layout::Rect::new(pane.left, pane.top, pane.width, 2);
            crate::ui::pane_renderer::PaneRenderer::render_column_headers(&mut direct, rect, false, true, true, false, has_status_col);
            header_of(&direct)
        };

        assert_ne!(expected(true), expected(false), "pane width must make the flag visible");
        assert_eq!(header_of(&buffer), expected(true));
        std::fs::remove_dir_all(&root).unwrap();
    }

    fn changed(app: &App, full_refresh: bool) -> FileSystemChangedEvent {
        FileSystemChangedEvent {
            directory_path: app.current_path.clone(),
            full_refresh,
        }
    }

    fn selected_name(app: &mut App) -> Option<String> {
        let index = app.selected_index;
        app.get_visible_entries().get(index).map(|entry| entry.name.clone())
    }

    #[test]
    fn file_system_changed_keeps_selection_by_name() {
        let root = test_root("fsc-keep");
        let mut app = app_at(&root, &["b.txt", "c.txt"]);
        app.selected_index = 1; // c.txt
        assert_eq!(selected_name(&mut app).as_deref(), Some("c.txt"));

        // a.txt sorts ahead of the selection and shifts its index
        std::fs::write(root.join("a.txt"), "x").unwrap();
        let event = changed(&app, false);
        app.handle_file_system_changed(event);

        assert_eq!(app.selected_index, 2);
        assert_eq!(selected_name(&mut app).as_deref(), Some("c.txt"));
        assert!(app.request_full_redraw);
        let _ = std::fs::remove_dir_all(&root);
    }

    #[test]
    fn file_system_changed_clamps_when_selection_deleted() {
        let root = test_root("fsc-clamp");
        let mut app = app_at(&root, &["a.txt", "b.txt", "c.txt"]);
        app.selected_index = 2; // c.txt
        let _ = app.get_visible_entries(); // populate the cache

        std::fs::remove_file(root.join("c.txt")).unwrap();
        let event = changed(&app, true);
        app.handle_file_system_changed(event);

        assert_eq!(app.selected_index, 1);
        assert_eq!(selected_name(&mut app).as_deref(), Some("b.txt"));
        let _ = std::fs::remove_dir_all(&root);
    }

    #[test]
    fn file_system_changed_resets_selection_when_directory_empties() {
        // C# falls through to Math.Min(idx, Math.Max(0, count - 1)) = 0
        let root = test_root("fsc-empty");
        let mut app = app_at(&root, &["a.txt", "b.txt"]);
        app.selected_index = 1;
        let _ = app.get_visible_entries();

        std::fs::remove_file(root.join("a.txt")).unwrap();
        std::fs::remove_file(root.join("b.txt")).unwrap();
        let event = changed(&app, false);
        app.handle_file_system_changed(event);

        assert_eq!(app.selected_index, 0);
        let _ = std::fs::remove_dir_all(&root);
    }

    #[test]
    fn file_system_changed_ignores_stale_directory() {
        let root = test_root("fsc-stale");
        let mut app = app_at(&root, &["a.txt", "b.txt"]);
        app.selected_index = 1;
        let _ = app.get_visible_entries();

        std::fs::write(root.join("0.txt"), "x").unwrap();
        app.handle_file_system_changed(FileSystemChangedEvent {
            directory_path: root.join("elsewhere").to_string_lossy().into_owned(),
            full_refresh: false,
        });

        // Cache not invalidated, selection and redraw flag untouched
        assert_eq!(app.selected_index, 1);
        assert_eq!(selected_name(&mut app).as_deref(), Some("b.txt"));
        assert!(!app.request_full_redraw);
        let _ = std::fs::remove_dir_all(&root);
    }

    #[test]
    fn file_system_changed_matches_directory_case_insensitively() {
        let root = test_root("fsc-case");
        let mut app = app_at(&root, &["b.txt"]);
        let _ = app.get_visible_entries();

        std::fs::write(root.join("a.txt"), "x").unwrap();
        app.handle_file_system_changed(FileSystemChangedEvent {
            directory_path: app.current_path.to_uppercase(),
            full_refresh: false,
        });

        assert!(app.request_full_redraw);
        let _ = std::fs::remove_dir_all(&root);
    }

    fn notification_text(app: &App) -> String {
        app.notification.as_ref().map(|n| n.message.clone()).unwrap_or_default()
    }

    fn fake_clipboard(app: &mut App) -> &mut super::FakeClipboard {
        match &mut app.os_clipboard {
            super::OsClipboard::Fake(fake) => fake,
            super::OsClipboard::System => panic!("tests use the fake clipboard"),
        }
    }

    fn palette_labels(app: &mut App) -> Vec<String> {
        app.build_action_palette_items().into_iter().map(|item| item.label).collect()
    }

    #[test]
    fn copy_selected_fills_both_clipboards_and_offers_paste() {
        let root = test_root("clip-copy");
        let mut app = app_at(&root, &["a.txt", "b.txt"]);
        assert!(!palette_labels(&mut app).contains(&"Paste".to_string()));

        app.dispatch(AppAction::Copy);

        let a = root.join("a.txt").to_string_lossy().into_owned();
        assert_eq!(app.clipboard_paths, vec![a.clone()]);
        assert!(!app.clipboard_is_cut);
        assert_eq!(notification_text(&app), "Copied 'a.txt'");
        assert_eq!(fake_clipboard(&mut app).files, Some((vec![a], false)));

        let labels = palette_labels(&mut app);
        let copy = labels.iter().position(|l| l == "Copy").unwrap();
        assert_eq!(&labels[copy..copy + 4], ["Copy", "Cut", "Paste", "Copy absolute path"]);
        let context: Vec<String> = app.build_context_menu_items().into_iter().map(|item| item.label).collect();
        assert_eq!(&context[3..7], ["Copy", "Cut", "Paste", "Copy path"]);
        std::fs::remove_dir_all(&root).unwrap();
    }

    #[test]
    fn cut_marked_paths_sets_cut_flag() {
        let root = test_root("clip-cut");
        let mut app = app_at(&root, &["a.txt", "b.txt"]);
        app.marked_paths.insert(root.join("a.txt").to_string_lossy().into_owned());
        app.marked_paths.insert(root.join("b.txt").to_string_lossy().into_owned());

        app.dispatch(AppAction::Cut);

        assert_eq!(app.clipboard_paths.len(), 2);
        assert!(app.clipboard_is_cut);
        assert_eq!(notification_text(&app), "Cut 2 item(s)");
        std::fs::remove_dir_all(&root).unwrap();
    }

    #[test]
    fn paste_with_empty_clipboard_notifies() {
        let root = test_root("clip-empty");
        let mut app = app_at(&root, &["a.txt"]);
        app.dispatch(AppAction::Paste);
        assert_eq!(notification_text(&app), "Clipboard is empty");
        std::fs::remove_dir_all(&root).unwrap();
    }

    #[test]
    fn paste_onto_existing_names_asks_to_overwrite() {
        let root = test_root("clip-conflict");
        let mut app = app_at(&root, &["a.txt"]);
        app.dispatch(AppAction::Copy);
        app.dispatch(AppAction::Paste);

        assert_eq!(app.input_mode, InputMode::Confirm);
        assert_eq!(app.modal.confirm_title.as_deref(), Some("Overwrite"));
        assert_eq!(app.modal.confirm_message.as_deref(), Some("1 item(s) already exist. Overwrite?"));
        assert!(matches!(app.modal.confirm_yes_action, Some(super::dialogs::ConfirmAction::Paste { overwrite: true })));
        std::fs::remove_dir_all(&root).unwrap();
    }

    #[test]
    fn paste_prefers_os_clipboard_files() {
        let root = test_root("clip-os");
        let source = test_root("clip-os-src");
        std::fs::write(source.join("from-os.txt"), "x").unwrap();
        let mut app = app_at(&root, &["a.txt"]);
        app.clipboard_paths = vec![root.join("a.txt").to_string_lossy().into_owned()];
        fake_clipboard(&mut app).files = Some((vec![source.join("from-os.txt").to_string_lossy().into_owned()], true));

        app.dispatch(AppAction::Paste);

        assert!(app.clipboard_is_cut);
        assert_eq!(app.input_mode, InputMode::FileOperation);
        assert_eq!(app.file_op_label, "Moving");
        app.file_operation_runner.cancel();
        std::fs::remove_dir_all(&root).unwrap();
        let _ = std::fs::remove_dir_all(&source);
    }

    #[test]
    fn completed_cut_clears_clipboard() {
        let root = test_root("clip-done");
        let mut app = app_at(&root, &["a.txt"]);
        app.clipboard_paths = vec!["x".to_string()];
        app.handle_file_operation_complete(crate::input::FileOperationCompleteEvent {
            success_count: 1,
            error_count: 1,
            was_cut: true,
        });
        assert_eq!(app.clipboard_paths.len(), 1, "errors keep the clipboard");
        app.handle_file_operation_complete(crate::input::FileOperationCompleteEvent {
            success_count: 1,
            error_count: 0,
            was_cut: true,
        });
        assert!(app.clipboard_paths.is_empty());
        std::fs::remove_dir_all(&root).unwrap();
    }

    #[test]
    fn copy_absolute_path_writes_text() {
        let root = test_root("clip-path");
        let mut app = app_at(&root, &["a.txt"]);
        app.dispatch(AppAction::CopyAbsolutePath);
        assert_eq!(fake_clipboard(&mut app).text.as_deref(), Some(root.join("a.txt").to_string_lossy().as_ref()));
        assert_eq!(notification_text(&app), "Copied path to clipboard");
        std::fs::remove_dir_all(&root).unwrap();
    }

    #[test]
    fn copy_git_relative_path_outside_repo_notifies() {
        let root = test_root("clip-norepo");
        let mut app = app_at(&root, &["a.txt"]);

        if crate::fs::git_utils::find_repo_root(&app.current_path).is_some() {
            return; // temp dir inside a repository: nothing to assert
        }

        app.dispatch(AppAction::CopyGitRelativePath);
        assert_eq!(notification_text(&app), "Not inside a git repository");
        std::fs::remove_dir_all(&root).unwrap();
    }

    #[test]
    fn cloud_download_complete_notifies() {
        let root = test_root("cloud");
        let mut app = app_at(&root, &["a.txt"]);
        app.handle_cloud_download_complete(crate::input::CloudDownloadCompleteEvent { error: None });
        assert_eq!(notification_text(&app), "Download complete");
        app.handle_cloud_download_complete(crate::input::CloudDownloadCompleteEvent { error: Some("boom".into()) });
        assert_eq!(notification_text(&app), "Download failed: boom");
        std::fs::remove_dir_all(&root).unwrap();
    }

    #[test]
    fn hydrate_file_reads_existing_and_fails_for_missing() {
        let root = test_root("hydrate");
        let file = root.join("data.bin");
        std::fs::write(&file, vec![7u8; 100_000]).unwrap();

        assert!(super::hydrate_file(&file.to_string_lossy()).is_ok());
        assert!(super::hydrate_file(&root.join("missing.bin").to_string_lossy()).is_err());
        std::fs::remove_dir_all(&root).unwrap();
    }

}

/// The OS clipboard behind wade's copy/cut/paste and path copies.
pub(crate) enum OsClipboard {
    System,
    /// Records writes and serves canned files (tests).
    Fake(FakeClipboard),
}

#[derive(Default)]
pub(crate) struct FakeClipboard {
    pub text: Option<String>,
    pub files: Option<(Vec<String>, bool)>,
}

impl OsClipboard {
    fn set_text(&mut self, text: &str) -> bool {
        match self {
            Self::System => crate::fs::system_clipboard::set_text(text),
            Self::Fake(fake) => {
                fake.text = Some(text.to_string());
                true
            }
        }
    }

    fn set_files(&mut self, paths: &[String], is_cut: bool) -> bool {
        match self {
            Self::System => crate::fs::system_clipboard::set_files(paths, is_cut),
            Self::Fake(fake) => {
                fake.files = (!paths.is_empty()).then(|| (paths.to_vec(), is_cut));
                fake.files.is_some()
            }
        }
    }

    fn get_files(&mut self) -> Option<(Vec<String>, bool)> {
        match self {
            Self::System => crate::fs::system_clipboard::get_files(),
            Self::Fake(fake) => fake.files.clone(),
        }
    }
}
