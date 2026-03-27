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
    public void Scan_FindsFilesAndDirectoriesInSubdirectories()
    {
        // Create a simple directory tree
        string sub = Path.Combine(_tempDir, "a", "b");
        Directory.CreateDirectory(sub);
        File.WriteAllText(Path.Combine(_tempDir, "root.txt"), "");
        File.WriteAllText(Path.Combine(sub, "deep.txt"), "");

        using InputPipeline pipeline = CreateTestPipeline();
        App.ScanFilesForFinder(_tempDir, showHidden: true, showSystem: true, pipeline, CancellationToken.None);

        List<FileSystemEntry>? entries = DrainPipeline(pipeline);
        Assert.NotNull(entries);
        Assert.Contains(entries, e => e.Name == "root.txt" && !e.IsDirectory);
        Assert.Contains(entries, e => e.Name == "deep.txt" && !e.IsDirectory);
        Assert.Contains(entries, e => e.Name == "a" && e.IsDirectory);
        Assert.Contains(entries, e => e.Name == "b" && e.IsDirectory);
    }

    [Fact]
    public void Scan_BfsOrder_CurrentDirectoryEntriesAppearFirst()
    {
        // Create: root/root.txt, root/child/child.txt, root/child/grandchild/deep.txt
        string child = Path.Combine(_tempDir, "child");
        string grandchild = Path.Combine(child, "grandchild");
        Directory.CreateDirectory(grandchild);
        File.WriteAllText(Path.Combine(_tempDir, "root.txt"), "");
        File.WriteAllText(Path.Combine(child, "child.txt"), "");
        File.WriteAllText(Path.Combine(grandchild, "deep.txt"), "");

        using InputPipeline pipeline = CreateTestPipeline();
        App.ScanFilesForFinder(_tempDir, showHidden: true, showSystem: true, pipeline, CancellationToken.None);

        List<FileSystemEntry>? entries = DrainPipeline(pipeline);
        Assert.NotNull(entries);

        // root.txt and child/ (depth 0) should appear before child.txt and grandchild/ (depth 1)
        // which should appear before deep.txt (depth 2)
        int rootTxtIndex = entries.FindIndex(e => e.Name == "root.txt");
        int childDirIndex = entries.FindIndex(e => e.Name == "child" && e.IsDirectory);
        int childTxtIndex = entries.FindIndex(e => e.Name == "child.txt");
        int grandchildDirIndex = entries.FindIndex(e => e.Name == "grandchild" && e.IsDirectory);
        int deepTxtIndex = entries.FindIndex(e => e.Name == "deep.txt");

        // Depth 0 entries before depth 1 entries
        Assert.True(rootTxtIndex < childTxtIndex, "root.txt should appear before child.txt");
        Assert.True(childDirIndex < childTxtIndex, "child/ should appear before child.txt");

        // Depth 1 entries before depth 2 entries
        Assert.True(childTxtIndex < deepTxtIndex, "child.txt should appear before deep.txt");
        Assert.True(grandchildDirIndex < deepTxtIndex, "grandchild/ should appear before deep.txt");
    }

    [Fact]
    public void Scan_SkipsHiddenFiles_WhenShowHiddenFalse()
    {
        File.WriteAllText(Path.Combine(_tempDir, ".hidden"), "");
        File.WriteAllText(Path.Combine(_tempDir, "visible.txt"), "");

        using InputPipeline pipeline = CreateTestPipeline();
        App.ScanFilesForFinder(_tempDir, showHidden: false, showSystem: true, pipeline, CancellationToken.None);

        List<FileSystemEntry>? entries = DrainPipeline(pipeline);
        Assert.NotNull(entries);
        Assert.DoesNotContain(entries, e => e.Name == ".hidden");
        Assert.Contains(entries, e => e.Name == "visible.txt");
    }

    [Fact]
    public void Scan_IncludesHiddenFiles_WhenShowHiddenTrue()
    {
        File.WriteAllText(Path.Combine(_tempDir, ".hidden"), "");
        File.WriteAllText(Path.Combine(_tempDir, "visible.txt"), "");

        using InputPipeline pipeline = CreateTestPipeline();
        App.ScanFilesForFinder(_tempDir, showHidden: true, showSystem: true, pipeline, CancellationToken.None);

        List<FileSystemEntry>? entries = DrainPipeline(pipeline);
        Assert.NotNull(entries);
        Assert.Contains(entries, e => e.Name == ".hidden");
        Assert.Contains(entries, e => e.Name == "visible.txt");
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

        List<FileSystemEntry>? entries = DrainPipeline(pipeline);
        Assert.NotNull(entries);
        Assert.DoesNotContain(entries, e => e.FullPath.Contains(".git"));
        Assert.DoesNotContain(entries, e => e.Name == ".git");
        Assert.Contains(entries, e => e.Name == "readme.md");
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

        List<FileSystemEntry>? entries = DrainPipeline(pipeline);
        Assert.NotNull(entries);
        Assert.DoesNotContain(entries, e => e.Name == ".hidden");
        Assert.DoesNotContain(entries, e => e.Name == "secret.txt");
        Assert.Contains(entries, e => e.Name == "visible.txt");
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

        List<FileSystemEntry>? entries = DrainPipeline(pipeline);
        Assert.NotNull(entries);
        Assert.Contains(entries, e => e.Name == ".hidden" && e.IsDirectory);
        Assert.Contains(entries, e => e.Name == "secret.txt");
        Assert.Contains(entries, e => e.Name == "visible.txt");
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

        List<FileSystemEntry>? entries = DrainPipeline(pipeline);
        Assert.NotNull(entries);
        Assert.DoesNotContain(entries, e => e.FullPath.Contains(".git"));
        Assert.Contains(entries, e => e.Name == "settings.json");
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
        List<FileSystemEntry>? entries = DrainPipeline(pipeline);
        Assert.Null(entries);
    }

    private static FileSystemEntry MakeEntry(string name, string fullPath) =>
        new(name, fullPath, IsDirectory: false, Size: 0, LastModified: DateTime.Now,
            LinkTarget: null, IsBrokenSymlink: false, IsDrive: false);

    private static InputPipeline CreateTestPipeline()
    {
        var source = new NullInputSource();
        return new InputPipeline(source);
    }

    /// <summary>
    /// Collects all partial result events and the completion event from the pipeline.
    /// Returns the combined list of entries, or null if cancelled/no results.
    /// </summary>
    private static List<FileSystemEntry>? DrainPipeline(InputPipeline pipeline)
    {
        var sentinel = new KeyEvent(ConsoleKey.Escape, '\x1b', false, false, false);
        pipeline.Inject(sentinel);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var entries = new List<FileSystemEntry>();
        bool completed = false;

        while (!cts.IsCancellationRequested)
        {
            InputEvent evt = pipeline.Take(cts.Token);

            if (evt is FileFinderPartialResultEvent partial)
            {
                entries.AddRange(partial.Entries);
                continue;
            }

            if (evt is FileFinderScanCompleteEvent)
            {
                completed = true;
                break;
            }

            if (evt == sentinel)
            {
                return entries.Count > 0 ? entries : null;
            }
        }

        return completed ? entries : null;
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
