using Xunit;

namespace Wade.Search.Tests;

public class SearchQueryTests
{
    [Theory]
    [InlineData("", false)]   // empty input -> Fuzzy mode
    [InlineData("'", true)]   // bare quote -> ExactSubstring mode (no body)
    public void Parse_EmptyBody_IsEmpty(string raw, bool expectedExactMode)
    {
        SearchQuery q = SearchQuery.Parse(raw);

        Assert.Equal(expectedExactMode ? QueryMode.ExactSubstring : QueryMode.Fuzzy, q.Mode);
        Assert.Equal("", q.Text);
        Assert.True(q.IsEmpty);
        Assert.False(q.CaseSensitive);
    }

    [Theory]
    [InlineData("foo")]
    [InlineData("Foo")]
    [InlineData("src/Wade")]
    public void Parse_NoPrefix_ReturnsFuzzy(string raw)
    {
        SearchQuery q = SearchQuery.Parse(raw);

        Assert.Equal(QueryMode.Fuzzy, q.Mode);
        Assert.Equal(raw, q.Text);
        Assert.False(q.IsEmpty);
        Assert.False(q.CaseSensitive); // Fuzzy mode is always case-insensitive today.
    }

    [Theory]
    [InlineData("'foo", "foo", false)]
    [InlineData("'Foo", "Foo", true)]
    [InlineData("'fooBar", "fooBar", true)]
    [InlineData("'src/Wade", "src/Wade", true)]
    [InlineData("'all-lower-with-digits-123", "all-lower-with-digits-123", false)]
    public void Parse_QuotePrefix_ReturnsExactSubstringWithSmartCase(string raw, string expectedText, bool expectedCaseSensitive)
    {
        SearchQuery q = SearchQuery.Parse(raw);

        Assert.Equal(QueryMode.ExactSubstring, q.Mode);
        Assert.Equal(expectedText, q.Text);
        Assert.Equal(expectedCaseSensitive, q.CaseSensitive);
        Assert.False(q.IsEmpty);
    }
}
