using System.Collections.Concurrent;
using System.Threading.Channels;

namespace Wade.Search;

/// <summary>
/// A thread-safe, segment-based search index for file paths.
/// Supports prefix search and fuzzy (Damerau-Levenshtein) matching with ranked results
/// streamed via <see cref="System.Threading.Channels.Channel{T}"/>.
/// </summary>
public sealed class SearchIndex
{
    // segment (case-insensitive) → set of full paths containing that segment
    private readonly ConcurrentDictionary<string, ConcurrentDictionary<string, byte>> _segmentToPaths
        = new(StringComparer.OrdinalIgnoreCase);

    // Sorted segments for O(log n) prefix range lookup
    private readonly SortedList<string, byte> _sortedSegments = new(StringComparer.OrdinalIgnoreCase);
    private readonly ReaderWriterLockSlim _sortedLock = new();

    // All indexed paths (for dedup)
    private readonly ConcurrentDictionary<string, byte> _allPaths = new(StringComparer.OrdinalIgnoreCase);

    // Per-path segment cache so we don't re-split during search
    private readonly ConcurrentDictionary<string, string[]> _pathSegments = new(StringComparer.OrdinalIgnoreCase);

    // Active query state
    private readonly object _queryLock = new();
    private ActiveQuery? _activeQuery;

    /// <summary>
    /// Number of distinct paths in the index.
    /// </summary>
    public int Count => _allPaths.Count;

    /// <summary>
    /// Add a path to the index. Thread-safe: may be called concurrently with
    /// <see cref="Search"/> and other <see cref="Add"/> calls. If an active query
    /// exists, checks the new path against it and pushes matching results to the
    /// live result channel immediately.
    /// </summary>
    public void Add(string path)
    {
        if (!_allPaths.TryAdd(path, 0))
        {
            return; // Already indexed.
        }

        string[] segments = PathSegmenter.Split(path);
        _pathSegments[path] = segments;

        foreach (string segment in segments)
        {
            ConcurrentDictionary<string, byte> pathSet = _segmentToPaths.GetOrAdd(
                segment,
                static _ => new ConcurrentDictionary<string, byte>(StringComparer.OrdinalIgnoreCase));
            pathSet.TryAdd(path, 0);

            // Add to sorted structure for prefix range lookups.
            _sortedLock.EnterWriteLock();
            try
            {
                string lower = segment.ToLowerInvariant();
                if (!_sortedSegments.ContainsKey(lower))
                {
                    _sortedSegments.Add(lower, 0);
                }
            }
            finally
            {
                _sortedLock.ExitWriteLock();
            }
        }

        // Live push: if there's an active query, check this new path immediately.
        lock (_queryLock)
        {
            _activeQuery?.TryMatch(path, segments);
        }
    }

    /// <summary>
    /// Begin a new search query. Cancels any previous active query.
    /// Returns a <see cref="ChannelReader{T}"/> that streams <see cref="SearchResult"/>
    /// items. The channel completes when <see cref="CancelSearch"/> is called or a new
    /// <see cref="Search"/> call replaces the active query.
    /// </summary>
    public ChannelReader<SearchResult> Search(string query, SearchOptions? options = null)
    {
        // Empty query: return an immediately-completed empty channel.
        if (string.IsNullOrEmpty(query))
        {
            var emptyChannel = Channel.CreateUnbounded<SearchResult>();
            emptyChannel.Writer.Complete();
            return emptyChannel.Reader;
        }

        options ??= new SearchOptions();
        var newQuery = new ActiveQuery(query, options);

        ActiveQuery? previousQuery;
        lock (_queryLock)
        {
            previousQuery = _activeQuery;
            _activeQuery = newQuery;
        }

        // Cancel and dispose the previous query outside the lock.
        if (previousQuery != null)
        {
            previousQuery.Complete();
            previousQuery.Dispose();
        }

        // Kick off background scan of existing index.
        _ = Task.Run(() => ScanExistingIndex(newQuery));

        return newQuery.Reader;
    }

