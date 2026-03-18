using Wade.Preview;

namespace Wade.Tests;

public class MsiInteropTests
{
    [Theory]
    [InlineData("SHORTN~1|LongFileName.txt", "LongFileName.txt")]
    [InlineData("plain.txt", "plain.txt")]
    [InlineData("", "")]
    [InlineData("A|B|C", "B|C")]
    [InlineData("SHORT~1|My Long File Name.docx", "My Long File Name.docx")]
    [InlineData("|leading", "leading")]
    public void ParseLongFileName_ReturnsExpected(string input, string expected)
    {
        string result = MsiFileName.ParseLongName(input);
        Assert.Equal(expected, result);
    }
}
