namespace Wade.Search;

/// <summary>
/// Configuration options for a search query.
/// </summary>
public sealed class SearchOptions
{
    /// <summary>
    /// Maximum Damerau-Levenshtein edit distance for fuzzy matches. Default: 2.
    /// </summary>
    public int MaxEditDistance { get; init; } = 2;

    /// <summary>
    /// Maximum number of results to stream. Default: int.MaxValue (no limit).
    /// </summary>
    public int MaxResults { get; init; } = int.MaxValue;
}
