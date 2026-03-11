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
}