    /// <summary>
    /// Cancel the current active query and complete its channel.
    /// No-op if no query is active.
    /// </summary>
    public void CancelSearch()
    {
        ActiveQuery? query;
        lock (_queryLock)
        {
            query = _activeQuery;
            _activeQuery = null;
        }

        if (query != null)
        {
            query.Complete();
            query.Dispose();
        }
    }

    /// <summary>
    /// Remove all entries from the index.
    /// </summary>
    public void Clear()
    {
        CancelSearch();
        _allPaths.Clear();
        _pathSegments.Clear();
        _segmentToPaths.Clear();

        _sortedLock.EnterWriteLock();
        try
        {
            _sortedSegments.Clear();
        }
        finally
        {
            _sortedLock.ExitWriteLock();
        }
    }

    /// <summary>
    /// Scan all existing indexed paths against the given query.
    /// Uses the sorted segment structure for efficient prefix range lookup,
    /// then falls back to fuzzy matching on all segments for remaining paths.
    /// </summary>
    private void ScanExistingIndex(ActiveQuery query)
    {
        CancellationToken ct = query.CancellationToken;

        try
        {
            // Phase 1: Find prefix-matching paths via sorted structure.
            HashSet<string> prefixMatchedPaths = FindPrefixMatchPaths(query.Query, ct);

            // Emit prefix matches first (they have the best score).
            foreach (string path in prefixMatchedPaths)
            {
                if (ct.IsCancellationRequested)
                {
                    return;
                }

                if (_pathSegments.TryGetValue(path, out string[]? segments))
                {
                    query.TryMatch(path, segments);
                }
            }

            // Phase 2: Fuzzy scan all remaining paths.
            foreach (string path in _allPaths.Keys)
            {
                if (ct.IsCancellationRequested)
                {
                    return;
                }

                if (prefixMatchedPaths.Contains(path))
                {
                    continue; // Already emitted in phase 1.
                }

                if (_pathSegments.TryGetValue(path, out string[]? segments))
                {
                    query.TryMatch(path, segments);
                }
            }

            query.MarkSnapshotComplete();
        }
        catch (OperationCanceledException)
        {
            // Expected on cancellation.
        }
    }

    /// <summary>
    /// Use the sorted segment structure to find all paths that have at least one
    /// segment starting with the query string (prefix match). O(log n + k) where
    /// k is the number of matching segments.
    /// </summary>
    private HashSet<string> FindPrefixMatchPaths(string query, CancellationToken ct)
    {
        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        string queryLower = query.ToLowerInvariant();

        _sortedLock.EnterReadLock();
        try
        {
            IList<string> keys = _sortedSegments.Keys;
            int startIndex = BinarySearchFirstPrefix(keys, queryLower);

            if (startIndex < 0)
            {
                return result;
            }

            // Walk forward through segments that start with the query.
            for (int i = startIndex; i < keys.Count; i++)
            {
                if (ct.IsCancellationRequested)
                {
                    break;
                }

                string segment = keys[i];
                if (!segment.StartsWith(queryLower, StringComparison.OrdinalIgnoreCase))
                {
                    break; // Past the prefix range.
                }

                // Look up all paths containing this segment.
                if (_segmentToPaths.TryGetValue(segment, out ConcurrentDictionary<string, byte>? pathSet))
                {
                    foreach (string path in pathSet.Keys)
                    {
                        result.Add(path);
                    }
                }
            }
        }
        finally
        {
            _sortedLock.ExitReadLock();
        }

        return result;
    }

    /// <summary>
    /// Binary search for the first key in the sorted list that starts with the given prefix.
    /// Returns the index or -1 if no such key exists.
    /// </summary>
    private static int BinarySearchFirstPrefix(IList<string> keys, string prefix)
    {
        int lo = 0;
        int hi = keys.Count - 1;
        int result = -1;

        while (lo <= hi)
        {
            int mid = lo + (hi - lo) / 2;
            string key = keys[mid];

            int cmp = string.Compare(key, 0, prefix, 0, prefix.Length, StringComparison.OrdinalIgnoreCase);

            if (cmp >= 0 && key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                result = mid;
                hi = mid - 1; // Look for an earlier match.
            }
            else if (cmp < 0)
            {
                lo = mid + 1;
            }
            else
            {
                hi = mid - 1;
            }
        }

        return result;
    }
}
