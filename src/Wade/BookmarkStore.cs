using System.Runtime.InteropServices;

namespace Wade;

internal sealed class BookmarkStore
{
    private static readonly StringComparison PathComparison =
        RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;

    private readonly string _filePath;
    private readonly List<string> _bookmarks = [];

    public IReadOnlyList<string> Bookmarks => _bookmarks;

    public BookmarkStore(string? filePath = null)
    {
        _filePath = filePath
                    ?? Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                        ".config", "wade", "bookmarks");
    }

    public void Load()
    {
        _bookmarks.Clear();

        if (!File.Exists(_filePath))
        {
            return;
        }

        foreach (string line in File.ReadAllLines(_filePath))
        {
            string trimmed = line.Trim();

            if (trimmed.Length == 0 || trimmed[0] == '#')
            {
                continue;
            }

            _bookmarks.Add(trimmed);
        }
    }

    public void Save()
    {
        string? dir = Path.GetDirectoryName(_filePath);

        if (dir is not null && !Directory.Exists(dir))
        {
            Directory.CreateDirectory(dir);
        }

        File.WriteAllLines(_filePath, _bookmarks);
    }

    public bool Contains(string path) =>
        _bookmarks.Any(b => b.Equals(path, PathComparison));

    public void Add(string path)
    {
        // Remove existing duplicate (MRU: move to top)
        _bookmarks.RemoveAll(b => b.Equals(path, PathComparison));
        _bookmarks.Insert(0, path);
        Save();
    }

    public void Remove(string path)
    {
        _bookmarks.RemoveAll(b => b.Equals(path, PathComparison));
        Save();
    }

    public void Toggle(string path)
    {
        if (Contains(path))
        {
            Remove(path);
        }
        else
        {
            Add(path);
        }
    }
}
