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

    /// <summary>
    /// Evaluate a path against this query. If it matches, write to the channel.
    /// Thread-safe: may be called from multiple threads concurrently.
    /// </summary>
    internal bool TryMatch(string path, string[] segments)
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

        int bestDistance = int.MaxValue;
        bool isPrefixMatch = false;

        foreach (string segment in segments)
        {
            if (segment.StartsWith(_query, StringComparison.OrdinalIgnoreCase))
            {
                isPrefixMatch = true;
                bestDistance = 0;
                break;
            }

            int distance = DamerauLevenshtein.Distance(_query.AsSpan(), segment.AsSpan(), _options.MaxEditDistance);
            if (distance < bestDistance)
            {
                bestDistance = distance;
            }
        }

        if (!isPrefixMatch && bestDistance > _options.MaxEditDistance)
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

            if (!_emittedPaths.Add(path))
            {
                return false; // Already emitted.
            }

            _resultCount++;
        }

        var result = new SearchResult(path, bestDistance, isPrefixMatch);
        _channel.Writer.TryWrite(result);
        return true;
    }

    /// <summary>
    /// Signal that the initial index scan is complete.
    /// The channel remains open for live pushes from Add().
    /// </summary>
    internal void MarkSnapshotComplete()
    {
        // Currently a no-op placeholder. The channel stays open until Complete().
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
