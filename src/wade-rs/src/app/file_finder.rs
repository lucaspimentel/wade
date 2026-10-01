//! Port of the Ctrl+F file finder in `src/Wade/App.cs` (`ShowFileFinder`,
//! `ScanFilesForFinder`, `StartFinderSearch`, `HandleFileFinderKey`,
//! `RenderFileFinder`): a BFS walk streams entries into a `SearchIndex`,
//! typing streams scored results back, and the dialog lists them with the
//! matched characters highlighted.

use std::collections::{HashMap, VecDeque};
use std::sync::mpsc::Sender;
use std::sync::Arc;
use std::time::{Duration, Instant};

use crate::app::{App, InputMode, KeyEvent};
use crate::console_key::ConsoleKey;
use crate::fs::directory_contents::system_time_to_date_parts;
use crate::fs::{FileSystemEntry, DRIVES_PATH};
use crate::input::{
    CancelToken, FileFinderPartialResultEvent, FileFinderScanCompleteEvent, FileFinderSearchResultEvent, InputEvent,
};
use crate::screen::{CellStyle, Color, ScreenBuffer};
use crate::search::{SearchIndex, SearchOptions, SearchResult};
use crate::ui::dialog_box::{self, BG_COLOR};
use crate::ui::file_icons;
use crate::ui::text_input::TextInput;

/// `ScanFilesForFinder`'s entry cap.
pub const MAX_ENTRIES: usize = 50_000;
/// Partial-result flush throttle of the walk.
pub const SCAN_FLUSH_INTERVAL: Duration = Duration::from_millis(200);
/// Result batching interval of the search pump.
pub const SEARCH_FLUSH_INTERVAL: Duration = Duration::from_millis(100);
/// Rows the selection scroll window keeps visible (and the PgUp/PgDn step).
pub const MAX_ITEM_ROWS: usize = 18;
/// Rows actually drawn: C# loops over `content.Height - 2` = 19 rows, one
/// more than the scroll window, so a 19th item shows below it.
const DRAWN_ITEM_ROWS: usize = MAX_ITEM_ROWS + 1;

const FOOTER: &str = "[\u{2191}\u{2193}] Navigate  [Enter] Open  [Esc] Cancel";

/// The finder's per-session state (the C# `_fileFinder*` fields).
pub struct FileFinderState {
    pub input: TextInput,
    pub selected_index: usize,
    pub scroll_offset: usize,
    /// Entries from the walk, in arrival order; `None` until the first batch.
    pub all_entries: Option<Vec<FileSystemEntry>>,
    pub index: Arc<SearchIndex>,
    pub last_query: String,
    /// Results of the current search; `None` until the first batch.
    pub results: Option<Vec<SearchResult>>,
    /// Full path -> index into `all_entries` (first occurrence wins).
    entry_cache: HashMap<String, usize>,
    /// Sorted (entry index, result index) pairs for a non-empty query.
    display_cache: Option<Vec<(usize, usize)>>,
    display_dirty: bool,
    pub scanning: bool,
    pub base_path: String,
    scan_cancel: CancelToken,
    search_cancel: Option<CancelToken>,
    pub search_id: u64,
}

impl FileFinderState {
    /// Port of the cache maintenance in `GetFinderDisplayEntries`: re-sorts
    /// the results (score descending, then path ordinal) and rebuilds the
    /// display list when they changed. Call before `display`.
    pub fn refresh_display(&mut self) {
        let Some(results) = self.results.as_mut() else {
            return;
        };

        if !self.display_dirty && self.display_cache.is_some() {
            return;
        }

        results.sort_by(|a, b| b.score.cmp(&a.score).then_with(|| a.path.cmp(&b.path)));

        // Skip results whose entries the walk has not delivered yet
        let display: Vec<(usize, usize)> = results
            .iter()
            .enumerate()
            .filter_map(|(result_index, result)| {
                self.entry_cache.get(&result.path).map(|&entry_index| (entry_index, result_index))
            })
            .collect();

        self.display_cache = Some(display);
        self.display_dirty = false;
    }

    /// Port of `GetFinderDisplayEntries`' result: every scanned entry for an
    /// empty query, otherwise the sorted results whose entries are known.
    /// Returns (entry index, match positions) pairs.
    #[must_use]
    pub fn display(&self) -> Vec<(usize, &[usize])> {
        if self.input.value().is_empty() {
            let count = self.all_entries.as_ref().map_or(0, Vec::len);
            return (0..count).map(|index| (index, &[][..])).collect();
        }

        let (Some(results), Some(display)) = (self.results.as_ref(), self.display_cache.as_ref()) else {
            return Vec::new();
        };

        display
            .iter()
            .map(|&(entry_index, result_index)| (entry_index, results[result_index].match_positions.as_slice()))
            .collect()
    }

    fn display_count(&mut self) -> usize {
        self.refresh_display();
        self.display().len()
    }

    fn entry(&self, index: usize) -> &FileSystemEntry {
        &self.all_entries.as_ref().unwrap()[index]
    }
}

