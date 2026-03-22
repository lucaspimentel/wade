namespace Wade.Tests;

public class BookmarkStoreTests
{
    private static string CreateTempFile(string content = "")
    {
        string path = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        File.WriteAllText(path, content);
        return path;
    }

    [Fact]
    public void Load_EmptyFile_ReturnsEmptyList()
    {
        string path = CreateTempFile();
        try
        {
            var store = new BookmarkStore(path);
            store.Load();
            Assert.Empty(store.Bookmarks);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Load_NonexistentFile_ReturnsEmptyList()
    {
        var store = new BookmarkStore("/nonexistent/path/bookmarks");
        store.Load();
        Assert.Empty(store.Bookmarks);
    }

    [Fact]
    public void Load_SkipsEmptyLinesAndComments()
    {
        string path = CreateTempFile("# comment\n\n/home/user\n\n# another\n/var/log\n");
        try
        {
            var store = new BookmarkStore(path);
            store.Load();
            Assert.Equal(2, store.Bookmarks.Count);
            Assert.Equal("/home/user", store.Bookmarks[0]);
            Assert.Equal("/var/log", store.Bookmarks[1]);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Add_PrependsToList()
    {
        string path = CreateTempFile();
        try
        {
            var store = new BookmarkStore(path);
            store.Load();
            store.Add("/first");
            store.Add("/second");
            Assert.Equal("/second", store.Bookmarks[0]);
            Assert.Equal("/first", store.Bookmarks[1]);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Add_PersistsToFile()
    {
        string path = CreateTempFile();
        try
        {
            var store = new BookmarkStore(path);
            store.Load();
            store.Add("/persisted");

            var store2 = new BookmarkStore(path);
            store2.Load();
            Assert.Single(store2.Bookmarks);
            Assert.Equal("/persisted", store2.Bookmarks[0]);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Add_Duplicate_MovesToTop()
    {
        string path = CreateTempFile();
        try
        {
            var store = new BookmarkStore(path);
            store.Load();
            store.Add("/first");
            store.Add("/second");
            store.Add("/first"); // re-add existing
            Assert.Equal(2, store.Bookmarks.Count);
            Assert.Equal("/first", store.Bookmarks[0]);
            Assert.Equal("/second", store.Bookmarks[1]);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Remove_RemovesEntry()
    {
        string path = CreateTempFile();
        try
        {
            var store = new BookmarkStore(path);
            store.Load();
            store.Add("/a");
            store.Add("/b");
            store.Remove("/b");
            Assert.Single(store.Bookmarks);
            Assert.Equal("/a", store.Bookmarks[0]);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Toggle_AddsIfAbsent_RemovesIfPresent()
    {
        string path = CreateTempFile();
        try
        {
            var store = new BookmarkStore(path);
            store.Load();

            store.Toggle("/toggled");
            Assert.Single(store.Bookmarks);
            Assert.Equal("/toggled", store.Bookmarks[0]);

            store.Toggle("/toggled");
            Assert.Empty(store.Bookmarks);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Contains_ReturnsTrueForExisting()
    {
        string path = CreateTempFile();
        try
        {
            var store = new BookmarkStore(path);
            store.Load();
            store.Add("/exists");
            Assert.True(store.Contains("/exists"));
            Assert.False(store.Contains("/nope"));
        }
        finally
        {
            File.Delete(path);
        }
    }
}
