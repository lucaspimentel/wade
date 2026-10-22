//! The config dialog (`,`): a list of rows over a draft copy of the
//! settings, saved back on close. Each row names the setting it edits and
//! the gate that greys it out.

use crate::app::AppConfig;

/// The setting a row edits.
#[derive(Clone, Copy)]
pub enum Setting {
    /// Cycles through the icon sets.
    Icons,
    /// Cycles through the sort modes.
    SortMode,
    /// An on/off setting.
    Flag(fn(&mut AppConfig) -> &mut bool),
}

/// When a row is enabled; the others are greyed out and skipped.
#[derive(Clone, Copy, Debug, PartialEq, Eq)]
pub enum ConfigGate {
    None,
    HiddenFiles,
    SizeColumn,
    PreviewPane,
    PreviewPaneAndFileMetadata,
    FilePreviews,
}

pub struct ConfigItem {
    pub label: &'static str,
    pub indent: usize,
    pub setting: Setting,
    pub gate: ConfigGate,
}

impl ConfigItem {
    fn new(label: &'static str, indent: usize, setting: Setting, gate: ConfigGate) -> Self {
        Self { label, indent, setting, gate }
    }

    fn flag(label: &'static str, indent: usize, flag: fn(&mut AppConfig) -> &mut bool, gate: ConfigGate) -> Self {
        Self::new(label, indent, Setting::Flag(flag), gate)
    }
}

pub struct ConfigDialogState {
    /// The settings being edited; copied back by `apply_to`.
    pub draft: AppConfig,
    pub items: Vec<ConfigItem>,
    pub selected_index: usize,
}

impl ConfigDialogState {
    #[must_use]
    pub fn from_app_config(config: &AppConfig) -> Self {
        Self {
            draft: *config,
            items: build_items(),
            selected_index: 0,
        }
    }

    /// Copies the draft into `config`. System files show only with hidden
    /// files, so that setting is cleared in both when hidden files are off.
    pub fn apply_to(&mut self, config: &mut AppConfig) {
        self.draft.show_system_files &= self.draft.show_hidden_files;
        *config = self.draft;
    }

    #[must_use]
    pub fn is_enabled(&self, index: usize) -> bool {
        let Some(item) = self.items.get(index) else {
            return false;
        };

        let draft = &self.draft;
        match item.gate {
            ConfigGate::None => true,
            ConfigGate::HiddenFiles => draft.show_hidden_files,
            ConfigGate::SizeColumn => draft.size_column_enabled,
            ConfigGate::PreviewPane => draft.preview_pane_enabled,
            ConfigGate::PreviewPaneAndFileMetadata => draft.preview_pane_enabled && draft.file_metadata_enabled,
            ConfigGate::FilePreviews => draft.file_previews_enabled,
        }
    }

    /// Previous enabled item, wrapping to the last enabled
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

    /// Next enabled item, wrapping to the first enabled
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

    pub fn toggle_selected(&mut self) {
        self.toggle(self.selected_index);
    }

    /// Enter or Space: flips a flag, or steps a cycling row forward.
    pub fn toggle(&mut self, index: usize) {
        let Some(setting) = self.items.get(index).map(|item| item.setting) else {
            return;
        };

        match setting {
            Setting::Icons => self.draft.icon_set = self.draft.icon_set.next(),
            Setting::SortMode => self.draft.sort_mode = self.draft.sort_mode.next(),
            Setting::Flag(flag) => {
                let value = flag(&mut self.draft);
                *value = !*value;
            }
        }
    }

    /// Right arrow: steps a cycling row forward; flags are unchanged.
    pub fn cycle_next_selected(&mut self) {
        match self.items.get(self.selected_index).map(|item| item.setting) {
            Some(Setting::SortMode) => self.draft.sort_mode = self.draft.sort_mode.next(),
            Some(Setting::Icons) => self.draft.icon_set = self.draft.icon_set.next(),
            _ => {}
        }
    }

    /// Left arrow: steps a cycling row back; flags are unchanged.
    pub fn cycle_prev_selected(&mut self) {
        match self.items.get(self.selected_index).map(|item| item.setting) {
            Some(Setting::SortMode) => self.draft.sort_mode = self.draft.sort_mode.prev(),
            Some(Setting::Icons) => self.draft.icon_set = self.draft.icon_set.prev(),
            _ => {}
        }
    }

