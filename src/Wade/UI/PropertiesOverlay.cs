using System.Globalization;
using System.Runtime.InteropServices;
using Wade.FileSystem;
using Wade.Terminal;

namespace Wade.UI;

internal static class PropertiesOverlay
{
    private static readonly Color LabelColor = new(120, 120, 140);
    private static readonly Color ValueColor = new(200, 200, 200);

    private static readonly string[] Labels =
    [
        "Name",
        "Path",
        "Type",
        "Target",
        "Size",
        "Created",
        "Modified",
        "Accessed",
        "Attributes",
        "Read-only",
    ];

    private const int LabelWidth = 12;

    public static void Render(ScreenBuffer buffer, int screenWidth, int screenHeight,
        FileSystemEntry entry, string? directorySizeText)
    {
        string[] values = BuildValues(entry, directorySizeText);

        int maxValueLen = 0;
        for (int i = 0; i < values.Length; i++)
        {
            if (values[i].Length > maxValueLen)
            {
                maxValueLen = values[i].Length;
            }
        }

        int contentWidth = LabelWidth + maxValueLen;

        // Clamp to screen width minus some padding
        int maxContentWidth = screenWidth - 8;
        if (contentWidth > maxContentWidth)
        {
            contentWidth = maxContentWidth;
        }

        int contentHeight = Labels.Length;

        var content = DialogBox.Render(
            buffer, screenWidth, screenHeight,
            contentWidth, contentHeight,
            title: "Properties",
            footer: "Press any key to close");

        var labelStyle = new CellStyle(LabelColor, DialogBox.BgColor, Dim: true);
        var valueStyle = new CellStyle(ValueColor, DialogBox.BgColor);

        int valueMaxWidth = contentWidth - LabelWidth;

        for (int i = 0; i < Labels.Length; i++)
        {
            int y = content.Top + i;
            buffer.WriteString(y, content.Left, Labels[i], labelStyle, LabelWidth);
            buffer.WriteString(y, content.Left + LabelWidth, values[i], valueStyle, valueMaxWidth);
        }
    }

    private static string[] BuildValues(FileSystemEntry entry, string? directorySizeText)
    {
        string type = entry.IsDrive
            ? "Drive"
            : entry.IsBrokenSymlink
                ? "Broken Symlink"
                : entry.IsSymlink
                    ? (entry.IsDirectory ? "Symlink \u2192 Directory" : "Symlink \u2192 File")
                    : entry.IsDirectory
                        ? "Directory"
                        : "File";

        string target = entry.LinkTarget ?? "\u2014";

        string size;
        if (entry.IsDirectory || entry.IsDrive)
        {
            size = directorySizeText ?? "\u2014";
        }
        else
        {
            Span<char> sizeBuf = stackalloc char[32];
            int n = FormatHelpers.FormatSize(sizeBuf, entry.Size);
            string formatted = sizeBuf[..n].ToString();
            size = $"{formatted} ({entry.Size:N0} bytes)";
        }

        string created;
        string accessed;
        string attributes;
        bool readOnly;

        try
        {
            if (entry.IsDrive)
            {
                var driveInfo = new DriveInfo(entry.FullPath);
                created = "\u2014";
                accessed = "\u2014";
                attributes = driveInfo.DriveType.ToString();
                readOnly = !driveInfo.IsReady;
            }
            else if (entry.IsDirectory)
            {
                var info = new DirectoryInfo(entry.FullPath);
                created = FormatDateTime(info.CreationTime);
                accessed = FormatDateTime(info.LastAccessTime);
                attributes = FormatAttributes(info.Attributes);
                readOnly = info.Attributes.HasFlag(FileAttributes.ReadOnly);
            }
            else
            {
                var info = new FileInfo(entry.FullPath);
                created = FormatDateTime(info.CreationTime);
                accessed = FormatDateTime(info.LastAccessTime);
                attributes = FormatAttributes(info.Attributes);
                readOnly = info.IsReadOnly;
            }
        }
        catch
        {
            created = "N/A";
            accessed = "N/A";
            attributes = "N/A";
            readOnly = false;
        }

        string modified = FormatDateTime(entry.LastModified);

        return
        [
            entry.Name,
            entry.FullPath,
            type,
            target,
            size,
            created,
            modified,
            accessed,
            attributes,
            readOnly ? "Yes" : "No",
        ];
    }

    private static string FormatDateTime(DateTime dt) =>
        dt.ToString("yyyy-MM-dd hh:mm tt", CultureInfo.InvariantCulture);

    private static string FormatAttributes(FileAttributes attrs)
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return FormatWindowsAttributes(attrs);
        }

        return FormatUnixAttributes(attrs);
    }

    private static string FormatWindowsAttributes(FileAttributes attrs)
    {
        Span<char> buf = stackalloc char[128];
        int pos = 0;

        AppendFlag(ref pos, buf, attrs, FileAttributes.ReadOnly, "ReadOnly");
        AppendFlag(ref pos, buf, attrs, FileAttributes.Hidden, "Hidden");
        AppendFlag(ref pos, buf, attrs, FileAttributes.System, "System");
        AppendFlag(ref pos, buf, attrs, FileAttributes.ReparsePoint, "ReparsePoint");
        AppendFlag(ref pos, buf, attrs, FileAttributes.Archive, "Archive");
        AppendFlag(ref pos, buf, attrs, FileAttributes.Compressed, "Compressed");
        AppendFlag(ref pos, buf, attrs, FileAttributes.Encrypted, "Encrypted");

        return pos > 0 ? buf[..pos].ToString() : "Normal";
    }

    private static string FormatUnixAttributes(FileAttributes attrs)
    {
        // On Unix, also try to show file mode permissions
        Span<char> buf = stackalloc char[128];
        int pos = 0;

        AppendFlag(ref pos, buf, attrs, FileAttributes.ReadOnly, "ReadOnly");
        AppendFlag(ref pos, buf, attrs, FileAttributes.Hidden, "Hidden");

        return pos > 0 ? buf[..pos].ToString() : "Normal";
    }

    private static void AppendFlag(ref int pos, Span<char> buf, FileAttributes attrs, FileAttributes flag, string name)
    {
        if (!attrs.HasFlag(flag))
        {
            return;
        }

        if (pos > 0)
        {
            ", ".AsSpan().CopyTo(buf[pos..]);
            pos += 2;
        }

        name.AsSpan().CopyTo(buf[pos..]);
        pos += name.Length;
    }
}
