namespace Wade.Tests;

public class CliToolTests
{
    [Fact]
    public void IsAvailable_CachedResult_ReturnsSameValue()
    {
        // Two calls should return the same value (cached)
        bool first = CliTool.IsAvailable("git", "--version", requireZeroExitCode: true);
        bool second = CliTool.IsAvailable("git", "--version", requireZeroExitCode: true);
        Assert.Equal(first, second);
    }

    [Fact]
    public void Run_NonExistentTool_ReturnsNull()
    {
        string? result = CliTool.Run("wade_nonexistent_tool_xyz", ["--version"]);
        Assert.Null(result);
    }

    [Fact]
    public void IsAvailable_NonExistentTool_ReturnsFalse()
    {
        bool result = CliTool.IsAvailable("wade_nonexistent_tool_xyz");
        Assert.False(result);
    }

    [Fact]
    public void Run_WithArgs_ReturnsOutput()
    {
        // git --version is available in CI and dev environments
        if (!CliTool.IsAvailable("git", "--version", requireZeroExitCode: true))
        {
            return; // skip if git not available
        }

        string? output = CliTool.Run("git", ["--version"]);
        Assert.NotNull(output);
        Assert.Contains("git version", output);
    }
}
