namespace Wade.Search;

/// <summary>
/// A search result from the index, carrying the matched path, best edit distance,
/// and whether the match was a prefix match.
/// </summary>
public sealed record SearchResult(string Path, int EditDistance, bool IsPrefixMatch)
{
    /// <summary>
    /// Composite ranking score: lower is better. Prefix matches get score 0;
    /// fuzzy matches get their edit distance.
    /// </summary>
    public int Score => IsPrefixMatch ? 0 : EditDistance;
}