    /// The value column: `◄ name ►` for cycling rows, `[X]`/`[ ]` for flags.
    #[must_use]
    pub fn format_value(&self, index: usize) -> String {
        let Some(item) = self.items.get(index) else {
            return String::new();
        };

        match item.setting {
            Setting::SortMode => format!("\u{25c4} {} \u{25ba}", self.draft.sort_mode.name()),
            Setting::Icons => format!("\u{25c4} {} \u{25ba}", self.draft.icon_set.name()),
            Setting::Flag(flag) => format_bool(*flag(&mut { self.draft })).to_string(),
        }
    }
}

/// The rows in display order. "Show System Files" is Windows-only.
fn build_items() -> Vec<ConfigItem> {
    use ConfigGate as G;

    let mut items = vec![
        ConfigItem::new("Icons", 0, Setting::Icons, G::None),
        ConfigItem::flag("Show Hidden Files", 0, |c| &mut c.show_hidden_files, G::None),
    ];

    #[cfg(windows)]
    items.push(ConfigItem::flag("Show System Files", 1, |c| &mut c.show_system_files, G::HiddenFiles));

    items.extend([
        ConfigItem::new("Sort Mode", 0, Setting::SortMode, G::None),
        ConfigItem::flag("Sort Ascending", 0, |c| &mut c.sort_ascending, G::None),
        ConfigItem::flag("Show Size Column", 0, |c| &mut c.size_column_enabled, G::None),
        ConfigItem::flag("Directory Sizes on SSD", 1, |c| &mut c.dir_size_ssd_enabled, G::SizeColumn),
        ConfigItem::flag("Directory Sizes on HDD", 1, |c| &mut c.dir_size_hdd_enabled, G::SizeColumn),
        ConfigItem::flag("Directory Sizes on Network", 1, |c| &mut c.dir_size_network_enabled, G::SizeColumn),
        ConfigItem::flag("Show Date Column", 0, |c| &mut c.date_column_enabled, G::None),
        ConfigItem::flag("Show Column Headers", 0, |c| &mut c.column_headers_enabled, G::None),
        ConfigItem::flag("Confirm Delete", 0, |c| &mut c.confirm_delete_enabled, G::None),
        ConfigItem::flag("Copy Symlinks as Link", 0, |c| &mut c.copy_symlinks_as_links_enabled, G::None),
        ConfigItem::flag("Change Terminal Title", 0, |c| &mut c.terminal_title_enabled, G::None),
        ConfigItem::flag("Show Git Status", 0, |c| &mut c.git_status_enabled, G::None),
        ConfigItem::flag("Show Left Pane", 0, |c| &mut c.parent_pane_enabled, G::None),
        ConfigItem::flag("Show Right Pane", 0, |c| &mut c.preview_pane_enabled, G::None),
        ConfigItem::flag("Show File Details", 1, |c| &mut c.file_metadata_enabled, G::PreviewPane),
        ConfigItem::flag("Show Archive Details", 2, |c| &mut c.archive_metadata_enabled, G::PreviewPaneAndFileMetadata),
        ConfigItem::flag(
            "Show PDF Details (pdfinfo)",
            2,
            |c| &mut c.pdf_metadata_enabled,
            G::PreviewPaneAndFileMetadata,
        ),
        ConfigItem::flag("Show Media Details (ffprobe)", 2, |c| &mut c.ffprobe_enabled, G::PreviewPaneAndFileMetadata),
        ConfigItem::flag(
            "Show Media Details (mediainfo)",
            2,
            |c| &mut c.mediainfo_enabled,
            G::PreviewPaneAndFileMetadata,
        ),
        ConfigItem::flag("Show File Previews", 0, |c| &mut c.file_previews_enabled, G::None),
        ConfigItem::flag("Show Image Previews", 1, |c| &mut c.image_previews_enabled, G::FilePreviews),
        ConfigItem::flag("Show PDF Previews (pdftopng)", 1, |c| &mut c.pdf_preview_enabled, G::FilePreviews),
        ConfigItem::flag("Show Archive Contents", 1, |c| &mut c.zip_preview_enabled, G::FilePreviews),
        ConfigItem::flag("Show Markdown Preview (built-in)", 1, |c| &mut c.markdown_preview_enabled, G::FilePreviews),
        ConfigItem::flag("Show CSV/TSV Tables", 1, |c| &mut c.csv_preview_enabled, G::FilePreviews),
    ]);

    items
}

