//! Port of src/Wade/Terminal/ScreenBuffer.cs: a double-buffered in-memory
//! screen grid that serializes diffs as ANSI escape sequences.
//!
//! Serialization is separated from writing (the C# side writes to stdout in
//! `Flush`; this port exposes `serialize` only until the app layer exists).

use crate::ansi::{RESET_ATTRIBUTES, append_move_cursor, append_set_bg, append_set_fg};
use crate::rune_width::rune_width;

#[derive(Clone, Copy, PartialEq, Eq, Debug)]
pub struct Color {
    pub r: u8,
    pub g: u8,
    pub b: u8,
}

#[derive(Clone, Copy, PartialEq, Eq, Default, Debug)]
pub struct CellStyle {
    pub fg: Option<Color>,
    pub bg: Option<Color>,
    pub bold: bool,
    pub dim: bool,
    pub inverse: bool,
    pub underline: bool,
    pub strikethrough: bool,
}

#[derive(Clone, Copy, PartialEq, Eq)]
pub struct Cell {
    ch: char,
    style: CellStyle,
    /// Rust-only: a kitty placeholder's row and column in its image,
    /// written as two diacritics after `ch`.
    placeholder: Option<(u16, u16)>,
}

impl Cell {
    const DEFAULT_STYLE: CellStyle = CellStyle {
        fg: None,
        bg: None,
        bold: false,
        dim: false,
        inverse: false,
        underline: false,
        strikethrough: false,
    };

    pub const EMPTY: Self = Self {
        ch: ' ',
        style: Self::DEFAULT_STYLE,
        placeholder: None,
    };
    pub const DIRTY: Self = Self {
        ch: '\0',
        style: CellStyle {
            fg: Some(Color { r: 255, g: 255, b: 255 }),
            bg: Some(Color { r: 255, g: 255, b: 255 }),
            bold: false,
            dim: false,
            inverse: false,
            underline: false,
            strikethrough: false,
        },
        placeholder: None,
    };
    pub const WIDE_CONTINUATION: Self = Self {
        ch: '\0',
        style: Self::DEFAULT_STYLE,
        placeholder: None,
    };

    #[must_use]
    pub fn is_wide_continuation(&self) -> bool {
        *self == Self::WIDE_CONTINUATION
    }
}

pub struct ScreenBuffer {
    width: i32,
    height: i32,
    front: Vec<Cell>,
    back: Vec<Cell>,
    /// Dirty-row bitfield: 1 bit per row, packed into u64s.
    dirty_rows: Vec<u64>,
}

impl ScreenBuffer {
    #[must_use]
    pub fn new(width: i32, height: i32) -> Self {
        let mut buffer = Self {
            width,
            height,
            front: Vec::new(),
            back: Vec::new(),
            dirty_rows: Vec::new(),
        };
        buffer.allocate();
        buffer
    }

    #[must_use]
    pub const fn width(&self) -> i32 {
        self.width
    }

    #[must_use]
    pub const fn height(&self) -> i32 {
        self.height
    }

    fn allocate(&mut self) {
        let len = (self.width * self.height) as usize;
        self.front = vec![Cell::EMPTY; len];
        self.back = vec![Cell::EMPTY; len];
        self.dirty_rows = vec![0; ((self.height + 63) / 64) as usize];
    }

    /// Text of one row of the pending (back) frame, without wide-character
    /// continuation cells. For tests and diagnostics.
    #[must_use]
    pub fn row_text(&self, row: i32) -> String {
        if row < 0 || row >= self.height {
            return String::new();
        }

        let start = (row * self.width) as usize;
        self.back[start..start + self.width as usize]
            .iter()
            .filter(|cell| !cell.is_wide_continuation())
            .map(|cell| cell.ch)
            .collect()
    }

    pub fn resize(&mut self, width: i32, height: i32) {
        self.width = width;
        self.height = height;
        self.allocate();
    }

    pub fn clear(&mut self) {
        self.back.fill(Cell::EMPTY);
        // Mark all rows dirty so serialize() will compare every row against the
        // front buffer. Without this, rows where nothing is rendered (back stays
        // Cell::EMPTY) would be skipped, leaving stale front-buffer content.
        self.dirty_rows.fill(u64::MAX);
    }