impl App {
    /// Port of `ShowFileFinder`: opens the dialog and starts the walk.
    pub fn show_file_finder(&mut self) {
        if self.current_path == DRIVES_PATH {
            return;
        }

        if let Some(previous) = self.file_finder.take() {
            close_state(&previous);
        }

        let base_path = self.current_path.clone();
        let index = Arc::new(SearchIndex::new(&base_path));
        let scan_cancel = CancelToken::new();

        self.input_mode = InputMode::FileFinder;
        self.file_finder = Some(FileFinderState {
            input: TextInput::default(),
            selected_index: 0,
            scroll_offset: 0,
            all_entries: None,
            index: Arc::clone(&index),
            last_query: String::new(),
            results: None,
            entry_cache: HashMap::new(),
            display_cache: None,
            display_dirty: false,
            scanning: true,
            base_path: base_path.clone(),
            scan_cancel: scan_cancel.clone(),
            search_cancel: None,
            search_id: 0,
        });

        let show_hidden = self.directory_contents.show_hidden_files;
        let show_system = self.directory_contents.show_system_files;
        let sender = self.pipeline.sender();

        std::thread::spawn(move || {
            scan_files_for_finder(&base_path, show_hidden, show_system, &sender, &scan_cancel, Some(&index));
        });
    }

    /// Port of `CloseFileFinder`.
    pub fn close_file_finder(&mut self) {
        if let Some(state) = self.file_finder.take() {
            close_state(&state);
        }

        self.input_mode = InputMode::Normal;
    }

    /// Port of the `FileFinderPartialResultEvent` handling in the main loop.
    pub fn handle_file_finder_partial_result(&mut self, event: FileFinderPartialResultEvent) {
        if self.input_mode != InputMode::FileFinder || event.base_path != self.current_path {
            return;
        }

        let Some(state) = self.file_finder.as_mut() else {
            return;
        };

        let all_entries = state.all_entries.get_or_insert_with(Vec::new);

        for entry in event.entries {
            state.entry_cache.entry(entry.full_path.clone()).or_insert(all_entries.len());
            all_entries.push(entry);
        }

        // Results whose entries arrive after them become displayable now
        // (C# waits for the next result batch; KNOWN_DEVIATIONS.md)
        state.display_dirty = true;
    }

    /// Port of the `FileFinderScanCompleteEvent` handling.
    pub fn handle_file_finder_scan_complete(&mut self, event: FileFinderScanCompleteEvent) {
        if self.input_mode != InputMode::FileFinder || event.base_path != self.current_path {
            return;
        }

        if let Some(state) = self.file_finder.as_mut() {
            state.scanning = false;
        }
    }

    /// Port of `HandleFileFinderSearchResult`.
    pub fn handle_file_finder_search_result(&mut self, event: FileFinderSearchResultEvent) {
        if self.input_mode != InputMode::FileFinder || event.base_path != self.current_path || event.results.is_empty() {
            return;
        }

        let Some(state) = self.file_finder.as_mut() else {
            return;
        };

        if event.search_id != state.search_id {
            return;
        }

        state.results.get_or_insert_with(Vec::new).extend(event.results);
        state.display_dirty = true;
    }

    /// Port of `StartFinderSearch`: (re)starts the search for the current
    /// input and a pump thread that batches results into events.
    pub fn start_finder_search(&mut self) {
        let sender = self.pipeline.sender();
        let base_path = self.current_path.clone();

        let Some(state) = self.file_finder.as_mut() else {
            return;
        };

        let query = state.input.value().to_string();

        if query == state.last_query {
            return;
        }

        state.last_query.clone_from(&query);
        state.results = None;
        state.display_cache = None;
        state.display_dirty = false;

        if let Some(cancel) = state.search_cancel.take() {
            cancel.cancel();
        }

        state.search_id += 1;
        let search_id = state.search_id;

        if query.is_empty() {
            state.index.cancel_search();
            return;
        }

        let cancel = CancelToken::new();
        state.search_cancel = Some(cancel.clone());
        let receiver = state.index.search(&query, SearchOptions::default());

        std::thread::spawn(move || {
            let send = |results: Vec<SearchResult>, is_complete: bool| {
                if cancel.is_cancelled() {
                    return false;
                }

                sender
                    .send(InputEvent::FileFinderSearchResult(FileFinderSearchResultEvent {
                        base_path: base_path.clone(),
                        results,
                        is_complete,
                        search_id,
                    }))
                    .is_ok()
            };

            let mut batch: Vec<SearchResult> = Vec::new();
            let mut last_flush = Instant::now();

            // Block for the next result, drain what is queued (flushing every
            // 100ms), then flush the remainder: results stay timely while the
            // channel stays open for live pushes.
            while let Ok(result) = receiver.recv() {
                if cancel.is_cancelled() {
                    return;
                }

                batch.push(result);

                while let Ok(result) = receiver.try_recv() {
                    batch.push(result);

                    if last_flush.elapsed() >= SEARCH_FLUSH_INTERVAL {
                        if !send(std::mem::take(&mut batch), false) {
                            return;
                        }

                        last_flush = Instant::now();
                    }
                }

                if !batch.is_empty() {
                    if !send(std::mem::take(&mut batch), false) {
                        return;
                    }

                    last_flush = Instant::now();
                }
            }

            // Channel completed: final flush
            if !batch.is_empty() {
                send(batch, true);
            }
        });
    }

    /// Port of the `InputMode.FileFinder` paste branch of `HandlePasteEvent`.
    pub fn paste_into_file_finder(&mut self, text: &str) {
        if let Some(state) = self.file_finder.as_mut() {
            state.input.insert_string(text);
            state.selected_index = 0;
            state.scroll_offset = 0;
        }

        self.start_finder_search();
    }

