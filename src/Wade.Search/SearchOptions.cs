namespace Wade.Search;

/// <summary>
/// Configuration options for a search query.
/// </summary>
public sealed class SearchOptions
{
    /// <summary>
    /// Maximum number of results to stream. Default: int.MaxValue (no limit).
    /// </summary>
    public int MaxResults { get; init; } = int.MaxValue;
}
