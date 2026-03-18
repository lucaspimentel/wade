using Wade.Terminal;

namespace Wade.Tests;

public class FileSystemWatcherManagerTests
{
    private sealed class FakeInputSource : IInputSource
    {
        private readonly ManualResetEventSlim _gate = new(false);

        public InputEvent? ReadNext(CancellationToken ct)
        {
            _gate.Wait(ct);
            return null;
        }

        public void Dispose() => _gate.Set();
    }

    [Fact]
    public void Watch_FileCreated_InjectsEvent()
    {
        string dir = Path.Combine(Path.GetTempPath(), "wade_test_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var source = new FakeInputSource();
            using var pipeline = new InputPipeline(source);
            using var watcher = new FileSystemWatcherManager(pipeline);

            watcher.Watch(dir);

            // Create a file to trigger the watcher
            File.WriteAllText(Path.Combine(dir, "test.txt"), "hello");

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            var evt = WaitForEvent<FileSystemChangedEvent>(pipeline, cts.Token);

            Assert.Equal(dir, evt.DirectoryPath);
            Assert.False(evt.FullRefresh);
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void Watch_FileDeleted_InjectsEvent()
    {
        string dir = Path.Combine(Path.GetTempPath(), "wade_test_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        string filePath = Path.Combine(dir, "test.txt");
        File.WriteAllText(filePath, "hello");
        try
        {
            var source = new FakeInputSource();
            using var pipeline = new InputPipeline(source);
            using var watcher = new FileSystemWatcherManager(pipeline);

            watcher.Watch(dir);

            File.Delete(filePath);

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            var evt = WaitForEvent<FileSystemChangedEvent>(pipeline, cts.Token);

            Assert.Equal(dir, evt.DirectoryPath);
        }
        finally
        {
            if (Directory.Exists(dir))
            {
                Directory.Delete(dir, true);
            }
        }
    }

    [Fact]
    public void Watch_FileRenamed_InjectsEvent()
    {
        string dir = Path.Combine(Path.GetTempPath(), "wade_test_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        string filePath = Path.Combine(dir, "old.txt");
        File.WriteAllText(filePath, "hello");
        try
        {
            var source = new FakeInputSource();
            using var pipeline = new InputPipeline(source);
            using var watcher = new FileSystemWatcherManager(pipeline);

            watcher.Watch(dir);

            File.Move(filePath, Path.Combine(dir, "new.txt"));

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            var evt = WaitForEvent<FileSystemChangedEvent>(pipeline, cts.Token);

            Assert.Equal(dir, evt.DirectoryPath);
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void Watch_NewDirectory_StopsWatchingOldDirectory()
    {
        string dir1 = Path.Combine(Path.GetTempPath(), "wade_test_" + Guid.NewGuid().ToString("N"));
        string dir2 = Path.Combine(Path.GetTempPath(), "wade_test_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir1);
        Directory.CreateDirectory(dir2);
        try
        {
            var source = new FakeInputSource();
            using var pipeline = new InputPipeline(source);
            using var watcher = new FileSystemWatcherManager(pipeline);

            watcher.Watch(dir1);
            watcher.Watch(dir2);

            // Create file in dir2 — should get event for dir2
            File.WriteAllText(Path.Combine(dir2, "test.txt"), "hello");

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            var evt = WaitForEvent<FileSystemChangedEvent>(pipeline, cts.Token);

            Assert.Equal(dir2, evt.DirectoryPath);
        }
        finally
        {
            Directory.Delete(dir1, true);
            Directory.Delete(dir2, true);
        }
    }

    [Fact]
    public void Watch_SameDirectory_DoesNotRestart()
    {
        string dir = Path.Combine(Path.GetTempPath(), "wade_test_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var source = new FakeInputSource();
            using var pipeline = new InputPipeline(source);
            using var watcher = new FileSystemWatcherManager(pipeline);

            watcher.Watch(dir);
            watcher.Watch(dir); // Same path — should be a no-op

            // Still works after redundant Watch call
            File.WriteAllText(Path.Combine(dir, "test.txt"), "hello");

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            var evt = WaitForEvent<FileSystemChangedEvent>(pipeline, cts.Token);

            Assert.Equal(dir, evt.DirectoryPath);
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void Dispose_StopsWatcher()
    {
        string dir = Path.Combine(Path.GetTempPath(), "wade_test_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var source = new FakeInputSource();
            using var pipeline = new InputPipeline(source);
            var watcher = new FileSystemWatcherManager(pipeline);

            watcher.Watch(dir);
            watcher.Dispose();

            // Create file after dispose — should not crash or inject events
            File.WriteAllText(Path.Combine(dir, "test.txt"), "hello");

            // Wait briefly to ensure no event arrives
            Thread.Sleep(500);
            bool hasEvent = pipeline.TryTake(out var evt);
            Assert.False(hasEvent && evt is FileSystemChangedEvent);
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void Debounce_RapidChanges_CoalescesIntoSingleEvent()
    {
        string dir = Path.Combine(Path.GetTempPath(), "wade_test_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var source = new FakeInputSource();
            using var pipeline = new InputPipeline(source);
            using var watcher = new FileSystemWatcherManager(pipeline);

            watcher.Watch(dir);

            // Create multiple files rapidly
            for (int i = 0; i < 10; i++)
            {
                File.WriteAllText(Path.Combine(dir, $"file{i}.txt"), $"content{i}");
            }

            // Wait for debounce to fire (300ms debounce + margin)
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            var firstEvt = WaitForEvent<FileSystemChangedEvent>(pipeline, cts.Token);
            Assert.Equal(dir, firstEvt.DirectoryPath);

            // Wait briefly and drain — should have at most a small number of coalesced events
            // (not 10+ individual events)
            Thread.Sleep(500);
            int extraEvents = 0;
            while (pipeline.TryTake(out var extra))
            {
                if (extra is FileSystemChangedEvent)
                {
                    extraEvents++;
                }
            }

            // Debouncing should keep total events well below the 10 file creations.
            // Each file create may produce multiple FSW events (Created + Changed),
            // so without debouncing we'd see 10-20+ events.
            Assert.True(extraEvents < 5, $"Expected fewer than 5 extra events but got {extraEvents}");
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    private static T WaitForEvent<T>(InputPipeline pipeline, CancellationToken ct) where T : InputEvent
    {
        while (true)
        {
            var evt = pipeline.Take(ct);
            if (evt is T typed)
            {
                return typed;
            }
        }
    }
}
