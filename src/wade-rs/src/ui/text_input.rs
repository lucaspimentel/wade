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

#[cfg(test)]
mod tests {
    //! Port of TextInputTests.cs.

    use super::TextInput;
    use crate::screen::{CellStyle, Color, ScreenBuffer};

    const STYLE: CellStyle = CellStyle {
        fg: Some(Color { r: 200, g: 200, b: 200 }),
        bg: None,
        bold: false,
        dim: false,
        inverse: false,
        underline: false,
        strikethrough: false,
    };

    fn at(value: &str, cursor: usize) -> TextInput {
        let mut input = TextInput::new(value);
        input.move_cursor_home();
        for _ in 0..cursor {
            input.move_cursor_right();
        }
        input
    }

    fn state(input: &TextInput) -> (&str, usize) {
        (input.value(), input.cursor_position())
    }

    fn rendered(input: &mut TextInput, max_width: i32) -> (String, String) {
        let mut buffer = ScreenBuffer::new(20, 3);
        input.render(&mut buffer, 0, 0, max_width, STYLE);
        let text = buffer.row_text(0);
        let mut raw = String::new();
        buffer.serialize(&mut raw);
        (text, raw)
    }

    #[test]
    fn construction_puts_the_cursor_at_the_end() {
        assert_eq!(state(&TextInput::new("")), ("", 0));
        assert_eq!(state(&TextInput::new("hello")), ("hello", 5));
    }

    #[test]
    fn insert_char_at_the_end_and_in_the_middle() {
        let mut input = TextInput::new("");
        input.insert_char('a');
        input.insert_char('b');
        assert_eq!(state(&input), ("ab", 2));

        let mut input = TextInput::new("ac");
        input.move_cursor_left();
        input.insert_char('b');
        assert_eq!(state(&input), ("abc", 2));
    }

    #[test]
    fn delete_backward_and_forward() {
        for (cursor, expected) in [(3, ("ab", 2)), (1, ("bc", 0)), (0, ("abc", 0))] {
            let mut input = at("abc", cursor);
            input.delete_backward();
            assert_eq!(state(&input), expected, "backward from {cursor}");
        }

        let mut input = at("abc", 0);
        input.delete_forward();
        assert_eq!(state(&input), ("bc", 0));
        let mut input = TextInput::new("abc");
        input.delete_forward();
        assert_eq!(state(&input), ("abc", 3), "no-op at the end");
    }

    #[test]
    fn cursor_moves_clamp_at_both_ends() {
        for (start, expected) in [(0, 0), (3, 2)] {
            let mut input = at("abc", start);
            input.move_cursor_left();
            assert_eq!(input.cursor_position(), expected);
        }
        for (start, expected) in [(3, 3), (0, 1)] {
            let mut input = at("abc", start);
            input.move_cursor_right();
            assert_eq!(input.cursor_position(), expected);
        }

        let mut input = TextInput::new("hello");
        input.move_cursor_home();
        assert_eq!(input.cursor_position(), 0);
        input.move_cursor_end();
        assert_eq!(input.cursor_position(), 5);
    }

    #[test]
    fn clear_resets_value_and_cursor() {
        let mut input = TextInput::new("hello");
        input.clear();
        assert_eq!(state(&input), ("", 0));
    }

    #[test]
    fn word_navigation() {
        let mut input = TextInput::new("hello world");
        input.move_cursor_word_left();
        assert_eq!(input.cursor_position(), 6);
        input.move_cursor_word_left();
        assert_eq!(input.cursor_position(), 0);
        input.move_cursor_word_left();
        assert_eq!(input.cursor_position(), 0, "no-op at the start");

        let mut input = TextInput::new("foo--bar");
        input.move_cursor_word_left();
        assert_eq!(input.cursor_position(), 5, "skips non-word characters");
        input.move_cursor_word_left();
        assert_eq!(input.cursor_position(), 0);

        let mut input = at("hello world", 0);
        input.move_cursor_word_right();
        assert_eq!(input.cursor_position(), 6);
        input.move_cursor_word_right();
        assert_eq!(input.cursor_position(), 11);
        input.move_cursor_word_right();
        assert_eq!(input.cursor_position(), 11, "no-op at the end");

        let sep = std::path::MAIN_SEPARATOR;
        let mut input = TextInput::new(&format!("src{sep}Wade{sep}App.cs"));
        input.move_cursor_word_left();
        input.move_cursor_word_left();
        input.move_cursor_word_left();
        assert_eq!(input.cursor_position(), 4, "path separators are word boundaries");
    }

    #[test]
    fn delete_word_backward() {
        let mut input = TextInput::new("hello world");
        input.delete_word_backward();
        assert_eq!(state(&input), ("hello ", 6));

        let mut input = TextInput::new("foo--bar");
        input.delete_word_backward();
        assert_eq!(state(&input), ("foo--", 5));
        input.delete_word_backward();
        assert_eq!(state(&input), ("", 0));

        let mut input = at("hello", 0);
        input.delete_word_backward();
        assert_eq!(state(&input), ("hello", 0), "no-op at the start");
    }

    #[test]
    fn insert_string_at_the_end_in_the_middle_and_empty() {
        let mut input = TextInput::new("hello");
        input.insert_string(" world");
        assert_eq!(state(&input), ("hello world", 11));

        let mut input = at("hlo", 1);
        input.insert_string("el");
        assert_eq!(state(&input), ("hello", 3));

        let mut input = TextInput::new("hello");
        input.insert_string("");
        assert_eq!(state(&input), ("hello", 5));
    }

    #[test]
    fn render_writes_text_and_an_inverse_cursor() {
        let mut buffer = ScreenBuffer::new(20, 3);
        TextInput::new("abc").render(&mut buffer, 1, 2, 10, STYLE);
        assert!(buffer.row_text(1).contains("abc"));

        let (_, raw) = rendered(&mut at("ab", 0), 10);
        assert!(raw.contains("\x1b[7m"), "cursor on a character");
        let (_, raw) = rendered(&mut TextInput::new("ab"), 10);
        assert!(raw.contains("\x1b[7m"), "cursor at the end renders an inverse space");
    }

    #[test]
    fn render_scrolls_a_window_around_the_cursor() {
        let mut input = TextInput::new("abcdefghij");
        let (text, _) = rendered(&mut input, 5);
        assert!(text.contains("ghij"));
        assert!(!text.contains("abcde"));

        input.move_cursor_home();
        let (text, _) = rendered(&mut input, 5);
        assert!(text.contains("abcd"), "scrolls back when the cursor moves left");
    }
}
