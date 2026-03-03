using Wade.FileSystem;
using Wade.Highlighting;
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
        StyledLine[] lines,
        int scrollOffset = 0)
    {
        var defaultStyle = new CellStyle(FileColor, null);
        var lineNumStyle = new CellStyle(DimColor, null);
        Span<char> lineNumBuf = stackalloc char[4];

        int contentLineNumber = 0;

        for (int row = 0; row < pane.Height; row++)
        {
            int lineIndex = scrollOffset + row;
            if (lineIndex >= lines.Length) break;

            var styledLine = lines[lineIndex];

            contentLineNumber++;

            // Line number (4 chars wide, right-aligned)
            lineNumBuf.Fill(' ');
            contentLineNumber.TryFormat(lineNumBuf, out int numLen);
            if (numLen < 4)
            {
                lineNumBuf[..numLen].CopyTo(lineNumBuf[(4 - numLen)..]);
                lineNumBuf[..(4 - numLen)].Fill(' ');
            }
            for (int i = 0; i < 4; i++)
                buffer.Put(pane.Top + row, pane.Left + i, lineNumBuf[i], lineNumStyle);
            buffer.Put(pane.Top + row, pane.Left + 4, ' ', lineNumStyle);

            // Content
            int contentCol = pane.Left + 5;
            int contentWidth = pane.Width - 5;

            if (styledLine.Spans is { Length: > 0 } spans)
                RenderStyledContent(buffer, pane.Top + row, contentCol, contentWidth, styledLine.Text, spans, defaultStyle);
            else
                buffer.WriteString(pane.Top + row, contentCol, styledLine.Text, defaultStyle, contentWidth);
        }
    }

    private static void RenderStyledContent(
        ScreenBuffer buffer,
        int row,
        int startCol,
        int maxWidth,
        string text,
        StyledSpan[] spans,
        CellStyle defaultStyle)
    {
        // Render text character by character, applying span styles.
        // Spans may overlap; last one wins if they do.
        // Build a quick lookup: which kind applies at each char position.
        // For performance, iterate spans sorted by start.
        // Simple approach: fill positions with span styles, gaps with default.

        int textLen = text.Length;
        int col = startCol;
        int charsWritten = 0;

        // Sort spans by start (they should already be roughly ordered but ensure it)
        ReadOnlySpan<StyledSpan> orderedSpans = spans;

        int pos = 0;
        while (pos < textLen && charsWritten < maxWidth)
        {
            // Find which span covers pos (if any)
            CellStyle style = defaultStyle;
            foreach (var span in orderedSpans)
            {
                if (span.Start <= pos && pos < span.Start + span.Length)
                {
                    style = SyntaxTheme.GetStyle(span.Kind);
                    break;
                }
            }

            var rune = new System.Text.Rune(text[pos]);
            // Handle surrogate pairs
            if (char.IsHighSurrogate(text[pos]) && pos + 1 < textLen && char.IsLowSurrogate(text[pos + 1]))
            {
                rune = new System.Text.Rune(text[pos], text[pos + 1]);
                pos++;
            }

            buffer.Put(row, col, rune, style);
            col++;
            charsWritten++;
            pos++;
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
