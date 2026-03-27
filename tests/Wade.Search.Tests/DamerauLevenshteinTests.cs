using Xunit;

namespace Wade.Search.Tests;

public class DamerauLevenshteinTests
{
    [Theory]
    [InlineData("hello", "hello", 0)]   // identical
    [InlineData("", "", 0)]              // both empty
    [InlineData("", "hello", 5)]         // empty source → target length
    [InlineData("hello", "", 5)]         // empty target → source length
    [InlineData("a", "a", 0)]            // single char, same
    [InlineData("a", "b", 1)]            // single char, different
    [InlineData("Hello", "hello", 0)]    // case insensitive
    [InlineData("ABC", "abc", 0)]        // case insensitive
    public void BasicDistance(string source, string target, int expected)
    {
        Assert.Equal(expected, DamerauLevenshtein.Distance(source, target));
    }

    [Theory]
    [InlineData("helo", "hello", 1)]       // insertion
    [InlineData("hello", "helo", 1)]       // deletion
    [InlineData("hello", "hallo", 1)]      // substitution
    [InlineData("ab", "ba", 1)]            // adjacent transposition
    [InlineData("abcde", "abced", 1)]      // transposition in longer string
    [InlineData("kitten", "sitting", 3)]   // multiple operations
    public void EditOperations(string source, string target, int expected)
    {
        Assert.Equal(expected, DamerauLevenshtein.Distance(source, target));
    }

    [Theory]
    [InlineData("abc", "abd", 1, 1)]            // within threshold
    [InlineData("abc", "xyz", 1, int.MaxValue)]  // exceeds threshold → early termination
    [InlineData("a", "abcd", 2, int.MaxValue)]   // length difference exceeds threshold
    public void MaxDistance(string source, string target, int maxDistance, int expected)
    {
        Assert.Equal(expected, DamerauLevenshtein.Distance(source, target, maxDistance));
    }
}
