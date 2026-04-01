using Xunit;

namespace Wade.Search.Tests;

public class SearchResultTests
{
    [Theory]
    [InlineData("path/file.txt", 100)]
    [InlineData("other/file.cs", 0)]
    [InlineData("deep/nested/path.rs", -5)]
    public void Score_ReflectsConstructorValue(string path, int score)
    {
        var result = new SearchResult(path, score, []);
        Assert.Equal(path, result.Path);
        Assert.Equal(score, result.Score);
        Assert.Empty(result.MatchPositions);
    }
}