    /// Port of `HandleFileFinderKey`.
    pub fn handle_file_finder_key(&mut self, key: KeyEvent) {
        if self.file_finder.is_none() {
            self.input_mode = InputMode::Normal;
            return;
        }

        if key.control {
            let state = self.file_finder.as_mut().unwrap();

            match key.key {
                ConsoleKey::LeftArrow => {
                    state.input.move_cursor_word_left();
                    return;
                }
                ConsoleKey::RightArrow => {
                    state.input.move_cursor_word_right();
                    return;
                }
                ConsoleKey::Backspace => {
                    state.input.delete_word_backward();
                    state.selected_index = 0;
                    state.scroll_offset = 0;
                    self.start_finder_search();
                    return;
                }
                _ => {}
            }
        }

        let count = self.file_finder.as_mut().unwrap().display_count();

        match key.key {
            ConsoleKey::Escape => {
                self.close_file_finder();
                return;
            }
            ConsoleKey::Enter => {
                let state = self.file_finder.as_mut().unwrap();

                if count > 0 && state.selected_index < count {
                    let (entry_index, _) = state.display()[state.selected_index];
                    let path = state.entry(entry_index).full_path.clone();
                    self.close_file_finder();
                    self.navigate_to_path(&path);
                }

                return;
            }
            ConsoleKey::UpArrow => {
                let state = self.file_finder.as_mut().unwrap();
                state.selected_index = state.selected_index.saturating_sub(1);
            }
            ConsoleKey::DownArrow => {
                let state = self.file_finder.as_mut().unwrap();

                if state.selected_index + 1 < count {
                    state.selected_index += 1;
                }
            }
            ConsoleKey::PageUp => {
                let state = self.file_finder.as_mut().unwrap();
                let step = MAX_ITEM_ROWS.min(count);
                state.selected_index = state.selected_index.saturating_sub(step);
            }
            ConsoleKey::PageDown => {
                let state = self.file_finder.as_mut().unwrap();
                let step = MAX_ITEM_ROWS.min(count);
                // C# Math.Min(count - 1, ...) goes to -1 on an empty list;
                // the clamp below turns that into 0
                state.selected_index = (state.selected_index + step).min(count.saturating_sub(1));
            }
            ConsoleKey::Home => self.file_finder.as_mut().unwrap().selected_index = 0,
            ConsoleKey::End => self.file_finder.as_mut().unwrap().selected_index = count.saturating_sub(1),
            ConsoleKey::Backspace => self.edit_finder_input(TextInput::delete_backward),
            ConsoleKey::Delete => self.edit_finder_input(TextInput::delete_forward),
            ConsoleKey::LeftArrow => self.file_finder.as_mut().unwrap().input.move_cursor_left(),
            ConsoleKey::RightArrow => self.file_finder.as_mut().unwrap().input.move_cursor_right(),
            _ => {
                let state = self.file_finder.as_mut().unwrap();

                if key.key == ConsoleKey::K && key.control {
                    state.selected_index = state.selected_index.saturating_sub(1);
                } else if key.key == ConsoleKey::J && key.control {
                    if state.selected_index + 1 < count {
                        state.selected_index += 1;
                    }
                } else if key.key_char >= 0x20
                    && let Some(ch) = char::from_u32(u32::from(key.key_char))
                {
                    state.input.insert_char(ch);
                    state.selected_index = 0;
                    state.scroll_offset = 0;
                    self.start_finder_search();
                }
            }
        }

        // Keep the selection valid and visible
        let state = self.file_finder.as_mut().unwrap();
        let count = state.display_count();
        state.selected_index = if count > 0 { state.selected_index.min(count - 1) } else { 0 };

        if state.selected_index < state.scroll_offset {
            state.scroll_offset = state.selected_index;
        } else if state.selected_index >= state.scroll_offset + MAX_ITEM_ROWS {
            state.scroll_offset = state.selected_index + 1 - MAX_ITEM_ROWS;
        }
    }

    fn edit_finder_input(&mut self, edit: fn(&mut TextInput)) {
        let state = self.file_finder.as_mut().unwrap();
        edit(&mut state.input);
        state.selected_index = 0;
        state.scroll_offset = 0;
        self.start_finder_search();
    }

    /// Port of `RenderFileFinder`: gathers the view and draws it.
    pub fn render_file_finder(&mut self, buffer: &mut ScreenBuffer, width: i32, height: i32) {
        let current_path = self.current_path.clone();
        let Some(state) = self.file_finder.as_mut() else {
            return;
        };

        state.refresh_display();

        // TextInput::render updates the input's scroll offset, so render a
        // copy and store it back once the view's borrows end
        let mut input = state.input.clone();

        {
            let total = state.index.count();
            let matching =
                if input.value().is_empty() { total } else { state.results.as_ref().map_or(0, Vec::len) };
            let display = state.display();
            let items: Vec<FinderItem<'_>> = display
                .iter()
                .skip(state.scroll_offset)
                .take(DRAWN_ITEM_ROWS)
                .map(|&(entry_index, positions)| FinderItem {
                    entry: state.entry(entry_index),
                    match_positions: positions,
                })
                .collect();

            let view = FinderView {
                scanning: state.scanning && state.all_entries.is_some(),
                has_entries: state.all_entries.is_some(),
                display_count: display.len(),
                matching,
                total,
                current_path: &current_path,
                selected_index: state.selected_index,
                scroll_offset: state.scroll_offset,
                items: &items,
            };

            render_file_finder_view(buffer, width, height, &view, &mut input);
        }

        state.input = input;
    }
}

