using System.Text;
using Wade.FileSystem;
using Wade.Highlighting;
using Wade.Terminal;

namespace Wade.UI;

internal static class PaneRenderer
{
    // Column widths
    private const int SizeWidth = 8;
    private const int GapWidth = 2;
    private const int StatusColWidth = 2;
    private const int FullDateWidth = 19;
    private const int DateOnlyWidth = 10;
    private const int ShortDateWidth = 6;

    // Tier thresholds: nameWidth must be >= the widest detail column in that tier
    private const int Tier1Detail = SizeWidth + GapWidth + FullDateWidth + GapWidth; // 31
    private const int Tier2Detail = SizeWidth + GapWidth + DateOnlyWidth + GapWidth; // 22
    private const int Tier3Detail = SizeWidth + GapWidth + ShortDateWidth + GapWidth; // 18
    private const int Tier4Detail = SizeWidth + GapWidth; // 10

    private const int Tier1MinWidth = 30 + Tier1Detail; // 61 — name >= 30
    private const int Tier2MinWidth = 22 + Tier2Detail; // 44 — name >= 22
    private const int Tier3MinWidth = 14 + Tier3Detail; // 32 — name >= 14
    private const int Tier4MinWidth = SizeWidth + Tier4Detail; // 18 — name >= Size(8)

    // Drive view column widths
    private const int DriveFormatWidth = 7;   // "exFAT  " / "NTFS   "
    private const int DriveLabelWidth = 16;   // volume label
    private const int DriveBarMinWidth = 10;

    // Drive view colors
    private static readonly Color DriveBarLowColor = new(60, 160, 200);    // teal — <70% used
    private static readonly Color DriveBarMedColor = new(220, 180, 50);    // yellow — 70-90%
    private static readonly Color DriveBarHighColor = new(220, 80, 80);    // red — >90%
    private static readonly Color DriveBarEmptyColor = new(50, 50, 50);    // dark gray
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

    private static readonly Color CloudPlaceholderColor = new(140, 140, 160);

    private static readonly Color GitModifiedColor = new(220, 180, 50);
    private static readonly Color GitStagedColor = new(80, 200, 200);
    private static readonly Color GitUntrackedColor = new(80, 200, 80);
    private static readonly Color GitConflictColor = new(220, 80, 80);

    private static readonly CellStyle ActiveSelectionStyle = new(SelectionFg, SelectionBg, Bold: true);
    private static readonly CellStyle InactiveSelectionStyle = new(SelectionFg, new Color(60, 60, 80), Bold: true);
    private static readonly CellStyle DirStyle = new(DirColor, null, Bold: true);
    private static readonly CellStyle FileStyle = new(FileColor, null);
    private static readonly CellStyle DetailStyle = new(DetailColor, null);
    private static readonly CellStyle SymlinkStyle = new(SymlinkColor, null);
    private static readonly CellStyle BrokenSymlinkStyle = new(BrokenSymlinkColor, null);
    private static readonly CellStyle CloudPlaceholderFileStyle = new(CloudPlaceholderColor, null);
    private static readonly CellStyle CloudPlaceholderDirStyle = new(CloudPlaceholderColor, null, Bold: true);

    private static readonly Color MarkedBg = new(60, 60, 0);
    private static readonly CellStyle MarkedStyle = new(FileColor, MarkedBg);
    private static readonly CellStyle MarkedDirStyle = new(DirColor, MarkedBg, Bold: true);
    private static readonly CellStyle MarkedSelectedStyle = new(SelectionFg, new Color(180, 180, 60), Bold: true);
    private static readonly CellStyle MarkedSymlinkStyle = new(SymlinkColor, MarkedBg);
    private static readonly CellStyle MarkedBrokenSymlinkStyle = new(BrokenSymlinkColor, MarkedBg);
    private static readonly CellStyle MarkedCloudPlaceholderStyle = new(CloudPlaceholderColor, MarkedBg);

