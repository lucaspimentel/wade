using System.Threading.Channels;
using Xunit;

namespace Wade.Search.Tests;

public class SearchIndexTests
{
    private static readonly string s_basePath = Path.GetTempPath();

    [Fact]
    public void Add_IncrementsCount()
    {
        var index = new SearchIndex(s_basePath);
        index.Add(MakeAbsPath("src", "Wade", "App.cs"));
        index.Add(MakeAbsPath("src", "Wade", "Program.cs"));

        Assert.Equal(2, index.Count);
    }

    [Fact]
    public void Add_DuplicatePath_NotCounted()
    {
        var index = new SearchIndex(s_basePath);
        string path = MakeAbsPath("src", "Wade", "App.cs");
        index.Add(path);
        index.Add(path);

        Assert.Equal(1, index.Count);
    }

    [Fact]
    public async Task Search_SubsequenceMatch_FindsResults()
    {
        var index = new SearchIndex(s_basePath);
        index.Add(MakeAbsPath("src", "Wade", "App.cs"));
        index.Add(MakeAbsPath("src", "Wade", "Program.cs"));
        index.Add(MakeAbsPath("tests", "Wade.Tests", "Test.cs"));

        ChannelReader<SearchResult> reader = index.Search("App");
        List<SearchResult> results = await DrainResultsAsync(index, reader);

        Assert.Contains(results, r => r.Path == MakeAbsPath("src", "Wade", "App.cs"));
    }

    [Fact]
    public async Task Search_CaseInsensitive()
    {
        var index = new SearchIndex(s_basePath);
        index.Add(MakeAbsPath("src", "Wade", "App.cs"));

        ChannelReader<SearchResult> reader = index.Search("app");
        List<SearchResult> results = await DrainResultsAsync(index, reader);

        Assert.Single(results);
        Assert.True(results[0].Score > int.MinValue);
    }

    [Fact]
    public async Task Search_SubstringInFileName_FindsResults()
    {
        var index = new SearchIndex(s_basePath);
        index.Add(MakeAbsPath("Documents", "report.pdf"));
        index.Add(MakeAbsPath("Documents", "notes.txt"));

        ChannelReader<SearchResult> reader = index.Search("pdf");
        List<SearchResult> results = await DrainResultsAsync(index, reader);

        Assert.Single(results);
        Assert.Equal(MakeAbsPath("Documents", "report.pdf"), results[0].Path);
    }

    [Fact]
    public async Task Search_NoMatch_ReturnsEmpty()
    {
        var index = new SearchIndex(s_basePath);
        index.Add(MakeAbsPath("src", "Wade", "App.cs"));

        ChannelReader<SearchResult> reader = index.Search("xyz");
        List<SearchResult> results = await DrainResultsAsync(index, reader);

        Assert.Empty(results);
    }

    [Fact]
    public async Task Search_DistinctResults()
    {
        var index = new SearchIndex(s_basePath);
        // Path with duplicate segment names — should still appear only once.
        index.Add(MakeAbsPath("src", "src", "file.cs"));

        ChannelReader<SearchResult> reader = index.Search("src");
        List<SearchResult> results = await DrainResultsAsync(index, reader);

        Assert.Single(results);
    }

    [Fact]
    public async Task Search_EmptyQuery_NoResults()
    {
        var index = new SearchIndex(s_basePath);
        index.Add(MakeAbsPath("src", "Wade", "App.cs"));

        ChannelReader<SearchResult> reader = index.Search("");
        List<SearchResult> results = await DrainResultsAsync(index, reader);

        Assert.Empty(results);
    }

    [Fact]
    public async Task Search_MaxResults_LimitsOutput()
    {
        var index = new SearchIndex(s_basePath);
        for (int i = 0; i < 100; i++)
        {
            index.Add(MakeAbsPath("src", $"File{i}.cs"));
        }

        ChannelReader<SearchResult> reader = index.Search("File", new SearchOptions { MaxResults = 5 });
        List<SearchResult> results = await DrainResultsAsync(index, reader);

        Assert.True(results.Count <= 5);
    }

    [Fact]
    public async Task Search_NewQuery_CancelsPrevious()
    {
        var index = new SearchIndex(s_basePath);
        index.Add(MakeAbsPath("src", "Wade", "App.cs"));

        ChannelReader<SearchResult> reader1 = index.Search("App");
        ChannelReader<SearchResult> reader2 = index.Search("Wade");

        // reader1 should be completed (cancelled).
        List<SearchResult> results1 = await DrainResultsAsync(reader1);
        // reader2 should have results.
        List<SearchResult> results2 = await DrainResultsAsync(index, reader2);

        Assert.Contains(results2, r => r.Path == MakeAbsPath("src", "Wade", "App.cs"));
        // reader1 was cancelled so may or may not have results — but it should complete.
    }

    [Fact]
    public void CancelSearch_CompletesChannel()
    {
        var index = new SearchIndex(s_basePath);
        index.Add(MakeAbsPath("src", "Wade", "App.cs"));

        ChannelReader<SearchResult> reader = index.Search("App");
        index.CancelSearch();

        // CancelSearch synchronously calls Writer.TryComplete(), so Completion is already resolved.
        Assert.True(reader.Completion.IsCompleted);
    }