fn close_state(state: &FileFinderState) {
    state.scan_cancel.cancel();

    if let Some(cancel) = &state.search_cancel {
        cancel.cancel();
    }

    // C# disposes the index here; the scan thread may still hold a clone
    state.index.cancel_search();
}

/// One visible finder row: the entry and its match positions in the path
/// relative to the finder's base.
pub struct FinderItem<'a> {
    pub entry: &'a FileSystemEntry,
    pub match_positions: &'a [usize],
}

/// Everything `render_file_finder_view` draws (the C# render reads these
/// from `App` fields). `items` starts at `scroll_offset`.
pub struct FinderView<'a> {
    pub scanning: bool,
    pub has_entries: bool,
    /// Length of the whole display list (not just `items`)
    pub display_count: usize,
    pub matching: usize,
    pub total: usize,
    pub current_path: &'a str,
    pub selected_index: usize,
    pub scroll_offset: usize,
    pub items: &'a [FinderItem<'a>],
}

/// Pure port of `RenderFileFinder`'s drawing.
pub fn render_file_finder_view(
    buffer: &mut ScreenBuffer,
    width: i32,
    height: i32,
    view: &FinderView<'_>,
    input: &mut TextInput,
) {
    // C# Math.Clamp(w * 3 / 4, 70, w - 8) throws below 78 columns; the port
    // lets the upper bound win instead (KNOWN_DEVIATIONS.md)
    let content_width = (width * 3 / 4).max(70).min(width - 8);
    let content_height = MAX_ITEM_ROWS as i32 + 3; // input + separator + count + item rows
    let title = if view.scanning { "Find File [scanning...]" } else { "Find File" };

    let content = dialog_box::render(
        buffer,
        width,
        height,
        content_width.max(FOOTER.chars().count() as i32),
        content_height.max(4),
        Some(title),
        Some(FOOTER),
    );

    let style = |fg: Color, bg: Color| CellStyle {
        fg: Some(fg),
        bg: Some(bg),
        ..CellStyle::default()
    };

    // Row 0: text input with "> " prefix
    buffer.write_string(
        content.top,
        content.left,
        "> ",
        style(Color { r: 220, g: 220, b: 100 }, BG_COLOR),
        i64::from(i32::MAX),
    );
    input.render(
        buffer,
        content.top,
        content.left + 2,
        content.width - 2,
        style(Color { r: 200, g: 200, b: 200 }, BG_COLOR),
    );

    // Row 1: separator with the result count
    let separator_style = CellStyle {
        fg: Some(dialog_box::BORDER_COLOR),
        bg: Some(BG_COLOR),
        dim: true,
        ..CellStyle::default()
    };

    for c in 0..content.width {
        buffer.put(content.top + 1, content.left + c, '\u{2500}', separator_style);
    }

    let count_text = format!("  {}/{} ", view.matching, view.total);
    buffer.write_string(
        content.top + 1,
        content.left + 1,
        &count_text,
        style(Color { r: 180, g: 180, b: 60 }, BG_COLOR),
        i64::from(i32::MAX),
    );

    if view.display_count == 0 {
        let empty_text = if view.has_entries { "No matches" } else { "" };
        buffer.write_string(
            content.top + 2,
            content.left + 1,
            empty_text,
            style(Color { r: 120, g: 120, b: 140 }, BG_COLOR),
            i64::from(i32::MAX),
        );
        return;
    }

    // Rows 2+: items
    let light = Color { r: 200, g: 200, b: 200 };
    let normal_style = style(light, BG_COLOR);
    let selected_style = style(Color { r: 20, g: 20, b: 35 }, light);
    let highlight_style = style(Color { r: 80, g: 250, b: 120 }, BG_COLOR);
    let highlight_selected_style = style(Color { r: 20, g: 120, b: 50 }, light);

    let visible_count = (content.height - 2).max(0) as usize;

    for (i, item) in view.items.iter().take(visible_count).enumerate() {
        let item_index = view.scroll_offset + i;
        let selected = item_index == view.selected_index;
        let row = content.top + 2 + i as i32;

        let base_style = if selected { selected_style } else { normal_style };
        let match_style = if selected { highlight_selected_style } else { highlight_style };

        if selected {
            buffer.fill_row(row, content.left, content.width, ' ', selected_style);
        }

        buffer.put(row, content.left + 1, file_icons::get_icon(item.entry), base_style);

        // Full relative path with match highlighting
        let relative = crate::search::index::relative_path(view.current_path, &item.entry.full_path);
        let max_chars = (content.width - 3).max(0) as usize;
        let mut mi = 0; // index into match_positions

        for (col, (ci, ch)) in (content.left + 3..).zip(relative.chars().enumerate().take(max_chars)) {
            let is_match = mi < item.match_positions.len() && item.match_positions[mi] == ci;

            if is_match {
                mi += 1;
            }

            buffer.put(row, col, ch, if is_match { match_style } else { base_style });
        }
    }
}

