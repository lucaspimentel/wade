using Wade.FileSystem;
using Wade.Terminal;

namespace Wade.UI;

internal static class PaneRenderer
{
    private static readonly Color DirColor = new(80, 160, 255);
    private static readonly Color FileColor = new(200, 200, 200);
    private static readonly Color SelectionFg = new(0, 0, 0);
    private static readonly Color SelectionBg = new(80, 160, 255);
    private static readonly Color DimColor = new(100, 100, 100);
    private static readonly Color BorderColor = new(60, 60, 60);

    public static void RenderFileList(
        ScreenBuffer buffer,
        Rect pane,
        List<FileSystemEntry> entries,
        int selectedIndex,
        int scrollOffset,
        bool isActive,
        bool showIcons = false)
    {
        for (int row = 0; row < pane.Height; row++)
        {
            int entryIndex = scrollOffset + row;

            if (entryIndex >= entries.Count)
            {
                // Empty row — just leave blank
                continue;
            }

            var entry = entries[entryIndex];
            bool isSelected = entryIndex == selectedIndex;

            CellStyle style;
            if (isSelected && isActive)
                style = new CellStyle(SelectionFg, SelectionBg, Bold: true);
            else if (isSelected)
                style = new CellStyle(SelectionFg, new Color(60, 60, 80), Bold: true);
            else if (entry.IsDirectory)
                style = new CellStyle(DirColor, null, Bold: true);
            else
                style = new CellStyle(FileColor, null);

            // If selected, fill the row background
            if (isSelected)
            {
                var bgStyle = style;
                for (int col = 0; col < pane.Width; col++)
                    buffer.Put(pane.Top + row, pane.Left + col, ' ', bgStyle);
            }

            string display;
            if (showIcons)
                display = FileIcons.GetIcon(entry).ToString() + " " + entry.Name;
            else
                display = (entry.IsDrive ? " " : entry.IsDirectory ? $"{Path.DirectorySeparatorChar}" : " ") + entry.Name;
            buffer.WriteString(pane.Top + row, pane.Left, display, style, pane.Width);
        }
    }

    public static void RenderPreview(
        ScreenBuffer buffer,
        Rect pane,
        string[] lines,
        int scrollOffset = 0)
    {
        var style = new CellStyle(FileColor, null);
        var lineNumStyle = new CellStyle(DimColor, null);

        for (int row = 0; row < pane.Height; row++)
        {
            int lineIndex = scrollOffset + row;
            if (lineIndex >= lines.Length) break;

            // Line number (4 chars wide)
            string lineNum = (lineIndex + 1).ToString().PadLeft(4);
            buffer.WriteString(pane.Top + row, pane.Left, lineNum, lineNumStyle, 4);
            buffer.Put(pane.Top + row, pane.Left + 4, ' ', lineNumStyle);

            // Content
            buffer.WriteString(pane.Top + row, pane.Left + 5, lines[lineIndex], style, pane.Width - 5);
        }
    }

    public static void RenderMessage(
        ScreenBuffer buffer,
        Rect pane,
        string message)
    {
        var style = new CellStyle(DimColor, null);
        buffer.WriteString(pane.Top, pane.Left + 1, message, style, pane.Width - 1);
    }

    public static void RenderBorders(ScreenBuffer buffer, Layout layout, int terminalHeight)
    {
        var style = new CellStyle(BorderColor, null);
        int borderCol1 = layout.LeftPane.Right;
        int borderCol2 = layout.CenterPane.Right;
        int contentHeight = terminalHeight - 1;

        for (int row = 0; row < contentHeight; row++)
        {
            buffer.Put(row, borderCol1, '│', style);
            buffer.Put(row, borderCol2, '│', style);
        }
    }
}