    [Fact]
    public void CancelSearch_NoActiveQuery_IsNoop()
    {
        var index = new SearchIndex(s_basePath);
        index.CancelSearch(); // Should not throw.
    }

    [Fact]
    public async Task LivePush_AddAfterSearch_PushesNewMatch()
    {
        var index = new SearchIndex(s_basePath);
        index.Add(MakeAbsPath("src", "existing.cs"));

        ChannelReader<SearchResult> reader = index.Search("newfile");

        // Wait for the snapshot scan to complete before adding.
        Task? snapshot = index.SnapshotCompleteTask;
        if (snapshot != null)
        {
            await snapshot.WaitAsync(TimeSpan.FromSeconds(5));
        }

        // Add a new path that matches — live push should emit it immediately.
        index.Add(MakeAbsPath("src", "newfile.cs"));

        index.CancelSearch();
        List<SearchResult> results = await DrainResultsAsync(reader);

        Assert.Contains(results, r => r.Path == MakeAbsPath("src", "newfile.cs"));
    }

    [Fact]
    public void Clear_EmptiesIndex()
    {
        var index = new SearchIndex(s_basePath);
        index.Add(MakeAbsPath("src", "file.cs"));
        Assert.Equal(1, index.Count);

        index.Clear();
        Assert.Equal(0, index.Count);
    }

    [Fact]
    public async Task ExactMatch_RankedAboveFuzzy()
    {
        var index = new SearchIndex(s_basePath);
        index.Add(MakeAbsPath("src", "Wade"));         // "Wade" is exact/consecutive
        index.Add(MakeAbsPath("src", "W_a_d_e"));      // "Wade" is spread out

        ChannelReader<SearchResult> reader = index.Search("Wade");
        List<SearchResult> results = await DrainResultsAsync(index, reader);

        Assert.True(results.Count >= 2);

        SearchResult exactResult = results.First(r => r.Path == MakeAbsPath("src", "Wade"));
        SearchResult spreadResult = results.First(r => r.Path == MakeAbsPath("src", "W_a_d_e"));

        Assert.True(exactResult.Score > spreadResult.Score,
            $"Exact score {exactResult.Score} should be > spread score {spreadResult.Score}");
    }

    [Fact]
    public async Task ConcurrentAddAndSearch_NoExceptions()
    {
        var index = new SearchIndex(s_basePath);

        // Start a search.
        ChannelReader<SearchResult> reader = index.Search("test");

        // Concurrently add many paths.
        var tasks = new List<Task>();
        for (int i = 0; i < 100; i++)
        {
            int n = i;
            tasks.Add(Task.Run(() => index.Add(MakeAbsPath("dir", $"test{n}.cs"))));
        }

        await Task.WhenAll(tasks);
        index.CancelSearch();

        List<SearchResult> results = await DrainResultsAsync(reader);
        // Should have some results (all paths contain "test" as a subsequence).
        Assert.True(results.Count > 0);
    }

    [Fact]
    public async Task FileNamePriority_RanksFileNameMatchHigher()
    {
        var index = new SearchIndex(s_basePath);
        index.Add(MakeAbsPath("src", "Wade", "App.cs"));
        index.Add(MakeAbsPath("src", "Applications", "Config.cs"));

        ChannelReader<SearchResult> reader = index.Search("App");
        List<SearchResult> results = await DrainResultsAsync(index, reader);

        Assert.True(results.Count >= 2);

        SearchResult appResult = results.First(r => r.Path == MakeAbsPath("src", "Wade", "App.cs"));
        SearchResult configResult = results.First(r => r.Path == MakeAbsPath("src", "Applications", "Config.cs"));

        Assert.True(appResult.Score > configResult.Score,
            $"App.cs score {appResult.Score} should be > Config.cs score {configResult.Score}");
    }

    private static string MakeAbsPath(params string[] parts) =>
        Path.Combine(s_basePath, string.Join(Path.DirectorySeparatorChar, parts));

    /// <summary>
    /// Drain all results from a channel reader, waiting for the snapshot scan to complete before cancelling.
    /// </summary>
    private static async Task<List<SearchResult>> DrainResultsAsync(SearchIndex index, ChannelReader<SearchResult> reader)
    {
        Task? snapshot = index.SnapshotCompleteTask;
        if (snapshot != null)
        {
            await snapshot.WaitAsync(TimeSpan.FromSeconds(5));
        }

        index.CancelSearch();
        return await DrainResultsAsync(reader);
    }

    /// <summary>
    /// Drain all results from an already-completed channel reader.
    /// </summary>
    private static async Task<List<SearchResult>> DrainResultsAsync(ChannelReader<SearchResult> reader)
    {
        var results = new List<SearchResult>();
        try
        {
            await foreach (SearchResult result in reader.ReadAllAsync())
            {
                results.Add(result);
            }
        }
        catch (ChannelClosedException)
        {
            // Expected.
        }

        return results;
    }
}