/// Port of `ScanFilesForFinder`: breadth-first walk (so current-directory
/// entries come first) that streams entries in batches every 200ms, adds
/// each path to `index`, and finishes with a scan-complete event. Always
/// skips `.git`; honors the hidden/system visibility settings; stops at the
/// 50,000-entry cap (checked per directory, like C#).
pub fn scan_files_for_finder(
    base_path: &str,
    show_hidden: bool,
    show_system: bool,
    out: &Sender<InputEvent>,
    cancel: &CancelToken,
    index: Option<&SearchIndex>,
) {
    let mut total_count = 0usize;
    let mut batch: Vec<FileSystemEntry> = Vec::new();
    let mut last_flush = Instant::now();
    let mut queue: VecDeque<std::path::PathBuf> = VecDeque::from([std::path::PathBuf::from(base_path)]);

    let send_batch = |batch: Vec<FileSystemEntry>| {
        let _ = out.send(InputEvent::FileFinderPartialResult(FileFinderPartialResultEvent {
            base_path: base_path.to_string(),
            entries: batch,
        }));
    };

    while total_count < MAX_ENTRIES {
        let Some(current_dir) = queue.pop_front() else {
            break;
        };

        if cancel.is_cancelled() {
            return;
        }

        // EnumerateFiles then EnumerateDirectories: one read_dir, split by kind
        let (files, dirs) = list_directory(&current_dir);

        for item in files {
            if cancel.is_cancelled() {
                return;
            }

            if !show_system && cfg!(windows) && has_attribute(&item.metadata, FILE_ATTRIBUTE_SYSTEM) {
                continue;
            }

            if !show_hidden && (has_attribute(&item.metadata, FILE_ATTRIBUTE_HIDDEN) || item.name.starts_with('.')) {
                continue;
            }

            let entry = make_entry(&item, false);
            if let Some(index) = index {
                index.add(&entry.full_path);
            }

            batch.push(entry);
            total_count += 1;
        }

        for item in dirs {
            // Always skip .git directories
            if item.name.eq_ignore_ascii_case(".git") {
                continue;
            }

            if !show_hidden && (item.name.starts_with('.') || has_attribute(&item.metadata, FILE_ATTRIBUTE_HIDDEN)) {
                continue;
            }

            if !show_system && cfg!(windows) && has_attribute(&item.metadata, FILE_ATTRIBUTE_SYSTEM) {
                continue;
            }

            let entry = make_entry(&item, true);
            if let Some(index) = index {
                index.add(&entry.full_path);
            }

            batch.push(entry);
            total_count += 1;
            queue.push_back(item.path);
        }

        // Flush the batch periodically (time-based throttle)
        if !batch.is_empty() && last_flush.elapsed() >= SCAN_FLUSH_INTERVAL {
            if cancel.is_cancelled() {
                return;
            }

            send_batch(std::mem::take(&mut batch));
            last_flush = Instant::now();
        }
    }

    if !cancel.is_cancelled() {
        if !batch.is_empty() {
            send_batch(batch);
        }

        let _ = out.send(InputEvent::FileFinderScanComplete(FileFinderScanCompleteEvent {
            base_path: base_path.to_string(),
        }));
    }
}

const FILE_ATTRIBUTE_HIDDEN: u32 = 0x0002;
const FILE_ATTRIBUTE_SYSTEM: u32 = 0x0004;

struct WalkItem {
    name: String,
    path: std::path::PathBuf,
    /// Metadata of the item itself (links not followed)
    metadata: std::fs::Metadata,
}

/// One directory's children split into (files, directories). Like .NET's
/// enumeration, a symlink or junction to a directory counts as a directory
/// (so the walk follows it); unreadable directories yield nothing.
fn list_directory(dir: &std::path::Path) -> (Vec<WalkItem>, Vec<WalkItem>) {
    let mut files = Vec::new();
    let mut dirs = Vec::new();

    let Ok(read_dir) = std::fs::read_dir(dir) else {
        return (files, dirs); // IgnoreInaccessible
    };

    for item in read_dir.flatten() {
        let (Ok(file_type), Ok(metadata)) = (item.file_type(), item.metadata()) else {
            continue;
        };

        let is_dir = file_type.is_dir()
            || (file_type.is_symlink() && std::fs::metadata(item.path()).is_ok_and(|target| target.is_dir()));

        let walk_item = WalkItem {
            name: item.file_name().to_string_lossy().into_owned(),
            path: item.path(),
            metadata,
        };

        if is_dir {
            dirs.push(walk_item);
        } else {
            files.push(walk_item);
        }
    }

    (files, dirs)
}

#[cfg(windows)]
fn has_attribute(metadata: &std::fs::Metadata, attribute: u32) -> bool {
    use std::os::windows::fs::MetadataExt;
    metadata.file_attributes() & attribute != 0
}

/// Unix has no attribute bits; .NET's Hidden is the dot prefix, checked by
/// the callers.
#[cfg(not(windows))]
fn has_attribute(_metadata: &std::fs::Metadata, _attribute: u32) -> bool {
    false
}

fn make_entry(item: &WalkItem, is_directory: bool) -> FileSystemEntry {
    let link_target = if item.metadata.file_type().is_symlink() {
        std::fs::read_link(&item.path).ok().map(|target| target.to_string_lossy().into_owned())
    } else {
        None
    };

    FileSystemEntry {
        name: item.name.clone(),
        full_path: item.path.to_string_lossy().into_owned(),
        is_directory,
        size: if is_directory { 0 } else { i64::try_from(item.metadata.len()).unwrap_or(0) },
        last_modified: system_time_to_date_parts(item.metadata.modified().unwrap_or(std::time::SystemTime::UNIX_EPOCH)),
        link_target,
        is_broken_symlink: false,
        is_drive: false,
        is_cloud_placeholder: false,
        is_junction_point: false,
        is_app_exec_link: false,
        app_exec_link_target: None,
        drive_format: None,
        drive_label: None,
        drive_free_space: 0,
        drive_total_size: 0,
    }
}

