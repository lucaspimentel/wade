//! Port of `src/Wade/UI/ConfigItem.cs` and `src/Wade/UI/ConfigDialogState.cs`:
//! the data-driven config dialog state. The C# model uses closures
//! (`FormatValue: Func<string>`, `Toggle: Action`, `EnabledWhen: Func<bool>`);
//! the Rust port models each item as a `ConfigField` + `ConfigGate` pair and
//! dispatches on those, which is equivalent and borrow-checker friendly.

use crate::fs::directory_contents::SortMode;

/// Identifies which `ConfigDialogState` setting an item edits.
#[derive(Clone, Copy, Debug, PartialEq, Eq)]
pub enum ConfigField {
    ShowIcons,
    ImagePreviews,
    ShowHiddenFiles,
    ShowSystemFiles,
    SortMode,
    SortAscending,
    ConfirmDelete,
    ParentPane,
    PreviewPane,
    SizeColumn,
    DateColumn,
    ColumnHeaders,
    ZipPreview,
    PdfPreview,
    PdfMetadata,
    MarkdownPreview,
    Ffprobe,
    Mediainfo,
    CopySymlinksAsLinks,
    TerminalTitle,
    GitStatus,
    FileMetadata,
    FilePreviews,
    ArchiveMetadata,
    DirSizeSsd,
    DirSizeHdd,
    DirSizeNetwork,
}

/// Mirrors the C# `EnabledWhen` predicates. `None` means always enabled.
#[derive(Clone, Copy, Debug, PartialEq, Eq)]
pub enum ConfigGate {
    None,
    HiddenFiles,
    SizeColumn,
    PreviewPane,
    PreviewPaneAndFileMetadata,
    FilePreviews,
}

/// Port of `ConfigItem` (pure data; behavior dispatched on `field`).
pub struct ConfigItem {
    pub label: &'static str,
    pub indent: usize,
    pub field: ConfigField,
    pub gate: ConfigGate,
}

impl ConfigItem {
    fn new(label: &'static str, indent: usize, field: ConfigField, gate: ConfigGate) -> Self {
        Self { label, indent, field, gate }
    }
}

/// Port of `ConfigDialogState`.
pub struct ConfigDialogState {
    pub show_icons: bool,
    pub image_previews: bool,
    pub show_hidden_files: bool,
    pub show_system_files: bool,
    pub sort_mode: SortMode,
    pub sort_ascending: bool,
    pub confirm_delete: bool,
    pub parent_pane: bool,
    pub preview_pane: bool,
    pub size_column: bool,
    pub date_column: bool,
    pub column_headers: bool,
    pub zip_preview: bool,
    pub pdf_preview: bool,
    pub pdf_metadata: bool,
    pub markdown_preview: bool,
    pub ffprobe: bool,
    pub mediainfo: bool,
    pub copy_symlinks_as_links: bool,
    pub terminal_title: bool,
    pub git_status: bool,
    pub file_metadata: bool,
    pub file_previews: bool,
    pub archive_metadata: bool,
    pub dir_size_ssd: bool,
    pub dir_size_hdd: bool,
    pub dir_size_network: bool,

    pub items: Vec<ConfigItem>,
    pub selected_index: usize,
}

impl ConfigDialogState {
    /// Port of `ConfigDialogState.FromConfig(AppConfig)`.
    #[must_use]
    pub fn from_app_config(config: &crate::app::AppConfig) -> Self {
        let mut state = Self {
            show_icons: config.show_icons_enabled,
            image_previews: config.image_previews_enabled,
            show_hidden_files: config.show_hidden_files,
            show_system_files: config.show_system_files,
            sort_mode: config.sort_mode,
            sort_ascending: config.sort_ascending,
            confirm_delete: config.confirm_delete_enabled,
            parent_pane: config.parent_pane_enabled,
            preview_pane: config.preview_pane_enabled,
            size_column: config.size_column_enabled,
            date_column: config.date_column_enabled,
            column_headers: config.column_headers_enabled,
            zip_preview: config.zip_preview_enabled,
            pdf_preview: config.pdf_preview_enabled,
            pdf_metadata: config.pdf_metadata_enabled,
            markdown_preview: config.markdown_preview_enabled,
            ffprobe: config.ffprobe_enabled,
            mediainfo: config.mediainfo_enabled,
            copy_symlinks_as_links: config.copy_symlinks_as_links_enabled,
            terminal_title: config.terminal_title_enabled,
            git_status: config.git_status_enabled,
            file_metadata: config.file_metadata_enabled,
            file_previews: config.file_previews_enabled,
            archive_metadata: config.archive_metadata_enabled,
            dir_size_ssd: config.dir_size_ssd_enabled,
            dir_size_hdd: config.dir_size_hdd_enabled,
            dir_size_network: config.dir_size_network_enabled,
            items: Vec::new(),
            selected_index: 0,
        };

        state.build_items();
        state
    }

