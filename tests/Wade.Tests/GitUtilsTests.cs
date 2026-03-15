using Wade.FileSystem;

namespace Wade.Tests;

public class GitUtilsTests
{
    [Fact]
    public void FindRepoRoot_InsideGitRepo_ReturnsRepoRoot()
    {
        // The test project itself is inside the wade git repo
        string testDir = AppContext.BaseDirectory;
        string? root = GitUtils.FindRepoRoot(testDir);

        Assert.NotNull(root);
        Assert.True(Directory.Exists(Path.Combine(root, ".git")),
            $"Expected .git directory at '{root}'");
    }

    [Fact]
    public void FindRepoRoot_OutsideGitRepo_ReturnsNull()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(tempDir);

        try
        {
            string? root = GitUtils.FindRepoRoot(tempDir);
            Assert.Null(root);
        }
        finally
        {
            Directory.Delete(tempDir);
        }
    }

    [Fact]
    public void ReadBranchName_ValidHead_ReturnsBranchName()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        string gitDir = Path.Combine(tempDir, ".git");
        Directory.CreateDirectory(gitDir);

        try
        {
            File.WriteAllText(Path.Combine(gitDir, "HEAD"), "ref: refs/heads/main\n");
            string? branch = GitUtils.ReadBranchName(tempDir);
            Assert.Equal("main", branch);
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public void ReadBranchName_FeatureBranch_ReturnsBranchName()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        string gitDir = Path.Combine(tempDir, ".git");
        Directory.CreateDirectory(gitDir);

        try
        {
            File.WriteAllText(Path.Combine(gitDir, "HEAD"), "ref: refs/heads/feature/my-branch\n");
            string? branch = GitUtils.ReadBranchName(tempDir);
            Assert.Equal("feature/my-branch", branch);
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public void ReadBranchName_DetachedHead_ReturnsNull()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        string gitDir = Path.Combine(tempDir, ".git");
        Directory.CreateDirectory(gitDir);

        try
        {
            File.WriteAllText(Path.Combine(gitDir, "HEAD"), "abc123def456789\n");
            string? branch = GitUtils.ReadBranchName(tempDir);
            Assert.Null(branch);
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public void ReadBranchName_MissingFile_ReturnsNull()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(tempDir);

        try
        {
            string? branch = GitUtils.ReadBranchName(tempDir);
            Assert.Null(branch);
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public void ReadBranchName_WorktreeGitFile_ReturnsBranchName()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(tempDir);

        // Simulate a worktree: .git is a file pointing to the real gitdir
        string realGitDir = Path.Combine(tempDir, "real-gitdir");
        Directory.CreateDirectory(realGitDir);
        File.WriteAllText(Path.Combine(realGitDir, "HEAD"), "ref: refs/heads/worktree-branch\n");
        File.WriteAllText(Path.Combine(tempDir, ".git"), $"gitdir: {realGitDir}\n");

        try
        {
            string? branch = GitUtils.ReadBranchName(tempDir);
            Assert.Equal("worktree-branch", branch);
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Theory]
    [InlineData(" M file.txt", (int)GitFileStatus.Modified)]
    [InlineData("M  file.txt", (int)GitFileStatus.Staged)]
    [InlineData("MM file.txt", (int)(GitFileStatus.Staged | GitFileStatus.Modified))]
    [InlineData("A  file.txt", (int)GitFileStatus.Staged)]
    [InlineData("D  file.txt", (int)GitFileStatus.Staged)]
    [InlineData(" D file.txt", (int)GitFileStatus.Modified)]
    [InlineData("R  old.txt -> new.txt", (int)GitFileStatus.Staged)]
    [InlineData("?? file.txt", (int)GitFileStatus.Untracked)]
    [InlineData("!! file.txt", (int)GitFileStatus.Ignored)]
    [InlineData("UU file.txt", (int)GitFileStatus.Conflict)]
    [InlineData("AA file.txt", (int)GitFileStatus.Conflict)]
    [InlineData("DD file.txt", (int)GitFileStatus.Conflict)]
    public void ParsePorcelainOutput_SingleFile_CorrectStatus(string line, int expectedInt)
    {
        var expected = (GitFileStatus)expectedInt;
        string repoRoot = OperatingSystem.IsWindows() ? @"C:\repo" : "/repo";
        var statuses = GitUtils.ParsePorcelainOutput(line + "\n", repoRoot);

        // Find the file entry (not a directory aggregate)
        var fileEntries = statuses
            .Where(kv => !Directory.Exists(kv.Key) && !kv.Key.Equals(repoRoot, StringComparison.OrdinalIgnoreCase))
            .ToList();

        Assert.Single(fileEntries);
        Assert.Equal(expected, fileEntries[0].Value);
    }

    [Fact]
    public void ParsePorcelainOutput_RenamedFile_UsesNewPath()
    {
        string repoRoot = OperatingSystem.IsWindows() ? @"C:\repo" : "/repo";
        string output = "R  old.txt -> new.txt\n";
        var statuses = GitUtils.ParsePorcelainOutput(output, repoRoot);

        string expectedPath = Path.Combine(repoRoot, "new.txt");
        Assert.True(statuses.ContainsKey(expectedPath), $"Expected key '{expectedPath}' in statuses");
    }

    [Fact]
    public void ParsePorcelainOutput_AggregatesDirectoryStatuses()
    {
        string repoRoot = OperatingSystem.IsWindows() ? @"C:\repo" : "/repo";
        string output = " M src/foo/bar.cs\nA  src/baz.cs\n";
        var statuses = GitUtils.ParsePorcelainOutput(output, repoRoot);

        // The "src" directory should have both Modified and Staged aggregated
        string srcDir = Path.Combine(repoRoot, "src");
        Assert.True(statuses.ContainsKey(srcDir), $"Expected aggregated status for '{srcDir}'");
        Assert.True(statuses[srcDir].HasFlag(GitFileStatus.Modified));
        Assert.True(statuses[srcDir].HasFlag(GitFileStatus.Staged));
    }

    [Fact]
    public void ParsePorcelainOutput_ForwardSlashesNormalized()
    {
        string repoRoot = OperatingSystem.IsWindows() ? @"C:\repo" : "/repo";
        string output = " M src/deep/file.cs\n";
        var statuses = GitUtils.ParsePorcelainOutput(output, repoRoot);

        string expectedPath = Path.Combine(repoRoot, "src", "deep", "file.cs");
        Assert.True(statuses.ContainsKey(expectedPath), $"Expected key '{expectedPath}' with platform separators");
    }

    [Fact]
    public void ParsePorcelainOutput_EmptyOutput_ReturnsEmptyDictionary()
    {
        string repoRoot = OperatingSystem.IsWindows() ? @"C:\repo" : "/repo";
        var statuses = GitUtils.ParsePorcelainOutput("", repoRoot);
        Assert.Empty(statuses);
    }

    [Fact]
    public void ParsePorcelainOutput_IgnoredNotPropagatedToDirectories()
    {
        string repoRoot = OperatingSystem.IsWindows() ? @"C:\repo" : "/repo";
        string output = "!! build/output.dll\n";
        var statuses = GitUtils.ParsePorcelainOutput(output, repoRoot);

        // The file should be Ignored
        string filePath = Path.Combine(repoRoot, "build", "output.dll");
        Assert.Equal(GitFileStatus.Ignored, statuses[filePath]);

        // The parent directory should NOT have aggregated Ignored status
        string buildDir = Path.Combine(repoRoot, "build");
        Assert.False(statuses.ContainsKey(buildDir),
            "Ignored status should not propagate to parent directories");
    }

    [Fact]
    public void QueryStatus_InsideGitRepo_ReturnsStatuses()
    {
        // Use the wade repo itself
        string? repoRoot = GitUtils.FindRepoRoot(AppContext.BaseDirectory);
        Assert.NotNull(repoRoot);

        var statuses = GitUtils.QueryStatus(repoRoot, CancellationToken.None);

        // Should return non-null (even if empty, it's a valid repo)
        Assert.NotNull(statuses);
    }

    [Fact]
    public void QueryStatus_Cancellation_ReturnsNull()
    {
        string? repoRoot = GitUtils.FindRepoRoot(AppContext.BaseDirectory);
        Assert.NotNull(repoRoot);

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var statuses = GitUtils.QueryStatus(repoRoot, cts.Token);
        Assert.Null(statuses);
    }

    [Fact]
    public void ParsePorcelainOutput_QuotedPaths_StripsQuotes()
    {
        string repoRoot = OperatingSystem.IsWindows() ? @"C:\repo" : "/repo";
        string output = " M \"src/special file.txt\"\n";
        var statuses = GitUtils.ParsePorcelainOutput(output, repoRoot);

        string expectedPath = Path.Combine(repoRoot, "src", "special file.txt");
        Assert.True(statuses.ContainsKey(expectedPath), $"Expected key '{expectedPath}' for quoted path");
    }
}
