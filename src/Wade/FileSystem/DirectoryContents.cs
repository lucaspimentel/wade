namespace Wade.FileSystem;

internal enum SortMode
{
    Name,
    Modified,
    Size,
    Extension,
}

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

    public Dictionary<string, long>? DirSizes { get; set; }

    public List<FileSystemEntry> GetEntries(string path)
    {
        if (path == DrivesPath)
        {
            return GetDriveEntries();
        }

        if (_cache.TryGetValue(path, out List<FileSystemEntry>? cached))
        {
            return cached;
        }

        List<FileSystemEntry> entries = LoadEntries(path);
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
        foreach (DriveInfo drive in DriveInfo.GetDrives())
        {
            if (!drive.IsReady)
            {
                continue;
            }

            string name = drive.Name.Length > 1
                ? drive.Name.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                : drive.Name;
            list.Add(new FileSystemEntry(
                name,
                PathCompletion.CapitalizeDriveLetter(drive.RootDirectory.FullName),
                IsDirectory: true,
                Size: 0,
                LastModified: default,
                LinkTarget: null,
                IsBrokenSymlink: false,
                IsDrive: true,
                DriveMediaType: DriveTypeDetector.Detect(drive),
                DriveFormat: drive.DriveFormat,
                DriveLabel: string.IsNullOrEmpty(drive.VolumeLabel) ? null : drive.VolumeLabel,
                DriveFreeSpace: drive.TotalFreeSpace,
                DriveTotalSize: drive.TotalSize));
        }

        return list;
    }

    public void Invalidate(string path) => _cache.Remove(path);

    public void InvalidateAll() => _cache.Clear();

    internal List<FileSystemEntry> LoadEntries(string path)
    {
        var list = new List<FileSystemEntry>();

        try
        {
            var dirInfo = new DirectoryInfo(path);

            foreach (DirectoryInfo dir in dirInfo.EnumerateDirectories())
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
                    IsCloudPlaceholder: CheckIsCloudPlaceholder(dir),
                    IsJunctionPoint: CheckIsJunctionPoint(dir)));
            }

            foreach (FileInfo file in dirInfo.EnumerateFiles())
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

                bool isAppExecLink = CheckIsAppExecLink(file);

                list.Add(new FileSystemEntry(
                    file.Name,
                    file.FullName,
                    IsDirectory: false,
                    Size: file.Length,
                    LastModified: file.LastWriteTime,
                    LinkTarget: file.LinkTarget,
                    IsBrokenSymlink: CheckBrokenSymlink(file),
                    IsDrive: false,
                    IsCloudPlaceholder: CheckIsCloudPlaceholder(file),
                    IsAppExecLink: isAppExecLink,
                    AppExecLinkTarget: isAppExecLink ? ReparsePointDetector.GetAppExecLinkTarget(file.FullName) : null));
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
                SortMode.Size => GetEffectiveSize(a).CompareTo(GetEffectiveSize(b)),
                SortMode.Extension => CompareExtension(a.Name, b.Name),
                _ => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase),
            };

            return SortAscending ? cmp : -cmp;
        });
    }

    private long GetEffectiveSize(FileSystemEntry entry)
    {
        if (entry.IsDirectory && DirSizes != null && DirSizes.TryGetValue(entry.FullPath, out long dirSize))
        {
            return dirSize;
        }

        return entry.Size;
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

    private static bool CheckIsAppExecLink(FileInfo file)
    {
        if (!file.Attributes.HasFlag(FileAttributes.ReparsePoint))
        {
            return false;
        }

        return ReparsePointDetector.IsAppExecLink(file.FullName);
    }

    private static bool CheckIsJunctionPoint(DirectoryInfo dir)
    {
        if (!dir.Attributes.HasFlag(FileAttributes.ReparsePoint))
        {
            return false;
        }

        return ReparsePointDetector.IsJunctionPoint(dir.FullName);
    }

    private static bool CheckBrokenSymlink(FileSystemInfo info)
    {
        if (info.LinkTarget == null)
        {
            return false;
        }

        try
        {
            FileSystemInfo? target = info.ResolveLinkTarget(returnFinalTarget: true);
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
    bool IsCloudPlaceholder = false,
    bool IsJunctionPoint = false,
    bool IsAppExecLink = false,
    string? AppExecLinkTarget = null,
    DriveMediaType DriveMediaType = DriveMediaType.Unknown,
    string? DriveFormat = null,
    string? DriveLabel = null,
    long DriveFreeSpace = 0,
    long DriveTotalSize = 0)
{
    public bool IsSymlink => LinkTarget != null;
}
