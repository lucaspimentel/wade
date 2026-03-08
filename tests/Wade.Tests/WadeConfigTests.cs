using Wade;
using Wade.FileSystem;

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
        Assert.False(config.ShowHiddenFiles);
        Assert.True(config.ConfirmDeleteEnabled);
        Assert.True(config.PreviewPaneEnabled);
        Assert.True(config.DetailColumnsEnabled);
        Assert.Equal(Directory.GetCurrentDirectory(), config.StartPath);
    }

    // ── Config file parsing ───────────────────────────────────────────────────

    [Theory]
    [InlineData("show_icons_enabled = true", true, true, false)]
    [InlineData("show_icons_enabled = false", false, true, false)]
    [InlineData("image_previews_enabled = true", true, true, false)]
    [InlineData("image_previews_enabled = false", true, false, false)]
    [InlineData("show_hidden_files = true", true, true, true)]
    [InlineData("show_hidden_files = false", true, true, false)]
    public void ConfigFile_ParsesBoolSettings(string line, bool expectedIcons, bool expectedPreviews, bool expectedHidden)
    {
        var path = WriteTempConfig(line);
        try
        {
            var config = WadeConfig.Load([], configFilePath: path);
            Assert.Equal(expectedIcons, config.ShowIconsEnabled);
            Assert.Equal(expectedPreviews, config.ImagePreviewsEnabled);
            Assert.Equal(expectedHidden, config.ShowHiddenFiles);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Theory]
    [InlineData("confirm_delete_enabled", true)]
    [InlineData("confirm_delete_enabled", false)]
    [InlineData("preview_pane_enabled", true)]
    [InlineData("preview_pane_enabled", false)]
    [InlineData("detail_columns_enabled", true)]
    [InlineData("detail_columns_enabled", false)]
    public void ConfigFile_ParsesNewBoolSettings(string key, bool value)
    {
        var path = WriteTempConfig($"{key} = {(value ? "true" : "false")}");
        try
        {
            var config = WadeConfig.Load([], configFilePath: path);
            bool actual = key switch
            {
                "confirm_delete_enabled" => config.ConfirmDeleteEnabled,
                "preview_pane_enabled" => config.PreviewPaneEnabled,
                "detail_columns_enabled" => config.DetailColumnsEnabled,
                _ => throw new ArgumentException($"Unknown key: {key}"),
            };
            Assert.Equal(value, actual);
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
    [InlineData(@"C:\",          @"C:\")]     // drive root preserved
    [InlineData(@"C:/",          @"C:\")]     // forward-slash drive root → backslash root
    [InlineData(@"/",            @"/")]       // Unix root preserved
    public void StartPath_TrailingSeparatorsAreStripped(string input, string expected)
    {
        var config = WadeConfig.Load([input], configFilePath: "/nonexistent/path.toml");
        Assert.Equal(expected, config.StartPath);
    }

    [Fact]
    public void StartPath_BareDriveLetter_PassesThrough()
    {
        var config = WadeConfig.Load(["C:"], configFilePath: "/nonexistent/path.toml");
        Assert.Equal("C:", config.StartPath);
    }

    [Fact]
    public void CliFlag_ShortHelp_DoesNotSetStartPath()
    {
        var config = WadeConfig.Load(["-h"], configFilePath: "/nonexistent/path.toml");
        Assert.True(config.ShowHelp);
        Assert.Equal(Directory.GetCurrentDirectory(), config.StartPath);
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

    // ── Sort config ──────────────────────────────────────────────────────────

    [Theory]
    [InlineData("name", 0)]      // SortMode.Name
    [InlineData("modified", 1)]  // SortMode.Modified
    [InlineData("size", 2)]      // SortMode.Size
    [InlineData("extension", 3)] // SortMode.Extension
    public void ConfigFile_ParsesSortMode(string mode, int expectedValue)
    {
        var expected = (SortMode)expectedValue;
        var path = WriteTempConfig($"sort_mode = {mode}");
        try
        {
            var config = WadeConfig.Load([], configFilePath: path);
            Assert.Equal(expected, config.SortMode);
        }
        finally { File.Delete(path); }
    }

    [Theory]
    [InlineData("true", true)]
    [InlineData("false", false)]
    public void ConfigFile_ParsesSortAscending(string value, bool expected)
    {
        var path = WriteTempConfig($"sort_ascending = {value}");
        try
        {
            var config = WadeConfig.Load([], configFilePath: path);
            Assert.Equal(expected, config.SortAscending);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void Defaults_SortMode_IsName()
    {
        var config = WadeConfig.Load([], configFilePath: "/nonexistent/path.toml");
        Assert.Equal(SortMode.Name, config.SortMode);
        Assert.True(config.SortAscending);
    }

    // ── CwdFile flag ────────────────────────────────────────────────────────

    [Fact]
    public void CliFlag_CwdFile_SetsCwdFilePath()
    {
        var config = WadeConfig.Load(["--cwd-file=/tmp/foo"], configFilePath: "/nonexistent/path.toml");
        Assert.Equal("/tmp/foo", config.CwdFilePath);
    }

    [Fact]
    public void Defaults_CwdFilePath_IsNull()
    {
        var config = WadeConfig.Load([], configFilePath: "/nonexistent/path.toml");
        Assert.Null(config.CwdFilePath);
    }

    [Fact]
    public void CwdFileFlag_DoesNotAffectStartPath()
    {
        var config = WadeConfig.Load(["--cwd-file=/tmp/foo"], configFilePath: "/nonexistent/path.toml");
        Assert.Equal(Directory.GetCurrentDirectory(), config.StartPath);
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

    // ── Save / round-trip ──────────────────────────────────────────────────

    [Fact]
    public void Save_RoundTrips_AllSettings()
    {
        var dir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        var configPath = Path.Combine(dir, "config.toml");
        try
        {
            var original = WadeConfig.Load([], configFilePath: configPath);
            original.ShowIconsEnabled = false;
            original.ImagePreviewsEnabled = false;
            original.ShowHiddenFiles = true;
            original.SortMode = SortMode.Extension;
            original.SortAscending = false;
            original.ConfirmDeleteEnabled = false;
            original.PreviewPaneEnabled = false;
            original.DetailColumnsEnabled = false;
            original.Save();

            var loaded = WadeConfig.Load([], configFilePath: configPath);
            Assert.False(loaded.ShowIconsEnabled);
            Assert.False(loaded.ImagePreviewsEnabled);
            Assert.True(loaded.ShowHiddenFiles);
            Assert.Equal(SortMode.Extension, loaded.SortMode);
            Assert.False(loaded.SortAscending);
            Assert.False(loaded.ConfirmDeleteEnabled);
            Assert.False(loaded.PreviewPaneEnabled);
            Assert.False(loaded.DetailColumnsEnabled);
        }
        finally
        {
            if (Directory.Exists(dir))
                Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void Save_CreatesParentDirectory()
    {
        var dir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName(), "nested");
        var configPath = Path.Combine(dir, "config.toml");
        try
        {
            var config = WadeConfig.Load([], configFilePath: configPath);
            config.Save();

            Assert.True(File.Exists(configPath));
        }
        finally
        {
            var root = Path.GetDirectoryName(dir)!;
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    [Theory]
    [InlineData(0)] // SortMode.Name
    [InlineData(1)] // SortMode.Modified
    [InlineData(2)] // SortMode.Size
    [InlineData(3)] // SortMode.Extension
    public void Save_RoundTrips_SortMode(int modeValue)
    {
        var mode = (SortMode)modeValue;
        var configPath = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName() + ".toml");
        try
        {
            var config = WadeConfig.Load([], configFilePath: configPath);
            config.SortMode = mode;
            config.Save();

            var loaded = WadeConfig.Load([], configFilePath: configPath);
            Assert.Equal(mode, loaded.SortMode);
        }
        finally
        {
            File.Delete(configPath);
        }
    }
}