#[cfg(test)]
mod tests {
    use std::path::{Path, PathBuf};
    use std::time::{Duration, Instant};

    use super::{render_file_finder_view, scan_files_for_finder, FinderItem, FinderView};
    use crate::app::{App, AppConfig, InputMode, KeyEvent};
    use crate::console_key::ConsoleKey;
    use crate::fs::FileSystemEntry;
    use crate::input::{CancelToken, FileFinderSearchResultEvent, InputEvent};
    use crate::screen::ScreenBuffer;
    use crate::search::SearchResult;
    use crate::ui::text_input::TextInput;

    fn test_root(name: &str) -> PathBuf {
        let dir = std::env::temp_dir().join(format!("wade-finder-{}-{}", std::process::id(), name));
        let _ = std::fs::remove_dir_all(&dir);
        std::fs::create_dir_all(&dir).unwrap();
        dir
    }

    fn touch(path: &Path) {
        if let Some(parent) = path.parent() {
            std::fs::create_dir_all(parent).unwrap();
        }

        std::fs::write(path, "").unwrap();
    }

    /// Runs the walk synchronously and collects (entries, completed).
    fn scan(root: &Path, show_hidden: bool, cancel: &CancelToken) -> (Vec<FileSystemEntry>, bool) {
        let (sender, receiver) = std::sync::mpsc::channel();
        scan_files_for_finder(root.to_str().unwrap(), show_hidden, true, &sender, cancel, None);
        drop(sender);

        let mut entries = Vec::new();
        let mut completed = false;

        for event in receiver {
            match event {
                InputEvent::FileFinderPartialResult(partial) => entries.extend(partial.entries),
                InputEvent::FileFinderScanComplete(_) => completed = true,
                other => panic!("unexpected event {other:?}"),
            }
        }

        (entries, completed)
    }

    fn has(entries: &[FileSystemEntry], name: &str, is_directory: bool) -> bool {
        entries.iter().any(|e| e.name == name && e.is_directory == is_directory)
    }

    fn position(entries: &[FileSystemEntry], name: &str) -> usize {
        entries.iter().position(|e| e.name == name).unwrap_or_else(|| panic!("{name} missing"))
    }

    #[test]
    fn scan_finds_files_and_directories_in_subdirectories() {
        let root = test_root("subdirs");
        touch(&root.join("root.txt"));
        touch(&root.join("a").join("b").join("deep.txt"));

        let (entries, completed) = scan(&root, true, &CancelToken::new());
        let _ = std::fs::remove_dir_all(&root);

        assert!(completed);
        assert!(has(&entries, "root.txt", false));
        assert!(has(&entries, "deep.txt", false));
        assert!(has(&entries, "a", true));
        assert!(has(&entries, "b", true));
    }

    #[test]
    fn scan_bfs_order_current_directory_entries_appear_first() {
        let root = test_root("bfs");
        touch(&root.join("root.txt"));
        touch(&root.join("child").join("child.txt"));
        touch(&root.join("child").join("grandchild").join("deep.txt"));

        let (entries, _) = scan(&root, true, &CancelToken::new());
        let _ = std::fs::remove_dir_all(&root);

        assert!(position(&entries, "root.txt") < position(&entries, "child.txt"));
        assert!(position(&entries, "child") < position(&entries, "child.txt"));
        assert!(position(&entries, "child.txt") < position(&entries, "deep.txt"));
        assert!(position(&entries, "grandchild") < position(&entries, "deep.txt"));
    }

    #[test]
    fn scan_skips_hidden_files_when_show_hidden_false() {
        let root = test_root("hidden-off");
        touch(&root.join(".hidden"));
        touch(&root.join("visible.txt"));

        let (entries, _) = scan(&root, false, &CancelToken::new());
        let _ = std::fs::remove_dir_all(&root);

        assert!(!entries.iter().any(|e| e.name == ".hidden"));
        assert!(has(&entries, "visible.txt", false));
    }

    #[test]
    fn scan_includes_hidden_files_when_show_hidden_true() {
        let root = test_root("hidden-on");
        touch(&root.join(".hidden"));
        touch(&root.join("visible.txt"));

        let (entries, _) = scan(&root, true, &CancelToken::new());
        let _ = std::fs::remove_dir_all(&root);

        assert!(has(&entries, ".hidden", false));
        assert!(has(&entries, "visible.txt", false));
    }

    #[test]
    fn scan_skips_git_directory() {
        let root = test_root("git");
        touch(&root.join(".git").join("objects").join("abc123"));
        touch(&root.join("readme.md"));

        let (entries, _) = scan(&root, true, &CancelToken::new());
        let _ = std::fs::remove_dir_all(&root);

        assert!(!entries.iter().any(|e| e.full_path.contains(".git")));
        assert!(has(&entries, "readme.md", false));
    }

