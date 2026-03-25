using Wade.FileSystem;
using Wade.UI;

namespace Wade.Tests;

public class ConfigDialogStateTests
{
    // ── FromConfig ───────────────────────────────────────────────────────────

    [Fact]
    public void FromConfig_CopiesAllProperties()
    {
        var config = WadeConfig.Load([], configFilePath: "/nonexistent/path.toml");
        config.ShowIconsEnabled = false;
        config.ImagePreviewsEnabled = false;
        config.ShowHiddenFiles = true;
        config.ShowSystemFiles = true;
        config.SortMode = SortMode.Extension;
        config.SortAscending = false;
        config.ConfirmDeleteEnabled = false;
        config.PreviewPaneEnabled = false;
        config.SizeColumnEnabled = false;
        config.DateColumnEnabled = false;
        config.ZipPreviewEnabled = false;
        config.CopySymlinksAsLinksEnabled = false;
        config.TerminalTitleEnabled = false;
        config.GitStatusEnabled = false;
        config.FileMetadataEnabled = false;
        config.FilePreviewsEnabled = false;
        config.ArchiveMetadataEnabled = false;
        config.GlowPreviewEnabled = false;
        config.PdfMetadataEnabled = false;

        var state = ConfigDialogState.FromConfig(config);

        Assert.False(state.ShowIcons);
        Assert.False(state.ImagePreviews);
        Assert.True(state.ShowHiddenFiles);
        Assert.True(state.ShowSystemFiles);
        Assert.Equal(SortMode.Extension, state.SortMode);
        Assert.False(state.SortAscending);
        Assert.False(state.ConfirmDelete);
        Assert.False(state.PreviewPane);
        Assert.False(state.SizeColumn);
        Assert.False(state.DateColumn);
        Assert.False(state.ZipPreview);
        Assert.False(state.CopySymlinksAsLinks);
        Assert.False(state.TerminalTitle);
        Assert.False(state.GitStatus);
        Assert.False(state.FileMetadata);
        Assert.False(state.FilePreviews);
        Assert.False(state.ArchiveMetadata);
        Assert.False(state.GlowPreview);
        Assert.False(state.PdfMetadata);
    }

    [Fact]
    public void FromConfig_BuildsItems()
    {
        var config = WadeConfig.Load([], configFilePath: "/nonexistent/path.toml");
        var state = ConfigDialogState.FromConfig(config);

        Assert.NotEmpty(state.Items);
        Assert.Equal("Show Icons", state.Items[0].Label);
    }

    // ── ApplyTo ──────────────────────────────────────────────────────────────

    [Fact]
    public void ApplyTo_WritesAllProperties()
    {
        var config = WadeConfig.Load([], configFilePath: "/nonexistent/path.toml");
        var state = ConfigDialogState.FromConfig(config);

        state.ShowIcons = false;
        state.ImagePreviews = false;
        state.ShowHiddenFiles = true;
        state.ShowSystemFiles = true;
        state.SortMode = SortMode.Size;
        state.SortAscending = false;
        state.ConfirmDelete = false;
        state.PreviewPane = false;
        state.SizeColumn = false;
        state.DateColumn = false;
        state.ZipPreview = false;
        state.CopySymlinksAsLinks = false;
        state.TerminalTitle = false;
        state.GitStatus = false;
        state.FileMetadata = false;
        state.FilePreviews = false;
        state.ArchiveMetadata = false;
        state.Ffprobe = false;

        state.ApplyTo(config);

        Assert.False(config.ShowIconsEnabled);
        Assert.False(config.ImagePreviewsEnabled);
        Assert.True(config.ShowHiddenFiles);
        Assert.True(config.ShowSystemFiles);
        Assert.Equal(SortMode.Size, config.SortMode);
        Assert.False(config.SortAscending);
        Assert.False(config.ConfirmDeleteEnabled);
        Assert.False(config.PreviewPaneEnabled);
        Assert.False(config.SizeColumnEnabled);
        Assert.False(config.DateColumnEnabled);
        Assert.False(config.ZipPreviewEnabled);
        Assert.False(config.CopySymlinksAsLinksEnabled);
        Assert.False(config.TerminalTitleEnabled);
        Assert.False(config.GitStatusEnabled);
        Assert.False(config.FileMetadataEnabled);
        Assert.False(config.FilePreviewsEnabled);
        Assert.False(config.ArchiveMetadataEnabled);
        Assert.False(config.FfprobeEnabled);
    }

