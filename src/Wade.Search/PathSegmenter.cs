namespace Wade.Search;

internal static class PathSegmenter
{
    private static readonly char[] s_separators = Path.DirectorySeparatorChar == Path.AltDirectorySeparatorChar
        ? [Path.DirectorySeparatorChar]
        : [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar];

    /// <summary>
    /// Split a path into non-empty segments on directory separator characters.
    /// </summary>
    internal static string[] Split(string path)
    {
        return path.Split(s_separators, StringSplitOptions.RemoveEmptyEntries);
    }
}