    private static readonly CellStyle GitModifiedFileStyle = new(GitModifiedColor, null);
    private static readonly CellStyle GitModifiedDirStyle = new(GitModifiedColor, null, Bold: true);
    private static readonly CellStyle GitStagedFileStyle = new(GitStagedColor, null);
    private static readonly CellStyle GitStagedDirStyle = new(GitStagedColor, null, Bold: true);
    private static readonly CellStyle GitUntrackedFileStyle = new(GitUntrackedColor, null);
    private static readonly CellStyle GitUntrackedDirStyle = new(GitUntrackedColor, null, Bold: true);
    private static readonly CellStyle GitConflictFileStyle = new(GitConflictColor, null);
    private static readonly CellStyle GitConflictDirStyle = new(GitConflictColor, null, Bold: true);

    private static readonly CellStyle MarkedGitModifiedStyle = new(GitModifiedColor, MarkedBg);
    private static readonly CellStyle MarkedGitStagedStyle = new(GitStagedColor, MarkedBg);
    private static readonly CellStyle MarkedGitUntrackedStyle = new(GitUntrackedColor, MarkedBg);
    private static readonly CellStyle MarkedGitConflictStyle = new(GitConflictColor, MarkedBg);

    private readonly record struct ColumnLayout(
        int DetailWidth,
        int DateWidth,
        int StatusColWidth,
        int NameWidth,
        int DriveLabelWidth,
        int DriveFormatWidth,
        int DriveFreeWidth,
        int DriveSizeWidth,
        int DriveBarWidth);

