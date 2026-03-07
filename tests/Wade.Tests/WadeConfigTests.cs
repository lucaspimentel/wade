using Wade;

namespace Wade.Tests;

public class WadeConfigTests
{
    // ── Helpers ──────────────────────────────────────────────────────────────

    private static string WriteTempConfig(string content)
    {
        var path = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName() + ".toml");
        File.WriteAllText(path, content);
        return path;
    }

    // ── Default values ────────────────────────────────────────────────────────

    [Fact]
    public void Defaults_WhenNothingProvided()
    {
        var config = WadeConfig.Load([], configFilePath: "/nonexistent/path.toml");

        Assert.True(config.ShowIconsEnabled);
        Assert.True(config.ImagePreviewsEnabled);
        Assert.False(config.ShowConfig);
        Assert.False(config.ShowHelp);
        Assert.Equal(Directory.GetCurrentDirectory(), config.StartPath);
    }

    // ── Config file parsing ───────────────────────────────────────────────────

    [Theory]
    [InlineData("show_icons_enabled = true", true, true)]
    [InlineData("show_icons_enabled = false", false, true)]
    [InlineData("image_previews_enabled = true", true, true)]
    [InlineData("image_previews_enabled = false", true, false)]
    public void ConfigFile_ParsesBoolSettings(string line, bool expectedIcons, bool expectedPreviews)
    {
        var path = WriteTempConfig(line);
        try
        {
            var config = WadeConfig.Load([], configFilePath: path);
            Assert.Equal(expectedIcons, config.ShowIconsEnabled);
            Assert.Equal(expectedPreviews, config.ImagePreviewsEnabled);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void ConfigFile_IgnoresCommentLines()
    {
        var path = WriteTempConfig("""
            # this is a comment
            # show_icons_enabled = true
            """);
        try
        {
            var config = WadeConfig.Load([], configFilePath: path);
            Assert.True(config.ShowIconsEnabled); // default is true; comments don't override it
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void ConfigFile_IgnoresBlankLines()
    {
        var path = WriteTempConfig("""

            show_icons_enabled = true

            image_previews_enabled = true

            """);
        try
        {
            var config = WadeConfig.Load([], configFilePath: path);
            Assert.True(config.ShowIconsEnabled);
            Assert.True(config.ImagePreviewsEnabled);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void ConfigFile_IgnoresMalformedLines()
    {
        var path = WriteTempConfig("""
            not_a_valid_line
            show_icons_enabled true
            = no_key
            """);
        try
        {
            var config = WadeConfig.Load([], configFilePath: path);
            Assert.True(config.ShowIconsEnabled); // default is true; malformed lines don't override it
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void ConfigFile_StripsInlineComments()
    {
        var path = WriteTempConfig("show_icons_enabled = true # enable icons");
        try
        {
            var config = WadeConfig.Load([], configFilePath: path);
            Assert.True(config.ShowIconsEnabled);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void ConfigFile_NonexistentFile_UsesDefaults()
    {
        var config = WadeConfig.Load([], configFilePath: "/does/not/exist.toml");
        Assert.True(config.ShowIconsEnabled);
        Assert.True(config.ImagePreviewsEnabled);
    }

    // ── CLI meta-flags ────────────────────────────────────────────────────────

    [Fact]
    public void CliFlag_ShowConfig_SetsShowConfig()
    {
        var config = WadeConfig.Load(["--show-config"], configFilePath: "/nonexistent/path.toml");
        Assert.True(config.ShowConfig);
    }

    [Theory]
    [InlineData("--help")]
    [InlineData("-h")]
    public void CliFlag_Help_SetsShowHelp(string flag)
    {
        var config = WadeConfig.Load([flag], configFilePath: "/nonexistent/path.toml");
        Assert.True(config.ShowHelp);
    }

    [Fact]
    public void CliFlag_ShowConfig_NotSet_ByDefault()
    {
        var config = WadeConfig.Load([], configFilePath: "/nonexistent/path.toml");
        Assert.False(config.ShowConfig);
    }

    // ── Positional path extraction ────────────────────────────────────────────

    [Theory]
    [InlineData(new[] { "/some/path" }, "/some/path")]
    [InlineData(new[] { "--show-config", "/some/path" }, "/some/path")]
    public void PositionalArg_ExtractsStartPath(string[] cliArgs, string expectedPath)
    {
        var config = WadeConfig.Load(cliArgs, configFilePath: "/nonexistent/path.toml");
        Assert.Equal(expectedPath, config.StartPath);
    }

    [Fact]
    public void NoPositionalArg_DefaultsToCurrentDirectory()
    {
        var config = WadeConfig.Load(["--show-config"], configFilePath: "/nonexistent/path.toml");
        Assert.Equal(Directory.GetCurrentDirectory(), config.StartPath);
    }

    [Theory]
    [InlineData(@"C:\foo\bar\",  @"C:\foo\bar")]
    [InlineData(@"C:\foo\bar/",  @"C:\foo\bar")]
    [InlineData(@"C:\foo\bar\\", @"C:\foo\bar")]
    [InlineData(@"C:\foo\bar",   @"C:\foo\bar")]
    public void StartPath_TrailingSeparatorsAreStripped(string input, string expected)
    {
        var config = WadeConfig.Load([input], configFilePath: "/nonexistent/path.toml");
        Assert.Equal(expected, config.StartPath);
    }

    [Theory]
    [InlineData("~")]
    [InlineData("~/Downloads")]
    [InlineData(@"~\Downloads")]
    public void StartPath_TildeExpandsToHomeDirectory(string input)
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var config = WadeConfig.Load([input], configFilePath: "/nonexistent/path.toml");

        Assert.StartsWith(home, config.StartPath, StringComparison.OrdinalIgnoreCase);
    }

    // ── ParseBool edge cases ──────────────────────────────────────────────────

    [Theory]
    [InlineData("true", true)]
    [InlineData("True", true)]
    [InlineData("TRUE", true)]
    [InlineData("1", true)]
    [InlineData("yes", true)]
    [InlineData("Yes", true)]
    [InlineData("YES", true)]
    [InlineData("false", false)]
    [InlineData("False", false)]
    [InlineData("FALSE", false)]
    [InlineData("0", false)]
    [InlineData("no", false)]
    [InlineData("No", false)]
    [InlineData("NO", false)]
    public void ParseBool_RecognizesAllAcceptedValues(string input, bool expected)
    {
        Assert.Equal(expected, WadeConfig.ParseBool(input, fallback: !expected));
    }

    [Theory]
    [InlineData("maybe", true)]
    [InlineData("", false)]
    [InlineData("2", true)]
    [InlineData("on", false)]
    public void ParseBool_UnknownValue_ReturnsFallback(string input, bool fallback)
    {
        Assert.Equal(fallback, WadeConfig.ParseBool(input, fallback));
    }
}
