using System.Text;
using System.Text.RegularExpressions;
using Wade.FileSystem;
using Wade.Terminal;
using Wade.UI;

namespace Wade.Tests;

public class PaneRendererTests
{
    private static string StripAnsi(string s) =>
        Regex.Replace(s, @"\x1b\[[^a-zA-Z]*[a-zA-Z]", "");

    private static string Flush(ScreenBuffer buf)
    {
        var sb = new StringBuilder();
        buf.Flush(sb);
        return StripAnsi(sb.ToString());
    }

    private static FileSystemEntry MakeFile(string name, long size = 0, DateTime lastModified = default) =>
        new(name, $@"C:\{name}", IsDirectory: false, Size: size, LastModified: lastModified);

    private static FileSystemEntry MakeDir(string name, DateTime lastModified = default) =>
        new(name, $@"C:\{name}", IsDirectory: true, Size: 0, LastModified: lastModified);

    private static Rect FullPane(int width = 40, int height = 10) => new(0, 0, width, height);

    [Fact]
    public void RenderFileList_ShowIcons_False_PrefixesWithSpaceOrSlash()
    {
        var buf = new ScreenBuffer(40, 10);
        var entries = new List<FileSystemEntry> { MakeFile("README.md") };
        PaneRenderer.RenderFileList(buf, FullPane(), entries, selectedIndex: 0, scrollOffset: 0, isActive: false, showIcons: false);
        var output = Flush(buf);
        Assert.Contains(" README.md", output); // space prefix for files
    }

    [Fact]
    public void RenderFileList_ShowIcons_True_PrefixesWithIconAndSpace()
    {
        var buf = new ScreenBuffer(40, 10);
        var entries = new List<FileSystemEntry> { MakeFile("README.md") };
        PaneRenderer.RenderFileList(buf, FullPane(), entries, selectedIndex: 0, scrollOffset: 0, isActive: false, showIcons: true);
        var output = Flush(buf);
        // Icon for .md is U+F48A (nf-oct-markdown), followed by a space, then the filename
        var icon = new Rune(0xF48A).ToString();
        Assert.Contains(icon + " README.md", output);
    }

    [Fact]
    public void RenderFileList_ShowIcons_True_SupplementaryPlaneIcon_CountsAsOneColumn()
    {
        // .cs maps to U+F031B (supplementary plane) — should occupy one cell
        var buf = new ScreenBuffer(40, 10);
        var entries = new List<FileSystemEntry> { MakeFile("Program.cs") };
        PaneRenderer.RenderFileList(buf, FullPane(), entries, selectedIndex: 0, scrollOffset: 0, isActive: false, showIcons: true);
        var output = Flush(buf);
        var icon = new Rune(0xF031B).ToString();
        Assert.Contains(icon + " Program.cs", output);
    }

    [Fact]
    public void RenderFileList_ShowIcons_True_Directory_UsesFolderIcon()
    {
        var buf = new ScreenBuffer(40, 10);
        var entries = new List<FileSystemEntry> { MakeDir("src") };
        PaneRenderer.RenderFileList(buf, FullPane(), entries, selectedIndex: 0, scrollOffset: 0, isActive: false, showIcons: true);
        var output = Flush(buf);
        var icon = new Rune(0xF114).ToString(); // nf-fa-folder
        Assert.Contains(icon + " src", output);
    }

    [Fact]
    public void RenderFileList_ScrollOffset_SkipsEntries()
    {
        var buf = new ScreenBuffer(40, 2);
        var entries = new List<FileSystemEntry>
        {
            MakeFile("alpha.txt"),
            MakeFile("beta.txt"),
            MakeFile("gamma.txt"),
        };
        PaneRenderer.RenderFileList(buf, FullPane(40, 2), entries, selectedIndex: 2, scrollOffset: 1, isActive: false);
        var output = Flush(buf);
        Assert.DoesNotContain("alpha", output);
        Assert.Contains("beta", output);
        Assert.Contains("gamma", output);
    }

    [Fact]
    public void RenderFileList_ShowDetails_Tier1_ShowsSizeAndFullDate()
    {
        var dt = new DateTime(2025, 3, 6, 14, 30, 0);
        var buf = new ScreenBuffer(50, 10);
        var entries = new List<FileSystemEntry> { MakeFile("test.txt", 1024, dt) };
        PaneRenderer.RenderFileList(buf, FullPane(50, 10), entries, selectedIndex: 0, scrollOffset: 0, isActive: false, showDetails: true);
        var output = Flush(buf);
        Assert.Contains("1.0 KB", output);
        Assert.Contains("2025-03-06 02:30 PM", output);
    }

    [Fact]
    public void RenderFileList_ShowDetails_DirectoryHasBlankSize()
    {
        var dt = new DateTime(2025, 3, 6, 14, 30, 0);
        var buf = new ScreenBuffer(50, 10);
        var entries = new List<FileSystemEntry> { MakeDir("src", dt) };
        PaneRenderer.RenderFileList(buf, FullPane(50, 10), entries, selectedIndex: 0, scrollOffset: 0, isActive: false, showDetails: true);
        var output = Flush(buf);
        // Date should still show
        Assert.Contains("2025-03-06", output);
        // Size should not appear for directories
        Assert.DoesNotContain("0 B", output);
    }