    /// Port of `ApplyTo(WadeConfig)`: copies the dialog state into the config,
    /// including the `ShowSystemFiles = ShowHiddenFiles && ShowSystemFiles`
    /// clamp with write-back into the dialog state.
    pub fn apply_to(&mut self, config: &mut crate::app::AppConfig) {
        config.show_icons_enabled = self.show_icons;
        config.image_previews_enabled = self.image_previews;
        config.show_hidden_files = self.show_hidden_files;
        config.show_system_files = self.show_hidden_files && self.show_system_files;
        self.show_system_files = config.show_system_files;
        config.sort_mode = self.sort_mode;
        config.sort_ascending = self.sort_ascending;
        config.confirm_delete_enabled = self.confirm_delete;
        config.parent_pane_enabled = self.parent_pane;
        config.preview_pane_enabled = self.preview_pane;
        config.size_column_enabled = self.size_column;
        config.date_column_enabled = self.date_column;
        config.column_headers_enabled = self.column_headers;
        config.zip_preview_enabled = self.zip_preview;
        config.pdf_preview_enabled = self.pdf_preview;
        config.pdf_metadata_enabled = self.pdf_metadata;
        config.markdown_preview_enabled = self.markdown_preview;
        config.ffprobe_enabled = self.ffprobe;
        config.mediainfo_enabled = self.mediainfo;
        config.copy_symlinks_as_links_enabled = self.copy_symlinks_as_links;
        config.terminal_title_enabled = self.terminal_title;
        config.git_status_enabled = self.git_status;
        config.file_metadata_enabled = self.file_metadata;
        config.file_previews_enabled = self.file_previews;
        config.archive_metadata_enabled = self.archive_metadata;
        config.dir_size_ssd_enabled = self.dir_size_ssd;
        config.dir_size_hdd_enabled = self.dir_size_hdd;
        config.dir_size_network_enabled = self.dir_size_network;
    }

