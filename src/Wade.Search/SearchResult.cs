namespace Wade.Search;

/// <summary>
/// A search result from the index, carrying the matched path and its score.
/// Higher scores indicate better matches.
/// </summary>
public sealed record SearchResult(string Path, int Score, int[] MatchPositions);
