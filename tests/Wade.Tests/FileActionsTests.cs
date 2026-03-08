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
            Directory.Delete(_tempDir, true);
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

        File.Delete(path);

        Assert.False(File.Exists(path));
    }

    [Fact]
    public void Delete_Directory_IsGone()
    {
        string dir = Path.Combine(_tempDir, "subdir");
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "child.txt"), "x");

        Directory.Delete(dir, true);

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

        foreach (string f in files)
            File.Delete(f);

        foreach (string f in files)
            Assert.False(File.Exists(f));
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
