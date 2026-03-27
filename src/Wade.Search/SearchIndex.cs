using System.Collections.Concurrent;
using System.Threading.Channels;

namespace Wade.Search;

/// <summary>
/// A thread-safe, segment-based search index for file paths.
/// Supports prefix search and fuzzy (Damerau-Levenshtein) matching with results
/// streamed via <see cref="System.Threading.Channels.Channel{T}"/>.
/// Results are streamed in two phases (prefix matches first, then fuzzy matches),
/// but are unordered within each phase. Consumers must sort by
/// <see cref="SearchResult.Score"/> if display ordering matters.
/// </summary>
public sealed class SearchIndex : IDisposable
{
    // segment (case-insensitive) → set of full paths containing that segment.
    // NOTE: Keys use OrdinalIgnoreCase, while _sortedSegments stores ToLowerInvariant() copies.
    // FindPrefixMatchPaths looks up lowercased segments from _sortedSegments in this dictionary,
    // which works because of the OrdinalIgnoreCase comparer. Keep these two in sync.
    private readonly ConcurrentDictionary<string, ConcurrentDictionary<string, byte>> _segmentToPaths
        = new(StringComparer.OrdinalIgnoreCase);

    // Sorted segments (lowercased) for prefix range lookup (SortedSet = O(log n) insert via red-black tree)
    private readonly SortedSet<string> _sortedSegments = new(StringComparer.OrdinalIgnoreCase);
    private readonly ReaderWriterLockSlim _sortedLock = new();

    // All indexed paths (for dedup) — case-sensitive to preserve distinct paths on Linux
    private readonly ConcurrentDictionary<string, byte> _allPaths = new(StringComparer.Ordinal);

    // Per-path segment cache so we don't re-split during search
    private readonly ConcurrentDictionary<string, string[]> _pathSegments = new(StringComparer.Ordinal);

    // Active query state
    private readonly object _queryLock = new();
    private ActiveQuery? _activeQuery;

    /// <summary>
    /// Number of distinct paths in the index.
    /// </summary>
    public int Count => _allPaths.Count;

    /// <summary>
    /// Task that completes when the active query's initial index scan finishes.
    /// Returns null if no query is active.
    /// </summary>
    internal Task? SnapshotCompleteTask
    {
        get
        {
            lock (_queryLock)
            {
                return _activeQuery?.SnapshotComplete;
            }
        }
    }

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

        // Update the concurrent segment→paths map outside the lock (ConcurrentDictionary is thread-safe).
        // Collect lowercased segments to batch-insert into _sortedSegments under a short write lock.
        var loweredSegments = new string[segments.Length];

        for (int i = 0; i < segments.Length; i++)
        {
            string segment = segments[i];

            ConcurrentDictionary<string, byte> pathSet = _segmentToPaths.GetOrAdd(
                segment,
                static _ => new ConcurrentDictionary<string, byte>(StringComparer.Ordinal));
            pathSet.TryAdd(path, 0);

            loweredSegments[i] = segment.ToLowerInvariant();
        }

        _sortedLock.EnterWriteLock();
        try
        {
            foreach (string lowered in loweredSegments)
            {
                _sortedSegments.Add(lowered);
            }
        }
        finally
        {
            _sortedLock.ExitWriteLock();
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
        // Empty query: cancel any active query and return an immediately-completed empty channel.
        if (string.IsNullOrEmpty(query))
        {
            CancelSearch();
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

    public void Dispose()
    {
        CancelSearch();
        _sortedLock.Dispose();
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
            // Note: _allPaths iteration is weakly consistent — concurrent Add() calls may cause
            // some paths to be visited here AND via live push in Add(). This is harmless because
            // ActiveQuery.TryMatch deduplicates via _emittedPaths.
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
        }
        catch (OperationCanceledException)
        {
            // Expected on cancellation.
        }
        catch (Exception)
        {
            // Prevent unobserved task exceptions. The query is effectively dead
            // at this point — the consumer will see the channel complete.
        }
        finally
        {
            query.MarkSnapshotComplete();
        }
    }

    /// <summary>
    /// Use the sorted segment structure to find all paths that have at least one
    /// segment starting with the query string (prefix match). O(log n + k) where
    /// k is the number of matching segments.
    /// </summary>
    private HashSet<string> FindPrefixMatchPaths(string query, CancellationToken ct)
    {
        var result = new HashSet<string>(StringComparer.Ordinal);
        string queryLower = query.ToLowerInvariant();

        // Upper bound for GetViewBetween: the query prefix with the highest possible trailing char.
        string upperBound = queryLower + "\uffff";

        // Collect matching segments under the read lock, then release before doing path lookups.
        // This keeps the lock hold time short — only the SortedSet iteration, not the path expansion.
        List<string> matchingSegments;

        _sortedLock.EnterReadLock();
        try
        {
            matchingSegments = new List<string>();

            foreach (string segment in _sortedSegments.GetViewBetween(queryLower, upperBound))
            {
                if (ct.IsCancellationRequested)
                {
                    break;
                }

                if (segment.StartsWith(queryLower, StringComparison.OrdinalIgnoreCase))
                {
                    matchingSegments.Add(segment);
                }
            }
        }
        finally
        {
            _sortedLock.ExitReadLock();
        }

        // Expand segments to paths outside the lock — _segmentToPaths is a ConcurrentDictionary.
        foreach (string segment in matchingSegments)
        {
            if (ct.IsCancellationRequested)
            {
                break;
            }

            if (_segmentToPaths.TryGetValue(segment, out ConcurrentDictionary<string, byte>? pathSet))
            {
                foreach (string path in pathSet.Keys)
                {
                    result.Add(path);
                }
            }
        }

        return result;
    }
}
