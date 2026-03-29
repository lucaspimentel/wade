namespace Wade.Search;

/// <summary>
/// Precomputed data for an indexed path, used for efficient scoring.
/// </summary>
internal readonly record struct PathEntry(string AbsolutePath, string RelativePath, int FileNameStart);