    /// Port of `BuildItems`: the exact C# item order and gating. The
    /// Windows-only "Show System Files" item is behind `#[cfg(windows)]`,
    /// matching the C# `OperatingSystem.IsWindows()` check (both sides omit
    /// it on non-Windows, so golden parity holds per-platform).
    fn build_items(&mut self) {
        let mut items = vec![
            ConfigItem::new("Show Icons", 0, ConfigField::ShowIcons, ConfigGate::None),
            ConfigItem::new("Show Hidden Files", 0, ConfigField::ShowHiddenFiles, ConfigGate::None),
        ];

        #[cfg(windows)]
        items.push(ConfigItem::new(
            "Show System Files",
            1,
            ConfigField::ShowSystemFiles,
            ConfigGate::HiddenFiles,
        ));

        items.extend([
            ConfigItem::new("Sort Mode", 0, ConfigField::SortMode, ConfigGate::None),
            ConfigItem::new("Sort Ascending", 0, ConfigField::SortAscending, ConfigGate::None),
            ConfigItem::new("Show Size Column", 0, ConfigField::SizeColumn, ConfigGate::None),
            ConfigItem::new("Directory Sizes on SSD", 1, ConfigField::DirSizeSsd, ConfigGate::SizeColumn),
            ConfigItem::new("Directory Sizes on HDD", 1, ConfigField::DirSizeHdd, ConfigGate::SizeColumn),
            ConfigItem::new("Directory Sizes on Network", 1, ConfigField::DirSizeNetwork, ConfigGate::SizeColumn),
            ConfigItem::new("Show Date Column", 0, ConfigField::DateColumn, ConfigGate::None),
            ConfigItem::new("Show Column Headers", 0, ConfigField::ColumnHeaders, ConfigGate::None),
            ConfigItem::new("Confirm Delete", 0, ConfigField::ConfirmDelete, ConfigGate::None),
            ConfigItem::new("Copy Symlinks as Link", 0, ConfigField::CopySymlinksAsLinks, ConfigGate::None),
            ConfigItem::new("Change Terminal Title", 0, ConfigField::TerminalTitle, ConfigGate::None),
            ConfigItem::new("Show Git Status", 0, ConfigField::GitStatus, ConfigGate::None),
            ConfigItem::new("Show Left Pane", 0, ConfigField::ParentPane, ConfigGate::None),
            ConfigItem::new("Show Right Pane", 0, ConfigField::PreviewPane, ConfigGate::None),
            ConfigItem::new("Show File Details", 1, ConfigField::FileMetadata, ConfigGate::PreviewPane),
            ConfigItem::new(
                "Show Archive Details",
                2,
                ConfigField::ArchiveMetadata,
                ConfigGate::PreviewPaneAndFileMetadata,
            ),
            ConfigItem::new(
                "Show PDF Details (pdfinfo)",
                2,
                ConfigField::PdfMetadata,
                ConfigGate::PreviewPaneAndFileMetadata,
            ),
            ConfigItem::new(
                "Show Media Details (ffprobe)",
                2,
                ConfigField::Ffprobe,
                ConfigGate::PreviewPaneAndFileMetadata,
            ),
            ConfigItem::new(
                "Show Media Details (mediainfo)",
                2,
                ConfigField::Mediainfo,
                ConfigGate::PreviewPaneAndFileMetadata,
            ),
            ConfigItem::new("Show File Previews", 0, ConfigField::FilePreviews, ConfigGate::None),
            ConfigItem::new("Show Image Previews", 1, ConfigField::ImagePreviews, ConfigGate::FilePreviews),
            ConfigItem::new(
                "Show PDF Previews (pdftopng)",
                1,
                ConfigField::PdfPreview,
                ConfigGate::FilePreviews,
            ),
            ConfigItem::new("Show Archive Contents", 1, ConfigField::ZipPreview, ConfigGate::FilePreviews),
            ConfigItem::new(
                "Show Markdown Preview (built-in)",
                1,
                ConfigField::MarkdownPreview,
                ConfigGate::FilePreviews,
            ),
        ]);

        self.items = items;
    }

    /// Port of `IsEnabled`.
    #[must_use]
    pub fn is_enabled(&self, index: usize) -> bool {
        let Some(item) = self.items.get(index) else {
            return false;
        };

        match item.gate {
            ConfigGate::None => true,
            ConfigGate::HiddenFiles => self.show_hidden_files,
            ConfigGate::SizeColumn => self.size_column,
            ConfigGate::PreviewPane => self.preview_pane,
            ConfigGate::PreviewPaneAndFileMetadata => self.preview_pane && self.file_metadata,
            ConfigGate::FilePreviews => self.file_previews,
        }
    }

    /// Port of `MoveUp`: previous enabled item, wrapping to the last enabled
    /// item before the current selection when none precede it.
    pub fn move_up(&mut self) {
        let mut prev = self.selected_index as isize - 1;
        while prev >= 0 && !self.is_enabled(prev as usize) {
            prev -= 1;
        }

        if prev >= 0 {
            self.selected_index = prev as usize;
        } else {
            let mut last = self.items.len() - 1;
            while last > self.selected_index && !self.is_enabled(last) {
                last -= 1;
            }

            if last > self.selected_index {
                self.selected_index = last;
            }
        }
    }

