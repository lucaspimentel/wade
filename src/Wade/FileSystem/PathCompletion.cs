namespace Wade.FileSystem;

internal static class PathCompletion
{
    /// <summary>
    /// Expands a leading <c>~</c> to the user's home directory.
    /// </summary>
    public static string ExpandTilde(string path)
    {
        if (path.StartsWith('~'))
        {
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            return Path.Join(home, path.AsSpan(1));
        }

        return path;
    }

    /// <summary>
    /// Normalizes path separators to the platform's preferred separator.
    /// On Windows this converts <c>/</c> to <c>\</c>; on Unix it's a no-op.
    /// </summary>
    public static string NormalizeSeparators(string path)
    {
        if (Path.DirectorySeparatorChar == '\\' && path.Contains('/'))
            return path.Replace('/', '\\');

        return path;
    }

    /// <summary>
    /// Given a partial path input, returns the first matching filesystem entry's full path,
    /// or null if no match is found.
    /// When <paramref name="showHidden"/> is false, hidden entries (dot-prefixed or
    /// <see cref="FileAttributes.Hidden"/>) are excluded unless the user is already
    /// typing a dot-prefixed name.
    /// </summary>
    public static string? GetSuggestion(string input, bool showHidden = true)
    {
        if (string.IsNullOrEmpty(input))
            return null;

        try
        {
            input = NormalizeSeparators(ExpandTilde(input));
            // If input ends with a separator and directory exists, suggest first child
            if (input[^1] == Path.DirectorySeparatorChar || input[^1] == Path.AltDirectorySeparatorChar)
            {
                if (Directory.Exists(input))
                    return FirstEntry(input, showHidden);

                return null;
            }

            // Split into parent + partial name
            string? parentDir = Path.GetDirectoryName(input);
            if (parentDir is null || !Directory.Exists(parentDir))
                return null;

            string partial = Path.GetFileName(input);
            if (string.IsNullOrEmpty(partial))
                return null;

            // If the user is typing a dot-prefixed name, don't filter hidden entries
            bool skipHidden = !showHidden && !partial.StartsWith('.');

            // Find first matching entry
            foreach (var entry in new DirectoryInfo(parentDir).EnumerateFileSystemInfos())
            {
                if (skipHidden && IsHidden(entry))
                    continue;

                if (entry.Name.StartsWith(partial, StringComparison.OrdinalIgnoreCase))
                    return entry.FullName;
            }
        }
        catch
        {
            // Silently ignore filesystem errors during completion
        }

        return null;
    }

    private static string? FirstEntry(string dirPath, bool showHidden)
    {
        try
        {
            foreach (var entry in new DirectoryInfo(dirPath).EnumerateFileSystemInfos())
            {
                if (!showHidden && IsHidden(entry))
                    continue;

                return entry.FullName;
            }
        }
        catch
        {
            // Silently ignore filesystem errors
        }

        return null;
    }

    private static bool IsHidden(FileSystemInfo entry) =>
        entry.Name.StartsWith('.') || (entry.Attributes & FileAttributes.Hidden) != 0;
}
