using System.Threading.Channels;
using Xunit;

namespace Wade.Search.Tests;

public class SearchIndexTests
{
    [Fact]
    public void Add_IncrementsCount()
    {
        var index = new SearchIndex();
        index.Add(MakePath("src", "Wade", "App.cs"));
        index.Add(MakePath("src", "Wade", "Program.cs"));

        Assert.Equal(2, index.Count);
    }

    [Fact]
    public void Add_DuplicatePath_NotCounted()
    {
        var index = new SearchIndex();
        string path = MakePath("src", "Wade", "App.cs");
        index.Add(path);
        index.Add(path);

        Assert.Equal(1, index.Count);
    }

    [Fact]
    public async Task Search_PrefixMatch_FindsResults()
    {
        var index = new SearchIndex();
        index.Add(MakePath("src", "Wade", "App.cs"));
        index.Add(MakePath("src", "Wade", "Program.cs"));
        index.Add(MakePath("tests", "Wade.Tests", "Test.cs"));

        ChannelReader<SearchResult> reader = index.Search("App");
        List<SearchResult> results = await DrainResultsAsync(index, reader);

        Assert.Contains(results, r => r.Path == MakePath("src", "Wade", "App.cs") && r.IsPrefixMatch);
    }

    [Fact]
    public async Task Search_PrefixMatch_CaseInsensitive()
    {
        var index = new SearchIndex();
        index.Add(MakePath("src", "Wade", "App.cs"));

        ChannelReader<SearchResult> reader = index.Search("app");
        List<SearchResult> results = await DrainResultsAsync(index, reader);

        Assert.Single(results);
        Assert.True(results[0].IsPrefixMatch);
    }

    [Fact]
    public async Task Search_FuzzyMatch_FindsNearMatches()
    {
        var index = new SearchIndex();
        index.Add(MakePath("src", "Wade", "App.cs"));

        // "Wede" is 1 edit from "Wade" (substitution a→e). Not a prefix of any segment.
        ChannelReader<SearchResult> reader = index.Search("Wede", new SearchOptions { MaxEditDistance = 1 });
        List<SearchResult> results = await DrainResultsAsync(index, reader);

        Assert.Single(results);
        Assert.False(results[0].IsPrefixMatch);
        Assert.Equal(1, results[0].EditDistance);
    }

    [Fact]
    public async Task Search_FuzzyMatch_BeyondMaxDistance_NoResults()
    {
        var index = new SearchIndex();
        index.Add(MakePath("src", "Wade", "App.cs"));

        ChannelReader<SearchResult> reader = index.Search("xyz", new SearchOptions { MaxEditDistance = 1 });
        List<SearchResult> results = await DrainResultsAsync(index, reader);

        Assert.Empty(results);
    }

    [Fact]
    public async Task Search_DistinctResults()
    {
        var index = new SearchIndex();
        // Path with duplicate segment names — should still appear only once.
        index.Add(MakePath("src", "src", "file.cs"));

        ChannelReader<SearchResult> reader = index.Search("src");
        List<SearchResult> results = await DrainResultsAsync(index, reader);

        Assert.Single(results);
    }

    [Fact]
    public async Task Search_EmptyQuery_NoResults()
    {
        var index = new SearchIndex();
        index.Add(MakePath("src", "Wade", "App.cs"));

        ChannelReader<SearchResult> reader = index.Search("");
        List<SearchResult> results = await DrainResultsAsync(index, reader);

        Assert.Empty(results);
    }

    [Fact]
    public async Task Search_MaxResults_LimitsOutput()
    {
        var index = new SearchIndex();
        for (int i = 0; i < 100; i++)
        {
            index.Add(MakePath("src", $"File{i}.cs"));
        }

        ChannelReader<SearchResult> reader = index.Search("File", new SearchOptions { MaxResults = 5 });
        List<SearchResult> results = await DrainResultsAsync(index, reader);

        Assert.True(results.Count <= 5);
    }

