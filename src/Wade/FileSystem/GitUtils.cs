namespace Wade.FileSystem;

internal static class GitUtils
{
    /// <summary>
    /// Walks up from <paramref name="path"/> looking for a directory containing a .git folder.
    /// Returns the repo root path, or null if not inside a git repository.
    /// </summary>
    public static string? FindRepoRoot(string path)
    {
        DirectoryInfo? dir = Directory.Exists(path)
            ? new DirectoryInfo(path)
            : new DirectoryInfo(Path.GetDirectoryName(path) ?? path);

        while (dir is not null)
        {
            string gitPath = Path.Combine(dir.FullName, ".git");

            if (Directory.Exists(gitPath) || File.Exists(gitPath))
            {
                return dir.FullName;
            }

            dir = dir.Parent;
        }

        return null;
    }
}
