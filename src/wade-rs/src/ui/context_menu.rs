//! Port of `src/Wade/UI/ContextMenuState.cs` and `src/Wade/UI/ContextMenuRenderer.cs`:
//! the right-click floating context menu.

use crate::screen::{CellStyle, Color, ScreenBuffer};
use crate::ui::action_palette::ActionMenuItem;
use crate::ui::dialog_box::BG_COLOR;
use crate::ui::layout::Rect;

/// Port of `ContextMenuState`.
pub struct ContextMenuState {
    pub items: Vec<ActionMenuItem>,
    pub anchor_row: i32,
    pub anchor_col: i32,
    pub selected_index: usize,
}

impl ContextMenuState {
    #[must_use]
    pub fn new(items: Vec<ActionMenuItem>, anchor_row: i32, anchor_col: i32) -> Self {
        Self {
            items,
            anchor_row,
            anchor_col,
            selected_index: 0,
        }
    }

    /// Port of `MoveUp` (wraps around).
    pub fn move_up(&mut self) {
        if self.items.is_empty() {
            return;
        }

        self.selected_index = if self.selected_index == 0 {
            self.items.len() - 1
        } else {
            self.selected_index - 1
        };
    }

    /// Port of `MoveDown` (wraps around).
    pub fn move_down(&mut self) {
        if self.items.is_empty() {
            return;
        }

        self.selected_index = if self.selected_index >= self.items.len() - 1 {
            0
        } else {
            self.selected_index + 1
        };
    }
}

/// Port of `ContextMenuRenderer.GetMenuRect`: box sized to the item labels,
/// anchored at the click position and clamped to the screen.
#[must_use]
pub fn get_menu_rect(screen_width: i32, screen_height: i32, state: &ContextMenuState) -> Rect {
    let item_count = state.items.len();
    let mut max_label_len = 0usize;
    let mut max_shortcut_len = 0usize;

    for item in &state.items {
        max_label_len = max_label_len.max(item.label.chars().count());
        max_shortcut_len = max_shortcut_len.max(item.shortcut.chars().count());
    }

    // Box: | + space + label + gap + shortcut + space + |
    let content_width = max_label_len as i32 + if max_shortcut_len > 0 { 2 + max_shortcut_len as i32 } else { 0 };
    let box_width = content_width + 4; // 2 border + 2 padding
    let box_height = item_count as i32 + 2; // top + bottom borders

    // Anchor at click position, clamp to screen
    let mut left = state.anchor_col;
    let mut top = state.anchor_row;

    if left + box_width > screen_width {
        left = screen_width - box_width;
    }

    if top + box_height > screen_height {
        top = screen_height - box_height;
    }

    left = left.max(0);
    top = top.max(0);

    Rect::new(left, top, box_width, box_height)
}

/// Port of `ContextMenuRenderer.Render`.
pub fn render(buffer: &mut ScreenBuffer, screen_width: i32, screen_height: i32, state: &ContextMenuState) {
    let box_rect = get_menu_rect(screen_width, screen_height, state);
    let item_count = state.items.len();

    let border_style = CellStyle {
        fg: Some(crate::ui::dialog_box::BORDER_COLOR),
        bg: Some(BG_COLOR),
        dim: true,
        ..CellStyle::default()
    };
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

    let content_left = box_rect.left + 2;
    let content_width = box_rect.width - 4;

    // Top border
    draw_horizontal_border(
        buffer,
        box_rect.top,
        box_rect.left,
        box_rect.width,
        ('\u{250c}', '\u{2500}', '\u{2510}'),
        border_style,
    );

    // Item rows
    for i in 0..item_count {
        let row = box_rect.top + 1 + i as i32;
        let item = &state.items[i];
        let is_selected = i == state.selected_index;

        // Fill row background
        fill_row(buffer, row, box_rect.left, box_rect.width);
        buffer.put(row, box_rect.left, '\u{2502}', border_style);
        buffer.put(row, box_rect.left + box_rect.width - 1, '\u{2502}', border_style);

        if is_selected {
            buffer.fill_row(row, content_left, content_width, ' ', selected_style);
        }

        let label_style = if is_selected { selected_style } else { normal_style };
        let shortcut = item.shortcut.as_str();
        let sc_style = if is_selected { shortcut_selected_style } else { shortcut_style };

        let label_max = content_width - shortcut.chars().count() as i32 - i32::from(!shortcut.is_empty());
        buffer.write_string(row, content_left, item.label.as_str(), label_style, i64::from(label_max));

        if !shortcut.is_empty() {
            let shortcut_col = content_left + content_width - shortcut.chars().count() as i32;
            buffer.write_string(row, shortcut_col, shortcut, sc_style, i64::from(i32::MAX));
        }
    }

    // Bottom border
    draw_horizontal_border(
        buffer,
        box_rect.top + 1 + item_count as i32,
        box_rect.left,
        box_rect.width,
        ('\u{2514}', '\u{2500}', '\u{2518}'),
        border_style,
    );
}