    /// Port of `MoveDown`: next enabled item, wrapping to the first enabled
    /// item after the start when none follow it.
    pub fn move_down(&mut self) {
        let max_index = self.items.len() - 1;
        let mut next = self.selected_index + 1;
        while next <= max_index && !self.is_enabled(next) {
            next += 1;
        }

        if next <= max_index {
            self.selected_index = next;
        } else {
            let mut first = 0;
            while first < self.selected_index && !self.is_enabled(first) {
                first += 1;
            }

            if first < self.selected_index {
                self.selected_index = first;
            }
        }
    }

    /// Port of `ToggleSelected`.
    pub fn toggle_selected(&mut self) {
        self.toggle(self.selected_index);
    }

    /// Toggle the item at `index` (the `Toggle` delegate).
    pub fn toggle(&mut self, index: usize) {
        let Some(field) = self.items.get(index).map(|item| item.field) else {
            return;
        };

        match field {
            ConfigField::ShowIcons => self.show_icons = !self.show_icons,
            ConfigField::ImagePreviews => self.image_previews = !self.image_previews,
            ConfigField::ShowHiddenFiles => self.show_hidden_files = !self.show_hidden_files,
            ConfigField::ShowSystemFiles => self.show_system_files = !self.show_system_files,
            ConfigField::SortMode => self.sort_mode = cycle_sort_mode_next(self.sort_mode),
            ConfigField::SortAscending => self.sort_ascending = !self.sort_ascending,
            ConfigField::ConfirmDelete => self.confirm_delete = !self.confirm_delete,
            ConfigField::ParentPane => self.parent_pane = !self.parent_pane,
            ConfigField::PreviewPane => self.preview_pane = !self.preview_pane,
            ConfigField::SizeColumn => self.size_column = !self.size_column,
            ConfigField::DateColumn => self.date_column = !self.date_column,
            ConfigField::ColumnHeaders => self.column_headers = !self.column_headers,
            ConfigField::ZipPreview => self.zip_preview = !self.zip_preview,
            ConfigField::PdfPreview => self.pdf_preview = !self.pdf_preview,
            ConfigField::PdfMetadata => self.pdf_metadata = !self.pdf_metadata,
            ConfigField::MarkdownPreview => self.markdown_preview = !self.markdown_preview,
            ConfigField::Ffprobe => self.ffprobe = !self.ffprobe,
            ConfigField::Mediainfo => self.mediainfo = !self.mediainfo,
            ConfigField::CopySymlinksAsLinks => self.copy_symlinks_as_links = !self.copy_symlinks_as_links,
            ConfigField::TerminalTitle => self.terminal_title = !self.terminal_title,
            ConfigField::GitStatus => self.git_status = !self.git_status,
            ConfigField::FileMetadata => self.file_metadata = !self.file_metadata,
            ConfigField::FilePreviews => self.file_previews = !self.file_previews,
            ConfigField::ArchiveMetadata => self.archive_metadata = !self.archive_metadata,
            ConfigField::DirSizeSsd => self.dir_size_ssd = !self.dir_size_ssd,
            ConfigField::DirSizeHdd => self.dir_size_hdd = !self.dir_size_hdd,
            ConfigField::DirSizeNetwork => self.dir_size_network = !self.dir_size_network,
        }
    }

    /// Port of `CycleNextSelected` (only Sort Mode is cycleable).
    pub fn cycle_next_selected(&mut self) {
        if self.items.get(self.selected_index).is_some_and(|item| item.field == ConfigField::SortMode) {
            self.sort_mode = cycle_sort_mode_next(self.sort_mode);
        }
    }

    /// Port of `CyclePrevSelected` (only Sort Mode is cycleable).
    pub fn cycle_prev_selected(&mut self) {
        if self.items.get(self.selected_index).is_some_and(|item| item.field == ConfigField::SortMode) {
            self.sort_mode = cycle_sort_mode_prev(self.sort_mode);
        }
    }

    /// Port of `item.FormatValue()`.
    #[must_use]
    pub fn format_value(&self, index: usize) -> String {
        let Some(item) = self.items.get(index) else {
            return String::new();
        };

        match item.field {
            ConfigField::SortMode => format!("\u{25c4} {} \u{25ba}", sort_mode_name(self.sort_mode)),
            _ => format_bool(self.get_field(item.field)).to_string(),
        }
    }