    [Fact]
    public void ApplyTo_SystemFiles_RequiresHiddenFiles()
    {
        var config = WadeConfig.Load([], configFilePath: "/nonexistent/path.toml");
        var state = ConfigDialogState.FromConfig(config);

        state.ShowHiddenFiles = false;
        state.ShowSystemFiles = true;
        state.ApplyTo(config);

        Assert.False(config.ShowSystemFiles);
    }

    // ── Navigation ───────────────────────────────────────────────────────────

    [Fact]
    public void MoveDown_AdvancesToNextItem()
    {
        ConfigDialogState state = CreateDefaultState();
        Assert.Equal(0, state.SelectedIndex);

        state.MoveDown();
        Assert.Equal(1, state.SelectedIndex);
    }

    [Fact]
    public void MoveUp_GoesToPreviousItem()
    {
        ConfigDialogState state = CreateDefaultState();
        state.SelectedIndex = 2;

        state.MoveUp();
        Assert.Equal(1, state.SelectedIndex);
    }

    [Fact]
    public void MoveUp_AtTop_WrapsToLastEnabledItem()
    {
        ConfigDialogState state = CreateDefaultState();
        int lastEnabled = state.Items.Count - 1;
        while (lastEnabled > 0 && !state.Items[lastEnabled].IsEnabled)
        {
            lastEnabled--;
        }

        state.MoveUp();

        Assert.Equal(lastEnabled, state.SelectedIndex);
    }

    [Fact]
    public void MoveDown_AtBottom_WrapsToFirstEnabledItem()
    {
        ConfigDialogState state = CreateDefaultState();
        int lastEnabled = state.Items.Count - 1;
        while (lastEnabled > 0 && !state.Items[lastEnabled].IsEnabled)
        {
            lastEnabled--;
        }

        state.SelectedIndex = lastEnabled;

        state.MoveDown();
        Assert.Equal(0, state.SelectedIndex);
    }

    [Fact]
    public void MoveDown_SkipsDisabledItems()
    {
        ConfigDialogState state = CreateDefaultState();

        // Disable FileMetadata so detail sub-items are disabled,
        // but PreviewPane stays on so "Show File Previews" is still enabled
        state.PreviewPane = true;
        state.FileMetadata = false;
        state.BuildItems();

        // Navigate to "Show File Details" (disabled because FileMetadata is off — wait, it's the toggle for it)
        // Better: navigate to "Show File Details" then MoveDown — the detail sub-items
        // (Archive Details, PDF Details, etc.) are disabled, so it should skip to "Show File Previews"
        int fileDetailsIndex = state.Items.FindIndex(i => i.Label == "Show File Details");
        state.SelectedIndex = fileDetailsIndex;

        state.MoveDown();

        // Should skip disabled detail sub-items and land on "Show File Previews"
        Assert.Equal("Show File Previews", state.Items[state.SelectedIndex].Label);
    }

    [Fact]
    public void MoveUp_SkipsDisabledItems()
    {
        ConfigDialogState state = CreateDefaultState();
        state.PreviewPane = true;
        state.FileMetadata = false;
        state.BuildItems();

        // Start at "Show File Previews"
        int filePreviewsIndex = state.Items.FindIndex(i => i.Label == "Show File Previews");
        state.SelectedIndex = filePreviewsIndex;

        state.MoveUp();

        // Should skip disabled detail sub-items and land on "Show File Details"
        Assert.Equal("Show File Details", state.Items[state.SelectedIndex].Label);
    }

    // ── Toggle ───────────────────────────────────────────────────────────────

    [Fact]
    public void ToggleSelected_TogglesBoolean()
    {
        ConfigDialogState state = CreateDefaultState();
        bool original = state.ShowIcons;

        state.ToggleSelected(); // index 0 = "Show Icons"

        Assert.NotEqual(original, state.ShowIcons);
    }