    private static ColumnLayout ComputeColumnLayout(
        int paneWidth,
        bool showSize,
        bool showDate,
        bool isDriveView,
        bool hasStatusCol)
    {
        int dateWidth = 0;
        int detailWidth = 0;
        int driveLabelWidth = 0;
        int driveFormatWidth = 0;
        int driveFreeWidth = 0;
        int driveSizeWidth = 0;
        int driveBarWidth = 0;

        if (isDriveView)
        {
            int minName = 8;
            int barCol = DriveBarMinWidth + GapWidth;
            int fixedWithLabel = DriveLabelWidth + GapWidth + DriveFormatWidth + GapWidth
                + SizeWidth + GapWidth + SizeWidth + GapWidth + barCol;
            int fixedFull = DriveFormatWidth + GapWidth + SizeWidth + GapWidth + SizeWidth + GapWidth + barCol;
            int fixedMedium = SizeWidth + GapWidth + SizeWidth + GapWidth + barCol;
            int fixedNarrow = SizeWidth + GapWidth + barCol;

            if (paneWidth >= minName + fixedWithLabel)
            {
                driveLabelWidth = DriveLabelWidth;
                driveFormatWidth = DriveFormatWidth;
                driveFreeWidth = SizeWidth;
                driveSizeWidth = SizeWidth;
                driveBarWidth = paneWidth - minName - DriveLabelWidth - GapWidth
                    - DriveFormatWidth - GapWidth - SizeWidth - GapWidth - SizeWidth - GapWidth - GapWidth;
                driveBarWidth = Math.Clamp(driveBarWidth, DriveBarMinWidth, 30);
                detailWidth = driveLabelWidth + GapWidth + driveFormatWidth + GapWidth
                    + driveFreeWidth + GapWidth + driveSizeWidth + GapWidth + driveBarWidth + GapWidth;
            }
            else if (paneWidth >= minName + fixedFull)
            {
                driveFormatWidth = DriveFormatWidth;
                driveFreeWidth = SizeWidth;
                driveSizeWidth = SizeWidth;
                driveBarWidth = paneWidth - minName
                    - DriveFormatWidth - GapWidth - SizeWidth - GapWidth - SizeWidth - GapWidth - GapWidth;
                driveBarWidth = Math.Clamp(driveBarWidth, DriveBarMinWidth, 30);
                detailWidth = driveFormatWidth + GapWidth
                    + driveFreeWidth + GapWidth + driveSizeWidth + GapWidth + driveBarWidth + GapWidth;
            }
            else if (paneWidth >= minName + fixedMedium)
            {
                driveFreeWidth = SizeWidth;
                driveSizeWidth = SizeWidth;
                driveBarWidth = paneWidth - minName - SizeWidth - GapWidth - SizeWidth - GapWidth - GapWidth;
                driveBarWidth = Math.Clamp(driveBarWidth, DriveBarMinWidth, 30);
                detailWidth = driveFreeWidth + GapWidth + driveSizeWidth + GapWidth + driveBarWidth + GapWidth;
            }
            else if (paneWidth >= minName + fixedNarrow)
            {
                driveSizeWidth = SizeWidth;
                driveBarWidth = paneWidth - minName - SizeWidth - GapWidth - GapWidth;
                driveBarWidth = Math.Clamp(driveBarWidth, DriveBarMinWidth, 30);
                detailWidth = driveSizeWidth + GapWidth + driveBarWidth + GapWidth;
            }
            else if (paneWidth >= minName + barCol)
            {
                driveBarWidth = paneWidth - minName - GapWidth;
                driveBarWidth = Math.Clamp(driveBarWidth, DriveBarMinWidth, 30);
                detailWidth = driveBarWidth + GapWidth;
            }
        }
        else if (showSize || showDate)
        {
            if (showSize && showDate)
            {
                if (paneWidth >= Tier1MinWidth)
                {
                    dateWidth = FullDateWidth;
                    detailWidth = SizeWidth + GapWidth + FullDateWidth + GapWidth;
                }
                else if (paneWidth >= Tier2MinWidth)
                {
                    dateWidth = DateOnlyWidth;
                    detailWidth = SizeWidth + GapWidth + DateOnlyWidth + GapWidth;
                }
                else if (paneWidth >= Tier3MinWidth)
                {
                    dateWidth = ShortDateWidth;
                    detailWidth = SizeWidth + GapWidth + ShortDateWidth + GapWidth;
                }
                else if (paneWidth >= Tier4MinWidth)
                {
                    detailWidth = SizeWidth + GapWidth;
                }
            }
            else if (showDate)
            {
                if (paneWidth >= Tier1MinWidth)
                {
                    dateWidth = FullDateWidth;
                    detailWidth = FullDateWidth + GapWidth;
                }
                else if (paneWidth >= Tier2MinWidth)
                {
                    dateWidth = DateOnlyWidth;
                    detailWidth = DateOnlyWidth + GapWidth;
                }
                else if (paneWidth >= Tier3MinWidth)
                {
                    dateWidth = ShortDateWidth;
                    detailWidth = ShortDateWidth + GapWidth;
                }
            }
            else
            {
                if (paneWidth >= Tier4MinWidth)
                {
                    detailWidth = SizeWidth + GapWidth;
                }
            }
        }

        int statusCol = hasStatusCol ? StatusColWidth : 0;
        int nameWidth = paneWidth - detailWidth - statusCol;

        return new ColumnLayout(
            detailWidth, dateWidth, statusCol, nameWidth,
            driveLabelWidth, driveFormatWidth, driveFreeWidth, driveSizeWidth, driveBarWidth);
    }

