using System.Collections.Concurrent;
using System.Runtime.InteropServices;

namespace Wade.Terminal;

internal sealed class InputPipeline : IDisposable
{
    private readonly IInputSource _source;
    private readonly BlockingCollection<InputEvent> _queue = new(boundedCapacity: 64);
    private readonly CancellationTokenSource _cts = new();
    private readonly Thread _readThread;
    private bool _disposed;

    public InputPipeline(IInputSource source)
    {
        _source = source;
        _readThread = new Thread(ReadLoop)
        {
            IsBackground = true,
            Name = "InputPipeline",
        };
        _readThread.Start();
    }

    public InputEvent Take(CancellationToken ct = default)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(_cts.Token, ct);
        return _queue.Take(linked.Token);
    }

    public bool TryTake(out InputEvent? evt)
    {
        return _queue.TryTake(out evt);
    }

    public static IInputSource CreatePlatformSource()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            return new WindowsInputSource();

        return new UnixInputSource();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _cts.Cancel();
        _queue.CompleteAdding();
        _readThread.Join(timeout: TimeSpan.FromSeconds(2));
        _source.Dispose();
        _queue.Dispose();
        _cts.Dispose();
    }

    private void ReadLoop()
    {
        try
        {
            while (!_cts.Token.IsCancellationRequested)
            {
                var evt = _source.ReadNext(_cts.Token);
                if (evt is not null && !_cts.Token.IsCancellationRequested)
                    _queue.Add(evt, _cts.Token);
            }
        }
        catch (OperationCanceledException)
        {
            // Normal shutdown
        }
        catch (InvalidOperationException)
        {
            // Collection completed
        }
    }
}
