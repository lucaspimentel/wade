using System.Globalization;
using System.Runtime.InteropServices;
using Wade.FileSystem;
using Wade.Preview;
using Wade.Terminal;

namespace Wade.UI;

internal static class PropertiesOverlay
{
    private const int LabelWidth = 12;
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

    /// <summary>
    /// Renders the properties overlay with scroll support. Returns the total content row count.
    /// </summary>
    public static int Render(ScreenBuffer buffer, int screenWidth, int screenHeight,
        FileSystemEntry entry, string? directorySizeText, GitFileStatus? gitStatus = null,
        MetadataSection[]? metadataSections = null, int scrollOffset = 0)
    {
        string[] values = BuildValues(entry, directorySizeText, gitStatus);

        // Build flat list of all renderable rows
        var rows = new List<Row>();

        // System property rows
        var labelStyle = new CellStyle(LabelColor, DialogBox.BgColor, Dim: true);
        var valueStyle = new CellStyle(ValueColor, DialogBox.BgColor);

        for (int i = 0; i < Labels.Length; i++)
        {
            CellStyle rowValueStyle = valueStyle;

            // Git status row gets colored
            if (i == Labels.Length - 1 && gitStatus is { } s && s != GitFileStatus.None)
            {
                rowValueStyle = new CellStyle(GetGitStatusColor(s), DialogBox.BgColor);
            }

            rows.Add(new Row(RowKind.LabelValue, Labels[i], values[i], labelStyle, rowValueStyle));
        }

        // Metadata rows
        int maxValueLen = 0;
        for (int i = 0; i < values.Length; i++)
        {
            if (values[i].Length > maxValueLen)
            {
                maxValueLen = values[i].Length;
            }
        }

        if (metadataSections is { Length: > 0 })
        {
            var headerStyle = new CellStyle(new Color(180, 180, 200), DialogBox.BgColor, Bold: true);

            // Blank separator between system props and metadata
            rows.Add(new Row(RowKind.Blank, "", "", labelStyle, valueStyle));

            foreach (MetadataSection section in metadataSections)
            {
                // Blank line before section (except first metadata section, already has separator)
                if (rows[^1].Kind != RowKind.Blank)
                {
                    rows.Add(new Row(RowKind.Blank, "", "", labelStyle, valueStyle));
                }

                if (section.Header is not null)
                {
                    rows.Add(new Row(RowKind.Header, "", section.Header, headerStyle, headerStyle));
                }

                foreach (MetadataEntry metaEntry in section.Entries)
                {
                    rows.Add(new Row(RowKind.LabelValue, metaEntry.Label, metaEntry.Value, labelStyle, valueStyle));

                    int rowLen = metaEntry.Label.Length > 0 ? LabelWidth + metaEntry.Value.Length : metaEntry.Value.Length + 2;
                    if (rowLen > maxValueLen + LabelWidth)
                    {
                        maxValueLen = rowLen - LabelWidth;
                    }
                }
            }
        }

        int totalContentHeight = rows.Count;

        // Compute content width
        int contentWidth = LabelWidth + maxValueLen;
        int maxContentWidth = screenWidth - 8;
        if (contentWidth > maxContentWidth)
        {
            contentWidth = maxContentWidth;
        }

        // Compute visible height — leave room for borders, title, footer
        // DialogBox uses: top border(1) + title(1) + separator(1) + content + blank(1) + footer(1) + bottom border(1) = content + 6
        int maxVisibleHeight = Math.Max(1, screenHeight - 8);
        bool scrollable = totalContentHeight > maxVisibleHeight;
        int visibleHeight = scrollable ? maxVisibleHeight : totalContentHeight;

        // Clamp scroll offset
        int maxScroll = Math.Max(0, totalContentHeight - visibleHeight);
        scrollOffset = Math.Clamp(scrollOffset, 0, maxScroll);

        string footer = scrollable
            ? "\u2191\u2193 scroll \u00b7 any key to close"
            : "Press any key to close";

        Rect content = DialogBox.Render(
            buffer, screenWidth, screenHeight,
            contentWidth, visibleHeight,
            title: "Properties",
            footer: footer);

        int valueMaxWidth = contentWidth - LabelWidth;

        // Render visible slice
        for (int i = 0; i < visibleHeight; i++)
        {
            int rowIndex = scrollOffset + i;
            if (rowIndex >= rows.Count)
            {
                break;
            }

            Row row = rows[rowIndex];
            int y = content.Top + i;

            switch (row.Kind)
            {
                case RowKind.Blank:
                    break;

                case RowKind.Header:
                    buffer.WriteString(y, content.Left, row.Value, row.ValueStyle, contentWidth);
                    break;

                case RowKind.LabelValue when row.Label.Length > 0:
                    buffer.WriteString(y, content.Left, row.Label, row.LabelStyle, LabelWidth);
                    buffer.WriteString(y, content.Left + LabelWidth, row.Value, row.ValueStyle, valueMaxWidth);
                    break;

                default:
                    // List item (label is empty but has value)
                    buffer.WriteString(y, content.Left + 2, row.Value, row.ValueStyle, valueMaxWidth);
                    break;
            }
        }

        return totalContentHeight;
    }