    pub fn put(&mut self, row: i32, col: i32, ch: char, style: CellStyle) {
        if row < 0 || row >= self.height || col < 0 || col >= self.width {
            return;
        }

        let idx = (row * self.width + col) as usize;
        self.back[idx] = Cell { ch, style, placeholder: None };

        // Wide characters occupy 2 terminal columns; store a continuation marker
        // in the next cell
        if rune_width(ch) == 2 && col + 1 < self.width {
            self.back[idx + 1] = Cell::WIDE_CONTINUATION;
        }

        self.mark_dirty(row);
    }

    /// Rust-only: a kitty placeholder cell showing cell (`image_row`,
    /// `image_col`) of image `image_id` (its id is the foreground color).
    pub fn put_placeholder(&mut self, row: i32, col: i32, image_id: u32, image_row: u16, image_col: u16) {
        if row < 0 || row >= self.height || col < 0 || col >= self.width {
            return;
        }

        let style = CellStyle {
            fg: Some(crate::imaging::kitty::id_color(image_id)),
            ..CellStyle::default()
        };
        let idx = (row * self.width + col) as usize;
        self.back[idx] = Cell {
            ch: crate::imaging::kitty::PLACEHOLDER,
            style,
            placeholder: Some((image_row, image_col)),
        };
        self.mark_dirty(row);
    }

    pub fn fill_row(&mut self, row: i32, start_col: i32, count: i32, ch: char, style: CellStyle) {
        if row < 0 || row >= self.height {
            return;
        }

        let clamped_start = start_col.max(0);
        let clamped_end = (start_col + count).min(self.width);
        if clamped_start >= clamped_end {
            return;
        }

        let cell = Cell { ch, style, placeholder: None };
        let start = (row * self.width + clamped_start) as usize;
        let end = (row * self.width + clamped_end) as usize;
        self.back[start..end].fill(cell);
        self.mark_dirty(row);
    }

    pub fn write_string(&mut self, row: i32, col: i32, text: &str, style: CellStyle, max_width: i64) {
        if row < 0 || row >= self.height {
            return;
        }

        let clamped_start = col.max(0);
        let clamped_end = (i64::from(col) + max_width).min(i64::from(self.width)) as i32;
        if clamped_start >= clamped_end {
            return;
        }

        let row_offset = row * self.width;
        let mut c = col;
        for ch in text.chars() {
            let w = rune_width(ch) as i32;
            if c + w > clamped_end {
                break;
            }

            if c >= 0 {
                let idx = (row_offset + c) as usize;
                self.back[idx] = Cell { ch, style, placeholder: None };
                if w == 2 && c + 1 < self.width {
                    self.back[idx + 1] = Cell::WIDE_CONTINUATION;
                }
            }

            c += w;
        }

        self.mark_dirty(row);
    }

    pub fn force_full_redraw(&mut self) {
        self.front.fill(Cell::DIRTY);
        self.dirty_rows.fill(u64::MAX);
    }

    /// Serializes the diff between front and back buffers into `out`,
    /// mirroring C# `ScreenBuffer.Serialize`. Clears `out` first and clears
    /// the dirty bits; calling it again with no writes produces an empty
    /// string.
    pub fn serialize(&mut self, out: &mut String) {
        out.clear();
        let mut current_style = CellStyle::default();
        let mut has_style = false;
        let mut last_row: i32 = -1;
        let mut last_col: i32 = -1;

        for row in 0..self.height {
            // Skip clean rows
            if self.dirty_rows[(row >> 6) as usize] & (1u64 << (row & 63)) == 0 {
                continue;
            }

            for col in 0..self.width {
                let idx = (row * self.width + col) as usize;
                if self.front[idx] == self.back[idx] {
                    continue;
                }

                self.front[idx] = self.back[idx];

                // Skip continuation cells: the wide character at col-1 already
                // covers this column
                if self.back[idx].is_wide_continuation() {
                    continue;
                }

                // Only emit cursor move if not already positioned here
                if row != last_row || col != last_col {
                    append_move_cursor(out, row, col);
                }

                if !has_style || current_style != self.back[idx].style {
                    let old = if has_style { current_style } else { CellStyle::default() };
                    append_style_diff(out, old, self.back[idx].style, has_style);
                    current_style = self.back[idx].style;
                    has_style = true;
                }

                out.push(self.back[idx].ch);
                if let Some((image_row, image_col)) = self.back[idx].placeholder {
                    let diacritic =
                        |i: u16| crate::imaging::kitty::DIACRITICS.get(usize::from(i)).copied().unwrap_or('\u{0305}');
                    out.push(diacritic(image_row));
                    out.push(diacritic(image_col));
                }

                let char_width = rune_width(self.back[idx].ch) as i32;
                last_row = row;
                last_col = col + char_width;
            }
        }

        // Clear dirty bits
        self.dirty_rows.fill(0);

        if !out.is_empty() {
            out.push_str(RESET_ATTRIBUTES);
        }
    }

