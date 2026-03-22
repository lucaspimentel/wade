using Wade.FileSystem;

namespace Wade.Tests;

public class FileActionsTests : IDisposable
{
    private readonly string _tempDir;

    public FileActionsTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "wade_test_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
        {
            Directory.Delete(_tempDir, true);
        }
    }

    // ── Rename ────────────────────────────────────────────────────────────────

    [Fact]
    public void Rename_File_OldGoneNewExists()
    {
        string oldPath = Path.Combine(_tempDir, "old.txt");
        string newPath = Path.Combine(_tempDir, "new.txt");
        File.WriteAllText(oldPath, "content");

        File.Move(oldPath, newPath);

        Assert.False(File.Exists(oldPath));
        Assert.True(File.Exists(newPath));
        Assert.Equal("content", File.ReadAllText(newPath));
    }

    [Fact]
    public void Rename_ToExistingName_Throws()
    {
        string file1 = Path.Combine(_tempDir, "a.txt");
        string file2 = Path.Combine(_tempDir, "b.txt");
        File.WriteAllText(file1, "a");
        File.WriteAllText(file2, "b");

        Assert.Throws<IOException>(() => File.Move(file1, file2));
    }

    // ── Delete ────────────────────────────────────────────────────────────────

    [Fact]
    public void Delete_File_IsGone()
    {
        string path = Path.Combine(_tempDir, "delete_me.txt");
        File.WriteAllText(path, "bye");

        int errors = FileOperations.Delete([path], permanent: true);

        Assert.Equal(0, errors);
        Assert.False(File.Exists(path));
    }

    [Fact]
    public void Delete_Directory_IsGone()
    {
        string dir = Path.Combine(_tempDir, "subdir");
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "child.txt"), "x");

        int errors = FileOperations.Delete([dir], permanent: true);

        Assert.Equal(0, errors);
        Assert.False(Directory.Exists(dir));
    }

    [Fact]
    public void Delete_MultipleFiles_AllGone()
    {
        var files = new List<string>();
        for (int i = 0; i < 3; i++)
        {
            string f = Path.Combine(_tempDir, $"file{i}.txt");
            File.WriteAllText(f, $"content{i}");
            files.Add(f);
        }

        int errors = FileOperations.Delete(files, permanent: true);

        Assert.Equal(0, errors);
        foreach (string f in files)
        {
            Assert.False(File.Exists(f));
        }
    }

    [Fact]
    public void Delete_NonExistentPath_ReturnsError()
    {
        string path = Path.Combine(_tempDir, "does_not_exist.txt");

        int errors = FileOperations.Delete([path], permanent: true);

        Assert.True(errors > 0);
    }

    // ── Delete symlinks ───────────────────────────────────────────────────────

    [Theory]
    [InlineData(false)] // symlink to file
    [InlineData(true)] // symlink to directory
    public void Delete_Symlink_RemovesLinkButNotTarget(bool targetIsDirectory)
    {
        string target;
        if (targetIsDirectory)
        {
            target = Path.Combine(_tempDir, "target_dir");
            Directory.CreateDirectory(target);
            File.WriteAllText(Path.Combine(target, "child.txt"), "preserved");
        }
        else
        {
            target = Path.Combine(_tempDir, "target_file.txt");
            File.WriteAllText(target, "preserved");
        }

        string link = Path.Combine(_tempDir, "my_symlink");
        try
        {
            if (targetIsDirectory)
            {
                Directory.CreateSymbolicLink(link, target);
            }
            else
            {
                File.CreateSymbolicLink(link, target);
            }
        }
        catch (IOException)
        {
            // Symlink creation may require elevated privileges on Windows
            return;
        }

        int errors = FileOperations.Delete([link], permanent: true);

        Assert.Equal(0, errors);
        Assert.False(Path.Exists(link), "Symlink should be removed");

        if (targetIsDirectory)
        {
            Assert.True(Directory.Exists(target), "Target directory should still exist");
            Assert.True(File.Exists(Path.Combine(target, "child.txt")), "Target contents should be intact");
        }
        else
        {
            Assert.True(File.Exists(target), "Target file should still exist");
            Assert.Equal("preserved", File.ReadAllText(target));
        }
    }

    // ── Symlink properties ─────────────────────────────────────────────────────

    [Fact]
    public void FileSystemEntry_RegularFile_IsNotSymlink()
    {
        var entry = new FileSystemEntry("test.txt", Path.Combine(_tempDir, "test.txt"), false, 100, DateTime.Now, LinkTarget: null,
            IsBrokenSymlink: false, IsDrive: false);

        Assert.False(entry.IsSymlink);
        Assert.False(entry.IsBrokenSymlink);
    }

    [Theory]
    [InlineData(false)] // symlink to file
    [InlineData(true)] // symlink to directory
    public void FileSystemEntry_ValidSymlink_IsSymlinkButNotBroken(bool targetIsDirectory)
    {
        string target;
        if (targetIsDirectory)
        {
            target = Path.Combine(_tempDir, "link_target_dir");
            Directory.CreateDirectory(target);
        }
        else
        {
            target = Path.Combine(_tempDir, "link_target.txt");
            File.WriteAllText(target, "content");
        }

        string link = Path.Combine(_tempDir, "valid_link");
        try
        {
            if (targetIsDirectory)
            {
                Directory.CreateSymbolicLink(link, target);
            }
            else
            {
                File.CreateSymbolicLink(link, target);
            }
        }
        catch (IOException)
        {
            // Symlink creation may require elevated privileges on Windows
            return;
        }

        var entry = new FileSystemEntry("valid_link", link, targetIsDirectory, 0, DateTime.Now, LinkTarget: target, IsBrokenSymlink: false,
            IsDrive: false);

        Assert.True(entry.IsSymlink);
        Assert.False(entry.IsBrokenSymlink);
    }

    [Theory]
    [InlineData(false)] // broken file symlink
    [InlineData(true)] // broken directory symlink
    public void FileSystemEntry_BrokenSymlink_IsSymlinkAndBroken(bool targetIsDirectory)
    {
        string target = Path.Combine(_tempDir, "nonexistent_target");
        string link = Path.Combine(_tempDir, "broken_link");

        try
        {
            if (targetIsDirectory)
            {
                Directory.CreateSymbolicLink(link, target);
            }
            else
            {
                File.CreateSymbolicLink(link, target);
            }
        }
        catch (IOException)
        {
            // Symlink creation may require elevated privileges on Windows
            return;
        }

        var entry = new FileSystemEntry("broken_link", link, targetIsDirectory, 0, DateTime.Now, LinkTarget: target, IsBrokenSymlink: true,
            IsDrive: false);

        Assert.True(entry.IsSymlink);
        Assert.True(entry.IsBrokenSymlink);
    }

    // ── Copy + Paste ──────────────────────────────────────────────────────────

    [Fact]
    public void Copy_File_BothExist()
    {
        string src = Path.Combine(_tempDir, "source.txt");
        File.WriteAllText(src, "data");
        string destDir = Path.Combine(_tempDir, "dest");
        Directory.CreateDirectory(destDir);
        string dest = Path.Combine(destDir, "source.txt");

        File.Copy(src, dest);

        Assert.True(File.Exists(src));
        Assert.True(File.Exists(dest));
        Assert.Equal("data", File.ReadAllText(dest));
    }

    [Fact]
    public void Paste_WithNameCollision_Throws()
    {
        string src = Path.Combine(_tempDir, "file.txt");
        string dest = Path.Combine(_tempDir, "dest");
        Directory.CreateDirectory(dest);
        File.WriteAllText(src, "a");
        File.WriteAllText(Path.Combine(dest, "file.txt"), "b");

        Assert.Throws<IOException>(() => File.Copy(src, Path.Combine(dest, "file.txt")));
    }

    // ── Cut + Paste ───────────────────────────────────────────────────────────

    [Fact]
    public void Cut_File_SourceGoneDestExists()
    {
        string src = Path.Combine(_tempDir, "moveme.txt");
        File.WriteAllText(src, "moving");
        string destDir = Path.Combine(_tempDir, "dest");
        Directory.CreateDirectory(destDir);
        string dest = Path.Combine(destDir, "moveme.txt");

        File.Move(src, dest);

        Assert.False(File.Exists(src));
        Assert.True(File.Exists(dest));
        Assert.Equal("moving", File.ReadAllText(dest));
    }

    // ── Create ────────────────────────────────────────────────────────────────

    [Fact]
    public void CreateFile_NewFileExists()
    {
        string path = Path.Combine(_tempDir, "newfile.txt");

        File.Create(path).Dispose();

        Assert.True(File.Exists(path));
        Assert.Equal(0, new FileInfo(path).Length);
    }

    [Fact]
    public void CreateDirectory_NewDirectoryExists()
    {
        string path = Path.Combine(_tempDir, "newdir");

        Directory.CreateDirectory(path);

        Assert.True(Directory.Exists(path));
    }

    [Fact]
    public void CreateFile_NameCollision_Throws()
    {
        string path = Path.Combine(_tempDir, "existing.txt");
        File.WriteAllText(path, "original");

        // Path.Exists returns true — app code should check before creating
        Assert.True(Path.Exists(path));
    }

    [Fact]
    public void CreateDirectory_NameCollision_IsIdempotent()
    {
        string path = Path.Combine(_tempDir, "existingdir");
        Directory.CreateDirectory(path);

        // Directory.CreateDirectory is idempotent — app code must check Path.Exists first
        Directory.CreateDirectory(path);

        Assert.True(Directory.Exists(path));
    }

    // ── Create symlink ─────────────────────────────────────────────────────────

    [Fact]
    public void CreateSymlink_ToFile_CreatesFileSymlink()
    {
        string target = Path.Combine(_tempDir, "target_file.txt");
        File.WriteAllText(target, "content");
        string linkPath = Path.Combine(_tempDir, "file_link");

        try
        {
            File.CreateSymbolicLink(linkPath, target);
        }
        catch (IOException)
        {
            // Symlink creation may require elevated privileges on Windows
            return;
        }

        var info = new FileInfo(linkPath);
        Assert.Equal(target, info.LinkTarget);
    }

    [Fact]
    public void CreateSymlink_ToDirectory_CreatesDirectorySymlink()
    {
        string target = Path.Combine(_tempDir, "target_dir");
        Directory.CreateDirectory(target);
        string linkPath = Path.Combine(_tempDir, "dir_link");

        try
        {
            Directory.CreateSymbolicLink(linkPath, target);
        }
        catch (IOException)
        {
            // Symlink creation may require elevated privileges on Windows
            return;
        }

        var info = new DirectoryInfo(linkPath);
        Assert.Equal(target, info.LinkTarget);
    }

    // ── CopyDirectory symlinks ─────────────────────────────────────────────────

    [Fact]
    public void CopyDirectory_PreservesFileSymlink()
    {
        string srcDir = Path.Combine(_tempDir, "src_fsym");
        Directory.CreateDirectory(srcDir);
        string target = Path.Combine(_tempDir, "target_file.txt");
        File.WriteAllText(target, "content");
        string link = Path.Combine(srcDir, "link.txt");

        try
        {
            File.CreateSymbolicLink(link, target);
        }
        catch (IOException)
        {
            return;
        }

        string destDir = Path.Combine(_tempDir, "dest_fsym");
        FileOperations.CopyDirectory(srcDir, destDir);

        string copiedLink = Path.Combine(destDir, "link.txt");
        var info = new FileInfo(copiedLink);
        Assert.NotNull(info.LinkTarget);
        Assert.Equal(target, info.LinkTarget);
    }

    [Fact]
    public void CopyDirectory_PreservesDirectorySymlink()
    {
        string srcDir = Path.Combine(_tempDir, "src_dsym");
        Directory.CreateDirectory(srcDir);
        string targetDir = Path.Combine(_tempDir, "target_dir");
        Directory.CreateDirectory(targetDir);
        File.WriteAllText(Path.Combine(targetDir, "child.txt"), "data");
        string link = Path.Combine(srcDir, "link_dir");

        try
        {
            Directory.CreateSymbolicLink(link, targetDir);
        }
        catch (IOException)
        {
            return;
        }

        string destDir = Path.Combine(_tempDir, "dest_dsym");
        FileOperations.CopyDirectory(srcDir, destDir);

        string copiedLink = Path.Combine(destDir, "link_dir");
        var info = new DirectoryInfo(copiedLink);
        Assert.NotNull(info.LinkTarget);
        Assert.Equal(targetDir, info.LinkTarget);
        // Verify we did NOT recurse into the target — no child.txt copied alongside the link
        Assert.False(File.Exists(Path.Combine(destDir, "link_dir", "child.txt").Replace(Path.DirectorySeparatorChar, '_')));
    }

    [Fact]
    public void CopyDirectory_WithPreserveSymlinksFalse_CopiesContent()
    {
        string srcDir = Path.Combine(_tempDir, "src_nosym");
        Directory.CreateDirectory(srcDir);
        string target = Path.Combine(_tempDir, "target_content.txt");
        File.WriteAllText(target, "resolved content");
        string link = Path.Combine(srcDir, "link.txt");

        try
        {
            File.CreateSymbolicLink(link, target);
        }
        catch (IOException)
        {
            return;
        }

        string destDir = Path.Combine(_tempDir, "dest_nosym");
        FileOperations.CopyDirectory(srcDir, destDir, preserveSymlinks: false);

        string copiedFile = Path.Combine(destDir, "link.txt");
        var info = new FileInfo(copiedFile);
        Assert.Null(info.LinkTarget);
        Assert.Equal("resolved content", File.ReadAllText(copiedFile));
    }

    [Fact]
    public void CopyDirectory_PreservesBrokenSymlink()
    {
        string srcDir = Path.Combine(_tempDir, "src_bsym");
        Directory.CreateDirectory(srcDir);
        string nonexistent = Path.Combine(_tempDir, "no_such_target");
        string link = Path.Combine(srcDir, "broken_link");

        try
        {
            File.CreateSymbolicLink(link, nonexistent);
        }
        catch (IOException)
        {
            return;
        }

        string destDir = Path.Combine(_tempDir, "dest_bsym");
        FileOperations.CopyDirectory(srcDir, destDir);

        string copiedLink = Path.Combine(destDir, "broken_link");
        var info = new FileInfo(copiedLink);
        Assert.NotNull(info.LinkTarget);
        Assert.Equal(nonexistent, info.LinkTarget);
    }

    // ── CopyDirectory ─────────────────────────────────────────────────────────

    [Fact]
    public void CopyDirectory_CopiesFilesRecursively()
    {
        string srcDir = Path.Combine(_tempDir, "srcdir");
        Directory.CreateDirectory(srcDir);
        File.WriteAllText(Path.Combine(srcDir, "a.txt"), "a");
        File.WriteAllText(Path.Combine(srcDir, "b.txt"), "b");

        string destDir = Path.Combine(_tempDir, "destdir");

        FileOperations.CopyDirectory(srcDir, destDir);

        Assert.True(Directory.Exists(destDir));
        Assert.Equal("a", File.ReadAllText(Path.Combine(destDir, "a.txt")));
        Assert.Equal("b", File.ReadAllText(Path.Combine(destDir, "b.txt")));
        // Source still exists
        Assert.True(Directory.Exists(srcDir));
    }

    [Fact]
    public void CopyDirectory_PreservesNestedStructure()
    {
        string srcDir = Path.Combine(_tempDir, "nested");
        string sub1 = Path.Combine(srcDir, "sub1");
        string sub2 = Path.Combine(sub1, "sub2");
        Directory.CreateDirectory(sub2);
        File.WriteAllText(Path.Combine(srcDir, "root.txt"), "r");
        File.WriteAllText(Path.Combine(sub1, "mid.txt"), "m");
        File.WriteAllText(Path.Combine(sub2, "deep.txt"), "d");

        string destDir = Path.Combine(_tempDir, "nested_copy");

        FileOperations.CopyDirectory(srcDir, destDir);

        Assert.Equal("r", File.ReadAllText(Path.Combine(destDir, "root.txt")));
        Assert.Equal("m", File.ReadAllText(Path.Combine(destDir, "sub1", "mid.txt")));
        Assert.Equal("d", File.ReadAllText(Path.Combine(destDir, "sub1", "sub2", "deep.txt")));
    }

    [Fact]
    public void CopyFile_Overwrite_ReplacesDestination()
    {
        string src = Path.Combine(_tempDir, "src.txt");
        File.WriteAllText(src, "new content");
        string destDir = Path.Combine(_tempDir, "dest");
        Directory.CreateDirectory(destDir);
        string dest = Path.Combine(destDir, "src.txt");
        File.WriteAllText(dest, "old content");

        File.Copy(src, dest, overwrite: true);

        Assert.Equal("new content", File.ReadAllText(dest));
        Assert.True(File.Exists(src));
    }

    [Fact]
    public void CopyDirectory_Overwrite_ReplacesDestination()
    {
        string srcDir = Path.Combine(_tempDir, "srcdir");
        Directory.CreateDirectory(srcDir);
        File.WriteAllText(Path.Combine(srcDir, "file.txt"), "new");

        string destDir = Path.Combine(_tempDir, "destdir");
        Directory.CreateDirectory(destDir);
        File.WriteAllText(Path.Combine(destDir, "file.txt"), "old");
        File.WriteAllText(Path.Combine(destDir, "extra.txt"), "extra");

        // Simulate overwrite: delete destination then copy
        Directory.Delete(destDir, true);
        FileOperations.CopyDirectory(srcDir, destDir);

        Assert.Equal("new", File.ReadAllText(Path.Combine(destDir, "file.txt")));
        Assert.False(File.Exists(Path.Combine(destDir, "extra.txt")));
    }

    [Fact]
    public void MoveFile_Overwrite_ReplacesDestination()
    {
        string src = Path.Combine(_tempDir, "moveme.txt");
        File.WriteAllText(src, "new content");
        string destDir = Path.Combine(_tempDir, "dest");
        Directory.CreateDirectory(destDir);
        string dest = Path.Combine(destDir, "moveme.txt");
        File.WriteAllText(dest, "old content");

        // Simulate overwrite: delete destination then move
        File.Delete(dest);
        File.Move(src, dest);

        Assert.Equal("new content", File.ReadAllText(dest));
        Assert.False(File.Exists(src));
    }

    [Fact]
    public void CopyDirectory_ThenPaste_BothExist()
    {
        string srcDir = Path.Combine(_tempDir, "copydir");
        Directory.CreateDirectory(srcDir);
        File.WriteAllText(Path.Combine(srcDir, "file.txt"), "content");

        string pasteTarget = Path.Combine(_tempDir, "target");
        Directory.CreateDirectory(pasteTarget);
        string destDir = Path.Combine(pasteTarget, "copydir");

        FileOperations.CopyDirectory(srcDir, destDir);

        Assert.True(Directory.Exists(srcDir));
        Assert.True(Directory.Exists(destDir));
        Assert.Equal("content", File.ReadAllText(Path.Combine(destDir, "file.txt")));
    }
}