    fn get_field(&self, field: ConfigField) -> bool {
        match field {
            ConfigField::ShowIcons => self.show_icons,
            ConfigField::ImagePreviews => self.image_previews,
            ConfigField::ShowHiddenFiles => self.show_hidden_files,
            ConfigField::ShowSystemFiles => self.show_system_files,
            ConfigField::SortMode => false, // sort mode renders its own value
            ConfigField::SortAscending => self.sort_ascending,
            ConfigField::ConfirmDelete => self.confirm_delete,
            ConfigField::ParentPane => self.parent_pane,
            ConfigField::PreviewPane => self.preview_pane,
            ConfigField::SizeColumn => self.size_column,
            ConfigField::DateColumn => self.date_column,
            ConfigField::ColumnHeaders => self.column_headers,
            ConfigField::ZipPreview => self.zip_preview,
            ConfigField::PdfPreview => self.pdf_preview,
            ConfigField::PdfMetadata => self.pdf_metadata,
            ConfigField::MarkdownPreview => self.markdown_preview,
            ConfigField::Ffprobe => self.ffprobe,
            ConfigField::Mediainfo => self.mediainfo,
            ConfigField::CopySymlinksAsLinks => self.copy_symlinks_as_links,
            ConfigField::TerminalTitle => self.terminal_title,
            ConfigField::GitStatus => self.git_status,
            ConfigField::FileMetadata => self.file_metadata,
            ConfigField::FilePreviews => self.file_previews,
            ConfigField::ArchiveMetadata => self.archive_metadata,
            ConfigField::DirSizeSsd => self.dir_size_ssd,
            ConfigField::DirSizeHdd => self.dir_size_hdd,
            ConfigField::DirSizeNetwork => self.dir_size_network,
        }
    }
}

/// Port of `ConfigDialogState.FormatBool`.
#[must_use]
pub fn format_bool(value: bool) -> &'static str {
    if value {
        "[X]"
    } else {
        "[ ]"
    }
}

#[must_use]
fn sort_mode_name(mode: SortMode) -> &'static str {
    match mode {
        SortMode::Name => "name",
        SortMode::Modified => "modified",
        SortMode::Size => "size",
        SortMode::Extension => "extension",
    }
}

/// Port of `CycleSortModeNext`.
#[must_use]
pub fn cycle_sort_mode_next(current: SortMode) -> SortMode {
    match current {
        SortMode::Name => SortMode::Modified,
        SortMode::Modified => SortMode::Size,
        SortMode::Size => SortMode::Extension,
        SortMode::Extension => SortMode::Name,
    }
}