    fn mark_dirty(&mut self, row: i32) {
        self.dirty_rows[(row >> 6) as usize] |= 1u64 << (row & 63);
    }
}

fn append_style_diff(out: &mut String, old_style: CellStyle, new_style: CellStyle, had_style: bool) {
    // If Bold, Dim, Inverse, Underline, or Strikethrough was on and is now off,
    // or the background changed, we must reset then reapply
    let needs_reset = !had_style
        || (old_style.bold && !new_style.bold)
        || (old_style.dim && !new_style.dim)
        || (old_style.inverse && !new_style.inverse)
        || (old_style.underline && !new_style.underline)
        || (old_style.strikethrough && !new_style.strikethrough)
        || old_style.bg != new_style.bg;

    if needs_reset {
        out.push_str(RESET_ATTRIBUTES);
        if let Some(fg) = new_style.fg {
            append_set_fg(out, fg);
        }

        if let Some(bg) = new_style.bg {
            append_set_bg(out, bg);
        }

        if new_style.bold {
            out.push_str("\x1b[1m");
        }

        if new_style.dim {
            out.push_str("\x1b[2m");
        }

        if new_style.inverse {
            out.push_str("\x1b[7m");
        }

        if new_style.underline {
            out.push_str("\x1b[4m");
        }

        if new_style.strikethrough {
            out.push_str("\x1b[9m");
        }

        return;
    }

    // Only emit what changed
    if old_style.fg != new_style.fg {
        match new_style.fg {
            Some(fg) => append_set_fg(out, fg),
            None => out.push_str("\x1b[39m"), // default fg
        }
    }

    if !old_style.bold && new_style.bold {
        out.push_str("\x1b[1m");
    }

    if !old_style.dim && new_style.dim {
        out.push_str("\x1b[2m");
    }

    if !old_style.inverse && new_style.inverse {
        out.push_str("\x1b[7m");
    }

    if !old_style.underline && new_style.underline {
        out.push_str("\x1b[4m");
    }

    if !old_style.strikethrough && new_style.strikethrough {
        out.push_str("\x1b[9m");
    }
}

#[cfg(test)]
mod tests {
    use super::ScreenBuffer;

    #[test]
    fn placeholder_cells_carry_the_id_color_and_both_diacritics() {
        let mut buffer = ScreenBuffer::new(4, 2);
        buffer.put_placeholder(1, 2, 0x01_0203, 0, 2);

        let mut out = String::new();
        buffer.serialize(&mut out);
        assert!(out.contains("\x1b[38;2;1;2;3m\u{10EEEE}\u{0305}\u{030E}"), "{out:?}");

        // Unchanged next frame: no output
        buffer.clear();
        buffer.put_placeholder(1, 2, 0x01_0203, 0, 2);
        buffer.serialize(&mut out);
        assert!(out.is_empty(), "{out:?}");

        // Another position in the image is a change
        buffer.clear();
        buffer.put_placeholder(1, 2, 0x01_0203, 1, 2);
        buffer.serialize(&mut out);
        assert!(out.contains("\u{10EEEE}\u{030D}\u{030E}"), "{out:?}");
    }

    #[test]
    fn placeholder_is_one_column_wide() {
        let mut buffer = ScreenBuffer::new(3, 1);
        buffer.put_placeholder(0, 0, 1, 0, 0);
        buffer.put_placeholder(0, 1, 1, 0, 1);

        let mut out = String::new();
        buffer.serialize(&mut out);
        assert_eq!(out.matches('H').count(), 1, "the second cell follows the first without a cursor move: {out:?}");
    }
}
