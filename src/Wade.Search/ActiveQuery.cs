using System.Threading.Channels;

namespace Wade.Search;

/// <summary>
/// Encapsulates the state for a single active search query: the channel, cancellation,
/// dedup set, and matching logic. Thread-safe for concurrent TryMatch calls.
/// </summary>
internal sealed class ActiveQuery : IDisposable
{
    private readonly Channel<SearchResult> _channel;
    private readonly CancellationTokenSource _cts;
    private readonly string _query;
    private readonly SearchOptions _options;
    private readonly HashSet<string> _emittedPaths = new(StringComparer.Ordinal);
    private readonly object _emitLock = new();
    private readonly TaskCompletionSource _snapshotTcs = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int _resultCount;

    internal ActiveQuery(string query, SearchOptions options)
    {
        _query = query;
        _options = options;
        _cts = new CancellationTokenSource();
        _channel = Channel.CreateUnbounded<SearchResult>(new UnboundedChannelOptions
        {
            SingleWriter = false,
            SingleReader = true,
        });
    }

    internal ChannelReader<SearchResult> Reader => _channel.Reader;
    internal CancellationToken CancellationToken => _cts.Token;
    internal string Query => _query;
    internal Task SnapshotComplete => _snapshotTcs.Task;
    internal bool IsMaxResultsReached => Volatile.Read(ref _resultCount) >= _options.MaxResults;

    /// <summary>
    /// Evaluate a path against this query using fuzzy subsequence scoring.
    /// If it matches, write to the channel.
    /// Thread-safe: may be called from multiple threads concurrently.
    /// </summary>
    internal bool TryMatch(string absolutePath, string relativePath, int fileNameStart)
    {
        try
        {
            if (_cts.IsCancellationRequested)
            {
                return false;
            }
        }
        catch (ObjectDisposedException)
        {
            return false;
        }

        if (Volatile.Read(ref _resultCount) >= _options.MaxResults)
        {
            return false;
        }

        int score = FuzzyScorer.ScoreWithFileNamePriority(
            _query.AsSpan(), relativePath.AsSpan(), fileNameStart, out int[] matchPositions);

        if (score == int.MinValue)
        {
            return false;
        }

        lock (_emitLock)
        {
            try
            {
                if (_cts.IsCancellationRequested)
                {
                    return false;
                }
            }
            catch (ObjectDisposedException)
            {
                return false;
            }

            if (_resultCount >= _options.MaxResults)
            {
                return false;
            }

            if (!_emittedPaths.Add(absolutePath))
            {
                return false; // Already emitted.
            }

            _resultCount++;

            var result = new SearchResult(absolutePath, score, matchPositions);
            _channel.Writer.TryWrite(result);
        }

        return true;
    }

    /// <summary>
    /// Signal that the initial index scan is complete.
    /// The channel remains open for live pushes from Add().
    /// </summary>
    internal void MarkSnapshotComplete()
    {
        _snapshotTcs.TrySetResult();
    }

    /// <summary>
    /// Complete the channel writer and cancel outstanding work.
    /// </summary>
    internal void Complete()
    {
        try
        {
            _cts.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // Already disposed by a concurrent call.
        }

        _channel.Writer.TryComplete();
    }

    public void Dispose()
    {
        _cts.Dispose();
    }
}
