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
    public bool ShowSystemFiles { get; set; }
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
                IsBrokenSymlink: false,
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
                // Hide system entries on Windows (e.g. $Recycle.Bin) unless ShowSystemFiles is enabled
                if (OperatingSystem.IsWindows() && !ShowSystemFiles &&
                    (dir.Attributes & FileAttributes.System) != 0)
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
                    IsBrokenSymlink: CheckBrokenSymlink(dir),
                    IsDrive: false,
                    IsCloudPlaceholder: CheckIsCloudPlaceholder(dir)));
            }

            foreach (var file in dirInfo.EnumerateFiles())
            {
                // Hide system entries on Windows (e.g. $Recycle.Bin) unless ShowSystemFiles is enabled
                if (OperatingSystem.IsWindows() && !ShowSystemFiles &&
                    (file.Attributes & FileAttributes.System) != 0)
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
                    IsBrokenSymlink: CheckBrokenSymlink(file),
                    IsDrive: false,
                    IsCloudPlaceholder: CheckIsCloudPlaceholder(file)));
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

    private static bool CheckIsCloudPlaceholder(FileSystemInfo info)
    {
        if (!OperatingSystem.IsWindows())
        {
            return false;
        }

        return IsCloudPlaceholderAttributes((int)info.Attributes);
    }

    internal static bool IsCloudPlaceholderAttributes(int attributeBits)
    {
        const int RecallOnDataAccess = 0x00400000;
        const int RecallOnOpen = 0x00004000;
        return (attributeBits & (RecallOnDataAccess | RecallOnOpen)) != 0;
    }

    private static bool CheckBrokenSymlink(FileSystemInfo info)
    {
        if (info.LinkTarget == null)
        {
            return false;
        }

        try
        {
            var target = info.ResolveLinkTarget(returnFinalTarget: true);
            return target == null || !Path.Exists(target.FullName);
        }
        catch
        {
            return true;
        }
    }
}

internal sealed record FileSystemEntry(
    string Name,
    string FullPath,
    bool IsDirectory,
    long Size,
    DateTime LastModified,
    string? LinkTarget,
    bool IsBrokenSymlink,
    bool IsDrive,
    bool IsCloudPlaceholder = false)
{
    public bool IsSymlink => LinkTarget != null;
}
