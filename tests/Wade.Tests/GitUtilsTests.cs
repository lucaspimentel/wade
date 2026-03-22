using System.Diagnostics;
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

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("C:\\")]
    [InlineData("D:\\")]
    public void FindRepoRoot_NoRepoAbovePath_ReturnsNull(string? path)
    {
        string? root = GitUtils.FindRepoRoot(path!);
        Assert.Null(root);
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
        Dictionary<string, GitFileStatus> statuses = GitUtils.ParsePorcelainOutput(line + "\n", repoRoot);

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
        Dictionary<string, GitFileStatus> statuses = GitUtils.ParsePorcelainOutput(output, repoRoot);

        string expectedPath = Path.Combine(repoRoot, "new.txt");
        Assert.True(statuses.ContainsKey(expectedPath), $"Expected key '{expectedPath}' in statuses");
    }

    [Fact]
    public void ParsePorcelainOutput_AggregatesDirectoryStatuses()
    {
        string repoRoot = OperatingSystem.IsWindows() ? @"C:\repo" : "/repo";
        string output = " M src/foo/bar.cs\nA  src/baz.cs\n";
        Dictionary<string, GitFileStatus> statuses = GitUtils.ParsePorcelainOutput(output, repoRoot);

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
        Dictionary<string, GitFileStatus> statuses = GitUtils.ParsePorcelainOutput(output, repoRoot);

        string expectedPath = Path.Combine(repoRoot, "src", "deep", "file.cs");
        Assert.True(statuses.ContainsKey(expectedPath), $"Expected key '{expectedPath}' with platform separators");
    }

    [Fact]
    public void ParsePorcelainOutput_EmptyOutput_ReturnsEmptyDictionary()
    {
        string repoRoot = OperatingSystem.IsWindows() ? @"C:\repo" : "/repo";
        Dictionary<string, GitFileStatus> statuses = GitUtils.ParsePorcelainOutput("", repoRoot);
        Assert.Empty(statuses);
    }

    [Fact]
    public void ParsePorcelainOutput_IgnoredNotPropagatedToDirectories()
    {
        string repoRoot = OperatingSystem.IsWindows() ? @"C:\repo" : "/repo";
        string output = "!! build/output.dll\n";
        Dictionary<string, GitFileStatus> statuses = GitUtils.ParsePorcelainOutput(output, repoRoot);

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

        Dictionary<string, GitFileStatus>? statuses = GitUtils.QueryStatus(repoRoot, CancellationToken.None);

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

        Dictionary<string, GitFileStatus>? statuses = GitUtils.QueryStatus(repoRoot, cts.Token);
        Assert.Null(statuses);
    }

    [Fact]
    public void ParsePorcelainOutput_QuotedPaths_StripsQuotes()
    {
        string repoRoot = OperatingSystem.IsWindows() ? @"C:\repo" : "/repo";
        string output = " M \"src/special file.txt\"\n";
        Dictionary<string, GitFileStatus> statuses = GitUtils.ParsePorcelainOutput(output, repoRoot);

        string expectedPath = Path.Combine(repoRoot, "src", "special file.txt");
        Assert.True(statuses.ContainsKey(expectedPath), $"Expected key '{expectedPath}' for quoted path");
    }

    [Fact]
    public void GetDiff_ModifiedFile_ReturnsDiffLines()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), "wade-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        try
        {
            // Initialize a git repo, add a file, commit, then modify it
            RunGit(tempDir, "init");
            RunGit(tempDir, "config user.email test@test.com");
            RunGit(tempDir, "config user.name Test");
            RunGit(tempDir, "config commit.gpgsign false");

            string filePath = Path.Combine(tempDir, "test.txt");
            File.WriteAllText(filePath, "line1\nline2\n");
            RunGit(tempDir, "add test.txt");
            RunGit(tempDir, "commit -m initial");

            File.WriteAllText(filePath, "line1\nmodified\n");

            string[]? diff = GitUtils.GetDiff(tempDir, filePath, staged: false, CancellationToken.None);

            Assert.NotNull(diff);
            Assert.True(diff.Length > 0);
            Assert.Contains(diff, l => l.StartsWith("-line2"));
            Assert.Contains(diff, l => l.StartsWith("+modified"));
        }
        finally
        {
            ForceDeleteDirectory(tempDir);
        }
    }

    [Fact]
    public void GetDiff_StagedFile_ReturnsDiffLines()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), "wade-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        try
        {
            RunGit(tempDir, "init");
            RunGit(tempDir, "config user.email test@test.com");
            RunGit(tempDir, "config user.name Test");
            RunGit(tempDir, "config commit.gpgsign false");

            string filePath = Path.Combine(tempDir, "test.txt");
            File.WriteAllText(filePath, "original\n");
            RunGit(tempDir, "add test.txt");
            RunGit(tempDir, "commit -m initial");

            File.WriteAllText(filePath, "staged change\n");
            RunGit(tempDir, "add test.txt");

            string[]? diff = GitUtils.GetDiff(tempDir, filePath, staged: true, CancellationToken.None);

            Assert.NotNull(diff);
            Assert.True(diff.Length > 0);
            Assert.Contains(diff, l => l.StartsWith("-original"));
            Assert.Contains(diff, l => l.StartsWith("+staged change"));
        }
        finally
        {
            ForceDeleteDirectory(tempDir);
        }
    }

    [Fact]
    public void GetDiff_CleanFile_ReturnsNull()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), "wade-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        try
        {
            RunGit(tempDir, "init");
            RunGit(tempDir, "config user.email test@test.com");
            RunGit(tempDir, "config user.name Test");
            RunGit(tempDir, "config commit.gpgsign false");

            string filePath = Path.Combine(tempDir, "test.txt");
            File.WriteAllText(filePath, "clean\n");
            RunGit(tempDir, "add test.txt");
            RunGit(tempDir, "commit -m initial");

            string[]? diff = GitUtils.GetDiff(tempDir, filePath, staged: false, CancellationToken.None);

            Assert.Null(diff);
        }
        finally
        {
            ForceDeleteDirectory(tempDir);
        }
    }

    [Fact]
    public void GetDiff_Cancellation_ReturnsNull()
    {
        string? repoRoot = GitUtils.FindRepoRoot(AppContext.BaseDirectory);
        Assert.NotNull(repoRoot);

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        // Use any file path — cancellation should take effect before or after git runs
        string filePath = Path.Combine(repoRoot, "CLAUDE.md");
        string[]? diff = GitUtils.GetDiff(repoRoot, filePath, staged: false, cts.Token);

        Assert.Null(diff);
    }

    [Fact]
    public void Stage_ModifiedFile_SetsStatusToStaged()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), "wade-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        try
        {
            RunGit(tempDir, "init");
            RunGit(tempDir, "config user.email test@test.com");
            RunGit(tempDir, "config user.name Test");
            RunGit(tempDir, "config commit.gpgsign false");

            string filePath = Path.Combine(tempDir, "test.txt");
            File.WriteAllText(filePath, "original\n");
            RunGit(tempDir, "add test.txt");
            RunGit(tempDir, "commit -m initial");

            File.WriteAllText(filePath, "modified\n");

            Dictionary<string, GitFileStatus>? statuses = GitUtils.QueryStatus(tempDir, CancellationToken.None)!;
            Assert.True(statuses[filePath].HasFlag(GitFileStatus.Modified));

            (bool Success, string? Error) result = GitUtils.Stage(tempDir, [filePath], CancellationToken.None);
            Assert.True(result.Success);

            statuses = GitUtils.QueryStatus(tempDir, CancellationToken.None)!;
            Assert.True(statuses[filePath].HasFlag(GitFileStatus.Staged));
            Assert.False(statuses[filePath].HasFlag(GitFileStatus.Modified));
        }
        finally
        {
            ForceDeleteDirectory(tempDir);
        }
    }

    [Fact]
    public void Stage_UntrackedFile_SetsStatusToStaged()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), "wade-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        try
        {
            RunGit(tempDir, "init");
            RunGit(tempDir, "config user.email test@test.com");
            RunGit(tempDir, "config user.name Test");
            RunGit(tempDir, "config commit.gpgsign false");

            // Need an initial commit so git status works properly
            string readmePath = Path.Combine(tempDir, "README.md");
            File.WriteAllText(readmePath, "readme\n");
            RunGit(tempDir, "add README.md");
            RunGit(tempDir, "commit -m initial");

            string filePath = Path.Combine(tempDir, "new-file.txt");
            File.WriteAllText(filePath, "new content\n");

            Dictionary<string, GitFileStatus>? statuses = GitUtils.QueryStatus(tempDir, CancellationToken.None)!;
            Assert.True(statuses[filePath].HasFlag(GitFileStatus.Untracked));

            (bool Success, string? Error) result = GitUtils.Stage(tempDir, [filePath], CancellationToken.None);
            Assert.True(result.Success);

            statuses = GitUtils.QueryStatus(tempDir, CancellationToken.None)!;
            Assert.True(statuses[filePath].HasFlag(GitFileStatus.Staged));
            Assert.False(statuses[filePath].HasFlag(GitFileStatus.Untracked));
        }
        finally
        {
            ForceDeleteDirectory(tempDir);
        }
    }

    [Fact]
    public void Unstage_StagedModifiedFile_SetsStatusToModified()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), "wade-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        try
        {
            RunGit(tempDir, "init");
            RunGit(tempDir, "config user.email test@test.com");
            RunGit(tempDir, "config user.name Test");
            RunGit(tempDir, "config commit.gpgsign false");

            string filePath = Path.Combine(tempDir, "test.txt");
            File.WriteAllText(filePath, "original\n");
            RunGit(tempDir, "add test.txt");
            RunGit(tempDir, "commit -m initial");

            File.WriteAllText(filePath, "modified\n");
            RunGit(tempDir, "add test.txt");

            Dictionary<string, GitFileStatus>? statuses = GitUtils.QueryStatus(tempDir, CancellationToken.None)!;
            Assert.True(statuses[filePath].HasFlag(GitFileStatus.Staged));

            (bool Success, string? Error) result = GitUtils.Unstage(tempDir, [filePath], CancellationToken.None);
            Assert.True(result.Success);

            statuses = GitUtils.QueryStatus(tempDir, CancellationToken.None)!;
            Assert.True(statuses[filePath].HasFlag(GitFileStatus.Modified));
            Assert.False(statuses[filePath].HasFlag(GitFileStatus.Staged));
        }
        finally
        {
            ForceDeleteDirectory(tempDir);
        }
    }

    [Fact]
    public void Unstage_NewStagedFile_SetsStatusToUntracked()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), "wade-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        try
        {
            RunGit(tempDir, "init");
            RunGit(tempDir, "config user.email test@test.com");
            RunGit(tempDir, "config user.name Test");
            RunGit(tempDir, "config commit.gpgsign false");

            string readmePath = Path.Combine(tempDir, "README.md");
            File.WriteAllText(readmePath, "readme\n");
            RunGit(tempDir, "add README.md");
            RunGit(tempDir, "commit -m initial");

            string filePath = Path.Combine(tempDir, "new-file.txt");
            File.WriteAllText(filePath, "new content\n");
            RunGit(tempDir, "add new-file.txt");

            Dictionary<string, GitFileStatus>? statuses = GitUtils.QueryStatus(tempDir, CancellationToken.None)!;
            Assert.True(statuses[filePath].HasFlag(GitFileStatus.Staged));

            (bool Success, string? Error) result = GitUtils.Unstage(tempDir, [filePath], CancellationToken.None);
            Assert.True(result.Success);

            statuses = GitUtils.QueryStatus(tempDir, CancellationToken.None)!;
            Assert.True(statuses[filePath].HasFlag(GitFileStatus.Untracked));
            Assert.False(statuses[filePath].HasFlag(GitFileStatus.Staged));
        }
        finally
        {
            ForceDeleteDirectory(tempDir);
        }
    }

    [Fact]
    public void StageAll_MultipleDirtyFiles_StagesAll()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), "wade-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        try
        {
            RunGit(tempDir, "init");
            RunGit(tempDir, "config user.email test@test.com");
            RunGit(tempDir, "config user.name Test");
            RunGit(tempDir, "config commit.gpgsign false");

            string file1 = Path.Combine(tempDir, "file1.txt");
            File.WriteAllText(file1, "original\n");
            RunGit(tempDir, "add file1.txt");
            RunGit(tempDir, "commit -m initial");

            // Modified tracked file
            File.WriteAllText(file1, "modified\n");

            // New untracked file
            string file2 = Path.Combine(tempDir, "file2.txt");
            File.WriteAllText(file2, "new\n");

            (bool Success, string? Error) result = GitUtils.StageAll(tempDir, CancellationToken.None);
            Assert.True(result.Success);

            Dictionary<string, GitFileStatus>? statuses = GitUtils.QueryStatus(tempDir, CancellationToken.None)!;
            Assert.True(statuses[file1].HasFlag(GitFileStatus.Staged));
            Assert.True(statuses[file2].HasFlag(GitFileStatus.Staged));
        }
        finally
        {
            ForceDeleteDirectory(tempDir);
        }
    }

    [Fact]
    public void Stage_MultiplePaths_StagesAll()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), "wade-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        try
        {
            RunGit(tempDir, "init");
            RunGit(tempDir, "config user.email test@test.com");
            RunGit(tempDir, "config user.name Test");
            RunGit(tempDir, "config commit.gpgsign false");

            string file1 = Path.Combine(tempDir, "a.txt");
            string file2 = Path.Combine(tempDir, "b.txt");
            File.WriteAllText(file1, "a\n");
            File.WriteAllText(file2, "b\n");
            RunGit(tempDir, "add .");
            RunGit(tempDir, "commit -m initial");

            File.WriteAllText(file1, "a modified\n");
            File.WriteAllText(file2, "b modified\n");

            (bool Success, string? Error) result = GitUtils.Stage(tempDir, [file1, file2], CancellationToken.None);
            Assert.True(result.Success);

            Dictionary<string, GitFileStatus>? statuses = GitUtils.QueryStatus(tempDir, CancellationToken.None)!;
            Assert.True(statuses[file1].HasFlag(GitFileStatus.Staged));
            Assert.True(statuses[file2].HasFlag(GitFileStatus.Staged));
        }
        finally
        {
            ForceDeleteDirectory(tempDir);
        }
    }

    [Fact]
    public void Stage_Cancellation_ReturnsFalse()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), "wade-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        try
        {
            RunGit(tempDir, "init");

            string filePath = Path.Combine(tempDir, "test.txt");
            File.WriteAllText(filePath, "content\n");

            using var cts = new CancellationTokenSource();
            cts.Cancel();

            (bool Success, string? Error) result = GitUtils.Stage(tempDir, [filePath], cts.Token);
            Assert.False(result.Success);
        }
        finally
        {
            ForceDeleteDirectory(tempDir);
        }
    }

    [Fact]
    public void RunGitCommand_ValidCommand_ReturnsSuccess()
    {
        string? repoRoot = GitUtils.FindRepoRoot(AppContext.BaseDirectory);
        Assert.NotNull(repoRoot);

        (bool Success, string? Error) result = GitUtils.RunGitCommand(repoRoot, "status", CancellationToken.None);
        Assert.True(result.Success);
        Assert.Null(result.Error);
    }

    [Fact]
    public void UnstageAll_MultipleStagedFiles_UnstagesAll()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), "wade-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        try
        {
            RunGit(tempDir, "init");
            RunGit(tempDir, "config user.email test@test.com");
            RunGit(tempDir, "config user.name Test");
            RunGit(tempDir, "config commit.gpgsign false");

            string file1 = Path.Combine(tempDir, "file1.txt");
            string file2 = Path.Combine(tempDir, "file2.txt");
            File.WriteAllText(file1, "original\n");
            File.WriteAllText(file2, "original\n");
            RunGit(tempDir, "add .");
            RunGit(tempDir, "commit -m initial");

            File.WriteAllText(file1, "modified\n");
            File.WriteAllText(file2, "modified\n");
            RunGit(tempDir, "add .");

            Dictionary<string, GitFileStatus>? statuses = GitUtils.QueryStatus(tempDir, CancellationToken.None)!;
            Assert.True(statuses[file1].HasFlag(GitFileStatus.Staged));
            Assert.True(statuses[file2].HasFlag(GitFileStatus.Staged));

            (bool Success, string? Error) result = GitUtils.UnstageAll(tempDir, CancellationToken.None);
            Assert.True(result.Success);

            statuses = GitUtils.QueryStatus(tempDir, CancellationToken.None)!;
            Assert.True(statuses[file1].HasFlag(GitFileStatus.Modified));
            Assert.False(statuses[file1].HasFlag(GitFileStatus.Staged));
            Assert.True(statuses[file2].HasFlag(GitFileStatus.Modified));
            Assert.False(statuses[file2].HasFlag(GitFileStatus.Staged));
        }
        finally
        {
            ForceDeleteDirectory(tempDir);
        }
    }

    [Fact]
    public void Commit_WithStagedChanges_Succeeds()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), "wade-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        try
        {
            RunGit(tempDir, "init");
            RunGit(tempDir, "config user.email test@test.com");
            RunGit(tempDir, "config user.name Test");
            RunGit(tempDir, "config commit.gpgsign false");

            string filePath = Path.Combine(tempDir, "test.txt");
            File.WriteAllText(filePath, "content\n");
            RunGit(tempDir, "add test.txt");

            (bool Success, string? Error) result = GitUtils.Commit(tempDir, "Initial commit", CancellationToken.None);
            Assert.True(result.Success);

            // After commit, file should no longer appear in status
            Dictionary<string, GitFileStatus>? statuses = GitUtils.QueryStatus(tempDir, CancellationToken.None)!;
            Assert.DoesNotContain(statuses, kv => kv.Key == filePath);
        }
        finally
        {
            ForceDeleteDirectory(tempDir);
        }
    }

    [Fact]
    public void Commit_MessageWithQuotes_Succeeds()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), "wade-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        try
        {
            RunGit(tempDir, "init");
            RunGit(tempDir, "config user.email test@test.com");
            RunGit(tempDir, "config user.name Test");
            RunGit(tempDir, "config commit.gpgsign false");

            string filePath = Path.Combine(tempDir, "test.txt");
            File.WriteAllText(filePath, "content\n");
            RunGit(tempDir, "add test.txt");

            (bool Success, string? Error) result = GitUtils.Commit(tempDir, "Fix \"broken\" thing", CancellationToken.None);
            Assert.True(result.Success);
        }
        finally
        {
            ForceDeleteDirectory(tempDir);
        }
    }

    [Fact]
    public void Commit_NothingStaged_Fails()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), "wade-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        try
        {
            RunGit(tempDir, "init");
            RunGit(tempDir, "config user.email test@test.com");
            RunGit(tempDir, "config user.name Test");
            RunGit(tempDir, "config commit.gpgsign false");

            // Initial commit so repo is valid
            string filePath = Path.Combine(tempDir, "test.txt");
            File.WriteAllText(filePath, "content\n");
            RunGit(tempDir, "add test.txt");
            RunGit(tempDir, "commit -m initial");

            // Nothing staged — commit should fail
            (bool Success, string? Error) result = GitUtils.Commit(tempDir, "Empty commit", CancellationToken.None);
            Assert.False(result.Success);
        }
        finally
        {
            ForceDeleteDirectory(tempDir);
        }
    }

    [Fact]
    public void Push_NoRemote_Fails()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), "wade-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        try
        {
            RunGit(tempDir, "init");
            RunGit(tempDir, "config user.email test@test.com");
            RunGit(tempDir, "config user.name Test");
            RunGit(tempDir, "config commit.gpgsign false");
            File.WriteAllText(Path.Combine(tempDir, "test.txt"), "content\n");
            RunGit(tempDir, "add .");
            RunGit(tempDir, "commit -m initial");

            (bool Success, string? Error) result = GitUtils.Push(tempDir, CancellationToken.None);
            Assert.False(result.Success);
        }
        finally
        {
            ForceDeleteDirectory(tempDir);
        }
    }

    [Fact]
    public void PushForceWithLease_NoRemote_Fails()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), "wade-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        try
        {
            RunGit(tempDir, "init");
            RunGit(tempDir, "config user.email test@test.com");
            RunGit(tempDir, "config user.name Test");
            RunGit(tempDir, "config commit.gpgsign false");
            File.WriteAllText(Path.Combine(tempDir, "test.txt"), "content\n");
            RunGit(tempDir, "add .");
            RunGit(tempDir, "commit -m initial");

            (bool Success, string? Error) result = GitUtils.PushForceWithLease(tempDir, CancellationToken.None);
            Assert.False(result.Success);
        }
        finally
        {
            ForceDeleteDirectory(tempDir);
        }
    }

    [Fact]
    public void Pull_NoRemote_Fails()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), "wade-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        try
        {
            RunGit(tempDir, "init");

            (bool Success, string? Error) result = GitUtils.Pull(tempDir, CancellationToken.None);
            Assert.False(result.Success);
        }
        finally
        {
            ForceDeleteDirectory(tempDir);
        }
    }

    [Fact]
    public void PullRebase_NoRemote_Fails()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), "wade-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        try
        {
            RunGit(tempDir, "init");

            (bool Success, string? Error) result = GitUtils.PullRebase(tempDir, CancellationToken.None);
            Assert.False(result.Success);
        }
        finally
        {
            ForceDeleteDirectory(tempDir);
        }
    }

    [Fact]
    public void Fetch_NoRemote_Succeeds()
    {
        // git fetch with no remote configured succeeds (no-op)
        string tempDir = Path.Combine(Path.GetTempPath(), "wade-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        try
        {
            RunGit(tempDir, "init");

            (bool Success, string? Error) result = GitUtils.Fetch(tempDir, CancellationToken.None);
            Assert.True(result.Success);
        }
        finally
        {
            ForceDeleteDirectory(tempDir);
        }
    }

    [Fact]
    public void GetAheadBehind_NoUpstream_ReturnsNull()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), "wade-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        try
        {
            RunGit(tempDir, "init");
            RunGit(tempDir, "config user.email test@test.com");
            RunGit(tempDir, "config user.name Test");
            RunGit(tempDir, "config commit.gpgsign false");
            File.WriteAllText(Path.Combine(tempDir, "test.txt"), "content\n");
            RunGit(tempDir, "add .");
            RunGit(tempDir, "commit -m initial");

            (int Ahead, int Behind)? result = GitUtils.GetAheadBehind(tempDir, CancellationToken.None);
            Assert.Null(result);
        }
        finally
        {
            ForceDeleteDirectory(tempDir);
        }
    }

    [Fact]
    public void GetAheadBehind_WithUpstream_ReturnsCounts()
    {
        string remoteDir = Path.Combine(Path.GetTempPath(), "wade-test-remote-" + Guid.NewGuid().ToString("N"));
        string localDir = Path.Combine(Path.GetTempPath(), "wade-test-local-" + Guid.NewGuid().ToString("N"));

        try
        {
            // Create a bare remote
            Directory.CreateDirectory(remoteDir);
            RunGit(remoteDir, "init --bare");

            // Clone it
            RunGit(Path.GetTempPath(), $"clone \"{remoteDir}\" \"{localDir}\"");
            RunGit(localDir, "config user.email test@test.com");
            RunGit(localDir, "config user.name Test");
            RunGit(localDir, "config commit.gpgsign false");

            // Create initial commit and push
            File.WriteAllText(Path.Combine(localDir, "test.txt"), "content\n");
            RunGit(localDir, "add .");
            RunGit(localDir, "commit -m initial");
            RunGit(localDir, "push");

            // Make a local commit (ahead by 1)
            File.WriteAllText(Path.Combine(localDir, "test.txt"), "modified\n");
            RunGit(localDir, "add .");
            RunGit(localDir, "commit -m local");

            (int Ahead, int Behind)? result = GitUtils.GetAheadBehind(localDir, CancellationToken.None);
            Assert.NotNull(result);
            Assert.Equal(1, result.Value.Ahead);
            Assert.Equal(0, result.Value.Behind);
        }
        finally
        {
            ForceDeleteDirectory(localDir);
            ForceDeleteDirectory(remoteDir);
        }
    }

    [Fact]
    public void RunGitCommand_InvalidCommand_ReturnsFailure()
    {
        string? repoRoot = GitUtils.FindRepoRoot(AppContext.BaseDirectory);
        Assert.NotNull(repoRoot);

        (bool Success, string? Error) result = GitUtils.RunGitCommand(repoRoot, "not-a-real-command", CancellationToken.None);
        Assert.False(result.Success);
        Assert.NotNull(result.Error);
    }

    private static void RunGit(string workDir, string arguments)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "git",
            Arguments = arguments,
            WorkingDirectory = workDir,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        using var process = Process.Start(psi)!;
        process.WaitForExit(10_000);

        if (process.ExitCode != 0)
        {
            string stderr = process.StandardError.ReadToEnd();
            throw new InvalidOperationException($"git {arguments} failed: {stderr}");
        }
    }

    private static void ForceDeleteDirectory(string path)
    {
        // Git creates read-only files in .git/objects; clear the attribute before deleting
        foreach (string file in Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories))
        {
            File.SetAttributes(file, FileAttributes.Normal);
        }

        // Retry to handle Windows file handle release delays (antivirus, indexer, git locks)
        for (int attempt = 0; attempt < 3; attempt++)
        {
            try
            {
                Directory.Delete(path, true);
                return;
            }
            catch (Exception) when (attempt < 2)
            {
                Thread.Sleep(100);
            }
        }
    }
}