/// Port of `CycleSortModePrev`.
#[must_use]
pub fn cycle_sort_mode_prev(current: SortMode) -> SortMode {
    match current {
        SortMode::Name => SortMode::Extension,
        SortMode::Modified => SortMode::Name,
        SortMode::Size => SortMode::Modified,
        SortMode::Extension => SortMode::Size,
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    fn default_state() -> ConfigDialogState {
        ConfigDialogState::from_app_config(&crate::app::AppConfig::default())
    }

    #[test]
    fn item_count_and_order() {
        let state = default_state();
        let labels: Vec<&str> = state.items.iter().map(|item| item.label).collect();
        let expected = vec![
            "Show Icons",
            "Show Hidden Files",
            #[cfg(windows)]
            "Show System Files",
            "Sort Mode",
            "Sort Ascending",
            "Show Size Column",
            "Directory Sizes on SSD",
            "Directory Sizes on HDD",
            "Directory Sizes on Network",
            "Show Date Column",
            "Show Column Headers",
            "Confirm Delete",
            "Copy Symlinks as Link",
            "Change Terminal Title",
            "Show Git Status",
            "Show Left Pane",
            "Show Right Pane",
            "Show File Details",
            "Show Archive Details",
            "Show PDF Details (pdfinfo)",
            "Show Media Details (ffprobe)",
            "Show Media Details (mediainfo)",
            "Show File Previews",
            "Show Image Previews",
            "Show PDF Previews (pdftopng)",
            "Show Archive Contents",
            "Show Markdown Preview (built-in)",
        ];
        assert_eq!(labels, expected);
    }

    #[test]
    fn gating_follows_dependencies() {
        let mut state = default_state();
        // Find indexes by label so the test is Windows/Unix agnostic
        let index_of = |label: &str, state: &ConfigDialogState| -> usize {
            state
                .items
                .iter()
                .position(|item| item.label == label)
                .expect("item")
        };

        let hdd = index_of("Directory Sizes on HDD", &state);
        assert!(state.is_enabled(hdd));

        state.size_column = false;
        assert!(!state.is_enabled(hdd));

        state.size_column = true;
        assert!(state.is_enabled(hdd));

        let file_details = index_of("Show File Details", &state);
        assert!(state.is_enabled(file_details));
        state.preview_pane = false;
        assert!(!state.is_enabled(file_details));

        let archive_details = index_of("Show Archive Details", &state);
        state.preview_pane = true;
        state.file_metadata = false;
        assert!(!state.is_enabled(archive_details));
        state.file_metadata = true;
        assert!(state.is_enabled(archive_details));
    }

    #[test]
    fn move_down_skips_disabled_and_wraps() {
        let mut state = default_state();
        let index_of = |label: &str, state: &ConfigDialogState| -> usize {
            state
                .items
                .iter()
                .position(|item| item.label == label)
                .expect("item")
        };

        let size_column = index_of("Show Size Column", &state);
        let date_column = index_of("Show Date Column", &state);

        // With Size Column off, the three dir-size items are skipped
        state.size_column = false;
        state.selected_index = size_column;
        state.move_down();
        assert_eq!(state.selected_index, date_column);

        // At the end, move down wraps to the first enabled item
        state.selected_index = state.items.len() - 1;
        state.move_down();
        assert_eq!(state.selected_index, 0);
    }

    #[test]
    fn move_up_wraps_to_last_enabled() {
        let mut state = default_state();
        state.size_column = false;
        state.selected_index = 0; // Show Icons (enabled)
        state.move_up();
        // Wraps to the last item (Show Markdown Preview, enabled)
        assert_eq!(state.selected_index, state.items.len() - 1);
    }

    #[test]
    fn toggle_and_cycle() {
        let mut state = default_state();
        assert!(state.show_icons);
        state.toggle_selected(); // Show Icons
        assert!(!state.show_icons);

        state.selected_index = state
            .items
            .iter()
            .position(|item| item.field == ConfigField::SortMode)
            .expect("sort mode item");
        assert_eq!(state.sort_mode, SortMode::Name);
        state.cycle_next_selected();
        assert_eq!(state.sort_mode, SortMode::Modified);
        state.cycle_prev_selected();
        assert_eq!(state.sort_mode, SortMode::Name);

        // Cycle on a bool item is a no-op (not cycleable)
        state.selected_index = 0;
        state.cycle_next_selected();
        assert_eq!(state.sort_mode, SortMode::Name);
        assert!(!state.show_icons);
    }

    #[test]
    fn apply_to_clamps_system_files() {
        let mut config = crate::app::AppConfig::default();
        let mut state = ConfigDialogState::from_app_config(&config);
        state.show_hidden_files = false;
        state.show_system_files = true;
        state.apply_to(&mut config);
        // Clamped: system files require hidden files
        assert!(!config.show_system_files);
        assert!(!state.show_system_files);

        state.show_hidden_files = true;
        state.show_system_files = true;
        state.apply_to(&mut config);
        assert!(config.show_system_files);
    }

    fn index_of(state: &ConfigDialogState, label: &str) -> usize {
        state.items.iter().position(|item| item.label == label).unwrap_or_else(|| panic!("{label}"))
    }

    #[test]
    fn move_up_skips_disabled_items() {
        let mut state = default_state();
        state.size_column = false;
        state.selected_index = index_of(&state, "Show Date Column");
        state.move_up();
        assert_eq!(state.selected_index, index_of(&state, "Show Size Column"));
    }

    #[test]
    fn toggling_a_tool_item_flips_it() {
        let mut state = default_state();
        state.selected_index = index_of(&state, "Show PDF Details (pdfinfo)");
        let before = state.pdf_metadata;
        state.toggle_selected();
        assert_eq!(state.pdf_metadata, !before);
        assert_eq!(state.format_value(state.selected_index), format_bool(!before));
    }

    #[test]
    fn sort_mode_cycles_through_every_mode() {
        let mut state = default_state();
        state.selected_index = index_of(&state, "Sort Mode");
        let mut seen = Vec::new();
        for _ in 0..4 {
            state.cycle_next_selected();
            seen.push(state.sort_mode);
        }
        assert_eq!(seen, [SortMode::Modified, SortMode::Size, SortMode::Extension, SortMode::Name]);

        seen.clear();
        for _ in 0..4 {
            state.cycle_prev_selected();
            seen.push(state.sort_mode);
        }
        assert_eq!(seen, [SortMode::Extension, SortMode::Size, SortMode::Modified, SortMode::Name]);
    }

    #[test]
    fn detail_and_preview_sub_items_follow_their_parents() {
        let mut state = default_state();
        let details = ["Show Archive Details", "Show PDF Details (pdfinfo)", "Show Media Details (ffprobe)", "Show Media Details (mediainfo)"];
        let previews = ["Show Image Previews", "Show PDF Previews (pdftopng)", "Show Archive Contents", "Show Markdown Preview (built-in)"];
        let enabled = |state: &ConfigDialogState, labels: &[&str]| labels.iter().map(|l| state.is_enabled(index_of(state, l))).collect::<Vec<_>>();

        assert_eq!(enabled(&state, &details), [true; 4]);
        state.file_metadata = false;
        assert_eq!(enabled(&state, &details), [false; 4]);
        state.file_metadata = true;
        state.preview_pane = false;
        assert_eq!(enabled(&state, &details), [false; 4]);

        assert_eq!(enabled(&state, &previews), [true; 4], "previews do not depend on the right pane");
        state.file_previews = false;
        assert_eq!(enabled(&state, &previews), [false; 4]);
        state.file_previews = true;
        assert_eq!(enabled(&state, &previews), [true; 4]);
    }

    #[test]
    fn every_item_round_trips_through_the_config() {
        // Toggle every item away from its default, save, and reopen: each
        // item shows the toggled value, so from_app_config and apply_to
        // cover every field
        let mut state = default_state();
        for index in 0..state.items.len() {
            state.toggle(index);
        }
        let mut config = crate::app::AppConfig::default();
        state.apply_to(&mut config);
        let reopened = ConfigDialogState::from_app_config(&config);

        let defaults = default_state();
        for index in 0..state.items.len() {
            let label = state.items[index].label;
            assert_eq!(reopened.format_value(index), state.format_value(index), "{label}");
            assert_ne!(reopened.format_value(index), defaults.format_value(index), "{label} changed from its default");
        }
    }

    #[test]
    fn items_have_the_csharp_indentation() {
        let state = default_state();
        let indent = |label: &str| state.items[index_of(&state, label)].indent;
        assert_eq!(indent("Show Icons"), 0);
        assert_eq!(indent("Show File Details"), 1);
        assert_eq!(indent("Show Archive Details"), 2);
        assert_eq!(indent("Directory Sizes on SSD"), 1);
        assert_eq!(indent("Show Image Previews"), 1);
    }

    #[test]
    fn format_bool_and_sort_value() {
        assert_eq!(format_bool(true), "[X]");
        assert_eq!(format_bool(false), "[ ]");

        let mut state = default_state();
        let sort_index = state
            .items
            .iter()
            .position(|item| item.field == ConfigField::SortMode)
            .expect("sort mode item");
        assert_eq!(state.format_value(sort_index), "\u{25c4} name \u{25ba}");
        state.sort_mode = SortMode::Extension;
        assert_eq!(state.format_value(sort_index), "\u{25c4} extension \u{25ba}");
    }
}
