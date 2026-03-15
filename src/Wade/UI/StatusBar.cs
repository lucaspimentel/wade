using Wade.FileSystem;
using Wade.Terminal;

namespace Wade.UI;

internal static class StatusBar
{
    private static readonly Color StatusFg = new(180, 180, 180);
    private static readonly Color StatusBg = new(30, 30, 50);
    private static readonly Color PathFg = new(80, 160, 255);
    private static readonly Color SuccessFg = new(80, 200, 80);
    private static readonly Color ErrorFg = new(220, 80, 80);

    public static void Render(
        ScreenBuffer buffer,
        Rect rect,
        string currentPath,
        int itemCount,
        int selectedIndex,
        FileSystemEntry? selectedEntry,
        string? fileTypeLabel = null,
        string? encoding = null,
        string? lineEnding = null,
        Notification? notification = null,
        int markedCount = 0,
        SortMode sortMode = SortMode.Name,
        bool sortAscending = true,
        int clipboardCount = 0,
        bool clipboardIsCut = false,
        string? branchName = null)
    {
        // Fill background
        var bgStyle = new CellStyle(StatusFg, StatusBg);
        for (int col = 0; col < rect.Width; col++)
        {
            buffer.Put(rect.Top, rect.Left + col, ' ', bgStyle);
        }

        // Left side: current path
        var pathStyle = new CellStyle(PathFg, StatusBg, Bold: true);
        int pathMaxWidth = rect.Width / 2;
        buffer.WriteString(rect.Top, rect.Left + 1, currentPath, pathStyle, pathMaxWidth);

        // Branch name (after path)
        int infoCol = rect.Left + 1 + Math.Min(currentPath.Length, pathMaxWidth);
        int infoMaxWidth = pathMaxWidth - Math.Min(currentPath.Length, pathMaxWidth);

        if (branchName is not null && infoMaxWidth > 0)
        {
            var branchStyle = new CellStyle(new Color(180, 140, 220), StatusBg);
            Span<char> branchBuf = stackalloc char[64];
            int branchLen = 0;
            branchBuf[branchLen++] = ' ';
            branchBuf[branchLen++] = ' ';

            int maxBranch = Math.Min(branchName.Length, branchBuf.Length - branchLen);
            maxBranch = Math.Min(maxBranch, infoMaxWidth - 2);
            if (maxBranch > 0)
            {
                branchName.AsSpan(0, maxBranch).CopyTo(branchBuf[branchLen..]);
                branchLen += maxBranch;
                buffer.WriteString(rect.Top, infoCol, branchBuf[..branchLen], branchStyle, infoMaxWidth);
                infoCol += branchLen;
                infoMaxWidth -= branchLen;
            }
        }

        // Mark count and clipboard indicator

        if (markedCount > 0)
        {
            var markStyle = new CellStyle(new Color(220, 220, 100), StatusBg);
            Span<char> markBuf = stackalloc char[16];
            int markLen = 0;
            markBuf[markLen++] = ' ';
            markBuf[markLen++] = ' ';
            markedCount.TryFormat(markBuf[markLen..], out int n);
            markLen += n;
            " marked".AsSpan().CopyTo(markBuf[markLen..]);
            markLen += 7;
            if (infoMaxWidth > 0)
            {
                buffer.WriteString(rect.Top, infoCol, markBuf[..markLen], markStyle, infoMaxWidth);
            }

            infoCol += markLen;
            infoMaxWidth -= markLen;
        }

        if (clipboardCount > 0 && infoMaxWidth > 0)
        {
            var clipStyle = new CellStyle(new Color(140, 180, 220), StatusBg);
            Span<char> clipBuf = stackalloc char[24];
            int clipLen = 0;
            clipBuf[clipLen++] = ' ';
            clipBuf[clipLen++] = ' ';
            clipBuf[clipLen++] = '[';
            clipboardCount.TryFormat(clipBuf[clipLen..], out int cn);
            clipLen += cn;
            ReadOnlySpan<char> clipLabel = clipboardIsCut ? " cut]" : " copied]";
            clipLabel.CopyTo(clipBuf[clipLen..]);
            clipLen += clipLabel.Length;
            buffer.WriteString(rect.Top, infoCol, clipBuf[..clipLen], clipStyle, infoMaxWidth);
        }

        // Right side: always show metadata right-aligned
        Span<char> rightBuf = stackalloc char[128];
        int rightLen = BuildRightText(rightBuf, itemCount, selectedIndex, selectedEntry, fileTypeLabel, encoding, lineEnding, sortMode, sortAscending);
        ReadOnlySpan<char> right = rightBuf[..rightLen];

        int rightCol = rect.Width - rightLen - 1;
        if (rightCol > 0)
        {
            for (int i = 0; i < rightLen; i++)
            {
                buffer.Put(rect.Top, rect.Left + rightCol + i, right[i], bgStyle);
            }
        }

        // Notification: render in the gap between left content and metadata
        if (notification is { } notif)
        {
            Color notifFg = notif.Kind switch
            {
                NotificationKind.Success => SuccessFg,
                NotificationKind.Error => ErrorFg,
                _ => StatusFg,
            };
            var notifStyle = new CellStyle(notifFg, StatusBg);

            // Available gap: from end of left half to 2 chars before metadata
            int gapEnd = rightCol > 0 ? rightCol - 2 : rect.Width - rightLen - 3;
            int gapStart = pathMaxWidth + 1;
            int gapWidth = gapEnd - gapStart;

            if (gapWidth > 0)
            {
                string message = notif.Message;
                if (message.Length > gapWidth)
                {
                    message = message[..gapWidth];
                }

                // Right-align notification within the gap (closer to metadata)
                int notifCol = gapEnd - message.Length;
                buffer.WriteString(rect.Top, rect.Left + notifCol, message, notifStyle);
            }
        }
    }

