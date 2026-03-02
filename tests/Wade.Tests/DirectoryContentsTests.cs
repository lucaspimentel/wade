using Wade.FileSystem;

namespace Wade.Tests;

public class DirectoryContentsTests
{
    [Fact]
    public void LoadEntries_ReturnsDirectoriesBeforeFiles()
    {
        // Use the test project's own directory as a known path
        string testDir = Path.GetTempPath();
        var entries = DirectoryContents.LoadEntries(testDir);

        bool seenFile = false;
        foreach (var entry in entries)
        {
            if (!entry.IsDirectory)
                seenFile = true;
            else if (seenFile)
                Assert.Fail("Directory found after a file — directories should come first.");
        }
    }

    [Fact]
    public void LoadEntries_InvalidPath_ReturnsEmptyList()
    {
        var entries = DirectoryContents.LoadEntries(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString()));
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
}
