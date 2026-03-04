using Wade.Terminal;

namespace Wade.Tests;

public class InputPipelineTests
{
    private sealed class FakeInputSource : IInputSource
    {
        private readonly Queue<InputEvent> _events = new();
        private readonly ManualResetEventSlim _hasEvents = new(false);
        private bool _disposed;

        public void Enqueue(InputEvent evt)
        {
            _events.Enqueue(evt);
            _hasEvents.Set();
        }

        public InputEvent? ReadNext(CancellationToken ct)
        {
            while (!ct.IsCancellationRequested)
            {
                if (_events.TryDequeue(out var evt))
                {
                    if (_events.Count == 0)
                        _hasEvents.Reset();
                    return evt;
                }

                _hasEvents.Wait(ct);
            }

            return null;
        }

        public void Dispose()
        {
            _disposed = true;
            _hasEvents.Set(); // unblock any waiting ReadNext
        }

        public bool IsDisposed => _disposed;
    }

    // ── Single event ────────────────────────────────────────────────────────────

    [Fact]
    public void Take_SingleEvent_ReturnsPostedEvent()
    {
        var source = new FakeInputSource();
        var expected = new KeyEvent(ConsoleKey.A, 'a', false, false, false);
        source.Enqueue(expected);

        using var pipeline = new InputPipeline(source);
        var result = pipeline.Take();

        Assert.Equal(expected, result);
    }

    // ── Multiple events ─────────────────────────────────────────────────────────

    [Fact]
    public void Take_MultipleEvents_ArrivesInOrder()
    {
        var source = new FakeInputSource();
        var evt1 = new KeyEvent(ConsoleKey.A, 'a', false, false, false);
        var evt2 = new KeyEvent(ConsoleKey.B, 'b', false, false, false);
        var evt3 = new ResizeEvent(80, 24);
        source.Enqueue(evt1);
        source.Enqueue(evt2);
        source.Enqueue(evt3);

        using var pipeline = new InputPipeline(source);
        Assert.Equal(evt1, pipeline.Take());
        Assert.Equal(evt2, pipeline.Take());
        Assert.Equal(evt3, pipeline.Take());
    }

    // ── TryTake ─────────────────────────────────────────────────────────────────

    [Fact]
    public void TryTake_EmptyQueue_ReturnsFalse()
    {
        var source = new FakeInputSource();
        using var pipeline = new InputPipeline(source);

        // Give the background thread a moment to start
        Thread.Sleep(50);

        bool result = pipeline.TryTake(out var evt);
        Assert.False(result);
    }

    // ── Inject ────────────────────────────────────────────────────────────────

    [Fact]
    public void Inject_WakesBlockedTake()
    {
        var source = new FakeInputSource();
        using var pipeline = new InputPipeline(source);

        var injected = new ResizeEvent(120, 40);

        // Inject from another thread while Take() is blocked
        Task.Run(async () =>
        {
            await Task.Delay(50);
            pipeline.Inject(injected);
        });

        var result = pipeline.Take();
        Assert.Equal(injected, result);
    }

    // ── Mouse events ──────────────────────────────────────────────────────────

    [Fact]
    public void Take_MouseEvent_ReturnsMouseEvent()
    {
        var source = new FakeInputSource();
        var expected = new MouseEvent(MouseButton.Left, 5, 10, false);
        source.Enqueue(expected);

        using var pipeline = new InputPipeline(source);
        var result = pipeline.Take();

        Assert.Equal(expected, result);
    }

    // ── Disposal ────────────────────────────────────────────────────────────────

    [Fact]
    public void Dispose_ShutsDownCleanly()
    {
        var source = new FakeInputSource();
        var pipeline = new InputPipeline(source);
        pipeline.Dispose();

        Assert.True(source.IsDisposed);
    }
}
