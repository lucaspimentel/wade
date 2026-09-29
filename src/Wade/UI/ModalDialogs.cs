using System.Collections.Generic;
using Wade.Terminal;

namespace Wade.UI;

using Wade.FileSystem;

/// <summary>
/// Static render cores for the modal dialogs, extracted from App.cs so the
/// golden-frame fixture runner can drive them directly.
/// </summary>
internal static class ConfirmDialog
{
    internal static void Render(ScreenBuffer buffer, int width, int height, string? title, string message)
    {
        string[] lines = message.Split('\n');
        string footer = "[Y/Enter] Yes  [N/Esc] No";
        int maxLineLen = 0;
        foreach (string line in lines)
        {
            if (line.Length > maxLineLen)
            {
                maxLineLen = line.Length;
            }
        }

        int contentWidth = Math.Max(maxLineLen, footer.Length) + 2;
        int contentHeight = lines.Length;

        Rect content = DialogBox.Render(buffer, width, height, contentWidth, contentHeight, title: title, footer: footer);

        var textStyle = new CellStyle(new Color(200, 200, 200), DialogBox.BgColor);
        var warnStyle = new CellStyle(new Color(255, 100, 100), DialogBox.BgColor);

        for (int i = 0; i < lines.Length; i++)
        {
            string line = lines[i];
            int msgCol = content.Left + (content.Width - line.Length) / 2;
            CellStyle style = i > 0 ? warnStyle : textStyle;
            buffer.WriteString(content.Top + i, msgCol, line, style);
        }
    }
}

internal static class TextInputDialog
{
    internal static void Render(ScreenBuffer buffer, int width, int height, string? title, TextInput? input)
    {
        int contentWidth = Math.Min(40, width - 8);
        int contentHeight = 1; // single row for text input
        string footer = "[Enter] Confirm  [Esc] Cancel";

        Rect content = DialogBox.Render(buffer, width, height, contentWidth, contentHeight, title: title, footer: footer);

        var inputStyle = new CellStyle(new Color(200, 200, 200), DialogBox.BgColor);
        input?.Render(buffer, content.Top, content.Left, content.Width, inputStyle);
    }
}

internal static class GoToPathDialog
{
    internal static void Render(ScreenBuffer buffer, int width, int height, TextInput? input, string? suggestion)
    {
        int contentWidth = Math.Min(60, width - 8);
        int contentHeight = 1;
        string footer = "[Tab] Complete  [\u2191] Up dir  [Esc] Clear/Close  [Enter] Go";

        Rect content = DialogBox.Render(buffer, width, height, contentWidth, contentHeight,
            title: "Go to path", footer: footer);

        var inputStyle = new CellStyle(new Color(200, 200, 200), DialogBox.BgColor);
        input?.Render(buffer, content.Top, content.Left, content.Width, inputStyle);

        // Inline ghost suffix — show the untyped remainder of the suggestion after the cursor
        if (suggestion is not null && input is not null)
        {
            string inputValue = input.Value;
            string expandedInput = PathCompletion.NormalizeSeparators(PathCompletion.ExpandTilde(inputValue));

            // Only show ghost when cursor is at end and suggestion extends beyond expanded input
            if (input.CursorPosition == inputValue.Length
                && suggestion.Length > expandedInput.Length
                && suggestion.StartsWith(expandedInput, StringComparison.OrdinalIgnoreCase))
            {
                int scrollOffset = input.ScrollOffset;
                int visualTextEnd = inputValue.Length - scrollOffset + 1; // +1 for cursor space
                int ghostCol = content.Left + visualTextEnd;
                int ghostMaxWidth = content.Width - visualTextEnd;

                if (ghostMaxWidth > 0)
                {
                    string ghost = suggestion[expandedInput.Length..];
                    var ghostStyle = new CellStyle(new Color(90, 90, 110), DialogBox.BgColor);
                    buffer.WriteString(content.Top, ghostCol, ghost, ghostStyle, ghostMaxWidth);
                }
            }
        }
    }
}