    [Fact]
    public void RenderFileList_ShowDetails_Tier2_ShowsSizeAndDateOnly()
    {
        // Tier 2: width 32–49
        var dt = new DateTime(2025, 3, 6, 14, 30, 0);
        var buf = new ScreenBuffer(35, 10);
        var entries = new List<FileSystemEntry> { MakeFile("test.txt", 2048, dt) };
        PaneRenderer.RenderFileList(buf, FullPane(35, 10), entries, selectedIndex: 0, scrollOffset: 0, isActive: false, showDetails: true);
        var output = Flush(buf);
        Assert.Contains("2.0 KB", output);
        Assert.Contains("2025-03-06", output);
        Assert.DoesNotContain("02:30 PM", output);
    }

    [Fact]
    public void RenderFileList_ShowDetails_Tier3_ShowsSizeAndShortDate()
    {
        // Tier 3: width 26–31
        var dt = new DateTime(2025, 3, 6, 14, 30, 0);
        var buf = new ScreenBuffer(28, 10);
        var entries = new List<FileSystemEntry> { MakeFile("test.txt", 2048, dt) };
        PaneRenderer.RenderFileList(buf, FullPane(28, 10), entries, selectedIndex: 0, scrollOffset: 0, isActive: false, showDetails: true);
        var output = Flush(buf);
        Assert.Contains("2.0 KB", output);
        Assert.Contains("Mar 06", output);
    }

    [Fact]
    public void RenderFileList_ShowDetails_Tier4_ShowsSizeOnly()
    {
        // Tier 4: width 18–25
        var dt = new DateTime(2025, 3, 6, 14, 30, 0);
        var buf = new ScreenBuffer(20, 10);
        var entries = new List<FileSystemEntry> { MakeFile("test.txt", 512, dt) };
        PaneRenderer.RenderFileList(buf, FullPane(20, 10), entries, selectedIndex: 0, scrollOffset: 0, isActive: false, showDetails: true);
        var output = Flush(buf);
        Assert.Contains("512 B", output);
        Assert.DoesNotContain("2025", output);
        Assert.DoesNotContain("Mar", output);
    }

    [Fact]
    public void RenderFileList_ShowDetails_Tier5_NameOnly()
    {
        // Tier 5: width < 18
        var dt = new DateTime(2025, 3, 6, 14, 30, 0);
        var buf = new ScreenBuffer(16, 10);
        var entries = new List<FileSystemEntry> { MakeFile("test.txt", 512, dt) };
        PaneRenderer.RenderFileList(buf, FullPane(16, 10), entries, selectedIndex: 0, scrollOffset: 0, isActive: false, showDetails: true);
        var output = Flush(buf);
        Assert.Contains("test.txt", output);
        Assert.DoesNotContain("512", output);
        Assert.DoesNotContain("2025", output);
    }

    // ── Multi-select (marked paths) ─────────────────────────────────────────────

    [Fact]
    public void RenderFileList_MarkedEntry_GetsMarkedStyle()
    {
        var entries = new List<FileSystemEntry> { MakeFile("a.txt"), MakeFile("b.txt") };
        var markedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { entries[1].FullPath };

        var bufMarked = new ScreenBuffer(40, 10);
        PaneRenderer.RenderFileList(bufMarked, FullPane(), entries, selectedIndex: 0, scrollOffset: 0, isActive: true, markedPaths: markedPaths);
        var markedOutput = new StringBuilder();
        bufMarked.Flush(markedOutput);

        var bufUnmarked = new ScreenBuffer(40, 10);
        PaneRenderer.RenderFileList(bufUnmarked, FullPane(), entries, selectedIndex: 0, scrollOffset: 0, isActive: true);
        var unmarkedOutput = new StringBuilder();
        bufUnmarked.Flush(unmarkedOutput);

        // The ANSI output should differ because the marked entry gets a different background
        Assert.NotEqual(unmarkedOutput.ToString(), markedOutput.ToString());
    }

    [Fact]
    public void RenderFileList_MarkedAndSelected_GetsMarkedSelectedStyle()
    {
        var entries = new List<FileSystemEntry> { MakeFile("a.txt") };
        var markedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { entries[0].FullPath };

        var bufMarkedSelected = new ScreenBuffer(40, 10);
        PaneRenderer.RenderFileList(bufMarkedSelected, FullPane(), entries, selectedIndex: 0, scrollOffset: 0, isActive: true, markedPaths: markedPaths);
        var markedSelectedOutput = new StringBuilder();
        bufMarkedSelected.Flush(markedSelectedOutput);

        var bufSelectedOnly = new ScreenBuffer(40, 10);
        PaneRenderer.RenderFileList(bufSelectedOnly, FullPane(), entries, selectedIndex: 0, scrollOffset: 0, isActive: true);
        var selectedOnlyOutput = new StringBuilder();
        bufSelectedOnly.Flush(selectedOnlyOutput);

        // Marked+selected should differ from just selected (different background color)
        Assert.NotEqual(selectedOnlyOutput.ToString(), markedSelectedOutput.ToString());
    }

    [Fact]
    public void RenderFileList_NullMarkedPaths_NoError()
    {
        var entries = new List<FileSystemEntry> { MakeFile("a.txt"), MakeDir("src") };

        var buf = new ScreenBuffer(40, 10);
        PaneRenderer.RenderFileList(buf, FullPane(), entries, selectedIndex: 0, scrollOffset: 0, isActive: true, markedPaths: null);
        var output = Flush(buf);

        Assert.Contains("a.txt", output);
        Assert.Contains("src", output);
    }
}