    public static void RenderColumnHeaders(
        ScreenBuffer buffer,
        Rect headerRect,
        bool showIcons,
        bool showSize,
        bool showDate,
        bool isDriveView,
        bool hasStatusCol)
    {
        var layout = ComputeColumnLayout(headerRect.Width, showSize, showDate, isDriveView, hasStatusCol);
        int row = headerRect.Top;
        int left = headerRect.Left;
        CellStyle headerStyle = new(new Color(140, 140, 140), null);

        // Fill background
        buffer.FillRow(row, left, headerRect.Width, ' ', headerStyle);

        // Name column header (left-aligned, after icon space)
        int nameCol = left + (showIcons ? 2 : 1);
        buffer.WriteString(row, nameCol, "Name", headerStyle, layout.NameWidth - (showIcons ? 2 : 1));

        if (layout.DetailWidth > 0)
        {
            int detailCol = left + headerRect.Width;

            if (isDriveView)
            {
                if (layout.DriveBarWidth > 0)
                {
                    detailCol -= GapWidth + layout.DriveBarWidth;
                    // Center "% Full" in the bar column
                    string label = "% Full";
                    int labelStart = (layout.DriveBarWidth - label.Length) / 2;
                    if (labelStart >= 0)
                    {
                        buffer.WriteString(row, detailCol + GapWidth + labelStart, label, headerStyle, label.Length);
                    }
                }

                if (layout.DriveSizeWidth > 0)
                {
                    detailCol -= GapWidth + layout.DriveSizeWidth;
                    buffer.WriteString(row, detailCol + GapWidth + (SizeWidth - 4), "Size", headerStyle, 4);
                }

                if (layout.DriveFreeWidth > 0)
                {
                    detailCol -= GapWidth + layout.DriveFreeWidth;
                    buffer.WriteString(row, detailCol + GapWidth + (SizeWidth - 4), "Free", headerStyle, 4);
                }

                if (layout.DriveFormatWidth > 0)
                {
                    detailCol -= GapWidth + layout.DriveFormatWidth;
                    buffer.WriteString(row, detailCol + GapWidth, "Format", headerStyle, 6);
                }

                if (layout.DriveLabelWidth > 0)
                {
                    detailCol -= GapWidth + layout.DriveLabelWidth;
                    buffer.WriteString(row, detailCol + GapWidth, "Label", headerStyle, 5);
                }
            }
            else
            {
                if (layout.DateWidth > 0)
                {
                    detailCol -= GapWidth + layout.DateWidth;
                    // Right-align "Date" in the date column
                    string label = "Date";
                    int offset = layout.DateWidth - label.Length;
                    buffer.WriteString(row, detailCol + GapWidth + offset, label, headerStyle, label.Length);
                }

                if (showSize && layout.DetailWidth > (layout.DateWidth > 0 ? layout.DateWidth + GapWidth : 0))
                {
                    detailCol -= GapWidth + SizeWidth;
                    buffer.WriteString(row, detailCol + GapWidth + (SizeWidth - 4), "Size", headerStyle, 4);
                }
            }
        }

        // Separator line below headers
        if (headerRect.Height > 1)
        {
            CellStyle separatorStyle = new(BorderColor, null);
            buffer.FillRow(row + 1, left, headerRect.Width, '\u2500', separatorStyle);
        }
    }

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
        HashSet<string>? markedPaths = null,
        Dictionary<string, GitFileStatus>? gitStatuses = null,
        Dictionary<string, long>? dirSizes = null,
        bool isDriveView = false)
    {
        bool hasCloudEntries = false;
        for (int i = 0; i < entries.Count; i++)
        {
            if (entries[i].IsCloudPlaceholder)
            {
                hasCloudEntries = true;
                break;
            }
        }

        bool hasStatusCol = gitStatuses is not null || hasCloudEntries;
        var col = ComputeColumnLayout(pane.Width, showSize, showDate, isDriveView, hasStatusCol);
        int dateWidth = col.DateWidth;
        int detailWidth = col.DetailWidth;
        int statusColWidth = col.StatusColWidth;
        int nameWidth = col.NameWidth;
        int driveLabelWidth = col.DriveLabelWidth;
        int driveFormatWidth = col.DriveFormatWidth;
        int driveFreeWidth = col.DriveFreeWidth;
        int driveSizeWidth = col.DriveSizeWidth;
        int driveBarWidth = col.DriveBarWidth;

        Span<char> sizeBuf = stackalloc char[SizeWidth];
        Span<char> tempBuf = stackalloc char[32];
        Span<char> dateBuf = stackalloc char[FullDateWidth];
        Span<char> barBuf = driveBarWidth > 0 ? stackalloc char[driveBarWidth] : [];

        for (int row = 0; row < pane.Height; row++)
        {
            int entryIndex = scrollOffset + row;

            if (entryIndex >= entries.Count)
            {
                // Empty row — just leave blank
                continue;
            }

            FileSystemEntry entry = entries[entryIndex];
            bool isSelected = entryIndex == selectedIndex;
            bool isMarked = markedPaths?.Contains(entry.FullPath) == true;

            GitFileStatus gitStatus = GetGitStatus(entry.FullPath, gitStatuses);

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
            else if (isMarked && entry.IsCloudPlaceholder)
            {
                style = MarkedCloudPlaceholderStyle;
            }
            else if (isMarked)
            {
                style = GetMarkedGitStyle(gitStatus, entry.IsDirectory) ?? (entry.IsDirectory ? MarkedDirStyle : MarkedStyle);
            }
            else if (entry.IsBrokenSymlink)
            {
                style = BrokenSymlinkStyle;
            }
            else if (entry.IsSymlink)
            {
                style = SymlinkStyle;
            }
            else if (entry.IsCloudPlaceholder)
            {
                style = entry.IsDirectory ? CloudPlaceholderDirStyle : CloudPlaceholderFileStyle;
            }
            else
            {
                style = GetGitStyle(gitStatus, entry.IsDirectory) ?? (entry.IsDirectory ? DirStyle : FileStyle);
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

                int nameStart = 2;
                int maxName = nameWidth - nameStart;
                int nameLen = Math.Min(entry.Name.Length, maxName);
                if (entry.Name.Length > maxName && maxName >= 2)
                {
                    buffer.WriteString(screenRow, entryCol + nameStart, entry.Name, style, maxName - 1);
                    buffer.Put(screenRow, entryCol + nameStart + maxName - 1, '\u2026', style);
                }
                else
                {
                    buffer.WriteString(screenRow, entryCol + nameStart, entry.Name, style, maxName);
                }

                nameCharsUsed = nameStart + nameLen;
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

            // Append " → target" suffix for symlinks and app exec aliases
            string? targetPath = entry.IsSymlink ? entry.LinkTarget
                : entry.IsAppExecLink ? entry.AppExecLinkTarget
                : null;

            if (targetPath is { } linkTarget)
            {
                int remaining = nameWidth - nameCharsUsed;
                if (remaining > 4) // need room for at least " → X"
                {
                    CellStyle suffixStyle = isSelected ? style : entry.IsBrokenSymlink ? BrokenSymlinkStyle : DetailStyle;
                    string arrow = " \u2192 ";
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

            // Status column (git status or cloud icon between name and size)
            if (statusColWidth > 0)
            {
                if (gitStatus != GitFileStatus.None)
                {
                    CellStyle gitIconStyle = isSelected ? style : GetGitIconStyle(gitStatus, isMarked);
                    buffer.Put(screenRow, entryCol + nameWidth, FileIcons.GetGitStatusIcon(gitStatus), gitIconStyle);
                }
                else if (entry.IsCloudPlaceholder)
                {
                    CellStyle cloudIconStyle = isSelected ? style : new CellStyle(CloudPlaceholderColor, isMarked ? MarkedBg : null);
                    buffer.Put(screenRow, entryCol + nameWidth, FileIcons.GetCloudIcon(), cloudIconStyle);
                }
            }

            // Render detail columns (right-aligned from pane edge)
            if (detailWidth > 0)
            {
                int detailCol = pane.Left + pane.Width;

                if (isDriveView)
                {
                    // Drive columns right to left: bar | size | free | format | label
                    // Always adjust detailCol for allocated columns so left columns stay aligned.

                    // Percent bar (rightmost)
                    if (driveBarWidth > 0)
                    {
                        detailCol -= GapWidth + driveBarWidth;

                        if (entry.DriveTotalSize > 0)
                        {
                            double fraction = (double)(entry.DriveTotalSize - entry.DriveFreeSpace) / entry.DriveTotalSize;

                            // Reserve first char as a spacer so the bar doesn't blend
                            // with the selection background color.
                            int actualBarWidth = driveBarWidth - 1;
                            var bar = FormatHelpers.FormatPercentBar(barBuf, fraction, actualBarWidth);

                            Color barColor = fraction > 0.9 ? DriveBarHighColor
                                : fraction > 0.7 ? DriveBarMedColor
                                : DriveBarLowColor;

                            int barCol = detailCol + GapWidth;
                            int labelEnd = bar.LabelStart + bar.LabelLength;

                            buffer.Put(screenRow, barCol, ' ', default);

                            for (int c = 0; c < actualBarWidth; c++)
                            {
                                bool isLabel = c >= bar.LabelStart && c < labelEnd;
                                bool isFilled = c < bar.FilledCount;

                                CellStyle charStyle;
                                if (isLabel)
                                {
                                    Color bg = isFilled ? barColor : DriveBarEmptyColor;
                                    charStyle = new CellStyle(new Color(0, 0, 0), bg);
                                }
                                else
                                {
                                    charStyle = isFilled ? new CellStyle(barColor, null) : new CellStyle(DriveBarEmptyColor, null);
                                }

                                buffer.Put(screenRow, barCol + 1 + c, barBuf[c], charStyle);
                            }
                        }
                    }

                    // Total size
                    if (driveSizeWidth > 0)
                    {
                        detailCol -= GapWidth + driveSizeWidth;

                        if (entry.DriveTotalSize > 0)
                        {
                            sizeBuf.Fill(' ');
                            int sizeLen = FormatHelpers.FormatSize(tempBuf, entry.DriveTotalSize);
                            if (sizeLen <= SizeWidth)
                            {
                                tempBuf[..sizeLen].CopyTo(sizeBuf[(SizeWidth - sizeLen)..]);
                            }

                            buffer.WriteString(screenRow, detailCol + GapWidth, sizeBuf, detailStyle, SizeWidth);
                        }
                    }

                    // Free space
                    if (driveFreeWidth > 0)
                    {
                        detailCol -= GapWidth + driveFreeWidth;

                        if (entry.DriveTotalSize > 0)
                        {
                            sizeBuf.Fill(' ');
                            int freeLen = FormatHelpers.FormatSize(tempBuf, entry.DriveFreeSpace);
                            if (freeLen <= SizeWidth)
                            {
                                tempBuf[..freeLen].CopyTo(sizeBuf[(SizeWidth - freeLen)..]);
                            }

                            buffer.WriteString(screenRow, detailCol + GapWidth, sizeBuf, detailStyle, SizeWidth);
                        }
                    }

                    // File system format
                    if (driveFormatWidth > 0)
                    {
                        detailCol -= GapWidth + driveFormatWidth;

                        if (entry.DriveFormat is { } fmt)
                        {
                            int fmtLen = Math.Min(fmt.Length, driveFormatWidth);
                            buffer.WriteString(screenRow, detailCol + GapWidth, fmt, detailStyle, fmtLen);
                        }
                    }

                    // Volume label
                    if (driveLabelWidth > 0)
                    {
                        detailCol -= GapWidth + driveLabelWidth;

                        if (entry.DriveLabel is { } label)
                        {
                            int lblLen = Math.Min(label.Length, driveLabelWidth);
                            buffer.WriteString(screenRow, detailCol + GapWidth, label, detailStyle, lblLen);
                        }
                    }
                }
                else
                {
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
                        long? displaySize = null;

                        if (!entry.IsDirectory)
                        {
                            displaySize = entry.Size;
                        }
                        else if (dirSizes != null && dirSizes.TryGetValue(entry.FullPath, out long dirSize))
                        {
                            displaySize = dirSize;
                        }

                        if (displaySize.HasValue)
                        {
                            sizeBuf.Fill(' ');
                            int sizeLen = FormatHelpers.FormatSize(tempBuf, displaySize.Value);
                            // Right-align size in the field
                            if (sizeLen <= SizeWidth)
                            {
                                tempBuf[..sizeLen].CopyTo(sizeBuf[(SizeWidth - sizeLen)..]);
                            }

                            buffer.WriteString(screenRow, detailCol + GapWidth, sizeBuf, detailStyle, SizeWidth);
                        }
                        else if (entry.IsDirectory && dirSizes != null)
                        {
                            // Directory size is loading — show indicator right-aligned
                            sizeBuf.Fill(' ');
                            sizeBuf[SizeWidth - 1] = '\u2026'; // …
                            buffer.WriteString(screenRow, detailCol + GapWidth, sizeBuf, detailStyle, SizeWidth);
                        }
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

            StyledLine styledLine = lines[lineIndex];

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
            foreach (StyledSpan span in orderedSpans)
            {
                if (span.Start <= pos && pos < span.Start + span.Length)
                {
                    style = SyntaxTheme.GetStyle(span.Kind);
                    break;
                }
            }

            Rune rune;
            // Handle surrogate pairs
            if (char.IsHighSurrogate(text[pos]) && pos + 1 < textLen && char.IsLowSurrogate(text[pos + 1]))
            {
                rune = new Rune(text[pos], text[pos + 1]);
                pos++;
            }
            else if (char.IsSurrogate(text[pos]))
            {
                rune = Rune.ReplacementChar;
            }
            else
            {
                rune = new Rune(text[pos]);
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
            CellStyle style = styleIndex < charStyles.Length ? charStyles[styleIndex] : defaultStyle;

            Rune rune;
            if (char.IsHighSurrogate(text[pos]) && pos + 1 < text.Length && char.IsLowSurrogate(text[pos + 1]))
            {
                rune = new Rune(text[pos], text[pos + 1]);
                pos++;
                styleIndex++;
            }
            else if (char.IsSurrogate(text[pos]))
            {
                rune = Rune.ReplacementChar;
            }
            else
            {
                rune = new Rune(text[pos]);
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

    private static GitFileStatus GetGitStatus(string path, Dictionary<string, GitFileStatus>? statuses)
        => statuses is not null && statuses.TryGetValue(path, out GitFileStatus s) ? s : GitFileStatus.None;

    private static CellStyle? GetGitStyle(GitFileStatus status, bool isDirectory)
    {
        if (status.HasFlag(GitFileStatus.Conflict))
        {
            return isDirectory ? GitConflictDirStyle : GitConflictFileStyle;
        }

        if (status.HasFlag(GitFileStatus.Staged))
        {
            return isDirectory ? GitStagedDirStyle : GitStagedFileStyle;
        }

        if (status.HasFlag(GitFileStatus.Modified))
        {
            return isDirectory ? GitModifiedDirStyle : GitModifiedFileStyle;
        }

        if (status.HasFlag(GitFileStatus.Untracked))
        {
            return isDirectory ? GitUntrackedDirStyle : GitUntrackedFileStyle;
        }

        return null;
    }

    private static CellStyle? GetMarkedGitStyle(GitFileStatus status, bool isDirectory)
    {
        _ = isDirectory; // unused but kept for API symmetry with GetGitStyle
        if (status.HasFlag(GitFileStatus.Conflict))
        {
            return MarkedGitConflictStyle;
        }

        if (status.HasFlag(GitFileStatus.Staged))
        {
            return MarkedGitStagedStyle;
        }

        if (status.HasFlag(GitFileStatus.Modified))
        {
            return MarkedGitModifiedStyle;
        }

        if (status.HasFlag(GitFileStatus.Untracked))
        {
            return MarkedGitUntrackedStyle;
        }

        return null;
    }

    private static CellStyle GetGitIconStyle(GitFileStatus status, bool isMarked)
    {
        Color? bg = isMarked ? MarkedBg : null;
        if (status.HasFlag(GitFileStatus.Conflict))
        {
            return new CellStyle(GitConflictColor, bg);
        }

        if (status.HasFlag(GitFileStatus.Staged))
        {
            return new CellStyle(GitStagedColor, bg);
        }

        if (status.HasFlag(GitFileStatus.Modified))
        {
            return new CellStyle(GitModifiedColor, bg);
        }

        if (status.HasFlag(GitFileStatus.Untracked))
        {
            return new CellStyle(GitUntrackedColor, bg);
        }

        return new CellStyle(FileColor, bg);
    }

    public static void RenderBorders(
        ScreenBuffer buffer,
        Layout layout,
        int terminalHeight,
        bool previewPaneEnabled = true,
        bool parentPaneEnabled = true)
    {
        var style = new CellStyle(BorderColor, null);
        int contentHeight = terminalHeight - 1;

        if (parentPaneEnabled)
        {
            int borderCol1 = layout.LeftPane.Right;

            for (int row = 0; row < contentHeight; row++)
            {
                buffer.Put(row, borderCol1, '│', style);
            }
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
