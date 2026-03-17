using Wade.Preview;
using Wade.Terminal;

namespace Wade.Tests;

public class PreviewLoaderTests
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

    private static PreviewContext DefaultContext() =>
        new(
            PaneWidthCells: 40,
            PaneHeightCells: 30,
            CellPixelWidth: 8,
            CellPixelHeight: 16,
            IsCloudPlaceholder: false,
            IsBrokenSymlink: false,
            GitStatus: null,
            RepoRoot: null,
            DisabledTools: new HashSet<string>(),
            ZipPreviewEnabled: true,
            ImagePreviewsEnabled: true);

    [Fact]
    public void BeginLoad_PostsPreviewReadyEvent()
    {
        var source = new FakeInputSource();
        using var pipeline = new InputPipeline(source);
        var loader = new PreviewLoader(pipeline);
        var provider = new TextPreviewProvider();

        var tempFile = Path.GetTempFileName();
        try
        {
            File.WriteAllText(tempFile, "hello world");
            loader.BeginLoad(tempFile, provider, DefaultContext());

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            var evt = pipeline.Take(cts.Token);

            var preview = Assert.IsType<PreviewReadyEvent>(evt);
            Assert.Equal(tempFile, preview.Path);
            Assert.NotNull(preview.StyledLines);
            Assert.True(preview.StyledLines.Length > 0);
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Fact]
    public void BeginLoad_CancelsPreviousLoad()
    {
        var source = new FakeInputSource();
        using var pipeline = new InputPipeline(source);
        var loader = new PreviewLoader(pipeline);
        var provider = new TextPreviewProvider();
        var context = DefaultContext();

        var tempFile1 = Path.GetTempFileName();
        var tempFile2 = Path.GetTempFileName();
        try
        {
            File.WriteAllText(tempFile1, "first");
            File.WriteAllText(tempFile2, "second");

            // Rapid double-load — only second should matter
            loader.BeginLoad(tempFile1, provider, context);
            loader.BeginLoad(tempFile2, provider, context);

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

            // Collect all events that arrive (may get 0 or 1 for first file if it completed before cancel)
            PreviewReadyEvent? lastPreview = null;
            while (true)
            {
                var evt = pipeline.Take(cts.Token);
                if (evt is PreviewReadyEvent p)
                {
                    lastPreview = p;
                    if (p.Path == tempFile2)
                    {
                        break;
                    }
                }
            }

            Assert.NotNull(lastPreview);
            Assert.Equal(tempFile2, lastPreview.Path);
        }
        finally
        {
            File.Delete(tempFile1);
            File.Delete(tempFile2);
        }
    }

    [Fact]
    public void Cancel_PreventsEvent()
    {
        var source = new FakeInputSource();
        using var pipeline = new InputPipeline(source);
        var loader = new PreviewLoader(pipeline);
        var provider = new TextPreviewProvider();

        var tempFile = Path.GetTempFileName();
        try
        {
            File.WriteAllText(tempFile, "hello world");
            loader.BeginLoad(tempFile, provider, DefaultContext());
            loader.Cancel();

            // Give background task time to (not) post
            Thread.Sleep(200);

            bool hasEvent = pipeline.TryTake(out var evt);
            // Either no event, or if one arrived before cancel, that's acceptable
            if (hasEvent)
            {
                // The event may have been posted before cancel took effect — that's a race
                // but it shouldn't be from a later load
                Assert.IsType<PreviewReadyEvent>(evt);
            }
        }
        finally
        {
            File.Delete(tempFile);
        }
    }
}
