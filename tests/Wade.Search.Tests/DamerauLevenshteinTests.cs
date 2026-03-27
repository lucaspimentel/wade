using Xunit;

namespace Wade.Search.Tests;

public class DamerauLevenshteinTests
{
    [Fact]
    public void IdenticalStrings_ReturnsZero()
    {
        Assert.Equal(0, DamerauLevenshtein.Distance("hello", "hello"));
    }

    [Fact]
    public void EmptySource_ReturnsTargetLength()
    {
        Assert.Equal(5, DamerauLevenshtein.Distance("", "hello"));
    }

    [Fact]
    public void EmptyTarget_ReturnsSourceLength()
    {
        Assert.Equal(5, DamerauLevenshtein.Distance("hello", ""));
    }

    [Fact]
    public void BothEmpty_ReturnsZero()
    {
        Assert.Equal(0, DamerauLevenshtein.Distance("", ""));
    }

    [Fact]
    public void SingleInsertion_ReturnsOne()
    {
        Assert.Equal(1, DamerauLevenshtein.Distance("helo", "hello"));
    }

    [Fact]
    public void SingleDeletion_ReturnsOne()
    {
        Assert.Equal(1, DamerauLevenshtein.Distance("hello", "helo"));
    }

    [Fact]
    public void SingleSubstitution_ReturnsOne()
    {
        Assert.Equal(1, DamerauLevenshtein.Distance("hello", "hallo"));
    }

    [Fact]
    public void AdjacentTransposition_ReturnsOne()
    {
        Assert.Equal(1, DamerauLevenshtein.Distance("ab", "ba"));
    }

    [Fact]
    public void AdjacentTransposition_InLongerString()
    {
        Assert.Equal(1, DamerauLevenshtein.Distance("abcdef", "abcfed"));
    }

    [Fact]
    public void CaseInsensitive()
    {
        Assert.Equal(0, DamerauLevenshtein.Distance("Hello", "hello"));
        Assert.Equal(0, DamerauLevenshtein.Distance("ABC", "abc"));
    }

    [Fact]
    public void MaxDistance_EarlyTermination()
    {
        int result = DamerauLevenshtein.Distance("abc", "xyz", maxDistance: 1);
        Assert.Equal(int.MaxValue, result);
    }

    [Fact]
    public void MaxDistance_WithinThreshold()
    {
        int result = DamerauLevenshtein.Distance("abc", "abd", maxDistance: 1);
        Assert.Equal(1, result);
    }

    [Fact]
    public void MaxDistance_LengthDifferenceExceedsThreshold()
    {
        int result = DamerauLevenshtein.Distance("a", "abcd", maxDistance: 2);
        Assert.Equal(int.MaxValue, result);
    }

    [Fact]
    public void SingleCharacterStrings_Same()
    {
        Assert.Equal(0, DamerauLevenshtein.Distance("a", "a"));
    }

    [Fact]
    public void SingleCharacterStrings_Different()
    {
        Assert.Equal(1, DamerauLevenshtein.Distance("a", "b"));
    }

    [Fact]
    public void MultipleOperations()
    {
        // "kitten" -> "sitting" = 3 (substitute k->s, substitute e->i, insert g)
        Assert.Equal(3, DamerauLevenshtein.Distance("kitten", "sitting"));
    }
}