internal static class ActionPaletteDialog
{
    internal static void Render(ScreenBuffer buffer, int width, int height, ActionMenuLevel level, int stackDepth)
    {
        IReadOnlyList<ActionMenuItem> filtered = level.GetFilteredItems();
        int contentWidth = Math.Min(60, width - 8);
        int itemRows = Math.Min(filtered.Count, 18);
        int contentHeight = itemRows + 2; // 1 row for text input + 1 separator + item rows
        string footer = stackDepth > 1
            ? "[\u2191\u2193] Navigate  [Enter] Select  [Esc] Back"
            : "[\u2191\u2193] Navigate  [Enter] Select  [Esc] Cancel";

        Rect content = DialogBox.Render(
            buffer, width, height,
            Math.Max(contentWidth, footer.Length),
            contentHeight,
            title: level.Title,
            footer: footer);

        // Row 0: text input with "> " prefix
        var prefixStyle = new CellStyle(new Color(220, 220, 100), DialogBox.BgColor);
        var inputStyle = new CellStyle(new Color(200, 200, 200), DialogBox.BgColor);
        buffer.WriteString(content.Top, content.Left, "> ", prefixStyle);
        level.Filter.Render(buffer, content.Top, content.Left + 2, content.Width - 2, inputStyle);

        // Row 1: separator
        var separatorStyle = new CellStyle(DialogBox.BorderColor, DialogBox.BgColor, Dim: true);
        for (int c = 0; c < content.Width; c++)
        {
            buffer.Put(content.Top + 1, content.Left + c, '\u2500', separatorStyle);
        }

        // Rows 2+: filtered items
        var normalStyle = new CellStyle(new Color(200, 200, 200), DialogBox.BgColor);
        var selectedStyle = new CellStyle(new Color(20, 20, 35), new Color(200, 200, 200));
        var shortcutStyle = new CellStyle(new Color(120, 120, 140), DialogBox.BgColor);
        var shortcutSelectedStyle = new CellStyle(new Color(20, 20, 35), new Color(200, 200, 200));
        var submenuIndicatorStyle = new CellStyle(new Color(120, 120, 140), DialogBox.BgColor);
        var submenuIndicatorSelectedStyle = new CellStyle(new Color(20, 20, 35), new Color(200, 200, 200));

        int visibleCount = content.Height - 2;

        for (int i = 0; i < visibleCount; i++)
        {
            int itemIndex = level.ScrollOffset + i;

            if (itemIndex >= filtered.Count)
            {
                break;
            }

            ActionMenuItem item = filtered[itemIndex];
            bool isSelected = itemIndex == level.SelectedIndex;
            int row = content.Top + 2 + i;

            CellStyle labelStyle = isSelected ? selectedStyle : normalStyle;

            // Fill entire row with selected background if selected
            if (isSelected)
            {
                buffer.FillRow(row, content.Left, content.Width, ' ', selectedStyle);
            }

            if (item.IsSubmenu)
            {
                // Submenu items show a "\u25b8" indicator on the right
                buffer.WriteString(row, content.Left + 1, item.Label, labelStyle, content.Width - 4);
                CellStyle indStyle = isSelected ? submenuIndicatorSelectedStyle : submenuIndicatorStyle;
                buffer.WriteString(row, content.Left + content.Width - 2, "\u25b8", indStyle);
            }
            else
            {
                string shortcut = item.Shortcut;
                CellStyle scStyle = isSelected ? shortcutSelectedStyle : shortcutStyle;
                buffer.WriteString(row, content.Left + 1, item.Label, labelStyle, content.Width - shortcut.Length - 3);
                int shortcutCol = content.Left + content.Width - shortcut.Length - 1;
                buffer.WriteString(row, shortcutCol, shortcut, scStyle);
            }
        }
    }
}

internal static class SearchBar
{
    internal static void Render(ScreenBuffer buffer, Rect centerPane, bool active, string filter, TextInput? input)
    {
        int row = centerPane.Bottom - 1;
        int col = centerPane.Left;
        int width = centerPane.Width;

        var labelStyle = new CellStyle(new Color(220, 220, 100), null);
        buffer.Put(row, col, '/', labelStyle);

        int inputCol = col + 1;
        int inputWidth = width - 1;

        if (active && input is not null)
        {
            var inputStyle = new CellStyle(new Color(200, 200, 200), null);
            input.Render(buffer, row, inputCol, inputWidth, inputStyle);
        }
        else
        {
            var textStyle = new CellStyle(new Color(200, 200, 200), null);
            buffer.WriteString(row, inputCol, filter, textStyle, inputWidth);
        }
    }
}

