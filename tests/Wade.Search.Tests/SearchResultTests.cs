using Xunit;

namespace Wade.Search.Tests;

public class SearchResultTests
{
    [Fact]
    public void PrefixMatch_ScoreIsZero()
    {
        var result = new SearchResult("path/file.txt", EditDistance: 0, IsPrefixMatch: true);
        Assert.Equal(0, result.Score);
    }

    [Fact]
    public void FuzzyMatch_ScoreIsEditDistance()
    {
        var result = new SearchResult("path/file.txt", EditDistance: 2, IsPrefixMatch: false);
        Assert.Equal(2, result.Score);
    }

    [Fact]
    public void PrefixMatch_ScoreIsZero_EvenWithNonZeroEditDistance()
    {
        // Prefix match always wins regardless of edit distance field.
        var result = new SearchResult("path/file.txt", EditDistance: 3, IsPrefixMatch: true);
        Assert.Equal(0, result.Score);
    }
}