    [Fact]
    public async Task Search_NewQuery_CancelsPrevious()
    {
        var index = new SearchIndex();
        index.Add(MakePath("src", "Wade", "App.cs"));

        ChannelReader<SearchResult> reader1 = index.Search("App");
        ChannelReader<SearchResult> reader2 = index.Search("Wade");

        // reader1 should be completed (cancelled).
        List<SearchResult> results1 = await DrainResultsAsync(reader1);
        // reader2 should have results.
        List<SearchResult> results2 = await DrainResultsAsync(index, reader2);

        Assert.Contains(results2, r => r.Path == MakePath("src", "Wade", "App.cs"));
        // reader1 was cancelled so may or may not have results — but it should complete.
    }

    [Fact]
    public async Task CancelSearch_CompletesChannel()
    {
        var index = new SearchIndex();
        index.Add(MakePath("src", "Wade", "App.cs"));

        ChannelReader<SearchResult> reader = index.Search("App");
        index.CancelSearch();

        // The channel should eventually complete.
        await reader.Completion.WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public void CancelSearch_NoActiveQuery_IsNoop()
    {
        var index = new SearchIndex();
        index.CancelSearch(); // Should not throw.
    }

    [Fact]
    public async Task LivePush_AddAfterSearch_PushesNewMatch()
    {
        var index = new SearchIndex();
        index.Add(MakePath("src", "existing.cs"));

        ChannelReader<SearchResult> reader = index.Search("newfile");

        // Wait a moment for the snapshot scan to complete.
        await Task.Delay(100);

        // Add a new path that matches.
        index.Add(MakePath("src", "newfile.cs"));

        // Give the live push a moment.
        await Task.Delay(100);

        index.CancelSearch();
        List<SearchResult> results = await DrainResultsAsync(reader);

        Assert.Contains(results, r => r.Path == MakePath("src", "newfile.cs"));
    }

    [Fact]
    public void Clear_EmptiesIndex()
    {
        var index = new SearchIndex();
        index.Add(MakePath("src", "file.cs"));
        Assert.Equal(1, index.Count);

        index.Clear();
        Assert.Equal(0, index.Count);
    }

    [Fact]
    public async Task PrefixMatches_RankedBeforeFuzzy()
    {
        var index = new SearchIndex();
        index.Add(MakePath("src", "Wade"));         // "Wade" is a prefix match for "Wade"
        index.Add(MakePath("src", "Wede"));         // "Wede" is 1 edit from "Wade" (not a prefix match)

        ChannelReader<SearchResult> reader = index.Search("Wade", new SearchOptions { MaxEditDistance = 2 });
        List<SearchResult> results = await DrainResultsAsync(index, reader);

        Assert.True(results.Count >= 2);

        SearchResult prefixResult = results.First(r => r.Path == MakePath("src", "Wade"));
        SearchResult fuzzyResult = results.First(r => r.Path == MakePath("src", "Wede"));

        Assert.True(prefixResult.Score < fuzzyResult.Score);
    }

    [Fact]
    public async Task ConcurrentAddAndSearch_NoExceptions()
    {
        var index = new SearchIndex();

        // Start a search.
        ChannelReader<SearchResult> reader = index.Search("test");

        // Concurrently add many paths.
        var tasks = new List<Task>();
        for (int i = 0; i < 100; i++)
        {
            int n = i;
            tasks.Add(Task.Run(() => index.Add(MakePath("dir", $"test{n}.cs"))));
        }

        await Task.WhenAll(tasks);
        index.CancelSearch();

        List<SearchResult> results = await DrainResultsAsync(reader);
        // Should have some results (all paths have "test" as prefix of a segment).
        Assert.True(results.Count > 0);
    }

    private static string MakePath(params string[] parts) =>
        string.Join(Path.DirectorySeparatorChar, parts);

    /// <summary>
    /// Drain all results from a channel reader, cancelling the search first to close the channel.
    /// </summary>
    private static async Task<List<SearchResult>> DrainResultsAsync(SearchIndex index, ChannelReader<SearchResult> reader)
    {
        // Give the background scan time to produce results.
        await Task.Delay(200);
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
