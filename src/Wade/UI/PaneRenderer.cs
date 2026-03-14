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

    private static readonly Color DetailColor = new(110, 110, 110);
    private static readonly Color SymlinkColor = new(0, 200, 200);
    private static readonly Color BrokenSymlinkColor = new(200, 60, 60);

    private static readonly CellStyle ActiveSelectionStyle = new(SelectionFg, SelectionBg, Bold: true);
    private static readonly CellStyle InactiveSelectionStyle = new(SelectionFg, new Color(60, 60, 80), Bold: true);
    private static readonly CellStyle DirStyle = new(DirColor, null, Bold: true);
    private static readonly CellStyle FileStyle = new(FileColor, null);
    private static readonly CellStyle DetailStyle = new(DetailColor, null);
    private static readonly CellStyle SymlinkStyle = new(SymlinkColor, null);
    private static readonly CellStyle BrokenSymlinkStyle = new(BrokenSymlinkColor, null);

    private static readonly Color MarkedBg = new(60, 60, 0);
    private static readonly CellStyle MarkedStyle = new(FileColor, MarkedBg);
    private static readonly CellStyle MarkedDirStyle = new(DirColor, MarkedBg, Bold: true);
    private static readonly CellStyle MarkedSelectedStyle = new(SelectionFg, new Color(180, 180, 60), Bold: true);
    private static readonly CellStyle MarkedSymlinkStyle = new(SymlinkColor, MarkedBg);
    private static readonly CellStyle MarkedBrokenSymlinkStyle = new(BrokenSymlinkColor, MarkedBg);

    // Column widths
    private const int SizeWidth = 8;
    private const int GapWidth = 2;
    private const int FullDateWidth = 19;
    private const int DateOnlyWidth = 10;
    private const int ShortDateWidth = 6;

    // Tier thresholds: nameWidth must be >= the widest detail column in that tier
    private const int Tier1Detail = SizeWidth + GapWidth + FullDateWidth + GapWidth;   // 31
    private const int Tier2Detail = SizeWidth + GapWidth + DateOnlyWidth + GapWidth;   // 22
    private const int Tier3Detail = SizeWidth + GapWidth + ShortDateWidth + GapWidth;  // 18
    private const int Tier4Detail = SizeWidth + GapWidth;                              // 10

    private const int Tier1MinWidth = FullDateWidth + Tier1Detail;  // 50 — name >= FullDate(19)
    private const int Tier2MinWidth = DateOnlyWidth + Tier2Detail;  // 32 — name >= DateOnly(10)
    private const int Tier3MinWidth = SizeWidth + Tier3Detail;      // 26 — name >= Size(8)
    private const int Tier4MinWidth = SizeWidth + Tier4Detail;      // 18 — name >= Size(8)

    public static void RenderFileList(
        ScreenBuffer buffer,
        Rect pane,
        List<FileSystemEntry> entries,
        int selectedIndex,
        int scrollOffset,
        bool isActive,
        bool showIcons = false,
        bool showSize = false,
        bool showDate = false,
        HashSet<string>? markedPaths = null)
    {
        // Determine detail tier based on pane width
        int dateWidth = 0;
        int detailWidth = 0;

        if (showSize || showDate)
        {
            if (showSize && showDate)
            {
                if (pane.Width >= Tier1MinWidth)
                {
                    dateWidth = FullDateWidth;
                    detailWidth = SizeWidth + GapWidth + FullDateWidth + GapWidth;
                }
                else if (pane.Width >= Tier2MinWidth)
                {
                    dateWidth = DateOnlyWidth;
                    detailWidth = SizeWidth + GapWidth + DateOnlyWidth + GapWidth;
                }
                else if (pane.Width >= Tier3MinWidth)
                {
                    dateWidth = ShortDateWidth;
                    detailWidth = SizeWidth + GapWidth + ShortDateWidth + GapWidth;
                }
                else if (pane.Width >= Tier4MinWidth)
                {
                    detailWidth = SizeWidth + GapWidth;
                }
            }
            else if (showDate)
            {
                // Date only (no size column)
                if (pane.Width >= Tier1MinWidth)
                {
                    dateWidth = FullDateWidth;
                    detailWidth = FullDateWidth + GapWidth;
                }
                else if (pane.Width >= Tier2MinWidth)
                {
                    dateWidth = DateOnlyWidth;
                    detailWidth = DateOnlyWidth + GapWidth;
                }
                else if (pane.Width >= Tier3MinWidth)
                {
                    dateWidth = ShortDateWidth;
                    detailWidth = ShortDateWidth + GapWidth;
                }
            }
            else // showSize only
            {
                if (pane.Width >= Tier4MinWidth)
                {
                    detailWidth = SizeWidth + GapWidth;
                }
            }
        }

        int nameWidth = pane.Width - detailWidth;

        Span<char> sizeBuf = stackalloc char[SizeWidth];
        Span<char> tempBuf = stackalloc char[32];
        Span<char> dateBuf = stackalloc char[FullDateWidth];

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
            bool isMarked = markedPaths?.Contains(entry.FullPath) == true;

            CellStyle style;
            if (isSelected && isMarked && isActive)
            {
                style = MarkedSelectedStyle;
            }
            else if (isSelected && isActive)
            {
                style = ActiveSelectionStyle;
            }
            else if (isSelected)
            {
                style = InactiveSelectionStyle;
            }
            else if (isMarked && entry.IsBrokenSymlink)
            {
                style = MarkedBrokenSymlinkStyle;
            }
            else if (isMarked && entry.IsSymlink)
            {
                style = MarkedSymlinkStyle;
            }
            else if (isMarked && entry.IsDirectory)
            {
                style = MarkedDirStyle;
            }
            else if (isMarked)
            {
                style = MarkedStyle;
            }
            else if (entry.IsBrokenSymlink)
            {
                style = BrokenSymlinkStyle;
            }
            else if (entry.IsSymlink)
            {
                style = SymlinkStyle;
            }
            else if (entry.IsDirectory)
            {
                style = DirStyle;
            }
            else
            {
                style = FileStyle;
            }

            CellStyle detailStyle = isSelected || isMarked ? style : DetailStyle;

            // If selected or marked, fill the row background
            if (isSelected || isMarked)
            {
                buffer.FillRow(pane.Top + row, pane.Left, pane.Width, ' ', style);
            }

            int screenRow = pane.Top + row;
            int entryCol = pane.Left;

            // Render name
            int nameCharsUsed;
            if (showIcons)
            {
                buffer.Put(screenRow, entryCol, FileIcons.GetIcon(entry), style);
                buffer.Put(screenRow, entryCol + 1, ' ', style);
                int maxName = nameWidth - 2;
                int nameLen = Math.Min(entry.Name.Length, maxName);
                if (entry.Name.Length > maxName && maxName >= 2)
                {
                    buffer.WriteString(screenRow, entryCol + 2, entry.Name, style, maxName - 1);
                    buffer.Put(screenRow, entryCol + 2 + maxName - 1, '\u2026', style);
                }
                else
                {
                    buffer.WriteString(screenRow, entryCol + 2, entry.Name, style, maxName);
                }

                nameCharsUsed = 2 + nameLen;
            }
            else
            {
                char prefix = entry.IsDirectory && !entry.IsDrive ? DirSeparatorChar : ' ';
                buffer.Put(screenRow, entryCol, prefix, style);
                int maxName = nameWidth - 1;
                int nameLen = Math.Min(entry.Name.Length, maxName);
                if (entry.Name.Length > maxName && maxName >= 2)
                {
                    buffer.WriteString(screenRow, entryCol + 1, entry.Name, style, maxName - 1);
                    buffer.Put(screenRow, entryCol + 1 + maxName - 1, '\u2026', style);
                }
                else
                {
                    buffer.WriteString(screenRow, entryCol + 1, entry.Name, style, maxName);
                }

                nameCharsUsed = 1 + nameLen;
            }

            // Append " → target" suffix for symlinks
            if (entry.IsSymlink && entry.LinkTarget is { } linkTarget)
            {
                int remaining = nameWidth - nameCharsUsed;
                if (remaining > 4) // need room for at least " → X"
                {
                    CellStyle suffixStyle = isSelected ? style : (entry.IsBrokenSymlink ? BrokenSymlinkStyle : DetailStyle);
                    string arrow = " → ";
                    int suffixCol = entryCol + nameCharsUsed;
                    buffer.WriteString(screenRow, suffixCol, arrow, suffixStyle, remaining);
                    remaining -= arrow.Length;
                    if (remaining > 0)
                    {
                        if (linkTarget.Length > remaining && remaining >= 2)
                        {
                            buffer.WriteString(screenRow, suffixCol + arrow.Length, linkTarget, suffixStyle, remaining - 1);
                            buffer.Put(screenRow, suffixCol + arrow.Length + remaining - 1, '\u2026', suffixStyle);
                        }
                        else
                        {
                            buffer.WriteString(screenRow, suffixCol + arrow.Length, linkTarget, suffixStyle, remaining);
                        }
                    }
                }
            }

            // Render detail columns (right-aligned from pane edge)
            if (detailWidth > 0)
            {
                int detailCol = pane.Left + pane.Width;

                // Date column (rightmost)
                if (dateWidth > 0)
                {
                    detailCol -= GapWidth + dateWidth;
                    int dateLen = FormatHelpers.FormatDate(dateBuf, entry.LastModified, dateWidth);
                    buffer.WriteString(screenRow, detailCol + GapWidth, dateBuf[..dateLen], detailStyle, dateWidth);
                }

                // Size column
                if (showSize)
                {
                    detailCol -= GapWidth + SizeWidth;
                    if (!entry.IsDirectory)
                    {
                        sizeBuf.Fill(' ');
                        int sizeLen = FormatHelpers.FormatSize(tempBuf, entry.Size);
                        // Right-align size in the field
                        if (sizeLen <= SizeWidth)
                        {
                            tempBuf[..sizeLen].CopyTo(sizeBuf[(SizeWidth - sizeLen)..]);
                        }

                        buffer.WriteString(screenRow, detailCol + GapWidth, sizeBuf, detailStyle, SizeWidth);
                    }
                }
            }
        }
    }

    public static void RenderPreview(
        ScreenBuffer buffer,
        Rect pane,
        StyledLine[] lines,
        int scrollOffset = 0,
        bool showLineNumbers = true)
    {
        var defaultStyle = new CellStyle(FileColor, null);
        var lineNumStyle = new CellStyle(DimColor, null);
        Span<char> lineNumBuf = stackalloc char[4];

        int contentLineNumber = scrollOffset;
        int lineNumWidth = showLineNumbers ? 5 : 0;

        for (int row = 0; row < pane.Height; row++)
        {
            int lineIndex = scrollOffset + row;
            if (lineIndex >= lines.Length)
            {
                break;
            }

            var styledLine = lines[lineIndex];

            contentLineNumber++;

            if (showLineNumbers)
            {
                // Line number (4 chars wide, right-aligned)
                lineNumBuf.Fill(' ');
                contentLineNumber.TryFormat(lineNumBuf, out int numLen);
                if (numLen < 4)
                {
                    lineNumBuf[..numLen].CopyTo(lineNumBuf[(4 - numLen)..]);
                    lineNumBuf[..(4 - numLen)].Fill(' ');
                }

                for (int i = 0; i < 4; i++)
                {
                    buffer.Put(pane.Top + row, pane.Left + i, lineNumBuf[i], lineNumStyle);
                }

                buffer.Put(pane.Top + row, pane.Left + 4, ' ', lineNumStyle);
            }

            // Content
            int contentCol = pane.Left + lineNumWidth;
            int contentWidth = pane.Width - lineNumWidth;

            if (styledLine.CharStyles is { } charStyles)
            {
                RenderPerCharContent(buffer, pane.Top + row, contentCol, contentWidth, styledLine.Text, charStyles, defaultStyle);
            }
            else if (styledLine.Spans is { Length: > 0 } spans)
            {
                RenderStyledContent(buffer, pane.Top + row, contentCol, contentWidth, styledLine.Text, spans, defaultStyle);
            }
            else
            {
                buffer.WriteString(pane.Top + row, contentCol, styledLine.Text, defaultStyle, contentWidth);
            }
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

            int w = RuneWidth.GetWidth(rune);
            if (charsWritten + w > maxWidth)
            {
                break;
            }

            buffer.Put(row, col, rune, style);
            col += w;
            charsWritten += w;
            pos++;
        }
    }

    private static void RenderPerCharContent(
        ScreenBuffer buffer,
        int row,
        int startCol,
        int maxWidth,
        string text,
        CellStyle[] charStyles,
        CellStyle defaultStyle)
    {
        int col = startCol;
        int charsWritten = 0;
        int styleIndex = 0;

        int pos = 0;
        while (pos < text.Length && charsWritten < maxWidth)
        {
            var style = styleIndex < charStyles.Length ? charStyles[styleIndex] : defaultStyle;

            var rune = new System.Text.Rune(text[pos]);
            if (char.IsHighSurrogate(text[pos]) && pos + 1 < text.Length && char.IsLowSurrogate(text[pos + 1]))
            {
                rune = new System.Text.Rune(text[pos], text[pos + 1]);
                pos++;
                styleIndex++;
            }

            int w = RuneWidth.GetWidth(rune);
            if (charsWritten + w > maxWidth)
            {
                break;
            }

            buffer.Put(row, col, rune, style);
            col += w;
            charsWritten += w;
            pos++;
            styleIndex++;
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

    public static void RenderBorders(ScreenBuffer buffer, Layout layout, int terminalHeight, bool previewPaneEnabled = true)
    {
        var style = new CellStyle(BorderColor, null);
        int borderCol1 = layout.LeftPane.Right;
        int contentHeight = terminalHeight - 1;

        for (int row = 0; row < contentHeight; row++)
        {
            buffer.Put(row, borderCol1, '│', style);
        }

        if (previewPaneEnabled)
        {
            int borderCol2 = layout.CenterPane.Right;

            for (int row = 0; row < contentHeight; row++)
            {
                buffer.Put(row, borderCol2, '│', style);
            }
        }
    }
}
