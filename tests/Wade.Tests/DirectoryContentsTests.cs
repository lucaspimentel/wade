using Wade.FileSystem;

namespace Wade.Tests;

public class DirectoryContentsTests
{
    [Fact]
    public void LoadEntries_ReturnsDirectoriesBeforeFiles()
    {
        // Use the test project's own directory as a known path
        string testDir = Path.GetTempPath();
        List<FileSystemEntry> entries = new DirectoryContents().LoadEntries(testDir);

        bool seenFile = false;
        foreach (FileSystemEntry entry in entries)
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
        List<FileSystemEntry> entries = new DirectoryContents().LoadEntries(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString()));
        Assert.Empty(entries);
    }

    [Fact]
    public void GetEntries_CachesResults()
    {
        var dc = new DirectoryContents();
        string path = Path.GetTempPath();

        List<FileSystemEntry> first = dc.GetEntries(path);
        List<FileSystemEntry> second = dc.GetEntries(path);

        Assert.Same(first, second);
    }

    [Fact]
    public void Invalidate_ClearsCachedPath()
    {
        var dc = new DirectoryContents();
        string path = Path.GetTempPath();

        List<FileSystemEntry> first = dc.GetEntries(path);
        dc.Invalidate(path);
        List<FileSystemEntry> second = dc.GetEntries(path);

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
        try
        {
            Directory.Delete(dir, recursive: true);
        }
        catch
        {
        }
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
            List<FileSystemEntry> entries = dc.LoadEntries(dir);

            Assert.Equal("old.txt", entries[0].Name);
            Assert.Equal("new.txt", entries[1].Name);
        }
        finally
        {
            CleanupDir(dir);
        }
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
            List<FileSystemEntry> entries = dc.LoadEntries(dir);

            Assert.Equal("small.txt", entries[0].Name);
            Assert.Equal("big.txt", entries[1].Name);
        }
        finally
        {
            CleanupDir(dir);
        }
    }

    [Fact]
    public void SortBySize_UsesInlineDirSizes_ForDirectories()
    {
        string dir = CreateSortTestDir();
        try
        {
            Directory.CreateDirectory(Path.Combine(dir, "small_dir"));
            Directory.CreateDirectory(Path.Combine(dir, "big_dir"));

            var dirSizes = new Dictionary<string, long>
            {
                [Path.Combine(dir, "small_dir")] = 100,
                [Path.Combine(dir, "big_dir")] = 5000,
            };

            var dc = new DirectoryContents { SortMode = SortMode.Size, DirSizes = dirSizes };
            List<FileSystemEntry> entries = dc.LoadEntries(dir);

            Assert.Equal("small_dir", entries[0].Name);
            Assert.Equal("big_dir", entries[1].Name);
        }
        finally
        {
            CleanupDir(dir);
        }
    }

    [Fact]
    public void SortBySize_UsesInlineDirSizes_Descending()
    {
        string dir = CreateSortTestDir();
        try
        {
            Directory.CreateDirectory(Path.Combine(dir, "small_dir"));
            Directory.CreateDirectory(Path.Combine(dir, "big_dir"));

            var dirSizes = new Dictionary<string, long>
            {
                [Path.Combine(dir, "small_dir")] = 100,
                [Path.Combine(dir, "big_dir")] = 5000,
            };

            var dc = new DirectoryContents { SortMode = SortMode.Size, SortAscending = false, DirSizes = dirSizes };
            List<FileSystemEntry> entries = dc.LoadEntries(dir);

            Assert.Equal("big_dir", entries[0].Name);
            Assert.Equal("small_dir", entries[1].Name);
        }
        finally
        {
            CleanupDir(dir);
        }
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
            List<FileSystemEntry> entries = dc.LoadEntries(dir);

            Assert.Equal("file.cs", entries[0].Name);
            Assert.Equal("file.md", entries[1].Name);
            Assert.Equal("file.txt", entries[2].Name);
        }
        finally
        {
            CleanupDir(dir);
        }
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
            List<FileSystemEntry> entries = dc.LoadEntries(dir);

            Assert.Equal("alpha.txt", entries[0].Name);
            Assert.Equal("beta.txt", entries[1].Name);
        }
        finally
        {
            CleanupDir(dir);
        }
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
            List<FileSystemEntry> entries = dc.LoadEntries(dir);

            Assert.Equal("zzz.txt", entries[0].Name);
            Assert.Equal("aaa.txt", entries[1].Name);
        }
        finally
        {
            CleanupDir(dir);
        }
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
            List<FileSystemEntry> entries = dc.LoadEntries(dir);

            Assert.True(entries[0].IsDirectory);
            Assert.False(entries[1].IsDirectory);
        }
        finally
        {
            CleanupDir(dir);
        }
    }

    // ── Symlink support ────────────────────────────────────────────────────

    [Fact]
    public void FileSystemEntry_LinkTarget_DefaultsToNull()
    {
        var entry = new FileSystemEntry("test.txt", "/tmp/test.txt", false, 100, DateTime.Now, LinkTarget: null, IsBrokenSymlink: false,
            IsDrive: false);
        Assert.Null(entry.LinkTarget);
    }

    [Fact]
    public void FileSystemEntry_LinkTarget_CanBeSet()
    {
        var entry = new FileSystemEntry("link.txt", "/tmp/link.txt", false, 0, DateTime.Now, LinkTarget: "/tmp/target.txt", IsBrokenSymlink: false,
            IsDrive: false);
        Assert.Equal("/tmp/target.txt", entry.LinkTarget);
    }

    [Fact]
    public void LoadEntries_DetectsSymlinks()
    {
        if (OperatingSystem.IsWindows())
        {
            return; // Creating symlinks on Windows requires elevated privileges
        }

        string dir = CreateSortTestDir();
        try
        {
            // Create a real file and a symlink to it
            string targetPath = Path.Combine(dir, "target.txt");
            File.WriteAllText(targetPath, "hello");

            string linkPath = Path.Combine(dir, "link.txt");
            File.CreateSymbolicLink(linkPath, targetPath);

            var dc = new DirectoryContents();
            List<FileSystemEntry> entries = dc.LoadEntries(dir);

            FileSystemEntry targetEntry = entries.Single(e => e.Name == "target.txt");
            FileSystemEntry linkEntry = entries.Single(e => e.Name == "link.txt");

            Assert.Null(targetEntry.LinkTarget);
            Assert.NotNull(linkEntry.LinkTarget);
        }
        finally
        {
            CleanupDir(dir);
        }
    }

    [Fact]
    public void LoadEntries_DetectsDirectorySymlinks()
    {
        if (OperatingSystem.IsWindows())
        {
            return; // Creating symlinks on Windows requires elevated privileges
        }

        string dir = CreateSortTestDir();
        try
        {
            string targetDir = Path.Combine(dir, "realdir");
            Directory.CreateDirectory(targetDir);

            string linkDir = Path.Combine(dir, "linkdir");
            Directory.CreateSymbolicLink(linkDir, targetDir);

            var dc = new DirectoryContents();
            List<FileSystemEntry> entries = dc.LoadEntries(dir);

            FileSystemEntry targetEntry = entries.Single(e => e.Name == "realdir");
            FileSystemEntry linkEntry = entries.Single(e => e.Name == "linkdir");

            Assert.Null(targetEntry.LinkTarget);
            Assert.NotNull(linkEntry.LinkTarget);
            Assert.True(linkEntry.IsDirectory);
        }
        finally
        {
            CleanupDir(dir);
        }
    }

    // ── Junction point support ──────────────────────────────────────────────

    [Fact]
    public void FileSystemEntry_IsJunctionPoint_DefaultsToFalse()
    {
        var entry = new FileSystemEntry("dir", "/tmp/dir", true, 0, DateTime.Now, LinkTarget: null, IsBrokenSymlink: false, IsDrive: false);
        Assert.False(entry.IsJunctionPoint);
    }

    [Fact]
    public void FileSystemEntry_IsJunctionPoint_CanBeSet()
    {
        var entry = new FileSystemEntry("junction", "/tmp/junction", true, 0, DateTime.Now, LinkTarget: "/tmp/target", IsBrokenSymlink: false,
            IsDrive: false, IsJunctionPoint: true);
        Assert.True(entry.IsJunctionPoint);
        Assert.True(entry.IsSymlink); // LinkTarget is set, so IsSymlink is also true
    }

    // ── App execution alias support ─────────────────────────────────────────

    [Fact]
    public void FileSystemEntry_IsAppExecLink_DefaultsToFalse()
    {
        var entry = new FileSystemEntry("wt.exe", "/tmp/wt.exe", false, 0, DateTime.Now, LinkTarget: null, IsBrokenSymlink: false, IsDrive: false);
        Assert.False(entry.IsAppExecLink);
        Assert.Null(entry.AppExecLinkTarget);
    }

    [Fact]
    public void FileSystemEntry_IsAppExecLink_CanBeSet()
    {
        var entry = new FileSystemEntry("wt.exe", "/tmp/wt.exe", false, 0, DateTime.Now, LinkTarget: null, IsBrokenSymlink: false,
            IsDrive: false, IsAppExecLink: true, AppExecLinkTarget: @"C:\Program Files\WindowsApps\wt.exe");
        Assert.True(entry.IsAppExecLink);
        Assert.Equal(@"C:\Program Files\WindowsApps\wt.exe", entry.AppExecLinkTarget);
    }

    // ── System+Hidden filtering (Windows only) ─────────────────────────────

    [Fact]
    public void LoadEntries_WindowsSystemHiddenEntries_ExcludedByDefault()
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

            // With ShowHiddenFiles = true but ShowSystemFiles = false, system+hidden entries should be excluded
            var dc = new DirectoryContents { ShowHiddenFiles = true, ShowSystemFiles = false };
            List<FileSystemEntry> entries = dc.LoadEntries(dir);

            Assert.DoesNotContain(entries, e => e.Name == "SystemHiddenDir");
            Assert.DoesNotContain(entries, e => e.Name == "systemhidden.txt");
            Assert.Contains(entries, e => e.Name == "normal.txt");
        }
        finally
        {
            CleanupDir(dir);
        }
    }

    [Fact]
    public void LoadEntries_WindowsSystemHiddenEntries_ShownWhenEnabled()
    {
        if (!OperatingSystem.IsWindows())
        {
            return; // FileAttributes.System is Windows-only
        }

        string dir = CreateSortTestDir();
        try
        {
            string subDir = Path.Combine(dir, "SystemHiddenDir");
            Directory.CreateDirectory(subDir);
            File.SetAttributes(subDir, FileAttributes.Directory | FileAttributes.System | FileAttributes.Hidden);

            string filePath = Path.Combine(dir, "systemhidden.txt");
            File.WriteAllText(filePath, "test");
            File.SetAttributes(filePath, FileAttributes.System | FileAttributes.Hidden);

            File.WriteAllText(Path.Combine(dir, "normal.txt"), "test");

            // With both ShowHiddenFiles and ShowSystemFiles = true, system+hidden entries should be visible
            var dc = new DirectoryContents { ShowHiddenFiles = true, ShowSystemFiles = true };
            List<FileSystemEntry> entries = dc.LoadEntries(dir);

            Assert.Contains(entries, e => e.Name == "SystemHiddenDir");
            Assert.Contains(entries, e => e.Name == "systemhidden.txt");
            Assert.Contains(entries, e => e.Name == "normal.txt");
        }
        finally
        {
            CleanupDir(dir);
        }
    }

    [Fact]
    public void LoadEntries_WindowsSystemOnlyEntries_ExcludedByDefault()
    {
        if (!OperatingSystem.IsWindows())
        {
            return; // FileAttributes.System is Windows-only
        }

        string dir = CreateSortTestDir();
        try
        {
            string filePath = Path.Combine(dir, "systemonly.txt");
            File.WriteAllText(filePath, "test");
            File.SetAttributes(filePath, FileAttributes.System);

            File.WriteAllText(Path.Combine(dir, "normal.txt"), "test");

            var dc = new DirectoryContents { ShowHiddenFiles = true, ShowSystemFiles = false };
            List<FileSystemEntry> entries = dc.LoadEntries(dir);

            Assert.DoesNotContain(entries, e => e.Name == "systemonly.txt");
            Assert.Contains(entries, e => e.Name == "normal.txt");
        }
        finally
        {
            CleanupDir(dir);
        }
    }

    [Fact]
    public void LoadEntries_WindowsSystemOnlyEntries_ShownWhenEnabled()
    {
        if (!OperatingSystem.IsWindows())
        {
            return; // FileAttributes.System is Windows-only
        }

        string dir = CreateSortTestDir();
        try
        {
            string filePath = Path.Combine(dir, "systemonly.txt");
            File.WriteAllText(filePath, "test");
            File.SetAttributes(filePath, FileAttributes.System);

            File.WriteAllText(Path.Combine(dir, "normal.txt"), "test");

            var dc = new DirectoryContents { ShowHiddenFiles = true, ShowSystemFiles = true };
            List<FileSystemEntry> entries = dc.LoadEntries(dir);

            Assert.Contains(entries, e => e.Name == "systemonly.txt");
            Assert.Contains(entries, e => e.Name == "normal.txt");
        }
        finally
        {
            CleanupDir(dir);
        }
    }
}
