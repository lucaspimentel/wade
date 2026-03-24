using Wade.Terminal;

namespace Wade.UI;

internal static class ContextMenuRenderer
{
    public static Rect GetMenuRect(int screenWidth, int screenHeight, ContextMenuState state)
    {
        int itemCount = state.Items.Length;
        int maxLabelLen = 0;
        int maxShortcutLen = 0;

        for (int i = 0; i < itemCount; i++)
        {
            ActionMenuItem item = state.Items[i];

            if (item.Label.Length > maxLabelLen)
            {
                maxLabelLen = item.Label.Length;
            }

            if (item.Shortcut.Length > maxShortcutLen)
            {
                maxShortcutLen = item.Shortcut.Length;
            }
        }

        // Box: │ + space + label + gap + shortcut + space + │
        int contentWidth = maxLabelLen + (maxShortcutLen > 0 ? 2 + maxShortcutLen : 0);
        int boxWidth = contentWidth + 4; // 2 border + 2 padding
        int boxHeight = itemCount + 2; // top + bottom borders

        // Anchor at click position, clamp to screen
        int left = state.AnchorCol;
        int top = state.AnchorRow;

        if (left + boxWidth > screenWidth)
        {
            left = screenWidth - boxWidth;
        }

        if (top + boxHeight > screenHeight)
        {
            top = screenHeight - boxHeight;
        }

        left = Math.Max(0, left);
        top = Math.Max(0, top);

        return new Rect(left, top, boxWidth, boxHeight);
    }

    public static void Render(ScreenBuffer buffer, int screenWidth, int screenHeight, ContextMenuState state)
    {
        Rect box = GetMenuRect(screenWidth, screenHeight, state);
        int itemCount = state.Items.Length;

        var borderStyle = new CellStyle(DialogBox.BorderColor, DialogBox.BgColor, Dim: true);
        var normalStyle = new CellStyle(new Color(200, 200, 200), DialogBox.BgColor);
        var selectedStyle = new CellStyle(new Color(20, 20, 35), new Color(200, 200, 200));
        var shortcutStyle = new CellStyle(new Color(120, 120, 140), DialogBox.BgColor);
        var shortcutSelectedStyle = new CellStyle(new Color(20, 20, 35), new Color(200, 200, 200));

        int contentLeft = box.Left + 2;
        int contentWidth = box.Width - 4;

        // Top border
        DrawHorizontalBorder(buffer, box.Top, box.Left, box.Width, '┌', '─', '┐', borderStyle);

        // Item rows
        for (int i = 0; i < itemCount; i++)
        {
            int row = box.Top + 1 + i;
            ActionMenuItem item = state.Items[i];
            bool isSelected = i == state.SelectedIndex;

            // Fill row background
            FillRow(buffer, row, box.Left, box.Width);
            buffer.Put(row, box.Left, '│', borderStyle);
            buffer.Put(row, box.Left + box.Width - 1, '│', borderStyle);

            if (isSelected)
            {
                buffer.FillRow(row, contentLeft, contentWidth, ' ', selectedStyle);
            }

            CellStyle labelStyle = isSelected ? selectedStyle : normalStyle;
            string shortcut = item.Shortcut;
            CellStyle scStyle = isSelected ? shortcutSelectedStyle : shortcutStyle;

            buffer.WriteString(row, contentLeft, item.Label, labelStyle, contentWidth - shortcut.Length - (shortcut.Length > 0 ? 1 : 0));

            if (shortcut.Length > 0)
            {
                int shortcutCol = contentLeft + contentWidth - shortcut.Length;
                buffer.WriteString(row, shortcutCol, shortcut, scStyle);
            }
        }

        // Bottom border
        DrawHorizontalBorder(buffer, box.Top + 1 + itemCount, box.Left, box.Width, '└', '─', '┘', borderStyle);
    }

    private static void DrawHorizontalBorder(
        ScreenBuffer buffer, int row, int left, int width,
        char leftCap, char fill, char rightCap, CellStyle style)
    {
        buffer.Put(row, left, leftCap, style);

        for (int c = 1; c < width - 1; c++)
        {
            buffer.Put(row, left + c, fill, style);
        }

        buffer.Put(row, left + width - 1, rightCap, style);
    }

    private static void FillRow(ScreenBuffer buffer, int row, int left, int width)
    {
        var style = new CellStyle(null, DialogBox.BgColor);

        for (int c = 0; c < width; c++)
        {
            buffer.Put(row, left + c, ' ', style);
        }
    }
}
