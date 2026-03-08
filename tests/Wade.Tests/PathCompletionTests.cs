using Wade.FileSystem;

namespace Wade.Tests;

public class PathCompletionTests
{
    private static string CreateTestDir()
    {
        string dir = Path.Combine(Path.GetTempPath(), "wade_completion_test_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    private static void CleanupDir(string dir)
    {
        try { Directory.Delete(dir, recursive: true); } catch { }
    }

    [Fact]
    public void Suggest_PartialName_ReturnsFirstMatch()
    {
        string dir = CreateTestDir();
        try
        {
            Directory.CreateDirectory(Path.Combine(dir, "alpha"));
            Directory.CreateDirectory(Path.Combine(dir, "beta"));

            string? suggestion = PathCompletion.GetSuggestion(Path.Combine(dir, "alp"));

            Assert.NotNull(suggestion);
            Assert.Equal(Path.Combine(dir, "alpha"), suggestion);
        }
        finally { CleanupDir(dir); }
    }

    [Fact]
    public void Suggest_DirectoryWithSeparator_ReturnsFirstChild()
    {
        string dir = CreateTestDir();
        try
        {
            File.WriteAllText(Path.Combine(dir, "file.txt"), "");

            string? suggestion = PathCompletion.GetSuggestion(dir + Path.DirectorySeparatorChar);

            Assert.NotNull(suggestion);
            Assert.StartsWith(dir, suggestion);
        }
        finally { CleanupDir(dir); }
    }

    [Fact]
    public void Suggest_NoMatch_ReturnsNull()
    {
        string dir = CreateTestDir();
        try
        {
            File.WriteAllText(Path.Combine(dir, "file.txt"), "");

            string? suggestion = PathCompletion.GetSuggestion(Path.Combine(dir, "zzz"));

            Assert.Null(suggestion);
        }
        finally { CleanupDir(dir); }
    }

    [Fact]
    public void Suggest_EmptyInput_ReturnsNull()
    {
        Assert.Null(PathCompletion.GetSuggestion(""));
        Assert.Null(PathCompletion.GetSuggestion(null!));
    }

    [Fact]
    public void Suggest_NonexistentParent_ReturnsNull()
    {
        string fakePath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "abc");

        Assert.Null(PathCompletion.GetSuggestion(fakePath));
    }

    [Fact]
    public void Suggest_CaseInsensitive()
    {
        string dir = CreateTestDir();
        try
        {
            Directory.CreateDirectory(Path.Combine(dir, "Alpha"));

            string? suggestion = PathCompletion.GetSuggestion(Path.Combine(dir, "alp"));

            Assert.NotNull(suggestion);
            Assert.EndsWith("Alpha", suggestion);
        }
        finally { CleanupDir(dir); }
    }

    [Fact]
    public void Suggest_EmptyDirectory_ReturnsNull()
    {
        string dir = CreateTestDir();
        try
        {
            string? suggestion = PathCompletion.GetSuggestion(dir + Path.DirectorySeparatorChar);

            Assert.Null(suggestion);
        }
        finally { CleanupDir(dir); }
    }

    // ── Tilde expansion ──────────────────────────────────────────────────────

    [Fact]
    public void ExpandTilde_ExpandsToHomeDirectory()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        Assert.Equal(home, PathCompletion.ExpandTilde("~"));
    }

    [Fact]
    public void ExpandTilde_ExpandsWithSubpath()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        string result = PathCompletion.ExpandTilde("~/Downloads");
        Assert.StartsWith(home, result);
        Assert.Contains("Downloads", result);
    }

    [Fact]
    public void ExpandTilde_NoTilde_ReturnsUnchanged()
    {
        Assert.Equal("/some/path", PathCompletion.ExpandTilde("/some/path"));
    }

    [Fact]
    public void Suggest_TildeWithSeparator_SuggestsHomeChild()
    {
        string? suggestion = PathCompletion.GetSuggestion("~" + Path.DirectorySeparatorChar);

        // Home directory should have at least one entry
        Assert.NotNull(suggestion);
    }

    // ── Separator normalization ──────────────────────────────────────────────

    [Fact]
    public void NormalizeSeparators_ForwardSlashesNormalized()
    {
        string result = PathCompletion.NormalizeSeparators("C:/Users/foo");
        if (Path.DirectorySeparatorChar == '\\')
        {
            Assert.Equal(@"C:\Users\foo", result);
        }
        else
        {
            Assert.Equal("C:/Users/foo", result); // no-op on Unix
        }
    }

    [Fact]
    public void NormalizeSeparators_NoSlashes_Unchanged()
    {
        Assert.Equal("foo", PathCompletion.NormalizeSeparators("foo"));
    }

    [Fact]
    public void Suggest_ForwardSlashes_StillMatches()
    {
        string dir = CreateTestDir();
        try
        {
            Directory.CreateDirectory(Path.Combine(dir, "alpha"));

            // Use forward slashes in the input
            string input = dir.Replace('\\', '/') + "/alp";
            string? suggestion = PathCompletion.GetSuggestion(input);

            Assert.NotNull(suggestion);
            Assert.EndsWith("alpha", suggestion);
        }
        finally { CleanupDir(dir); }
    }

    // ── Drive letter capitalization ─────────────────────────────────────────

    [Theory]
    [InlineData(@"c:\Users\foo", @"C:\Users\foo")]
    [InlineData(@"d:\", @"D:\")]
    [InlineData(@"C:\Users\foo", @"C:\Users\foo")]
    [InlineData(@"D:\", @"D:\")]
    [InlineData("/usr/local/bin", "/usr/local/bin")]
    [InlineData("relative/path", "relative/path")]
    [InlineData("", "")]
    [InlineData("x", "x")]
    public void CapitalizeDriveLetter_ReturnsExpected(string input, string expected)
    {
        Assert.Equal(expected, PathCompletion.CapitalizeDriveLetter(input));
    }
}