    [Fact]
    public void ToggleSelected_ToolToggle_FlipsBool()
    {
        ConfigDialogState state = CreateDefaultState();

        // Navigate to a tool toggle item (e.g. pdfinfo)
        int pdfinfoIndex = state.Items.FindIndex(i => i.Label.Contains("pdfinfo"));
        state.SelectedIndex = pdfinfoIndex;
        state.ToggleSelected();

        Assert.False(state.PdfMetadata);

        // Toggle again to re-enable
        state.ToggleSelected();
        Assert.True(state.PdfMetadata);
    }

    // ── Sort mode cycling ────────────────────────────────────────────────────

    [Fact]
    public void CycleNextSelected_OnSortMode_CyclesForward()
    {
        ConfigDialogState state = CreateDefaultState();
        int sortIndex = state.Items.FindIndex(i => i.Label == "Sort Mode");
        state.SelectedIndex = sortIndex;

        Assert.Equal(SortMode.Name, state.SortMode);

        state.CycleNextSelected();
        Assert.Equal(SortMode.Modified, state.SortMode);

        state.CycleNextSelected();
        Assert.Equal(SortMode.Size, state.SortMode);

        state.CycleNextSelected();
        Assert.Equal(SortMode.Extension, state.SortMode);

        state.CycleNextSelected();
        Assert.Equal(SortMode.Name, state.SortMode);
    }

    [Fact]
    public void CyclePrevSelected_OnSortMode_CyclesBackward()
    {
        ConfigDialogState state = CreateDefaultState();
        int sortIndex = state.Items.FindIndex(i => i.Label == "Sort Mode");
        state.SelectedIndex = sortIndex;

        Assert.Equal(SortMode.Name, state.SortMode);

        state.CyclePrevSelected();
        Assert.Equal(SortMode.Extension, state.SortMode);

        state.CyclePrevSelected();
        Assert.Equal(SortMode.Size, state.SortMode);
    }

    [Fact]
    public void CycleNextSelected_OnNonCycleable_DoesNothing()
    {
        ConfigDialogState state = CreateDefaultState();
        // Index 0 is "Show Icons" — not cycleable
        bool original = state.ShowIcons;

        state.CycleNextSelected();

        Assert.Equal(original, state.ShowIcons);
    }

    // ── Enabled predicates ───────────────────────────────────────────────────

    [Theory]
    [InlineData("Show File Details")]
    [InlineData("Show File Previews")]
    public void IndentedItems_DisabledWhenPreviewPaneOff(string label)
    {
        ConfigDialogState state = CreateDefaultState();
        state.PreviewPane = false;
        state.BuildItems();

        ConfigItem? item = state.Items.Find(i => i.Label == label);
        Assert.NotNull(item);
        Assert.False(item.IsEnabled);
    }

    [Theory]
    [InlineData("Show File Details")]
    [InlineData("Show File Previews")]
    public void IndentedItems_EnabledWhenPreviewPaneOn(string label)
    {
        ConfigDialogState state = CreateDefaultState();
        state.PreviewPane = true;
        state.BuildItems();

        ConfigItem? item = state.Items.Find(i => i.Label == label);
        Assert.NotNull(item);
        Assert.True(item.IsEnabled);
    }

    [Theory]
    [InlineData("Show Archive Details")]
    [InlineData("Show PDF Details (pdfinfo)")]
    [InlineData("Show Media Details (ffprobe)")]
    [InlineData("Show Media Details (mediainfo)")]
    public void DetailSubItems_DisabledWhenFileMetadataOff(string label)
    {
        ConfigDialogState state = CreateDefaultState();
        state.PreviewPane = true;
        state.FileMetadata = false;
        state.BuildItems();

        ConfigItem? item = state.Items.Find(i => i.Label == label);
        Assert.NotNull(item);
        Assert.False(item.IsEnabled);
    }

