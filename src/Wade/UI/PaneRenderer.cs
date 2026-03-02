using Wade.FileSystem;
using Wade.Terminal;

namespace Wade.UI;

internal static class PaneRenderer
{
    private static readonly char DirSeparatorChar = Path.DirectorySeparatorChar;

    private static readonly Color DirColor = new(80, 160, 255);
    private static readonly Color FileColor = new(200, 200, 200);
    private static readonly Color SelectionFg = new(0, 0, 0);
    private static readonly Color SelectionBg = new(80, 160, 255);
    private static readonly Color DimColor = new(100, 100, 100);
    private static readonly Color BorderColor = new(60, 60, 60);

    private static readonly CellStyle ActiveSelectionStyle = new(SelectionFg, SelectionBg, Bold: true);
    private static readonly CellStyle InactiveSelectionStyle = new(SelectionFg, new Color(60, 60, 80), Bold: true);
    private static readonly CellStyle DirStyle = new(DirColor, null, Bold: true);
    private static readonly CellStyle FileStyle = new(FileColor, null);

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
                style = ActiveSelectionStyle;
            else if (isSelected)
                style = InactiveSelectionStyle;
            else if (entry.IsDirectory)
                style = DirStyle;
            else
                style = FileStyle;

            // If selected, fill the row background
            if (isSelected)
                buffer.FillRow(pane.Top + row, pane.Left, pane.Width, ' ', style);

            int entryCol = pane.Left;
            if (showIcons)
            {
                buffer.Put(pane.Top + row, entryCol, FileIcons.GetIcon(entry), style);
                buffer.Put(pane.Top + row, entryCol + 1, ' ', style);
                buffer.WriteString(pane.Top + row, entryCol + 2, entry.Name, style, pane.Width - 2);
            }
            else
            {
                char prefix = entry.IsDirectory && !entry.IsDrive ? DirSeparatorChar : ' ';
                buffer.Put(pane.Top + row, entryCol, prefix, style);
                buffer.WriteString(pane.Top + row, entryCol + 1, entry.Name, style, pane.Width - 1);
            }
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
        Span<char> lineNumBuf = stackalloc char[4];

        for (int row = 0; row < pane.Height; row++)
        {
            int lineIndex = scrollOffset + row;
            if (lineIndex >= lines.Length) break;

            // Line number (4 chars wide, right-aligned)
            lineNumBuf.Fill(' ');
            (lineIndex + 1).TryFormat(lineNumBuf, out int numLen);
            // Right-align: shift digits to the right within the 4-char buffer
            if (numLen < 4)
            {
                lineNumBuf[..numLen].CopyTo(lineNumBuf[(4 - numLen)..]);
                lineNumBuf[..(4 - numLen)].Fill(' ');
            }
            for (int i = 0; i < 4; i++)
                buffer.Put(pane.Top + row, pane.Left + i, lineNumBuf[i], lineNumStyle);
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