    private static int BuildRightText(Span<char> buf, int itemCount, int selectedIndex, FileSystemEntry? selectedEntry, string? fileTypeLabel, string? encoding = null, string? lineEnding = null, SortMode sortMode = SortMode.Name, bool sortAscending = true)
    {
        int pos = 0;

        pos += FormatSortIndicator(buf[pos..], sortMode, sortAscending);

        if (fileTypeLabel is not null && selectedEntry is { IsDirectory: false })
        {
            fileTypeLabel.AsSpan().CopyTo(buf[pos..]);
            pos += fileTypeLabel.Length;
            buf[pos++] = ' ';
            buf[pos++] = ' ';
        }

        if (encoding is not null && selectedEntry is { IsDirectory: false })
        {
            encoding.AsSpan().CopyTo(buf[pos..]);
            pos += encoding.Length;
            buf[pos++] = ' ';
            buf[pos++] = ' ';
        }

        if (lineEnding is not null && selectedEntry is { IsDirectory: false })
        {
            lineEnding.AsSpan().CopyTo(buf[pos..]);
            pos += lineEnding.Length;
            buf[pos++] = ' ';
            buf[pos++] = ' ';
        }

        if (selectedEntry is not null)
        {
            if (selectedEntry.IsDirectory)
            {
                "dir".AsSpan().CopyTo(buf[pos..]);
                pos += 3;
            }
            else
            {
                pos += FormatHelpers.FormatSize(buf[pos..], selectedEntry.Size);
            }

            buf[pos++] = ' ';
            buf[pos++] = ' ';
        }

        if (itemCount > 0)
        {
            (selectedIndex + 1).TryFormat(buf[pos..], out int n);
            pos += n;
            buf[pos++] = '/';
            itemCount.TryFormat(buf[pos..], out int m);
            pos += m;
        }
        else
        {
            "empty".AsSpan().CopyTo(buf[pos..]);
            pos += 5;
        }

        return pos;
    }

    private static int FormatSortIndicator(Span<char> buf, SortMode sortMode, bool sortAscending)
    {
        ReadOnlySpan<char> label = sortMode switch
        {
            SortMode.Modified => "time",
            SortMode.Size => "size",
            SortMode.Extension => "ext",
            _ => "name",
        };
        label.CopyTo(buf);
        int pos = label.Length;
        buf[pos++] = sortAscending ? '\u2191' : '\u2193'; // ↑ or ↓
        buf[pos++] = ' ';
        buf[pos++] = ' ';
        return pos;
    }

}