internal static class ConfigDialog
{
    internal static void Render(ScreenBuffer buffer, int width, int height, ConfigDialogState state)
    {
        const int ContentWidth = 47;
        int contentHeight = state.Items.Count + 1;
        const string Footer = "[Space] Toggle [\u25c4\u25ba] Cycle [Enter] Save [Esc] Cancel";

        Rect content = DialogBox.Render(buffer, width, height, Math.Max(ContentWidth, Footer.Length), contentHeight, title: "Configuration",
            footer: Footer);

        var normalStyle = new CellStyle(new Color(200, 200, 200), DialogBox.BgColor);
        var selectedStyle = new CellStyle(new Color(20, 20, 35), new Color(200, 200, 200));
        var valueStyle = new CellStyle(new Color(100, 200, 255), DialogBox.BgColor);
        var valueSelectedStyle = new CellStyle(new Color(20, 20, 35), new Color(200, 200, 200));
        var disabledStyle = new CellStyle(new Color(80, 80, 80), DialogBox.BgColor);

        for (int i = 0; i < state.Items.Count; i++)
        {
            ConfigItem item = state.Items[i];
            bool selected = i == state.SelectedIndex;
            CellStyle style, vStyle;

            if (!item.IsEnabled)
            {
                style = disabledStyle;
                vStyle = disabledStyle;
            }
            else
            {
                style = selected ? selectedStyle : normalStyle;
                vStyle = selected ? valueSelectedStyle : valueStyle;
            }

            int row = content.Top + i;
            string label = item.Indent > 0
                ? new string(' ', item.Indent * 2) + item.Label
                : item.Label;

            const int labelWidth = 34;
            buffer.WriteString(row, content.Left, label, style, labelWidth);
            buffer.WriteString(row, content.Left + labelWidth, item.FormatValue(), vStyle, content.Width - labelWidth);
        }
    }
}

internal static class BookmarksDialog
{
    internal static void Render(
        ScreenBuffer buffer, int width, int height,
        IReadOnlyList<string> filtered, int selectedIndex, int scrollOffset, TextInput? input)
    {
        int contentWidth = Math.Min(70, width - 8);
        int itemRows = Math.Min(filtered.Count, 18);
        int contentHeight = itemRows + 2; // 1 row for text input + 1 separator + item rows
        const string Footer = "[\u2191\u2193] Navigate [Enter] Open [d] Remove [1-9] Jump  [B] Add/Remove  [Esc] Close";

        Rect content = DialogBox.Render(
            buffer, width, height,
            Math.Max(contentWidth, Footer.Length),
            Math.Max(contentHeight, 3),
            title: "Bookmarks",
            footer: Footer);

        // Row 0: text input with "> " prefix
        var prefixStyle = new CellStyle(new Color(220, 220, 100), DialogBox.BgColor);
        var inputStyle = new CellStyle(new Color(200, 200, 200), DialogBox.BgColor);
        buffer.WriteString(content.Top, content.Left, "> ", prefixStyle);
        input?.Render(buffer, content.Top, content.Left + 2, content.Width - 2, inputStyle);

        // Row 1: separator
        var separatorStyle = new CellStyle(DialogBox.BorderColor, DialogBox.BgColor, Dim: true);
        for (int c = 0; c < content.Width; c++)
        {
            buffer.Put(content.Top + 1, content.Left + c, '\u2500', separatorStyle);
        }

        if (filtered.Count == 0)
        {
            var emptyStyle = new CellStyle(new Color(120, 120, 140), DialogBox.BgColor);
            buffer.WriteString(content.Top + 2, content.Left + 1, "No bookmarks", emptyStyle);
            return;
        }

        // Rows 2+: bookmark items
        var normalStyle = new CellStyle(new Color(200, 200, 200), DialogBox.BgColor);
        var selectedStyle = new CellStyle(new Color(20, 20, 35), new Color(200, 200, 200));
        var numberStyle = new CellStyle(new Color(220, 220, 100), DialogBox.BgColor);
        var numberSelectedStyle = new CellStyle(new Color(20, 20, 35), new Color(200, 200, 200));
        var dimStyle = new CellStyle(new Color(120, 120, 140), DialogBox.BgColor);
        var dimSelectedStyle = new CellStyle(new Color(80, 80, 100), new Color(200, 200, 200));

        int visibleCount = content.Height - 2;

        for (int i = 0; i < visibleCount; i++)
        {
            int itemIndex = scrollOffset + i;

            if (itemIndex >= filtered.Count)
            {
                break;
            }

            string path = filtered[itemIndex];
            bool isSelected = itemIndex == selectedIndex;
            bool exists = Directory.Exists(path) || File.Exists(path);
            int row = content.Top + 2 + i;

            CellStyle labelStyle = isSelected
                ? exists ? selectedStyle : dimSelectedStyle
                : exists
                    ? normalStyle
                    : dimStyle;

            CellStyle numStyle = isSelected ? numberSelectedStyle : numberStyle;

            if (isSelected)
            {
                buffer.FillRow(row, content.Left, content.Width, ' ', selectedStyle);
            }

            // Number prefix [1]-[9] for first 9 items
            int col = content.Left + 1;

            if (itemIndex < 9)
            {
                string num = $"[{itemIndex + 1}] ";
                buffer.WriteString(row, col, num, numStyle);
                col += num.Length;
            }
            else
            {
                col += 4; // align with numbered items
            }

            buffer.WriteString(row, col, path, labelStyle, content.Width - (col - content.Left) - 1);
        }
    }
}