    [Theory]
    [InlineData("Show Image Previews")]
    [InlineData("Show PDF Previews (pdftopng)")]
    [InlineData("Show Archive Contents")]
    [InlineData("Show Markdown Preview (glow)")]
    public void PreviewSubItems_DisabledWhenFilePreviewsOff(string label)
    {
        ConfigDialogState state = CreateDefaultState();
        state.PreviewPane = true;
        state.FilePreviews = false;
        state.BuildItems();

        ConfigItem? item = state.Items.Find(i => i.Label == label);
        Assert.NotNull(item);
        Assert.False(item.IsEnabled);
    }

    [Theory]
    [InlineData("Show Image Previews")]
    [InlineData("Show PDF Previews (pdftopng)")]
    [InlineData("Show Archive Contents")]
    [InlineData("Show Markdown Preview (glow)")]
    public void PreviewSubItems_EnabledWhenFilePreviewsOn(string label)
    {
        ConfigDialogState state = CreateDefaultState();
        state.PreviewPane = true;
        state.FilePreviews = true;
        state.BuildItems();

        ConfigItem? item = state.Items.Find(i => i.Label == label);
        Assert.NotNull(item);
        Assert.True(item.IsEnabled);
    }

    // ── FormatValue ──────────────────────────────────────────────────────────

    [Fact]
    public void FormatValue_ReflectsCurrentState()
    {
        ConfigDialogState state = CreateDefaultState();
        ConfigItem showIcons = state.Items[0]; // "Show Icons"

        state.ShowIcons = true;
        Assert.Equal("[X]", showIcons.FormatValue());

        state.ShowIcons = false;
        Assert.Equal("[ ]", showIcons.FormatValue());
    }

    [Fact]
    public void FormatValue_SortMode_ShowsCurrentMode()
    {
        ConfigDialogState state = CreateDefaultState();
        ConfigItem sortItem = state.Items.Find(i => i.Label == "Sort Mode")!;

        state.SortMode = SortMode.Extension;
        Assert.Contains("extension", sortItem.FormatValue());
    }

    [Fact]
    public void FormatValue_ToolItem_ReflectsBool()
    {
        ConfigDialogState state = CreateDefaultState();
        ConfigItem pdfinfoItem = state.Items.Find(i => i.Label.Contains("pdfinfo"))!;

        Assert.Equal("[X]", pdfinfoItem.FormatValue()); // enabled by default
        state.PdfMetadata = false;
        Assert.Equal("[ ]", pdfinfoItem.FormatValue());
    }

    // ── Item structure ───────────────────────────────────────────────────────

    [Fact]
    public void Items_HaveCorrectIndentation()
    {
        ConfigDialogState state = CreateDefaultState();

        Assert.Equal(0, state.Items.Find(i => i.Label == "Show Icons")!.Indent);
        Assert.Equal(1, state.Items.Find(i => i.Label == "Show File Details")!.Indent);
        Assert.Equal(2, state.Items.Find(i => i.Label == "Show Archive Details")!.Indent);
    }

    [Fact]
    public void Items_SortMode_IsCycleable()
    {
        ConfigDialogState state = CreateDefaultState();
        ConfigItem sortItem = state.Items.Find(i => i.Label == "Sort Mode")!;

        Assert.True(sortItem.IsCycleable);
    }

    [Theory]
    [InlineData("Show Icons")]
    [InlineData("Show Hidden Files")]
    [InlineData("Show File Details")]
    public void Items_BoolItems_AreNotCycleable(string label)
    {
        ConfigDialogState state = CreateDefaultState();
        ConfigItem item = state.Items.Find(i => i.Label == label)!;

        Assert.False(item.IsCycleable);
    }

    // ── FormatBool ───────────────────────────────────────────────────────────

    [Theory]
    [InlineData(true, "[X]")]
    [InlineData(false, "[ ]")]
    public void FormatBool_ReturnsCheckbox(bool value, string expected) => Assert.Equal(expected, ConfigDialogState.FormatBool(value));

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static ConfigDialogState CreateDefaultState()
    {
        var config = WadeConfig.Load([], configFilePath: "/nonexistent/path.toml");
        return ConfigDialogState.FromConfig(config);
    }
}