#[must_use]
pub fn format_bool(value: bool) -> &'static str {
    if value { "[X]" } else { "[ ]" }
}

#[cfg(test)]
mod tests {
    use super::*;
    use crate::fs::directory_contents::SortMode;
    use crate::ui::file_icons::IconSet;

    fn default_state() -> ConfigDialogState {
        ConfigDialogState::from_app_config(&crate::app::AppConfig::default())
    }

    #[test]
    fn item_count_and_order() {
        let state = default_state();
        let labels: Vec<&str> = state.items.iter().map(|item| item.label).collect();
        let expected = vec![
            "Icons",
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
            "Show CSV/TSV Tables",
        ];
        assert_eq!(labels, expected);
    }

    #[test]
    fn gating_follows_dependencies() {
        let mut state = default_state();
        // Find indexes by label so the test is Windows/Unix agnostic
        let index_of = |label: &str, state: &ConfigDialogState| -> usize {
            state.items.iter().position(|item| item.label == label).expect("item")
        };

        let hdd = index_of("Directory Sizes on HDD", &state);
        assert!(state.is_enabled(hdd));

        state.draft.size_column_enabled = false;
        assert!(!state.is_enabled(hdd));

        state.draft.size_column_enabled = true;
        assert!(state.is_enabled(hdd));

        let file_details = index_of("Show File Details", &state);
        assert!(state.is_enabled(file_details));
        state.draft.preview_pane_enabled = false;
        assert!(!state.is_enabled(file_details));

        let archive_details = index_of("Show Archive Details", &state);
        state.draft.preview_pane_enabled = true;
        state.draft.file_metadata_enabled = false;
        assert!(!state.is_enabled(archive_details));
        state.draft.file_metadata_enabled = true;
        assert!(state.is_enabled(archive_details));
    }

    #[test]
    fn move_down_skips_disabled_and_wraps() {
        let mut state = default_state();
        let index_of = |label: &str, state: &ConfigDialogState| -> usize {
            state.items.iter().position(|item| item.label == label).expect("item")
        };

        let size_column = index_of("Show Size Column", &state);
        let date_column = index_of("Show Date Column", &state);

        // With Size Column off, the three dir-size items are skipped
        state.draft.size_column_enabled = false;
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
        state.draft.size_column_enabled = false;
        state.selected_index = 0; // Icons (enabled)
        state.move_up();
        // Wraps to the last item (Show CSV/TSV Tables, enabled)
        assert_eq!(state.selected_index, state.items.len() - 1);
    }

    #[test]
    fn toggle_and_cycle() {
        let mut state = default_state();
        assert_eq!(state.draft.icon_set, IconSet::Nerd);
        state.toggle_selected(); // Icons: Enter steps forward
        assert_eq!(state.draft.icon_set, IconSet::Emoji);
        state.cycle_next_selected();
        assert_eq!(state.draft.icon_set, IconSet::Ascii);
        state.cycle_prev_selected();
        state.cycle_prev_selected();
        state.cycle_prev_selected();
        assert_eq!(state.draft.icon_set, IconSet::None, "wraps backwards");
        assert_eq!(state.format_value(0), "\u{25c4} none \u{25ba}");

        state.selected_index = state
            .items
            .iter()
            .position(|item| matches!(item.setting, Setting::SortMode))
            .expect("sort mode item");
        assert_eq!(state.draft.sort_mode, SortMode::Name);
        state.cycle_next_selected();
        assert_eq!(state.draft.sort_mode, SortMode::Modified);
        state.cycle_prev_selected();
        assert_eq!(state.draft.sort_mode, SortMode::Name);
        assert_eq!(state.draft.icon_set, IconSet::None, "sort mode cycling leaves the icon set alone");

        // Cycle on a bool item is a no-op (not cycleable)
        state.selected_index = index_of(&state, "Show Hidden Files");
        state.cycle_next_selected();
        assert_eq!(state.draft.sort_mode, SortMode::Name);
        assert!(!state.draft.show_hidden_files);
    }