fn draw_horizontal_border(
    buffer: &mut ScreenBuffer,
    row: i32,
    left: i32,
    width: i32,
    (left_cap, fill, right_cap): (char, char, char),
    style: CellStyle,
) {
    buffer.put(row, left, left_cap, style);

    for c in 1..width - 1 {
        buffer.put(row, left + c, fill, style);
    }

    buffer.put(row, left + width - 1, right_cap, style);
}

fn fill_row(buffer: &mut ScreenBuffer, row: i32, left: i32, width: i32) {
    let style = CellStyle {
        fg: None,
        bg: Some(BG_COLOR),
        ..CellStyle::default()
    };

    for c in 0..width {
        buffer.put(row, left + c, ' ', style);
    }
}

#[cfg(test)]
mod tests {
    use super::*;
    use crate::app::input_reader::AppAction;

    fn items() -> Vec<ActionMenuItem> {
        vec![
            ActionMenuItem::new("Open with default app", "o", AppAction::None),
            ActionMenuItem::new("Rename", "F2", AppAction::None),
        ]
    }

    #[test]
    fn wraps_around() {
        let mut state = ContextMenuState::new(items(), 5, 5);
        assert_eq!(state.selected_index, 0);
        state.move_up();
        assert_eq!(state.selected_index, 1);
        state.move_down();
        assert_eq!(state.selected_index, 0);
    }

    #[test]
    fn clamped_to_screen() {
        let state = ContextMenuState::new(items(), 100, 100);
        let rect = get_menu_rect(80, 25, &state);
        // Box: label 21 + gap 2 + shortcut 2 = 25 content, +4 border = 29 wide
        assert_eq!(rect.width, 29);
        assert_eq!(rect.left + rect.width, 80);
        assert_eq!(rect.top + rect.height, 25);
    }

    #[test]
    fn empty_shortcut_widens_label_only() {
        let state = ContextMenuState::new(vec![ActionMenuItem::new("Git: Stage", "", AppAction::None)], 0, 0);
        let rect = get_menu_rect(80, 25, &state);
        assert_eq!(rect.width, 10 + 4);
    }

    // Port of the rest of ContextMenuTests.cs

    fn abc() -> Vec<ActionMenuItem> {
        vec![
            ActionMenuItem::new("A", "", AppAction::Copy),
            ActionMenuItem::new("B", "", AppAction::Cut),
            ActionMenuItem::new("C", "", AppAction::Paste),
        ]
    }

    #[test]
    fn moves_advance_and_wrap_at_both_ends() {
        let mut state = ContextMenuState::new(abc(), 0, 0);
        state.move_down();
        assert_eq!(state.selected_index, 1);
        state.move_up();
        assert_eq!(state.selected_index, 0);
        state.move_up();
        assert_eq!(state.selected_index, 2, "up from the first wraps to the last");
        state.move_down();
        assert_eq!(state.selected_index, 0, "down from the last wraps to the first");
    }

    #[test]
    fn moves_on_an_empty_menu_do_nothing() {
        let mut state = ContextMenuState::new(Vec::new(), 0, 0);
        state.move_down();
        state.move_up();
        assert_eq!(state.selected_index, 0);
    }

    fn copy_paste(row: i32, col: i32) -> Rect {
        let state = ContextMenuState::new(
            vec![
                ActionMenuItem::new("Copy", "c", AppAction::Copy),
                ActionMenuItem::new("Paste", "v", AppAction::Paste),
            ],
            row,
            col,
        );
        get_menu_rect(80, 24, &state)
    }

    #[test]
    fn menu_rect_sits_at_the_anchor_and_clamps_to_the_screen() {
        let rect = copy_paste(5, 10);
        assert_eq!((rect.left, rect.top), (10, 5));

        assert!(copy_paste(5, 75).left + copy_paste(5, 75).width <= 80, "right edge");
        assert!(copy_paste(22, 10).top + copy_paste(22, 10).height <= 24, "bottom edge");
        let corner = copy_paste(23, 79);
        assert!(corner.left >= 0 && corner.top >= 0);
        assert!(corner.left + corner.width <= 80 && corner.top + corner.height <= 24);
    }

    #[test]
    fn menu_rect_height_is_items_plus_borders() {
        for (row, col) in [(0, 0), (10, 10)] {
            let state = ContextMenuState::new(abc(), row, col);
            assert_eq!(get_menu_rect(80, 24, &state).height, 5);
        }
    }
}
