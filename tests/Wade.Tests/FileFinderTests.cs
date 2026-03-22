using Wade.FileSystem;
using Wade.Terminal;

namespace Wade.Tests;

public class FileFinderTests : IDisposable
{
    private readonly string _tempDir;

    public FileFinderTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "wade_test_" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_tempDir, true);
        }
        catch
        {
            // Best effort cleanup
        }
    }

    [Fact]
    public void FilterEntries_EmptyFilter_ReturnsAll()
    {
        var entries = new List<FileSystemEntry>
        {
            MakeEntry("foo.txt", "/base/foo.txt"),
            MakeEntry("bar.cs", "/base/bar.cs"),
        };

        List<FileSystemEntry> result = App.GetFilteredFileFinderEntries(entries, "", "/base");

        Assert.Equal(2, result.Count);
    }

    [Fact]
    public void FilterEntries_SubstringMatch_CaseInsensitive()
    {
        var entries = new List<FileSystemEntry>
        {
            MakeEntry("Foo.txt", Path.Combine(_tempDir, "Foo.txt")),
            MakeEntry("bar.cs", Path.Combine(_tempDir, "bar.cs")),
            MakeEntry("FooBar.txt", Path.Combine(_tempDir, "sub", "FooBar.txt")),
        };

        List<FileSystemEntry> result = App.GetFilteredFileFinderEntries(entries, "foo", _tempDir);

        Assert.Equal(2, result.Count);
        Assert.All(result, e => Assert.Contains("Foo", e.Name, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void FilterEntries_NoMatches_ReturnsEmpty()
    {
        var entries = new List<FileSystemEntry>
        {
            MakeEntry("foo.txt", "/base/foo.txt"),
            MakeEntry("bar.cs", "/base/bar.cs"),
        };

        List<FileSystemEntry> result = App.GetFilteredFileFinderEntries(entries, "zzz", "/base");

        Assert.Empty(result);
    }

    [Fact]
    public void FilterEntries_NullEntries_ReturnsEmpty()
    {
        List<FileSystemEntry> result = App.GetFilteredFileFinderEntries(null, "test", "/base");

        Assert.Empty(result);
    }

    [Fact]
    public void FilterEntries_MatchesRelativePath()
    {
        string subDir = Path.Combine(_tempDir, "src", "Wade");
        var entries = new List<FileSystemEntry>
        {
            MakeEntry("App.cs", Path.Combine(subDir, "App.cs")),
            MakeEntry("Other.cs", Path.Combine(_tempDir, "Other.cs")),
        };

        // Filter by path component, not just filename
        List<FileSystemEntry> result = App.GetFilteredFileFinderEntries(entries, "Wade", _tempDir);

        Assert.Single(result);
        Assert.Equal("App.cs", result[0].Name);
    }

    [Fact]
    public void Scan_FindsFilesInSubdirectories()
    {
        // Create a simple directory tree
        string sub = Path.Combine(_tempDir, "a", "b");
        Directory.CreateDirectory(sub);
        File.WriteAllText(Path.Combine(_tempDir, "root.txt"), "");
        File.WriteAllText(Path.Combine(sub, "deep.txt"), "");

        using InputPipeline pipeline = CreateTestPipeline();
        App.ScanFilesForFinder(_tempDir, showHidden: true, showSystem: true, pipeline, CancellationToken.None);

        InputEvent? evt = DrainPipeline(pipeline);
        Assert.NotNull(evt);
        FileFinderScanCompleteEvent scanEvt = Assert.IsType<FileFinderScanCompleteEvent>(evt);
        Assert.Equal(_tempDir, scanEvt.BasePath);
        Assert.True(scanEvt.Entries.Count >= 2);
        Assert.Contains(scanEvt.Entries, e => e.Name == "root.txt");
        Assert.Contains(scanEvt.Entries, e => e.Name == "deep.txt");
    }

    [Fact]
    public void Scan_SkipsHiddenFiles_WhenShowHiddenFalse()
    {
        File.WriteAllText(Path.Combine(_tempDir, ".hidden"), "");
        File.WriteAllText(Path.Combine(_tempDir, "visible.txt"), "");

        using InputPipeline pipeline = CreateTestPipeline();
        App.ScanFilesForFinder(_tempDir, showHidden: false, showSystem: true, pipeline, CancellationToken.None);

        InputEvent? evt = DrainPipeline(pipeline);
        Assert.NotNull(evt);
        FileFinderScanCompleteEvent scanEvt = Assert.IsType<FileFinderScanCompleteEvent>(evt);
        Assert.DoesNotContain(scanEvt.Entries, e => e.Name == ".hidden");
        Assert.Contains(scanEvt.Entries, e => e.Name == "visible.txt");
    }

    [Fact]
    public void Scan_IncludesHiddenFiles_WhenShowHiddenTrue()
    {
        File.WriteAllText(Path.Combine(_tempDir, ".hidden"), "");
        File.WriteAllText(Path.Combine(_tempDir, "visible.txt"), "");

        using InputPipeline pipeline = CreateTestPipeline();
        App.ScanFilesForFinder(_tempDir, showHidden: true, showSystem: true, pipeline, CancellationToken.None);

        InputEvent? evt = DrainPipeline(pipeline);
        Assert.NotNull(evt);
        FileFinderScanCompleteEvent scanEvt = Assert.IsType<FileFinderScanCompleteEvent>(evt);
        Assert.Contains(scanEvt.Entries, e => e.Name == ".hidden");
        Assert.Contains(scanEvt.Entries, e => e.Name == "visible.txt");
    }

    [Fact]
    public void Scan_SkipsGitDirectory()
    {
        string gitDir = Path.Combine(_tempDir, ".git", "objects");
        Directory.CreateDirectory(gitDir);
        File.WriteAllText(Path.Combine(gitDir, "abc123"), "");
        File.WriteAllText(Path.Combine(_tempDir, "readme.md"), "");

        using InputPipeline pipeline = CreateTestPipeline();
        App.ScanFilesForFinder(_tempDir, showHidden: true, showSystem: true, pipeline, CancellationToken.None);

        InputEvent? evt = DrainPipeline(pipeline);
        Assert.NotNull(evt);
        FileFinderScanCompleteEvent scanEvt = Assert.IsType<FileFinderScanCompleteEvent>(evt);
        Assert.DoesNotContain(scanEvt.Entries, e => e.FullPath.Contains(".git"));
        Assert.Contains(scanEvt.Entries, e => e.Name == "readme.md");
    }

    [Fact]
    public void Scan_SkipsHiddenDotDirectories_WhenHiddenDisabled()
    {
        string hiddenDir = Path.Combine(_tempDir, ".hidden");
        Directory.CreateDirectory(hiddenDir);
        File.WriteAllText(Path.Combine(hiddenDir, "secret.txt"), "");
        File.WriteAllText(Path.Combine(_tempDir, "visible.txt"), "");

        using InputPipeline pipeline = CreateTestPipeline();
        App.ScanFilesForFinder(_tempDir, showHidden: false, showSystem: true, pipeline, CancellationToken.None);

        InputEvent? evt = DrainPipeline(pipeline);
        Assert.NotNull(evt);
        FileFinderScanCompleteEvent scanEvt = Assert.IsType<FileFinderScanCompleteEvent>(evt);
        Assert.DoesNotContain(scanEvt.Entries, e => e.Name == "secret.txt");
        Assert.Contains(scanEvt.Entries, e => e.Name == "visible.txt");
    }

    [Fact]
    public void Scan_IncludesHiddenDotDirectories_WhenHiddenEnabled()
    {
        string hiddenDir = Path.Combine(_tempDir, ".hidden");
        Directory.CreateDirectory(hiddenDir);
        File.WriteAllText(Path.Combine(hiddenDir, "secret.txt"), "");
        File.WriteAllText(Path.Combine(_tempDir, "visible.txt"), "");

        using InputPipeline pipeline = CreateTestPipeline();
        App.ScanFilesForFinder(_tempDir, showHidden: true, showSystem: true, pipeline, CancellationToken.None);

        InputEvent? evt = DrainPipeline(pipeline);
        Assert.NotNull(evt);
        FileFinderScanCompleteEvent scanEvt = Assert.IsType<FileFinderScanCompleteEvent>(evt);
        Assert.Contains(scanEvt.Entries, e => e.Name == "secret.txt");
        Assert.Contains(scanEvt.Entries, e => e.Name == "visible.txt");
    }

    [Fact]
    public void Scan_AlwaysSkipsGit_EvenWhenHiddenEnabled()
    {
        string gitDir = Path.Combine(_tempDir, ".git", "refs");
        Directory.CreateDirectory(gitDir);
        File.WriteAllText(Path.Combine(gitDir, "HEAD"), "");

        string hiddenDir = Path.Combine(_tempDir, ".config");
        Directory.CreateDirectory(hiddenDir);
        File.WriteAllText(Path.Combine(hiddenDir, "settings.json"), "");

        using InputPipeline pipeline = CreateTestPipeline();
        App.ScanFilesForFinder(_tempDir, showHidden: true, showSystem: true, pipeline, CancellationToken.None);

        InputEvent? evt = DrainPipeline(pipeline);
        Assert.NotNull(evt);
        FileFinderScanCompleteEvent scanEvt = Assert.IsType<FileFinderScanCompleteEvent>(evt);
        Assert.DoesNotContain(scanEvt.Entries, e => e.FullPath.Contains(".git"));
        Assert.Contains(scanEvt.Entries, e => e.Name == "settings.json");
    }

    [Fact]
    public void Scan_RespectsCancel()
    {
        File.WriteAllText(Path.Combine(_tempDir, "file.txt"), "");

        using var cts = new CancellationTokenSource();
        cts.Cancel(); // Cancel before scan

        using InputPipeline pipeline = CreateTestPipeline();
        App.ScanFilesForFinder(_tempDir, showHidden: true, showSystem: true, pipeline, cts.Token);

        // No event should be injected
        InputEvent? evt = DrainPipeline(pipeline);
        Assert.Null(evt);
    }

    private static FileSystemEntry MakeEntry(string name, string fullPath) =>
        new(name, fullPath, IsDirectory: false, Size: 0, LastModified: DateTime.Now,
            LinkTarget: null, IsBrokenSymlink: false, IsDrive: false);

    private static InputPipeline CreateTestPipeline()
    {
        var source = new NullInputSource();
        return new InputPipeline(source);
    }

    private static InputEvent? DrainPipeline(InputPipeline pipeline)
    {
        var sentinel = new KeyEvent(ConsoleKey.Escape, '\x1b', false, false, false);
        pipeline.Inject(sentinel);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        while (!cts.IsCancellationRequested)
        {
            InputEvent evt = pipeline.Take(cts.Token);

            if (evt is FileFinderScanCompleteEvent)
            {
                return evt;
            }

            if (evt == sentinel)
            {
                return null;
            }
        }

        return null;
    }

    private sealed class NullInputSource : IInputSource
    {
        public void Dispose()
        {
        }

        public InputEvent? ReadNext(CancellationToken ct)
        {
            ct.WaitHandle.WaitOne();
            throw new OperationCanceledException(ct);
        }
    }
}
