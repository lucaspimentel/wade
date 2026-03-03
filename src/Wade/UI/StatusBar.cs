using Wade.FileSystem;
using Wade.Terminal;

namespace Wade.UI;

internal static class StatusBar
{
    private static readonly Color StatusFg = new(180, 180, 180);
    private static readonly Color StatusBg = new(30, 30, 50);
    private static readonly Color PathFg = new(80, 160, 255);

    public static void Render(
        ScreenBuffer buffer,
        Rect rect,
        string currentPath,
        int itemCount,
        int selectedIndex,
        FileSystemEntry? selectedEntry,
        string? fileTypeLabel = null,
        string? encoding = null,
        string? lineEnding = null)
    {
        // Fill background
        var bgStyle = new CellStyle(StatusFg, StatusBg);
        for (int col = 0; col < rect.Width; col++)
            buffer.Put(rect.Top, rect.Left + col, ' ', bgStyle);

        // Left side: current path
        var pathStyle = new CellStyle(PathFg, StatusBg, Bold: true);
        buffer.WriteString(rect.Top, rect.Left + 1, currentPath, pathStyle, rect.Width / 2);

        // Right side: item count + selected info — build without heap allocations
        Span<char> rightBuf = stackalloc char[128];
        int rightLen = BuildRightText(rightBuf, itemCount, selectedIndex, selectedEntry, fileTypeLabel, encoding, lineEnding);
        ReadOnlySpan<char> right = rightBuf[..rightLen];

        int rightCol = rect.Width - rightLen - 1;
        if (rightCol > 0)
        {
            for (int i = 0; i < rightLen; i++)
                buffer.Put(rect.Top, rect.Left + rightCol + i, right[i], bgStyle);
        }
    }

    private static int BuildRightText(Span<char> buf, int itemCount, int selectedIndex, FileSystemEntry? selectedEntry, string? fileTypeLabel, string? encoding = null, string? lineEnding = null)
    {
        int pos = 0;

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
                pos += FormatSize(buf[pos..], selectedEntry.Size);
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

    private static int FormatSize(Span<char> buf, long bytes)
    {
        if (bytes < 1024)
        {
            bytes.TryFormat(buf, out int n);
            " B".AsSpan().CopyTo(buf[n..]);
            return n + 2;
        }
        if (bytes < 1024 * 1024)
        {
            (bytes / 1024.0).TryFormat(buf, out int n, "F1");
            " KB".AsSpan().CopyTo(buf[n..]);
            return n + 3;
        }
        if (bytes < 1024L * 1024 * 1024)
        {
            (bytes / (1024.0 * 1024.0)).TryFormat(buf, out int n, "F1");
            " MB".AsSpan().CopyTo(buf[n..]);
            return n + 3;
        }
        {
            (bytes / (1024.0 * 1024.0 * 1024.0)).TryFormat(buf, out int n, "F1");
            " GB".AsSpan().CopyTo(buf[n..]);
            return n + 3;
        }
    }
}
