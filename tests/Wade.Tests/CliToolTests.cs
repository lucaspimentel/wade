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

    [Fact]
    public void Run_WithCancelledToken_ReturnsNull()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        string? result = CliTool.Run("git", ["--version"], ct: cts.Token);
        Assert.Null(result);
    }

    [Fact]
    public void Run_CancellationDuringExecution_ReturnsQuickly()
    {
        using var cts = new CancellationTokenSource();

        string fileName;
        string[] args;

        if (OperatingSystem.IsWindows())
        {
            fileName = "ping";
            args = ["-n", "100", "localhost"];
        }
        else
        {
            fileName = "sleep";
            args = ["60"];
        }

        // Cancel after 500ms
        cts.CancelAfter(TimeSpan.FromMilliseconds(500));

        var sw = System.Diagnostics.Stopwatch.StartNew();
        string? result = CliTool.Run(fileName, args, timeoutMs: 60_000, ct: cts.Token);
        sw.Stop();

        Assert.Null(result);
        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(5), $"Expected quick return but took {sw.Elapsed}");
    }
}