    #[test]
    fn scan_skips_hidden_dot_directories_when_hidden_disabled() {
        let root = test_root("dotdir-off");
        touch(&root.join(".hidden").join("secret.txt"));
        touch(&root.join("visible.txt"));

        let (entries, _) = scan(&root, false, &CancelToken::new());
        let _ = std::fs::remove_dir_all(&root);

        assert!(!entries.iter().any(|e| e.name == ".hidden" || e.name == "secret.txt"));
        assert!(has(&entries, "visible.txt", false));
    }

    #[test]
    fn scan_includes_hidden_dot_directories_when_hidden_enabled() {
        let root = test_root("dotdir-on");
        touch(&root.join(".hidden").join("secret.txt"));
        touch(&root.join("visible.txt"));

        let (entries, _) = scan(&root, true, &CancelToken::new());
        let _ = std::fs::remove_dir_all(&root);

        assert!(has(&entries, ".hidden", true));
        assert!(has(&entries, "secret.txt", false));
        assert!(has(&entries, "visible.txt", false));
    }

    #[test]
    fn scan_always_skips_git_even_when_hidden_enabled() {
        let root = test_root("git-hidden");
        touch(&root.join(".git").join("refs").join("HEAD"));
        touch(&root.join(".config").join("settings.json"));

        let (entries, _) = scan(&root, true, &CancelToken::new());
        let _ = std::fs::remove_dir_all(&root);

        assert!(!entries.iter().any(|e| e.full_path.contains(".git")));
        assert!(has(&entries, "settings.json", false));
    }

    #[test]
    fn scan_respects_cancel() {
        let root = test_root("cancel");
        touch(&root.join("file.txt"));

        let cancel = CancelToken::new();
        cancel.cancel(); // Cancel before the scan
        let (entries, completed) = scan(&root, true, &cancel);
        let _ = std::fs::remove_dir_all(&root);

        assert!(entries.is_empty());
        assert!(!completed);
    }

    #[cfg(unix)]
    #[test]
    fn scan_follows_symlinked_directories() {
        let root = test_root("symlink");
        let outside = test_root("symlink-target");
        touch(&outside.join("inside.txt"));
        std::os::unix::fs::symlink(&outside, root.join("link")).unwrap();

        let (entries, _) = scan(&root, true, &CancelToken::new());
        let _ = std::fs::remove_dir_all(&root);
        let _ = std::fs::remove_dir_all(&outside);

        // Like .NET's EnumerateDirectories: listed as a directory and walked
        assert!(has(&entries, "link", true));
        assert!(has(&entries, "inside.txt", false));
    }

    // --- App-level flow ---

    fn app_at(root: &Path) -> App {
        let mut app = App::new(AppConfig {
            git_status_enabled: false,
            ..AppConfig::default()
        });
        app.current_path = root.to_string_lossy().into_owned();
        app
    }

    fn key(key: ConsoleKey, ch: char) -> KeyEvent {
        KeyEvent {
            key,
            key_char: ch as u16,
            shift: false,
            alt: false,
            control: false,
        }
    }

    fn type_text(app: &mut App, text: &str) {
        for ch in text.chars() {
            app.handle_file_finder_key(key(ConsoleKey::None, ch));
        }
    }

    /// Routes finder events from the app's pipeline until `done` holds.
    fn pump_until(app: &mut App, done: impl Fn(&mut App) -> bool) {
        let deadline = Instant::now() + Duration::from_secs(10);

        while !done(app) {
            assert!(Instant::now() < deadline, "timed out waiting for finder events");

            match app.pipeline.try_take() {
                Some(InputEvent::FileFinderPartialResult(event)) => app.handle_file_finder_partial_result(event),
                Some(InputEvent::FileFinderScanComplete(event)) => app.handle_file_finder_scan_complete(event),
                Some(InputEvent::FileFinderSearchResult(event)) => app.handle_file_finder_search_result(event),
                Some(_) => {}
                None => std::thread::sleep(Duration::from_millis(10)),
            }
        }
    }

    fn scan_done(app: &mut App) -> bool {
        app.file_finder.as_ref().is_some_and(|state| !state.scanning)
    }

    fn display_names(app: &mut App) -> Vec<String> {
        let state = app.file_finder.as_mut().unwrap();
        state.refresh_display();
        state
            .display()
            .iter()
            .map(|&(index, _)| state.entry(index).name.clone())
            .collect()
    }

    #[test]
    fn empty_query_lists_every_scanned_entry() {
        let root = test_root("app-all");
        touch(&root.join("a.txt"));
        touch(&root.join("sub").join("b.txt"));

        let mut app = app_at(&root);
        app.show_file_finder();
        assert_eq!(app.input_mode, InputMode::FileFinder);
        pump_until(&mut app, scan_done);

        assert_eq!(display_names(&mut app), ["a.txt", "sub", "b.txt"]);
        assert_eq!(app.file_finder.as_ref().unwrap().index.count(), 3);
        app.close_file_finder();
        let _ = std::fs::remove_dir_all(&root);
    }

