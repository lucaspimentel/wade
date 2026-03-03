using Wade;

namespace Wade.Tests;

public class WadeConfigTests
{
    // ── Helpers ──────────────────────────────────────────────────────────────

    private sealed class MockEnv : IEnvironmentVariablesProvider
    {
        private readonly Dictionary<string, string> _vars;

        public MockEnv(Dictionary<string, string>? vars = null)
        {
            _vars = vars ?? [];
        }

        public string? GetEnvironmentVariable(string name) =>
            _vars.TryGetValue(name, out var v) ? v : null;
    }

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
        var config = WadeConfig.Load([], configFilePath: "/nonexistent/path.toml", env: new MockEnv());

        Assert.True(config.ShowIconsEnabled);
        Assert.False(config.ImagePreviewsEnabled);
        Assert.False(config.ShowConfig);
        Assert.Equal(Directory.GetCurrentDirectory(), config.StartPath);
    }

    // ── Config file parsing ───────────────────────────────────────────────────

    [Theory]
    [InlineData("show_icons_enabled = true", true, false)]
    [InlineData("show_icons_enabled = false", false, false)]
    [InlineData("image_previews_enabled = true", true, true)]
    [InlineData("image_previews_enabled = false", true, false)]
    public void ConfigFile_ParsesBoolSettings(string line, bool expectedIcons, bool expectedPreviews)
    {
        var path = WriteTempConfig(line);
        try
        {
            var config = WadeConfig.Load([], configFilePath: path, env: new MockEnv());
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
            var config = WadeConfig.Load([], configFilePath: path, env: new MockEnv());
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
            var config = WadeConfig.Load([], configFilePath: path, env: new MockEnv());
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
            var config = WadeConfig.Load([], configFilePath: path, env: new MockEnv());
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
            var config = WadeConfig.Load([], configFilePath: path, env: new MockEnv());
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
        var config = WadeConfig.Load([], configFilePath: "/does/not/exist.toml", env: new MockEnv());
        Assert.True(config.ShowIconsEnabled);
        Assert.False(config.ImagePreviewsEnabled);
    }

    // ── Env var overrides ─────────────────────────────────────────────────────

    [Theory]
    [InlineData("WADE_SHOW_ICONS_ENABLED", "true", true, false)]
    [InlineData("WADE_SHOW_ICONS_ENABLED", "false", false, false)]
    [InlineData("WADE_IMAGE_PREVIEWS_ENABLED", "true", false, true)]
    [InlineData("WADE_IMAGE_PREVIEWS_ENABLED", "false", false, false)]
    public void EnvVar_OverridesConfigFile(string envKey, string envValue, bool expectedIcons, bool expectedPreviews)
    {
        // Config file sets both to false; env var overrides one
        var path = WriteTempConfig("""
            show_icons_enabled = false
            image_previews_enabled = false
            """);
        try
        {
            var env = new MockEnv(new Dictionary<string, string> { [envKey] = envValue });
            var config = WadeConfig.Load([], configFilePath: path, env: env);
            Assert.Equal(expectedIcons, config.ShowIconsEnabled);
            Assert.Equal(expectedPreviews, config.ImagePreviewsEnabled);
        }
        finally
        {
            File.Delete(path);
        }
    }

    // ── CLI flag overrides ────────────────────────────────────────────────────

    [Theory]
    [InlineData("--show-icons-enabled=true", true, false)]
    [InlineData("--show-icons-enabled=false", false, false)]
    [InlineData("--image-previews-enabled=true", true, true)]
    [InlineData("--image-previews-enabled=false", true, false)]
    public void CliFlag_KeyEquals_ParsesBool(string cliFlag, bool expectedIcons, bool expectedPreviews)
    {
        var config = WadeConfig.Load([cliFlag], configFilePath: "/nonexistent/path.toml", env: new MockEnv());
        Assert.Equal(expectedIcons, config.ShowIconsEnabled);
        Assert.Equal(expectedPreviews, config.ImagePreviewsEnabled);
    }

    [Theory]
    [InlineData("--show-icons-enabled", "WADE_SHOW_ICONS_ENABLED", true)]
    [InlineData("--no-show-icons-enabled", "WADE_SHOW_ICONS_ENABLED", false)]
    [InlineData("--image-previews-enabled", "WADE_IMAGE_PREVIEWS_ENABLED", true)]
    [InlineData("--no-image-previews-enabled", "WADE_IMAGE_PREVIEWS_ENABLED", false)]
    public void CliFlag_OverridesEnvVar(string cliFlag, string envKey, bool expectedValue)
    {
        // Env var set to opposite — CLI flag should win
        var envValue = expectedValue ? "false" : "true";
        var env = new MockEnv(new Dictionary<string, string> { [envKey] = envValue });
        var config = WadeConfig.Load([cliFlag], configFilePath: "/nonexistent/path.toml", env: env);

        if (envKey == "WADE_SHOW_ICONS_ENABLED")
            Assert.Equal(expectedValue, config.ShowIconsEnabled);
        else
            Assert.Equal(expectedValue, config.ImagePreviewsEnabled);
    }

    [Fact]
    public void CliFlag_ShowConfig_SetsShowConfig()
    {
        var config = WadeConfig.Load(["--show-config"], configFilePath: "/nonexistent/path.toml", env: new MockEnv());
        Assert.True(config.ShowConfig);
    }

    [Fact]
    public void CliFlag_ShowConfig_NotSet_ByDefault()
    {
        var config = WadeConfig.Load([], configFilePath: "/nonexistent/path.toml", env: new MockEnv());
        Assert.False(config.ShowConfig);
    }

    // ── Positional path extraction ────────────────────────────────────────────

    [Theory]
    [InlineData(new[] { "/some/path" }, "/some/path")]
    [InlineData(new[] { "--show-icons-enabled", "/some/path" }, "/some/path")]
    [InlineData(new[] { "/some/path", "--show-icons-enabled" }, "/some/path")]
    [InlineData(new[] { "--show-config", "/some/path", "--image-previews-enabled" }, "/some/path")]
    public void PositionalArg_ExtractsStartPath(string[] cliArgs, string expectedPath)
    {
        var config = WadeConfig.Load(cliArgs, configFilePath: "/nonexistent/path.toml", env: new MockEnv());
        Assert.Equal(expectedPath, config.StartPath);
    }

    [Fact]
    public void NoPositionalArg_DefaultsToCurrentDirectory()
    {
        var config = WadeConfig.Load(["--show-config"], configFilePath: "/nonexistent/path.toml", env: new MockEnv());
        Assert.Equal(Directory.GetCurrentDirectory(), config.StartPath);
    }

    [Theory]
    [InlineData(@"C:\foo\bar\",  @"C:\foo\bar")]
    [InlineData(@"C:\foo\bar/",  @"C:\foo\bar")]
    [InlineData(@"C:\foo\bar\\", @"C:\foo\bar")]
    [InlineData(@"C:\foo\bar",   @"C:\foo\bar")]
    public void StartPath_TrailingSeparatorsAreStripped(string input, string expected)
    {
        var config = WadeConfig.Load([input], configFilePath: "/nonexistent/path.toml", env: new MockEnv());
        Assert.Equal(expected, config.StartPath);
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

    // ── Three-tier priority ───────────────────────────────────────────────────

    [Fact]
    public void ThreeTierPriority_CliFlagWinsOverEnvAndConfig()
    {
        var path = WriteTempConfig("show_icons_enabled = false");
        try
        {
            var env = new MockEnv(new Dictionary<string, string> { ["WADE_SHOW_ICONS_ENABLED"] = "false" });
            var config = WadeConfig.Load(["--show-icons-enabled"], configFilePath: path, env: env);
            Assert.True(config.ShowIconsEnabled);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void ThreeTierPriority_EnvVarWinsOverConfigFile()
    {
        var path = WriteTempConfig("show_icons_enabled = false");
        try
        {
            var env = new MockEnv(new Dictionary<string, string> { ["WADE_SHOW_ICONS_ENABLED"] = "true" });
            var config = WadeConfig.Load([], configFilePath: path, env: env);
            Assert.True(config.ShowIconsEnabled);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
