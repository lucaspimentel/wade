//! Port of `src/Wade/UI/TextInput.cs`: single-line text input state.

use crate::screen::{CellStyle, ScreenBuffer};

#[derive(Clone, Default)]
pub struct TextInput {
    buffer: String,
    cursor_position: usize,
    scroll_offset: usize,
}

impl TextInput {
    #[must_use]
    pub fn new(initial_value: &str) -> Self {
        Self {
            buffer: initial_value.to_string(),
            cursor_position: initial_value.chars().count(),
            scroll_offset: 0,
        }
    }

    #[must_use]
    pub fn value(&self) -> &str {
        &self.buffer
    }

    #[must_use]
    pub fn cursor_position(&self) -> usize {
        self.cursor_position
    }

    #[must_use]
    pub fn scroll_offset(&self) -> usize {
        self.scroll_offset
    }

    pub fn insert_char(&mut self, ch: char) {
        let byte_idx = byte_index_of_char(&self.buffer, self.cursor_position);
        self.buffer.insert(byte_idx, ch);
        self.cursor_position += 1;
    }

    pub fn insert_string(&mut self, text: &str) {
        if text.is_empty() {
            return;
        }

        let byte_idx = byte_index_of_char(&self.buffer, self.cursor_position);
        self.buffer.insert_str(byte_idx, text);
        self.cursor_position += text.chars().count();
    }

    pub fn delete_backward(&mut self) {
        if self.cursor_position == 0 {
            return;
        }

        let start = byte_index_of_char(&self.buffer, self.cursor_position - 1);
        let end = byte_index_of_char(&self.buffer, self.cursor_position);
        self.buffer.replace_range(start..end, "");
        self.cursor_position -= 1;
    }

    pub fn delete_forward(&mut self) {
        if self.cursor_position >= self.buffer.chars().count() {
            return;
        }

        let start = byte_index_of_char(&self.buffer, self.cursor_position);
        let end = byte_index_of_char(&self.buffer, self.cursor_position + 1);
        self.buffer.replace_range(start..end, "");
    }

    pub fn move_cursor_left(&mut self) {
        if self.cursor_position > 0 {
            self.cursor_position -= 1;
        }
    }

    pub fn move_cursor_right(&mut self) {
        if self.cursor_position < self.buffer.chars().count() {
            self.cursor_position += 1;
        }
    }

    pub fn move_cursor_word_left(&mut self) {
        let chars: Vec<char> = self.buffer.chars().collect();
        if self.cursor_position == 0 {
            return;
        }

        let mut pos = self.cursor_position;
        // Skip non-word characters
        while pos > 0 && !chars[pos - 1].is_alphanumeric() {
            pos -= 1;
        }

        // Skip word characters
        while pos > 0 && chars[pos - 1].is_alphanumeric() {
            pos -= 1;
        }

        self.cursor_position = pos;
    }

    pub fn move_cursor_word_right(&mut self) {
        let chars: Vec<char> = self.buffer.chars().collect();
        let len = chars.len();
        if self.cursor_position >= len {
            return;
        }

        let mut pos = self.cursor_position;
        // Skip word characters
        while pos < len && chars[pos].is_alphanumeric() {
            pos += 1;
        }

        // Skip non-word characters
        while pos < len && !chars[pos].is_alphanumeric() {
            pos += 1;
        }

        self.cursor_position = pos;
    }

    pub fn delete_word_backward(&mut self) {
        if self.cursor_position == 0 {
            return;
        }

        let old_position = self.cursor_position;
        self.move_cursor_word_left();
        let start = byte_index_of_char(&self.buffer, self.cursor_position);
        let end = byte_index_of_char(&self.buffer, old_position);
        self.buffer.replace_range(start..end, "");
    }

    pub fn move_cursor_home(&mut self) {
        self.cursor_position = 0;
    }

    pub fn move_cursor_end(&mut self) {
        self.cursor_position = self.buffer.chars().count();
    }

    pub fn clear(&mut self) {
        self.buffer.clear();
        self.cursor_position = 0;
        self.scroll_offset = 0;
    }

    pub fn render(&mut self, buffer: &mut ScreenBuffer, row: i32, col: i32, max_width: i32, style: CellStyle) {
        if max_width <= 0 {
            return;
        }

        let text_len = self.buffer.chars().count() as i32;

        // Adjust scroll offset to keep cursor visible
        let cursor = self.cursor_position as i32;
        if cursor < self.scroll_offset as i32 {
            self.scroll_offset = self.cursor_position;
        } else if cursor >= self.scroll_offset as i32 + max_width {
            self.scroll_offset = (cursor - max_width + 1).max(0) as usize;
        }

        let cursor_style = CellStyle {
            inverse: true,
            ..style
        };
        let scroll = self.scroll_offset as i32;
        let visible_end = (scroll + max_width).min(text_len);
        let chars: Vec<char> = self.buffer.chars().collect();

        // Render visible text
        let mut c = col;
        for i in scroll..visible_end {
            let cell_style = if usize::try_from(i).ok() == Some(self.cursor_position) {
                cursor_style
            } else {
                style
            };
            buffer.put(row, c, chars[i as usize], cell_style);
            c += 1;
        }

        // Cursor at end of text — render inverse space
        if cursor >= text_len && cursor >= scroll && cursor - scroll < max_width {
            buffer.put(row, col + cursor - scroll, ' ', cursor_style);
            c = c.max(col + cursor - scroll + 1);
        }

        // Fill remaining width with spaces to clear stale content
        let remaining = max_width - (c - col);
        if remaining > 0 {
            buffer.fill_row(row, c, remaining, ' ', style);
        }
    }
}

fn byte_index_of_char(s: &str, char_index: usize) -> usize {
    s.char_indices()
        .nth(char_index)
        .map(|(i, _)| i)
        .unwrap_or(s.len())
}
