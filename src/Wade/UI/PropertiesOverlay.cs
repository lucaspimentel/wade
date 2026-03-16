using System.Globalization;
using System.Runtime.InteropServices;
using Wade.FileSystem;
using Wade.Preview;
using Wade.Terminal;

namespace Wade.UI;

internal static class PropertiesOverlay
{
    private static readonly Color LabelColor = new(120, 120, 140);
    private static readonly Color ValueColor = new(200, 200, 200);
    private static readonly Color GitModifiedColor = new(220, 180, 50);
    private static readonly Color GitStagedColor = new(80, 200, 200);
    private static readonly Color GitUntrackedColor = new(80, 200, 80);
    private static readonly Color GitConflictColor = new(220, 80, 80);

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
        "Git status",
    ];

    private const int LabelWidth = 12;

    public static void Render(ScreenBuffer buffer, int screenWidth, int screenHeight,
        FileSystemEntry entry, string? directorySizeText, GitFileStatus? gitStatus = null,
        MetadataSection[]? metadataSections = null)
    {
        string[] values = BuildValues(entry, directorySizeText, gitStatus);

        int maxValueLen = 0;
        for (int i = 0; i < values.Length; i++)
        {
            if (values[i].Length > maxValueLen)
            {
                maxValueLen = values[i].Length;
            }
        }

        // Compute metadata rows
        int metadataRowCount = 0;
        List<(string Label, string Value)>? metadataRows = null;

        if (metadataSections is { Length: > 0 })
        {
            metadataRows = [];

            foreach (MetadataSection section in metadataSections)
            {
                // Blank line before section
                if (metadataRows.Count > 0)
                {
                    metadataRows.Add(("", ""));
                }

                // Section header
                if (section.Header is not null)
                {
                    metadataRows.Add(("", section.Header));
                }

                foreach (MetadataEntry metaEntry in section.Entries)
                {
                    metadataRows.Add((metaEntry.Label, metaEntry.Value));

                    int rowLen = metaEntry.Label.Length > 0 ? LabelWidth + metaEntry.Value.Length : metaEntry.Value.Length + 2;
                    if (rowLen > maxValueLen + LabelWidth)
                    {
                        maxValueLen = rowLen - LabelWidth;
                    }
                }
            }

            metadataRowCount = 1 + metadataRows.Count; // 1 blank separator + rows
        }

        int contentWidth = LabelWidth + maxValueLen;

        // Clamp to screen width minus some padding
        int maxContentWidth = screenWidth - 8;
        if (contentWidth > maxContentWidth)
        {
            contentWidth = maxContentWidth;
        }

        int contentHeight = Labels.Length + metadataRowCount;

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

        // Overwrite git status row with colored value if applicable
        if (gitStatus is { } s && s != GitFileStatus.None)
        {
            int gitRowIndex = Labels.Length - 1; // last row
            int gitY = content.Top + gitRowIndex;
            Color gitColor = GetGitStatusColor(s);
            var gitValueStyle = new CellStyle(gitColor, DialogBox.BgColor);
            buffer.WriteString(gitY, content.Left + LabelWidth, values[gitRowIndex], gitValueStyle, valueMaxWidth);
        }

        // Render metadata sections below file system properties
        if (metadataRows is { Count: > 0 })
        {
            int metaStartY = content.Top + Labels.Length + 1; // +1 for blank separator
            var headerStyle = new CellStyle(new Color(180, 180, 200), DialogBox.BgColor, Bold: true);

            for (int i = 0; i < metadataRows.Count; i++)
            {
                int y = metaStartY + i;
                (string label, string value) = metadataRows[i];

                if (label.Length == 0 && value.Length == 0)
                {
                    // Blank separator row
                    continue;
                }

                if (label.Length == 0 && metadataRows.Count > i + 1 && (i == 0 || metadataRows[i - 1] is { Label: "", Value: "" }))
                {
                    // Section header
                    buffer.WriteString(y, content.Left, value, headerStyle, contentWidth);
                }
                else if (label.Length > 0)
                {
                    buffer.WriteString(y, content.Left, label, labelStyle, LabelWidth);
                    buffer.WriteString(y, content.Left + LabelWidth, value, valueStyle, valueMaxWidth);
                }
                else
                {
                    // List item
                    buffer.WriteString(y, content.Left + 2, value, valueStyle, valueMaxWidth);
                }
            }
        }
    }

    private static string[] BuildValues(FileSystemEntry entry, string? directorySizeText, GitFileStatus? gitStatus)
    {
        string type = entry.IsDrive
            ? "Drive"
            : entry.IsBrokenSymlink
                ? "Broken Symlink"
                : entry.IsSymlink
                    ? (entry.IsDirectory ? "Symlink \u2192 Directory" : "Symlink \u2192 File")
                    : entry.IsDirectory
                        ? "Directory"
                        : FilePreview.GetFileTypeLabel(entry.FullPath) ?? "File";

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

        if (entry.IsCloudPlaceholder)
        {
            attributes = attributes == "Normal"
                ? "Cloud file"
                : $"Cloud file, {attributes}";
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
            FormatGitStatus(gitStatus),
        ];
    }

    internal static string FormatGitStatus(GitFileStatus? status)
    {
        if (status is null or GitFileStatus.None)
        {
            return "\u2014";
        }

        GitFileStatus s = status.Value;
        Span<char> buf = stackalloc char[64];
        int pos = 0;

        if (s.HasFlag(GitFileStatus.Conflict))
        {
            AppendLabel(ref pos, buf, "Conflict");
        }

        if (s.HasFlag(GitFileStatus.Staged))
        {
            AppendLabel(ref pos, buf, "Staged");
        }

        if (s.HasFlag(GitFileStatus.Modified))
        {
            AppendLabel(ref pos, buf, "Modified");
        }

        if (s.HasFlag(GitFileStatus.Untracked))
        {
            AppendLabel(ref pos, buf, "Untracked");
        }

        return pos > 0 ? buf[..pos].ToString() : "\u2014";
    }

    private static Color GetGitStatusColor(GitFileStatus status)
    {
        if (status.HasFlag(GitFileStatus.Conflict))
        {
            return GitConflictColor;
        }

        if (status.HasFlag(GitFileStatus.Staged))
        {
            return GitStagedColor;
        }

        if (status.HasFlag(GitFileStatus.Modified))
        {
            return GitModifiedColor;
        }

        return GitUntrackedColor;
    }

    private static void AppendLabel(ref int pos, Span<char> buf, string label)
    {
        if (pos > 0)
        {
            ", ".AsSpan().CopyTo(buf[pos..]);
            pos += 2;
        }

        label.AsSpan().CopyTo(buf[pos..]);
        pos += label.Length;
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
