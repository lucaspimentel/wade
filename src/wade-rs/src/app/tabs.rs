//! Rust-only: multiple tabs (GitHub issue #17). The active tab's state lives
//! inline in `App` (so the many readers of `current_path` are unchanged);
//! switching swaps it with the saved `TabState` of the target tab.

use std::collections::{HashMap, HashSet};

use crate::fs::DRIVES_PATH;
use crate::screen::{CellStyle, Color, ScreenBuffer};

use super::App;

/// At most 9 tabs, one per digit key.
pub const MAX_TABS: usize = 9;

const BAR_FG: Color = Color { r: 150, g: 150, b: 160 };
const BAR_BG: Color = Color { r: 30, g: 30, b: 50 };
const ACTIVE_FG: Color = Color { r: 255, g: 255, b: 255 };
const ACTIVE_BG: Color = Color { r: 60, g: 90, b: 150 };
const SEPARATOR: char = '\u{2502}';

/// The per-tab view state (the fields `App` holds inline for the active tab).
#[derive(Clone, Default)]
pub(crate) struct TabState {
    pub current_path: String,
    pub selected_index: usize,
    pub scroll_offset: usize,
    pub selected_index_per_dir: HashMap<String, usize>,
    pub marked_paths: HashSet<String>,
    pub search_filter: String,
}

/// The label shown for a tab: `~` for home, `Drives` for the drives view,
/// the path itself for a root, else the last path component.
#[must_use]
pub fn tab_label(path: &str) -> String {
    if path == DRIVES_PATH {
        return "Drives".to_string();
    }

    let home = std::env::var_os("USERPROFILE").or_else(|| std::env::var_os("HOME"));
    if home.is_some_and(|home| std::path::Path::new(&home) == std::path::Path::new(path)) {
        return "~".to_string();
    }

    match std::path::Path::new(path).file_name() {
        Some(name) => name.to_string_lossy().into_owned(),
        None => path.to_string(),
    }
}

/// The text of each tab (` N label `), shortened with `…` so the whole bar
/// (tabs joined by a separator) fits in `width`.
#[must_use]
pub fn tab_texts(labels: &[String], width: usize) -> Vec<String> {
    let count = labels.len();
    if count == 0 {
        return Vec::new();
    }

    let separators = count - 1;
    let full = |i: usize, label: &str| format!(" {} {label} ", i + 1);
    let total: usize = labels.iter().enumerate().map(|(i, l)| full(i, l).chars().count()).sum::<usize>() + separators;

    if total <= width {
        return labels.iter().enumerate().map(|(i, l)| full(i, l)).collect();
    }

    // Each tab keeps " N " plus at least one label character
    let overhead = 4;
    let budget = width.saturating_sub(separators) / count;
    let max_label = budget.saturating_sub(overhead).max(1);

    labels
        .iter()
        .enumerate()
        .map(|(i, label)| {
            let chars: Vec<char> = label.chars().collect();
            let shown: String = if chars.len() > max_label {
                let mut s: String = chars[..max_label.saturating_sub(1)].iter().collect();
                s.push('\u{2026}');
                s
            } else {
                label.clone()
            };
            full(i, &shown)
        })
        .collect()
}

/// Start and end (exclusive) columns of each tab, in order; shared by the
/// renderer and mouse hit-testing.
#[must_use]
pub fn tab_spans(texts: &[String]) -> Vec<(i32, i32)> {
    let mut col = 0i32;
    texts
        .iter()
        .map(|text| {
            let start = col;
            col += i32::try_from(text.chars().count()).unwrap_or(0);
            let span = (start, col);
            col += 1; // separator
            span
        })
        .collect()
}

impl App {
    #[must_use]
    pub(crate) fn tab_count(&self) -> usize {
        self.tabs.len()
    }

    #[must_use]
    pub(crate) fn tab_bar_visible(&self) -> bool {
        self.tabs.len() > 1
    }

    /// Recalculates the layout, leaving row 0 for the tab bar when shown.
    pub(crate) fn recalc_layout(&mut self) {
        let top = i32::from(self.tab_bar_visible());
        self.layout.calculate_with_top(self.last_width, self.last_height, self.preview_pane_enabled, self.parent_pane_enabled, top);
    }

    fn tab_labels(&self) -> Vec<String> {
        (0..self.tabs.len())
            .map(|i| tab_label(if i == self.active_tab { &self.current_path } else { &self.tabs[i].current_path }))
            .collect()
    }

    fn tab_bar_texts(&self) -> Vec<String> {
        tab_texts(&self.tab_labels(), usize::try_from(self.last_width).unwrap_or(0))
    }

    /// Moves the inline per-view fields into the active tab's slot.
    fn save_active_tab(&mut self) {
        let slot = &mut self.tabs[self.active_tab];
        slot.current_path = std::mem::take(&mut self.current_path);
        slot.selected_index = self.selected_index;
        slot.scroll_offset = self.scroll_offset;
        slot.selected_index_per_dir = std::mem::take(&mut self.selected_index_per_dir);
        slot.marked_paths = std::mem::take(&mut self.marked_paths);
        slot.search_filter = std::mem::take(&mut self.search_filter);
    }

