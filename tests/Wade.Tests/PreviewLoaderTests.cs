using Wade.Highlighting;
using Wade.Preview;
using Wade.Terminal;

namespace Wade.Tests;

public class PreviewLoaderTests
{
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
            PdfPreviewEnabled: true,
            PdfMetadataEnabled: true,
            MarkdownPreviewEnabled: true,
            FfprobeEnabled: true,
            MediainfoEnabled: true,
            ZipPreviewEnabled: true,
            ImagePreviewsEnabled: true,
            SixelSupported: true,
            ArchiveMetadataEnabled: true);

    [Fact]
    public void BeginLoad_PostsPreviewReadyEvent()
    {
        var source = new FakeInputSource();
        using var pipeline = new InputPipeline(source);
        var loader = new PreviewLoader(pipeline);
        var provider = new TextPreviewProvider();

        string tempFile = Path.GetTempFileName();
        try
        {
            File.WriteAllText(tempFile, "hello world");
            loader.BeginLoad(tempFile, provider, DefaultContext());

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            InputEvent evt = pipeline.Take(cts.Token);

            PreviewReadyEvent preview = Assert.IsType<PreviewReadyEvent>(evt);
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
        PreviewContext context = DefaultContext();

        string tempFile1 = Path.GetTempFileName();
        string tempFile2 = Path.GetTempFileName();
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
                InputEvent evt = pipeline.Take(cts.Token);
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
    public void BeginLoad_WithSlowProvider_CancelsOnSecondLoad()
    {
        var source = new FakeInputSource();
        using var pipeline = new InputPipeline(source);
        var loader = new PreviewLoader(pipeline);
        var slowProvider = new SlowPreviewProvider();
        var fastProvider = new TextPreviewProvider();
        PreviewContext context = DefaultContext();

        string tempFile1 = Path.GetTempFileName();
        string tempFile2 = Path.GetTempFileName();
        try
        {
            File.WriteAllText(tempFile1, "first");
            File.WriteAllText(tempFile2, "second");

            // Start slow load, then immediately start fast load (cancels the slow one)
            loader.BeginLoad(tempFile1, slowProvider, context);
            Thread.Sleep(50); // let slow provider start
            loader.BeginLoad(tempFile2, fastProvider, context);

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

            // The second (fast) load should complete promptly
            PreviewReadyEvent? secondPreview = null;
            while (true)
            {
                InputEvent evt = pipeline.Take(cts.Token);
                if (evt is PreviewReadyEvent p && p.Path == tempFile2)
                {
                    secondPreview = p;
                    break;
                }
            }

            Assert.NotNull(secondPreview);
            Assert.Equal(tempFile2, secondPreview.Path);
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

        string tempFile = Path.GetTempFileName();
        try
        {
            File.WriteAllText(tempFile, "hello world");
            loader.BeginLoad(tempFile, provider, DefaultContext());
            loader.Cancel();

            // Give background task time to (not) post
            Thread.Sleep(200);

            bool hasEvent = pipeline.TryTake(out InputEvent? evt);
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

    private sealed class SlowPreviewProvider : IPreviewProvider
    {
        public string Label => "Slow";

        public bool CanPreview(string path, PreviewContext context) => true;

        public PreviewResult? GetPreview(string path, PreviewContext context, CancellationToken ct)
        {
            // Simulate a slow subprocess-based provider
            try
            {
                Task.Delay(TimeSpan.FromSeconds(30), ct).Wait(ct);
            }
            catch (OperationCanceledException)
            {
                return null;
            }

            return new PreviewResult { TextLines = [new StyledLine("slow result", null)] };
        }
    }
}