    #[test]
    fn query_ranks_file_name_matches_first() {
        let root = test_root("app-rank");
        touch(&root.join("src").join("Applications").join("Config.cs"));
        touch(&root.join("src").join("Wade").join("App.cs"));

        let mut app = app_at(&root);
        app.show_file_finder();
        pump_until(&mut app, scan_done);
        type_text(&mut app, "App");
        pump_until(&mut app, |app| app.file_finder.as_ref().unwrap().results.as_ref().is_some_and(|r| r.len() >= 2));

        let names = display_names(&mut app);
        assert_eq!(names[0], "App.cs");
        assert!(names.contains(&"Config.cs".to_string()) || names.contains(&"Applications".to_string()));

        let state = app.file_finder.as_mut().unwrap();
        let display = state.display();
        let sep = std::path::MAIN_SEPARATOR;
        // "src/Wade/" is 9 chars: App.cs's match is offset into the relative path
        assert_eq!(display[0].1, [9, 10, 11], "positions index src{sep}Wade{sep}App.cs");
        app.close_file_finder();
        let _ = std::fs::remove_dir_all(&root);
    }

    #[test]
    fn enter_navigates_to_the_selected_file() {
        let root = test_root("app-enter");
        touch(&root.join("deep").join("target.txt"));
        touch(&root.join("other.md"));

        let mut app = app_at(&root);
        app.show_file_finder();
        pump_until(&mut app, scan_done);
        type_text(&mut app, "target");
        pump_until(&mut app, |app| app.file_finder.as_ref().unwrap().results.is_some());

        app.handle_file_finder_key(key(ConsoleKey::Enter, '\r'));

        assert_eq!(app.input_mode, InputMode::Normal);
        assert!(app.file_finder.is_none());
        assert_eq!(Path::new(&app.current_path), root.join("deep"));
        let entries = app.get_visible_entries();
        assert_eq!(entries[app.selected_index].name, "target.txt");
        let _ = std::fs::remove_dir_all(&root);
    }

    #[test]
    fn escape_closes_and_clears_state() {
        let root = test_root("app-esc");
        touch(&root.join("a.txt"));

        let mut app = app_at(&root);
        app.show_file_finder();
        app.handle_file_finder_key(key(ConsoleKey::Escape, '\u{1b}'));

        assert_eq!(app.input_mode, InputMode::Normal);
        assert!(app.file_finder.is_none());
        let _ = std::fs::remove_dir_all(&root);
    }

    #[test]
    fn stale_search_results_are_ignored() {
        let root = test_root("app-stale");
        touch(&root.join("a.txt"));

        let mut app = app_at(&root);
        app.show_file_finder();
        pump_until(&mut app, scan_done);

        let base_path = app.current_path.clone();
        app.handle_file_finder_search_result(FileFinderSearchResultEvent {
            base_path,
            results: vec![SearchResult {
                path: root.join("a.txt").to_string_lossy().into_owned(),
                score: 1,
                match_positions: vec![0],
            }],
            is_complete: false,
            search_id: 999,
        });

        assert!(app.file_finder.as_ref().unwrap().results.is_none());
        app.close_file_finder();
        let _ = std::fs::remove_dir_all(&root);
    }

    #[test]
    fn navigation_keys_clamp_and_scroll() {
        let root = test_root("app-nav");

        for i in 0..30 {
            touch(&root.join(format!("f{i:02}.txt")));
        }

        let mut app = app_at(&root);
        app.show_file_finder();
        pump_until(&mut app, scan_done);

        app.handle_file_finder_key(key(ConsoleKey::End, '\0'));
        let state = app.file_finder.as_ref().unwrap();
        assert_eq!(state.selected_index, 29);
        assert_eq!(state.scroll_offset, 29 + 1 - super::MAX_ITEM_ROWS);

        app.handle_file_finder_key(key(ConsoleKey::PageUp, '\0'));
        assert_eq!(app.file_finder.as_ref().unwrap().selected_index, 11);

        app.handle_file_finder_key(key(ConsoleKey::Home, '\0'));
        app.handle_file_finder_key(key(ConsoleKey::UpArrow, '\0'));
        let state = app.file_finder.as_ref().unwrap();
        assert_eq!((state.selected_index, state.scroll_offset), (0, 0));

        app.close_file_finder();
        let _ = std::fs::remove_dir_all(&root);
    }

    #[test]
    fn drives_view_does_not_open_the_finder() {
        let mut app = App::new(AppConfig::default());
        app.current_path = crate::fs::DRIVES_PATH.to_string();
        app.show_file_finder();
        assert!(app.file_finder.is_none());
        assert_ne!(app.input_mode, InputMode::FileFinder);
    }

    #[test]
    fn narrow_screen_renders_without_panicking() {
        // C# Math.Clamp throws below 78 columns; the port renders
        let entry = FileSystemEntry {
            name: "a.txt".to_string(),
            full_path: "a.txt".to_string(),
            is_directory: false,
            size: 0,
            last_modified: crate::ui::format_helpers::DateParts::default(),
            link_target: None,
            is_broken_symlink: false,
            is_drive: false,
            is_cloud_placeholder: false,
            is_junction_point: false,
            is_app_exec_link: false,
            app_exec_link_target: None,
            drive_format: None,
            drive_label: None,
            drive_free_space: 0,
            drive_total_size: 0,
        };
        let items = [FinderItem {
            entry: &entry,
            match_positions: &[0],
        }];
        let view = FinderView {
            scanning: false,
            has_entries: true,
            display_count: 1,
            matching: 1,
            total: 1,
            current_path: "",
            selected_index: 0,
            scroll_offset: 0,
            items: &items,
        };

        let mut buffer = ScreenBuffer::new(60, 30);
        render_file_finder_view(&mut buffer, 60, 30, &view, &mut TextInput::new("a"));
    }
}