    /// Makes tab `index` active: loads its fields and re-syncs everything
    /// derived from the current directory (like `navigate_to_directory`,
    /// but keeping the tab's marks, filter and selection).
    fn restore_tab(&mut self, index: usize) {
        self.active_tab = index;
        let slot = std::mem::take(&mut self.tabs[index]);
        self.current_path = slot.current_path;
        self.selected_index = slot.selected_index;
        self.scroll_offset = slot.scroll_offset;
        self.selected_index_per_dir = slot.selected_index_per_dir;
        self.marked_paths = slot.marked_paths;
        self.search_filter = slot.search_filter;

        self.filtered_entries = None;
        if matches!(self.input_mode, crate::input::InputMode::Search | crate::input::InputMode::ExpandedPreview) {
            self.input_mode = crate::input::InputMode::Normal;
            self.modal.search_input = None;
        }

        self.clear_preview_cache();
        self.update_terminal_title();
        self.refresh_git_status();
        self.clamp_selection_and_scroll();
        self.recalc_layout();
        self.request_full_redraw = true;
    }

    /// Switches to tab `index`; no-op when out of range or already active.
    pub(crate) fn switch_tab(&mut self, index: usize) {
        if index >= self.tabs.len() || index == self.active_tab {
            return;
        }

        self.save_active_tab();
        self.restore_tab(index);
    }

    /// Opens a tab at the current directory, right after the active one.
    pub(crate) fn new_tab(&mut self) {
        if self.tabs.len() >= MAX_TABS {
            self.show_notification(&format!("At most {MAX_TABS} tabs"), crate::ui::notification::NotificationKind::Info);
            return;
        }

        let fresh = TabState {
            current_path: self.current_path.clone(),
            selected_index: self.selected_index,
            scroll_offset: self.scroll_offset,
            selected_index_per_dir: self.selected_index_per_dir.clone(),
            ..TabState::default()
        };

        self.save_active_tab();
        let index = self.active_tab + 1;
        self.tabs.insert(index, fresh);
        self.restore_tab(index);
    }

    /// The next or previous tab, wrapping.
    pub(crate) fn cycle_tab(&mut self, forward: bool) {
        let count = self.tabs.len();
        let index = if forward { (self.active_tab + 1) % count } else { (self.active_tab + count - 1) % count };
        self.switch_tab(index);
    }

    /// Closes the active tab. Returns false when it is the only one (the
    /// caller then quits like `q`).
    pub(crate) fn close_tab(&mut self) -> bool {
        if self.tabs.len() <= 1 {
            return false;
        }

        // The active slot is a placeholder; the closed tab's state is dropped
        self.tabs.remove(self.active_tab);
        let index = self.active_tab.min(self.tabs.len() - 1);
        self.restore_tab(index);
        true
    }

    /// The tab under column `col` of the tab bar.
    pub(crate) fn tab_at_column(&self, col: i32) -> Option<usize> {
        tab_spans(&self.tab_bar_texts()).iter().position(|&(start, end)| col >= start && col < end)
    }

    /// Draws the tab bar on row 0 (only with 2+ tabs).
    pub(crate) fn render_tab_bar(&self, buffer: &mut ScreenBuffer) {
        if !self.tab_bar_visible() {
            return;
        }

        let bar = CellStyle { fg: Some(BAR_FG), bg: Some(BAR_BG), ..CellStyle::default() };
        let active = CellStyle { fg: Some(ACTIVE_FG), bg: Some(ACTIVE_BG), bold: true, ..CellStyle::default() };

        for col in 0..self.last_width {
            buffer.put(0, col, ' ', bar);
        }

        let texts = self.tab_bar_texts();
        for (i, (text, (start, end))) in texts.iter().zip(tab_spans(&texts)).enumerate() {
            let style = if i == self.active_tab { active } else { bar };
            buffer.write_string(0, start, text, style, i64::from(end - start));

            if i + 1 < texts.len() && end < self.last_width {
                buffer.put(0, end, SEPARATOR, bar);
            }
        }
    }
}

#[cfg(test)]
mod tests {
    use super::{tab_label, tab_spans, tab_texts};

    #[test]
    fn labels_use_the_last_component_home_drives_and_roots() {
        let sep = std::path::MAIN_SEPARATOR;
        assert_eq!(tab_label(&format!("{sep}tmp{sep}project")), "project");
        assert_eq!(tab_label(crate::fs::DRIVES_PATH), "Drives");
        let root = if cfg!(windows) { "C:\\".to_string() } else { "/".to_string() };
        assert_eq!(tab_label(&root), root);

        if let Some(home) = std::env::var_os("USERPROFILE").or_else(|| std::env::var_os("HOME")) {
            assert_eq!(tab_label(&home.to_string_lossy()), "~");
        }
    }

    #[test]
    fn texts_fit_the_width_and_keep_the_numbers() {
        let labels: Vec<String> = ["alpha", "a-very-long-directory-name", "c"].iter().map(|s| s.to_string()).collect();
        assert_eq!(tab_texts(&labels, 200), [" 1 alpha ", " 2 a-very-long-directory-name ", " 3 c "]);

        let narrow = tab_texts(&labels, 30);
        let total: usize = narrow.iter().map(|t| t.chars().count()).sum::<usize>() + 2;
        assert!(total <= 30, "{narrow:?}");
        assert!(narrow[1].starts_with(" 2 ") && narrow[1].contains('\u{2026}'), "{narrow:?}");
        assert_eq!(narrow[2], " 3 c ");
    }

    #[test]
    fn spans_are_ordered_with_one_separator_column() {
        let texts = vec![" 1 a ".to_string(), " 2 bb ".to_string()];
        assert_eq!(tab_spans(&texts), [(0, 5), (6, 12)]);
    }
}
