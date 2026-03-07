namespace Wade.FileSystem;

internal sealed class DirectoryContents
{
    /// <summary>
    /// Sentinel path representing the list of drives (used as a virtual parent of drive roots).
    /// </summary>
    public const string DrivesPath = "::drives";

    private readonly Dictionary<string, List<FileSystemEntry>> _cache = new(StringComparer.OrdinalIgnoreCase);

    public bool ShowHiddenFiles { get; set; }

    public List<FileSystemEntry> GetEntries(string path)
    {
        if (path == DrivesPath)
            return GetDriveEntries();

        if (_cache.TryGetValue(path, out var cached))
            return cached;

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
            if (!drive.IsReady) continue;
            string name = drive.Name.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            list.Add(new FileSystemEntry(
                name,
                drive.RootDirectory.FullName,
                IsDirectory: true,
                Size: 0,
                LastModified: default,
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
                if (!ShowHiddenFiles &&
                    ((dir.Attributes & FileAttributes.Hidden) != 0 || dir.Name.StartsWith('.')))
                    continue;

                list.Add(new FileSystemEntry(
                    dir.Name,
                    dir.FullName,
                    IsDirectory: true,
                    Size: 0,
                    LastModified: dir.LastWriteTime));
            }

            foreach (var file in dirInfo.EnumerateFiles())
            {
                if (!ShowHiddenFiles &&
                    ((file.Attributes & FileAttributes.Hidden) != 0 || file.Name.StartsWith('.')))
                    continue;

                list.Add(new FileSystemEntry(
                    file.Name,
                    file.FullName,
                    IsDirectory: false,
                    Size: file.Length,
                    LastModified: file.LastWriteTime));
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

        // Directories first, then files, both sorted by name
        list.Sort((a, b) =>
        {
            if (a.IsDirectory != b.IsDirectory)
                return a.IsDirectory ? -1 : 1;
            return string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);
        });

        return list;
    }
}

internal sealed record FileSystemEntry(
    string Name,
    string FullPath,
    bool IsDirectory,
    long Size,
    DateTime LastModified,
    bool IsDrive = false);