    #[test]
    fn apply_to_clamps_system_files() {
        let mut config = crate::app::AppConfig::default();
        let mut state = ConfigDialogState::from_app_config(&config);
        state.draft.show_hidden_files = false;
        state.draft.show_system_files = true;
        state.apply_to(&mut config);
        // Clamped: system files require hidden files
        assert!(!config.show_system_files);
        assert!(!state.draft.show_system_files);

        state.draft.show_hidden_files = true;
        state.draft.show_system_files = true;
        state.apply_to(&mut config);
        assert!(config.show_system_files);
    }

    fn index_of(state: &ConfigDialogState, label: &str) -> usize {
        state.items.iter().position(|item| item.label == label).unwrap_or_else(|| panic!("{label}"))
    }

    #[test]
    fn move_up_skips_disabled_items() {
        let mut state = default_state();
        state.draft.size_column_enabled = false;
        state.selected_index = index_of(&state, "Show Date Column");
        state.move_up();
        assert_eq!(state.selected_index, index_of(&state, "Show Size Column"));
    }

    #[test]
    fn toggling_a_tool_item_flips_it() {
        let mut state = default_state();
        state.selected_index = index_of(&state, "Show PDF Details (pdfinfo)");
        let before = state.draft.pdf_metadata_enabled;
        state.toggle_selected();
        assert_eq!(state.draft.pdf_metadata_enabled, !before);
        assert_eq!(state.format_value(state.selected_index), format_bool(!before));
    }

    #[test]
    fn sort_mode_cycles_through_every_mode() {
        let mut state = default_state();
        state.selected_index = index_of(&state, "Sort Mode");
        let mut seen = Vec::new();
        for _ in 0..4 {
            state.cycle_next_selected();
            seen.push(state.draft.sort_mode);
        }
        assert_eq!(seen, [SortMode::Modified, SortMode::Size, SortMode::Extension, SortMode::Name]);

        seen.clear();
        for _ in 0..4 {
            state.cycle_prev_selected();
            seen.push(state.draft.sort_mode);
        }
        assert_eq!(seen, [SortMode::Extension, SortMode::Size, SortMode::Modified, SortMode::Name]);
    }

    #[test]
    fn detail_and_preview_sub_items_follow_their_parents() {
        let mut state = default_state();
        let details = [
            "Show Archive Details",
            "Show PDF Details (pdfinfo)",
            "Show Media Details (ffprobe)",
            "Show Media Details (mediainfo)",
        ];
        let previews = [
            "Show Image Previews",
            "Show PDF Previews (pdftopng)",
            "Show Archive Contents",
            "Show Markdown Preview (built-in)",
            "Show CSV/TSV Tables",
        ];
        let enabled = |state: &ConfigDialogState, labels: &[&str]| {
            labels.iter().map(|l| state.is_enabled(index_of(state, l))).collect::<Vec<_>>()
        };

        assert_eq!(enabled(&state, &details), [true; 4]);
        state.draft.file_metadata_enabled = false;
        assert_eq!(enabled(&state, &details), [false; 4]);
        state.draft.file_metadata_enabled = true;
        state.draft.preview_pane_enabled = false;
        assert_eq!(enabled(&state, &details), [false; 4]);

        assert_eq!(enabled(&state, &previews), [true; 5], "previews do not depend on the right pane");
        state.draft.file_previews_enabled = false;
        assert_eq!(enabled(&state, &previews), [false; 5]);
        state.draft.file_previews_enabled = true;
        assert_eq!(enabled(&state, &previews), [true; 5]);
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
    fn items_are_indented_under_their_parent() {
        let state = default_state();
        let indent = |label: &str| state.items[index_of(&state, label)].indent;
        assert_eq!(indent("Icons"), 0);
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
            .position(|item| matches!(item.setting, Setting::SortMode))
            .expect("sort mode item");
        assert_eq!(state.format_value(sort_index), "\u{25c4} name \u{25ba}");
        state.draft.sort_mode = SortMode::Extension;
        assert_eq!(state.format_value(sort_index), "\u{25c4} extension \u{25ba}");
    }
}
