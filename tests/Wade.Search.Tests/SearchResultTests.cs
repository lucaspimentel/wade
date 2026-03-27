using Xunit;

namespace Wade.Search.Tests;

public class SearchResultTests
{
    [Theory]
    [InlineData(0, true, 0)]   // prefix match → score 0
    [InlineData(3, true, 0)]   // prefix match → score 0 regardless of edit distance
    [InlineData(2, false, 2)]  // fuzzy match → score equals edit distance
    public void Score_ReflectsMatchType(int editDistance, bool isPrefixMatch, int expectedScore)
    {
        var result = new SearchResult("path/file.txt", editDistance, isPrefixMatch);
        Assert.Equal(expectedScore, result.Score);
    }
}