    private static string[] BuildValues(FileSystemEntry entry, string? directorySizeText, GitFileStatus? gitStatus)
    {
        string type = entry.IsDrive
            ? "Drive"
            : entry.IsBrokenSymlink
                ? "Broken Symlink"
                : entry.IsAppExecLink
                    ? "App Execution Alias"
                    : entry.IsJunctionPoint
                        ? "Junction \u2192 Directory"
                        : entry.IsSymlink
                            ? entry.IsDirectory ? "Symlink \u2192 Directory" : "Symlink \u2192 File"
                            : entry.IsDirectory
                                ? "Directory"
                                : FilePreview.GetFileTypeLabel(entry.FullPath) ?? "File";

        string target = entry.AppExecLinkTarget ?? entry.LinkTarget ?? "\u2014";

        string size;
        if (entry.IsDrive && entry.DriveTotalSize > 0)
        {
            Span<char> freeBuf = stackalloc char[32];
            Span<char> totalBuf = stackalloc char[32];
            int freeLen = FormatHelpers.FormatSize(freeBuf, entry.DriveFreeSpace);
            int totalLen = FormatHelpers.FormatSize(totalBuf, entry.DriveTotalSize);
            double usedPercent = 100.0 * (entry.DriveTotalSize - entry.DriveFreeSpace) / entry.DriveTotalSize;
            size = $"{freeBuf[..freeLen]} free of {totalBuf[..totalLen]} ({usedPercent:F0}% used)";
        }
        else if (entry.IsDirectory || entry.IsDrive)
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

                string mediaType = entry.DriveMediaType switch
                {
                    DriveMediaType.Ssd => "SSD",
                    DriveMediaType.Hdd => "HDD",
                    _ => driveInfo.DriveType.ToString(),
                };

                // Combine media type, file system, and volume label
                var parts = new List<string> { mediaType };
                if (entry.DriveFormat != null)
                {
                    parts.Add(entry.DriveFormat);
                }

                string volumeLabel = driveInfo.VolumeLabel;
                if (!string.IsNullOrEmpty(volumeLabel))
                {
                    parts.Add($"\"{volumeLabel}\"");
                }

                attributes = string.Join(", ", parts);
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

        if (entry.IsAppExecLink)
        {
            attributes = attributes.Replace("ReparsePoint", "AppExecLink");
        }
        else if (entry.IsJunctionPoint)
        {
            attributes = attributes.Replace("ReparsePoint", "Junction");
        }
        else if (entry.IsSymlink)
        {
            attributes = attributes.Replace("ReparsePoint", "Symlink");
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

    private enum RowKind
    {
        Blank,
        Header,
        LabelValue,
    }

    private readonly record struct Row(RowKind Kind, string Label, string Value, CellStyle LabelStyle, CellStyle ValueStyle);
}
