namespace Wade.FileSystem;

internal enum SortMode { Name, Modified, Size, Extension }

internal sealed class DirectoryContents
{
    /// <summary>
    /// Sentinel path representing the list of drives (used as a virtual parent of drive roots).
    /// </summary>
    public const string DrivesPath = "::drives";

    private readonly Dictionary<string, List<FileSystemEntry>> _cache = new(StringComparer.OrdinalIgnoreCase);

    public bool ShowHiddenFiles { get; set; }
    public SortMode SortMode { get; set; } = SortMode.Name;
    public bool SortAscending { get; set; } = true;

    public List<FileSystemEntry> GetEntries(string path)
    {
        if (path == DrivesPath)
        {
            return GetDriveEntries();
        }

        if (_cache.TryGetValue(path, out var cached))
        {
            return cached;
        }

        var entries = LoadEntries(path);
        _cache[path] = entries;
        return entries;
    }

    public static bool IsDriveRoot(string path)
    {
        return Path.GetPathRoot(path) is { } root
               && string.Equals(Path.GetFullPath(path), Path.GetFullPath(root), StringComparison.OrdinalIgnoreCase);
    }

    private static List<FileSystemEntry> GetDriveEntries()
    {
        var list = new List<FileSystemEntry>();
        foreach (var drive in DriveInfo.GetDrives())
        {
            if (!drive.IsReady)
            {
                continue;
            }

            string name = drive.Name.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            list.Add(new FileSystemEntry(
                name,
                PathCompletion.CapitalizeDriveLetter(drive.RootDirectory.FullName),
                IsDirectory: true,
                Size: 0,
                LastModified: default,
                LinkTarget: null,
                IsDrive: true));
        }

        return list;
    }

    public void Invalidate(string path)
    {
        _cache.Remove(path);
    }

    public void InvalidateAll()
    {
        _cache.Clear();
    }

    internal List<FileSystemEntry> LoadEntries(string path)
    {
        var list = new List<FileSystemEntry>();

        try
        {
            var dirInfo = new DirectoryInfo(path);

            foreach (var dir in dirInfo.EnumerateDirectories())
            {
                // Always hide system+hidden entries on Windows (e.g. $Recycle.Bin)
                if (OperatingSystem.IsWindows() &&
                    (dir.Attributes & (FileAttributes.System | FileAttributes.Hidden)) == (FileAttributes.System | FileAttributes.Hidden))
                {
                    continue;
                }

                if (!ShowHiddenFiles &&
                    ((dir.Attributes & FileAttributes.Hidden) != 0 || dir.Name.StartsWith('.')))
                {
                    continue;
                }

                list.Add(new FileSystemEntry(
                    dir.Name,
                    dir.FullName,
                    IsDirectory: true,
                    Size: 0,
                    LastModified: dir.LastWriteTime,
                    LinkTarget: dir.LinkTarget,
                    IsDrive: false));
            }

            foreach (var file in dirInfo.EnumerateFiles())
            {
                // Always hide system+hidden entries on Windows (e.g. $Recycle.Bin)
                if (OperatingSystem.IsWindows() &&
                    (file.Attributes & (FileAttributes.System | FileAttributes.Hidden)) == (FileAttributes.System | FileAttributes.Hidden))
                {
                    continue;
                }

                if (!ShowHiddenFiles &&
                    ((file.Attributes & FileAttributes.Hidden) != 0 || file.Name.StartsWith('.')))
                {
                    continue;
                }

                list.Add(new FileSystemEntry(
                    file.Name,
                    file.FullName,
                    IsDirectory: false,
                    Size: file.Length,
                    LastModified: file.LastWriteTime,
                    LinkTarget: file.LinkTarget,
                    IsDrive: false));
            }
        }
        catch (UnauthorizedAccessException)
        {
            // Silently skip inaccessible directories
        }
        catch (IOException)
        {
            // Silently skip I/O errors
        }

        SortEntries(list);

        return list;
    }

    private void SortEntries(List<FileSystemEntry> list)
    {
        list.Sort((a, b) =>
        {
            if (a.IsDirectory != b.IsDirectory)
            {
                return a.IsDirectory ? -1 : 1;
            }

            int cmp = SortMode switch
            {
                SortMode.Modified => a.LastModified.CompareTo(b.LastModified),
                SortMode.Size => a.Size.CompareTo(b.Size),
                SortMode.Extension => CompareExtension(a.Name, b.Name),
                _ => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase),
            };

            return SortAscending ? cmp : -cmp;
        });
    }

    private static int CompareExtension(string nameA, string nameB)
    {
        int cmp = string.Compare(
            Path.GetExtension(nameA), Path.GetExtension(nameB),
            StringComparison.OrdinalIgnoreCase);
        return cmp != 0 ? cmp : string.Compare(nameA, nameB, StringComparison.OrdinalIgnoreCase);
    }
}

internal sealed record FileSystemEntry(
    string Name,
    string FullPath,
    bool IsDirectory,
    long Size,
    DateTime LastModified,
    string? LinkTarget,
    bool IsDrive)
{
    public bool IsSymlink => LinkTarget != null;

    public bool IsBrokenSymlink
    {
        get
        {
            if (LinkTarget == null)
            {
                return false;
            }

            string resolvedTarget = Path.IsPathFullyQualified(LinkTarget)
                ? LinkTarget
                : Path.GetFullPath(LinkTarget, Path.GetDirectoryName(FullPath)!);

            return !Path.Exists(resolvedTarget);
        }
    }
}
