using System.Collections.Concurrent;
using System.Threading.Channels;

namespace Wade.Search;

/// <summary>
/// A thread-safe search index for file paths.
/// Supports fuzzy subsequence matching with boundary-aware scoring and filename priority.
/// Results streamed via <see cref="System.Threading.Channels.Channel{T}"/>.
/// Consumers should sort by <see cref="SearchResult.Score"/> descending (higher is better).
/// </summary>
public sealed class SearchIndex : IDisposable
{
    private static readonly char[] s_separators = Path.DirectorySeparatorChar == Path.AltDirectorySeparatorChar
        ? [Path.DirectorySeparatorChar]
        : [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar];

    private readonly string _basePath;

    // All indexed paths (for dedup) — case-sensitive to preserve distinct paths on Linux
    private readonly ConcurrentDictionary<string, byte> _allPaths = new(StringComparer.Ordinal);

    // Per-path precomputed data for scoring
    private readonly ConcurrentDictionary<string, PathEntry> _pathEntries = new(StringComparer.Ordinal);

    // Active query state
    private readonly object _queryLock = new();
    private ActiveQuery? _activeQuery;

    /// <summary>
    /// Create a new search index rooted at the given base path.
    /// Paths added via <see cref="Add"/> will be scored against their relative path from this base.
    /// </summary>
    public SearchIndex(string basePath)
    {
        _basePath = basePath;
    }

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

        string relative = Path.GetRelativePath(_basePath, path);
        int fileNameStart = relative.LastIndexOfAny(s_separators) + 1;
        _pathEntries[path] = new PathEntry(path, relative, fileNameStart);

        // Live push: if there's an active query, check this new path immediately.
        lock (_queryLock)
        {
            _activeQuery?.TryMatch(path, relative, fileNameStart);
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
        _pathEntries.Clear();
    }

    public void Dispose()
    {
        CancelSearch();
    }

    /// <summary>
    /// Scan all existing indexed paths against the given query.
    /// Single pass: iterate all paths and score each one.
    /// </summary>
    private void ScanExistingIndex(ActiveQuery query)
    {
        CancellationToken ct = query.CancellationToken;

        try
        {
            // Note: _pathEntries iteration is weakly consistent — concurrent Add() calls may cause
            // some paths to be visited here AND via live push in Add(). This is harmless because
            // ActiveQuery.TryMatch deduplicates via _emittedPaths.
            foreach (PathEntry entry in _pathEntries.Values)
            {
                if (ct.IsCancellationRequested || query.IsMaxResultsReached)
                {
                    return;
                }

                query.TryMatch(entry.AbsolutePath, entry.RelativePath, entry.FileNameStart);
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
}
