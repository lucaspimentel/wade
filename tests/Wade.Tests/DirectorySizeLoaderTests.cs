using Wade.Terminal;

namespace Wade.Tests;

public class DirectorySizeLoaderTests
{
    [Fact]
    public void CalculateSize_EmptyDirectory_ReturnsZero()
    {
        string dir = Path.Combine(Path.GetTempPath(), "wade_test_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var source = new FakeInputSource();
            using var pipeline = new InputPipeline(source);
            var loader = new DirectorySizeLoader(pipeline);

            loader.BeginCalculation(dir);

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            InputEvent evt = pipeline.Take(cts.Token);

            DirectorySizeReadyEvent result = Assert.IsType<DirectorySizeReadyEvent>(evt);
            Assert.Equal(dir, result.Path);
            Assert.Equal(0, result.TotalBytes);
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void CalculateSize_WithFiles_ReturnsTotalSize()
    {
        string dir = Path.Combine(Path.GetTempPath(), "wade_test_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            File.WriteAllBytes(Path.Combine(dir, "a.bin"), new byte[100]);
            File.WriteAllBytes(Path.Combine(dir, "b.bin"), new byte[200]);

            var source = new FakeInputSource();
            using var pipeline = new InputPipeline(source);
            var loader = new DirectorySizeLoader(pipeline);

            loader.BeginCalculation(dir);

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            InputEvent evt = pipeline.Take(cts.Token);

            DirectorySizeReadyEvent result = Assert.IsType<DirectorySizeReadyEvent>(evt);
            Assert.Equal(dir, result.Path);
            Assert.Equal(300, result.TotalBytes);
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void CalculateSize_WithNestedDirectories_IncludesAllFiles()
    {
        string dir = Path.Combine(Path.GetTempPath(), "wade_test_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            File.WriteAllBytes(Path.Combine(dir, "root.bin"), new byte[50]);

            string sub = Path.Combine(dir, "sub");
            Directory.CreateDirectory(sub);
            File.WriteAllBytes(Path.Combine(sub, "child.bin"), new byte[75]);

            string deep = Path.Combine(sub, "deep");
            Directory.CreateDirectory(deep);
            File.WriteAllBytes(Path.Combine(deep, "leaf.bin"), new byte[25]);

            var source = new FakeInputSource();
            using var pipeline = new InputPipeline(source);
            var loader = new DirectorySizeLoader(pipeline);

            loader.BeginCalculation(dir);

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            InputEvent evt = pipeline.Take(cts.Token);

            DirectorySizeReadyEvent result = Assert.IsType<DirectorySizeReadyEvent>(evt);
            Assert.Equal(dir, result.Path);
            Assert.Equal(150, result.TotalBytes);
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void BeginCalculation_CancelsPreviousCalculation()
    {
        string dir1 = Path.Combine(Path.GetTempPath(), "wade_test_" + Guid.NewGuid().ToString("N"));
        string dir2 = Path.Combine(Path.GetTempPath(), "wade_test_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir1);
        Directory.CreateDirectory(dir2);
        try
        {
            File.WriteAllBytes(Path.Combine(dir2, "file.bin"), new byte[42]);

            var source = new FakeInputSource();
            using var pipeline = new InputPipeline(source);
            var loader = new DirectorySizeLoader(pipeline);

            // Start first, then immediately start second (cancels first)
            loader.BeginCalculation(dir1);
            loader.BeginCalculation(dir2);

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

            // We should get at least one event for dir2
            DirectorySizeReadyEvent? dir2Result = null;
            while (dir2Result is null)
            {
                InputEvent evt = pipeline.Take(cts.Token);
                if (evt is DirectorySizeReadyEvent ds && ds.Path == dir2)
                {
                    dir2Result = ds;
                }
            }

            Assert.Equal(42, dir2Result.TotalBytes);
        }
        finally
        {
            Directory.Delete(dir1, true);
            Directory.Delete(dir2, true);
        }
    }

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
}
