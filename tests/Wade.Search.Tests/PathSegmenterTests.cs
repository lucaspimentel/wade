using Xunit;

namespace Wade.Search.Tests;

public class PathSegmenterTests
{
    [Fact]
    public void SplitsOnDirectorySeparator()
    {
        string path = string.Join(Path.DirectorySeparatorChar.ToString(), "src", "Wade", "App.cs");
        string[] segments = PathSegmenter.Split(path);

        Assert.Equal(["src", "Wade", "App.cs"], segments);
    }

    [Fact]
    public void TrailingSeparator_Ignored()
    {
        string path = "src" + Path.DirectorySeparatorChar + "Wade" + Path.DirectorySeparatorChar;
        string[] segments = PathSegmenter.Split(path);

        Assert.Equal(["src", "Wade"], segments);
    }

    [Fact]
    public void LeadingSeparator_Ignored()
    {
        string path = Path.DirectorySeparatorChar + "src" + Path.DirectorySeparatorChar + "file.txt";
        string[] segments = PathSegmenter.Split(path);

        Assert.Equal(["src", "file.txt"], segments);
    }

    [Fact]
    public void SingleSegment()
    {
        string[] segments = PathSegmenter.Split("file.txt");
        Assert.Equal(["file.txt"], segments);
    }

    [Fact]
    public void EmptyString_ReturnsEmpty()
    {
        string[] segments = PathSegmenter.Split("");
        Assert.Empty(segments);
    }

    [Fact]
    public void ConsecutiveSeparators_Ignored()
    {
        string sep = Path.DirectorySeparatorChar.ToString();
        string path = "src" + sep + sep + "Wade";
        string[] segments = PathSegmenter.Split(path);

        Assert.Equal(["src", "Wade"], segments);
    }
}
