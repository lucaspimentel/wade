//! Port of the Rect record and Layout class in src/Wade/UI/Layout.cs.

/// Mirrors C# `Rect(int Left, int Top, int Width, int Height)`.
#[derive(Clone, Copy, PartialEq, Eq, Debug, Default)]
pub struct Rect {
    pub left: i32,
    pub top: i32,
    pub width: i32,
    pub height: i32,
}

impl Rect {
    #[must_use]
    pub const fn new(left: i32, top: i32, width: i32, height: i32) -> Self {
        Self {
            left,
            top,
            width,
            height,
        }
    }

    #[must_use]
    pub const fn right(&self) -> i32 {
        self.left + self.width
    }

    #[must_use]
    pub const fn bottom(&self) -> i32 {
        self.top + self.height
    }

    /// Port of `Rect.CenterContent`.
    #[must_use]
    pub const fn center_content(&self, content_width: i32, content_height: i32) -> (i32, i32) {
        let cw = self.width - content_width;
        let ch = self.height - content_height;
        let col = self.left + if cw > 0 { cw / 2 } else { 0 };
        let row = self.top + if ch > 0 { ch / 2 } else { 0 };
        (row, col)
    }
}

/// Layout pane selector, used by the renderer fixtures.
#[derive(Clone, Copy, PartialEq, Eq, Debug)]
pub enum Pane {
    Left,
    Center,
    Right,
}

#[derive(Clone, Copy, Debug, Default)]
pub struct Layout {
    pub left_pane: Rect,
    pub center_pane: Rect,
    pub right_pane: Rect,
    pub expanded_pane: Rect,
    pub status_bar: Rect,
}

impl Layout {
    /// Port of `Layout.Calculate`.
    pub fn calculate(&mut self, terminal_width: i32, terminal_height: i32, preview_pane_enabled: bool, parent_pane_enabled: bool) {
        self.calculate_with_top(terminal_width, terminal_height, preview_pane_enabled, parent_pane_enabled, 0);
    }

    /// `calculate` with the first `top` rows reserved (Rust only: the tab
    /// bar).
    pub fn calculate_with_top(
        &mut self,
        terminal_width: i32,
        terminal_height: i32,
        preview_pane_enabled: bool,
        parent_pane_enabled: bool,
        top: i32,
    ) {
        // Reserve 1 row for status bar at the bottom
        let mut content_height = terminal_height - 1 - top;
        if content_height < 1 {
            content_height = 1;
        }

        let (left, center, right) = if parent_pane_enabled && preview_pane_enabled {
            // 3 panes: 20% / 40% / 40% with 2 border columns
            let usable_width = terminal_width - 2;
            let left_width = (usable_width * 20 / 100).max(1);
            let center_width = (usable_width * 40 / 100).max(1);
            let right_width = (usable_width - left_width - center_width).max(1);
            (
                Rect::new(0, 0, left_width, content_height),
                Rect::new(left_width + 1, 0, center_width, content_height),
                Rect::new(left_width + 1 + center_width + 1, 0, right_width, content_height),
            )
        } else if parent_pane_enabled {
            // Left + center: 25% / 75% with 1 border column
            let usable_width = terminal_width - 1;
            let left_width = (usable_width * 25 / 100).max(1);
            let center_width = (usable_width - left_width).max(1);
            (
                Rect::new(0, 0, left_width, content_height),
                Rect::new(left_width + 1, 0, center_width, content_height),
                Rect::new(0, 0, 0, 0),
            )
        } else if preview_pane_enabled {
            // Center + right: 50% / 50% with 1 border column
            let usable_width = terminal_width - 1;
            let center_width = (usable_width / 2).max(1);
            let right_width = (usable_width - center_width).max(1);
            (
                Rect::new(0, 0, 0, 0),
                Rect::new(0, 0, center_width, content_height),
                Rect::new(center_width + 1, 0, right_width, content_height),
            )
        } else {
            // Center only: full width, no borders
            (
                Rect::new(0, 0, 0, 0),
                Rect::new(0, 0, terminal_width, content_height),
                Rect::new(0, 0, 0, 0),
            )
        };

        let shift = |rect: Rect| if rect.width == 0 && rect.height == 0 { rect } else { Rect::new(rect.left, rect.top + top, rect.width, rect.height) };
        self.left_pane = shift(left);
        self.center_pane = shift(center);
        self.right_pane = shift(right);
        self.expanded_pane = Rect::new(0, top, terminal_width, content_height);
        self.status_bar = Rect::new(0, terminal_height - 1, terminal_width, 1);
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn three_pane_layout_matches_csharp_ratios() {
        // C#: width 100 -> usable 98, left = 98*20/100 = 19, center = 98*40/100 = 39
        let mut layout = Layout::default();
        layout.calculate(100, 30, true, true);
        assert_eq!(layout.left_pane, Rect::new(0, 0, 19, 29));
        assert_eq!(layout.center_pane, Rect::new(20, 0, 39, 29));
        assert_eq!(layout.right_pane, Rect::new(60, 0, 40, 29));
        assert_eq!(layout.status_bar, Rect::new(0, 29, 100, 1));
    }

    #[test]
    fn a_top_offset_shifts_and_shortens_every_pane() {
        let mut layout = Layout::default();
        layout.calculate_with_top(100, 30, true, true, 1);
        assert_eq!(layout.left_pane, Rect::new(0, 1, 19, 28));
        assert_eq!(layout.center_pane, Rect::new(20, 1, 39, 28));
        assert_eq!(layout.right_pane, Rect::new(60, 1, 40, 28));
        assert_eq!(layout.expanded_pane, Rect::new(0, 1, 100, 28));
        assert_eq!(layout.status_bar, Rect::new(0, 29, 100, 1));

        layout.calculate_with_top(80, 25, false, true, 1);
        assert_eq!(layout.right_pane, Rect::new(0, 0, 0, 0), "a hidden pane stays empty");
    }

    #[test]
    fn center_only_layout_is_full_width() {
        let mut layout = Layout::default();
        layout.calculate(80, 25, false, false);
        assert_eq!(layout.center_pane, Rect::new(0, 0, 80, 24));
    }
}
