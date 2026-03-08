using Wade.FileSystem;

namespace Wade.Tests;

public class DirectoryContentsTests
{
    [Fact]
    public void LoadEntries_ReturnsDirectoriesBeforeFiles()
    {
        // Use the test project's own directory as a known path
        string testDir = Path.GetTempPath();
        var entries = new DirectoryContents().LoadEntries(testDir);

        bool seenFile = false;
        foreach (var entry in entries)
        {
            if (!entry.IsDirectory)
            {
                seenFile = true;
            }
            else if (seenFile)
            {
                Assert.Fail("Directory found after a file — directories should come first.");
            }
        }
    }

    [Fact]
    public void LoadEntries_InvalidPath_ReturnsEmptyList()
    {
        var entries = new DirectoryContents().LoadEntries(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString()));
        Assert.Empty(entries);
    }

    [Fact]
    public void GetEntries_CachesResults()
    {
        var dc = new DirectoryContents();
        string path = Path.GetTempPath();

        var first = dc.GetEntries(path);
        var second = dc.GetEntries(path);

        Assert.Same(first, second);
    }

    [Fact]
    public void Invalidate_ClearsCachedPath()
    {
        var dc = new DirectoryContents();
        string path = Path.GetTempPath();

        var first = dc.GetEntries(path);
        dc.Invalidate(path);
        var second = dc.GetEntries(path);

        Assert.NotSame(first, second);
    }

    // ── Sort mode tests ──────────────────────────────────────────────────────

    private static string CreateSortTestDir()
    {
        string dir = Path.Combine(Path.GetTempPath(), "wade_sort_test_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    private static void CleanupDir(string dir)
    {
        try { Directory.Delete(dir, recursive: true); } catch { }
    }

    [Fact]
    public void SortByModified_OrdersByLastModified()
    {
        string dir = CreateSortTestDir();
        try
        {
            File.WriteAllText(Path.Combine(dir, "old.txt"), "");
            File.SetLastWriteTime(Path.Combine(dir, "old.txt"), new DateTime(2020, 1, 1));
            File.WriteAllText(Path.Combine(dir, "new.txt"), "");
            File.SetLastWriteTime(Path.Combine(dir, "new.txt"), new DateTime(2024, 1, 1));

            var dc = new DirectoryContents { SortMode = SortMode.Modified };
            var entries = dc.LoadEntries(dir);

            Assert.Equal("old.txt", entries[0].Name);
            Assert.Equal("new.txt", entries[1].Name);
        }
        finally { CleanupDir(dir); }
    }

    [Fact]
    public void SortBySize_OrdersByFileSize()
    {
        string dir = CreateSortTestDir();
        try
        {
            File.WriteAllText(Path.Combine(dir, "small.txt"), "a");
            File.WriteAllText(Path.Combine(dir, "big.txt"), new string('x', 1000));

            var dc = new DirectoryContents { SortMode = SortMode.Size };
            var entries = dc.LoadEntries(dir);

            Assert.Equal("small.txt", entries[0].Name);
            Assert.Equal("big.txt", entries[1].Name);
        }
        finally { CleanupDir(dir); }
    }

    [Fact]
    public void SortByExtension_OrdersByExtension()
    {
        string dir = CreateSortTestDir();
        try
        {
            File.WriteAllText(Path.Combine(dir, "file.txt"), "");
            File.WriteAllText(Path.Combine(dir, "file.cs"), "");
            File.WriteAllText(Path.Combine(dir, "file.md"), "");

            var dc = new DirectoryContents { SortMode = SortMode.Extension };
            var entries = dc.LoadEntries(dir);

            Assert.Equal("file.cs", entries[0].Name);
            Assert.Equal("file.md", entries[1].Name);
            Assert.Equal("file.txt", entries[2].Name);
        }
        finally { CleanupDir(dir); }
    }

    [Fact]
    public void SortByExtension_TiebreaksByName()
    {
        string dir = CreateSortTestDir();
        try
        {
            File.WriteAllText(Path.Combine(dir, "beta.txt"), "");
            File.WriteAllText(Path.Combine(dir, "alpha.txt"), "");

            var dc = new DirectoryContents { SortMode = SortMode.Extension };
            var entries = dc.LoadEntries(dir);

            Assert.Equal("alpha.txt", entries[0].Name);
            Assert.Equal("beta.txt", entries[1].Name);
        }
        finally { CleanupDir(dir); }
    }

    [Fact]
    public void SortDescending_ReversesOrder()
    {
        string dir = CreateSortTestDir();
        try
        {
            File.WriteAllText(Path.Combine(dir, "aaa.txt"), "");
            File.WriteAllText(Path.Combine(dir, "zzz.txt"), "");

            var dc = new DirectoryContents { SortMode = SortMode.Name, SortAscending = false };
            var entries = dc.LoadEntries(dir);

            Assert.Equal("zzz.txt", entries[0].Name);
            Assert.Equal("aaa.txt", entries[1].Name);
        }
        finally { CleanupDir(dir); }
    }

    [Fact]
    public void SortDescending_DirectoriesStillFirst()
    {
        string dir = CreateSortTestDir();
        try
        {
            Directory.CreateDirectory(Path.Combine(dir, "subdir"));
            File.WriteAllText(Path.Combine(dir, "file.txt"), "");

            var dc = new DirectoryContents { SortMode = SortMode.Name, SortAscending = false };
            var entries = dc.LoadEntries(dir);

            Assert.True(entries[0].IsDirectory);
            Assert.False(entries[1].IsDirectory);
        }
        finally { CleanupDir(dir); }
    }

    // ── System+Hidden filtering (Windows only) ─────────────────────────────

    [Fact]
    public void LoadEntries_WindowsSystemHiddenEntries_AlwaysExcluded()
    {
        if (!OperatingSystem.IsWindows())
        {
            return; // FileAttributes.System is Windows-only
        }

        string dir = CreateSortTestDir();
        try
        {
            // Create a subdirectory and file with System | Hidden attributes
            string subDir = Path.Combine(dir, "SystemHiddenDir");
            Directory.CreateDirectory(subDir);
            File.SetAttributes(subDir, FileAttributes.Directory | FileAttributes.System | FileAttributes.Hidden);

            string filePath = Path.Combine(dir, "systemhidden.txt");
            File.WriteAllText(filePath, "test");
            File.SetAttributes(filePath, FileAttributes.System | FileAttributes.Hidden);

            // Also create a normal entry to ensure it's still returned
            File.WriteAllText(Path.Combine(dir, "normal.txt"), "test");

            // Even with ShowHiddenFiles = true, system+hidden entries should be excluded
            var dc = new DirectoryContents { ShowHiddenFiles = true };
            var entries = dc.LoadEntries(dir);

            Assert.DoesNotContain(entries, e => e.Name == "SystemHiddenDir");
            Assert.DoesNotContain(entries, e => e.Name == "systemhidden.txt");
            Assert.Contains(entries, e => e.Name == "normal.txt");
        }
        finally { CleanupDir(dir); }
    }
}
