using System.Text;
using System.Text.RegularExpressions;
using Wade.FileSystem;
using Wade.Highlighting;
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
        new(name, $@"C:\{name}", IsDirectory: false, Size: size, LastModified: lastModified, LinkTarget: null, IsBrokenSymlink: false,
            IsDrive: false);

    private static FileSystemEntry MakeDir(string name, DateTime lastModified = default) =>
        new(name, $@"C:\{name}", IsDirectory: true, Size: 0, LastModified: lastModified, LinkTarget: null, IsBrokenSymlink: false, IsDrive: false);

    private static FileSystemEntry MakeSymlink(string name, string target, bool broken = false) =>
        new(name, $@"C:\{name}", IsDirectory: false, Size: 0, LastModified: default, LinkTarget: target, IsBrokenSymlink: broken, IsDrive: false);

    private static Rect FullPane(int width = 40, int height = 10) => new(0, 0, width, height);

    [Fact]
    public void RenderFileList_ShowIcons_False_PrefixesWithSpaceOrSlash()
    {
        var buf = new ScreenBuffer(40, 10);
        var entries = new List<FileSystemEntry> { MakeFile("README.md") };
        PaneRenderer.RenderFileList(buf, FullPane(), entries, selectedIndex: 0, scrollOffset: 0, isActive: false, showIcons: false);
        string output = Flush(buf);
        Assert.Contains(" README.md", output); // space prefix for files
    }

    [Fact]
    public void RenderFileList_ShowIcons_True_PrefixesWithIconAndSpace()
    {
        var buf = new ScreenBuffer(40, 10);
        var entries = new List<FileSystemEntry> { MakeFile("README.md") };
        PaneRenderer.RenderFileList(buf, FullPane(), entries, selectedIndex: 0, scrollOffset: 0, isActive: false, showIcons: true);
        string output = Flush(buf);
        // Icon for .md is U+F48A (nf-oct-markdown), followed by a space, then the filename
        string icon = new Rune(0xF48A).ToString();
        Assert.Contains(icon + " README.md", output);
    }

    [Fact]
    public void RenderFileList_ShowIcons_True_SupplementaryPlaneIcon_CountsAsOneColumn()
    {
        // .cs maps to U+F031B (supplementary plane) — should occupy one cell
        var buf = new ScreenBuffer(40, 10);
        var entries = new List<FileSystemEntry> { MakeFile("Program.cs") };
        PaneRenderer.RenderFileList(buf, FullPane(), entries, selectedIndex: 0, scrollOffset: 0, isActive: false, showIcons: true);
        string output = Flush(buf);
        string icon = new Rune(0xF031B).ToString();
        Assert.Contains(icon + " Program.cs", output);
    }

    [Fact]
    public void RenderFileList_ShowIcons_True_Directory_UsesFolderIcon()
    {
        var buf = new ScreenBuffer(40, 10);
        var entries = new List<FileSystemEntry> { MakeDir("src") };
        PaneRenderer.RenderFileList(buf, FullPane(), entries, selectedIndex: 0, scrollOffset: 0, isActive: false, showIcons: true);
        string output = Flush(buf);
        string icon = new Rune(0xF114).ToString(); // nf-fa-folder
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
        string output = Flush(buf);
        Assert.DoesNotContain("alpha", output);
        Assert.Contains("beta", output);
        Assert.Contains("gamma", output);
    }

    [Fact]
    public void RenderFileList_ShowDetails_Tier1_ShowsSizeAndFullDate()
    {
        var dt = new DateTime(2025, 3, 6, 14, 30, 0);
        var buf = new ScreenBuffer(65, 10);
        var entries = new List<FileSystemEntry> { MakeFile("test.txt", 1024, dt) };
        PaneRenderer.RenderFileList(buf, FullPane(65), entries, selectedIndex: 0, scrollOffset: 0, isActive: false, showSize: true,
            showDate: true);
        string output = Flush(buf);
        Assert.Contains("1.0 KB", output);
        Assert.Contains("2025-03-06 02:30 PM", output);
    }

    [Fact]
    public void RenderFileList_ShowDetails_DirectoryHasBlankSize()
    {
        var dt = new DateTime(2025, 3, 6, 14, 30, 0);
        var buf = new ScreenBuffer(65, 10);
        var entries = new List<FileSystemEntry> { MakeDir("src", dt) };
        PaneRenderer.RenderFileList(buf, FullPane(65), entries, selectedIndex: 0, scrollOffset: 0, isActive: false, showSize: true,
            showDate: true);
        string output = Flush(buf);
        // Date should still show
        Assert.Contains("2025-03-06", output);
        // Size should not appear for directories
        Assert.DoesNotContain("0 B", output);
    }

    [Fact]
    public void RenderFileList_ShowDetails_Tier2_ShowsSizeAndDateOnly()
    {
        // Tier 2: width 44–60
        var dt = new DateTime(2025, 3, 6, 14, 30, 0);
        var buf = new ScreenBuffer(48, 10);
        var entries = new List<FileSystemEntry> { MakeFile("test.txt", 2048, dt) };
        PaneRenderer.RenderFileList(buf, FullPane(48), entries, selectedIndex: 0, scrollOffset: 0, isActive: false, showSize: true,
            showDate: true);
        string output = Flush(buf);
        Assert.Contains("2.0 KB", output);
        Assert.Contains("2025-03-06", output);
        Assert.DoesNotContain("02:30 PM", output);
    }

    [Fact]
    public void RenderFileList_ShowDetails_Tier3_ShowsSizeAndShortDate()
    {
        // Tier 3: width 32–43
        var dt = new DateTime(2025, 3, 6, 14, 30, 0);
        var buf = new ScreenBuffer(35, 10);
        var entries = new List<FileSystemEntry> { MakeFile("test.txt", 2048, dt) };
        PaneRenderer.RenderFileList(buf, FullPane(35), entries, selectedIndex: 0, scrollOffset: 0, isActive: false, showSize: true,
            showDate: true);
        string output = Flush(buf);
        Assert.Contains("2.0 KB", output);
        Assert.Contains("Mar 06", output);
    }

    [Fact]
    public void RenderFileList_ShowDetails_Tier4_ShowsSizeOnly()
    {
        // Tier 4: width 18–31
        var dt = new DateTime(2025, 3, 6, 14, 30, 0);
        var buf = new ScreenBuffer(20, 10);
        var entries = new List<FileSystemEntry> { MakeFile("test.txt", 512, dt) };
        PaneRenderer.RenderFileList(buf, FullPane(20), entries, selectedIndex: 0, scrollOffset: 0, isActive: false, showSize: true,
            showDate: true);
        string output = Flush(buf);
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
        PaneRenderer.RenderFileList(buf, FullPane(16), entries, selectedIndex: 0, scrollOffset: 0, isActive: false, showSize: true,
            showDate: true);
        string output = Flush(buf);
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
        PaneRenderer.RenderFileList(bufMarkedSelected, FullPane(), entries, selectedIndex: 0, scrollOffset: 0, isActive: true,
            markedPaths: markedPaths);
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
        string output = Flush(buf);

        Assert.Contains("a.txt", output);
        Assert.Contains("src", output);
    }

    // ── Independent column toggles ──────────────────────────────────────────────

    [Fact]
    public void RenderFileList_SizeOnly_ShowsSizeButNoDate()
    {
        var dt = new DateTime(2025, 3, 6, 14, 30, 0);
        var buf = new ScreenBuffer(50, 10);
        var entries = new List<FileSystemEntry> { MakeFile("test.txt", 1024, dt) };
        PaneRenderer.RenderFileList(
            buf, FullPane(50), entries, selectedIndex: 0, scrollOffset: 0,
            isActive: false, showSize: true, showDate: false);
        string output = Flush(buf);
        Assert.Contains("1.0 KB", output);
        Assert.DoesNotContain("2025-03-06", output);
    }

    [Fact]
    public void RenderFileList_DateOnly_ShowsDateButNoSize()
    {
        var dt = new DateTime(2025, 3, 6, 14, 30, 0);
        var buf = new ScreenBuffer(50, 10);
        var entries = new List<FileSystemEntry> { MakeFile("test.txt", 1024, dt) };
        PaneRenderer.RenderFileList(
            buf, FullPane(50), entries, selectedIndex: 0, scrollOffset: 0,
            isActive: false, showSize: false, showDate: true);
        string output = Flush(buf);
        Assert.Contains("2025-03-06", output);
        Assert.DoesNotContain("1.0 KB", output);
    }

    // ── Ellipsis for truncated names ──────────────────────────────────────────

    [Fact]
    public void RenderFileList_NameFits_NoEllipsis()
    {
        var buf = new ScreenBuffer(40, 10);
        var entries = new List<FileSystemEntry> { MakeFile("short.txt") };
        PaneRenderer.RenderFileList(buf, FullPane(), entries, selectedIndex: 0, scrollOffset: 0, isActive: false, showIcons: false);
        string output = Flush(buf);
        Assert.Contains("short.txt", output);
        Assert.DoesNotContain("\u2026", output);
    }

    [Fact]
    public void RenderFileList_NameTruncated_NoIcons_EndsWithEllipsis()
    {
        // Width 15, no icons: prefix=' '(1) leaves 14 chars for name
        string longName = new string('A', 20) + ".txt"; // 24 chars, won't fit in 14
        var buf = new ScreenBuffer(15, 10);
        var entries = new List<FileSystemEntry> { MakeFile(longName) };
        PaneRenderer.RenderFileList(buf, FullPane(15), entries, selectedIndex: 0, scrollOffset: 0, isActive: false, showIcons: false);
        string output = Flush(buf);
        // Should show 13 chars of the name + ellipsis
        Assert.Contains(longName[..13] + "\u2026", output);
    }

    [Fact]
    public void RenderFileList_NameTruncated_WithIcons_EndsWithEllipsis()
    {
        // Width 15, icons: icon(1)+space(1) leaves 13 chars for name
        string longName = new string('B', 20) + ".txt"; // 24 chars, won't fit in 13
        var buf = new ScreenBuffer(15, 10);
        var entries = new List<FileSystemEntry> { MakeFile(longName) };
        PaneRenderer.RenderFileList(buf, FullPane(15), entries, selectedIndex: 0, scrollOffset: 0, isActive: false, showIcons: true);
        string output = Flush(buf);
        Assert.Contains(longName[..12] + "\u2026", output);
    }

    [Fact]
    public void RenderFileList_SymlinkTargetTruncated_EndsWithEllipsis()
    {
        // Short name so symlink target gets space, but target is very long
        string longTarget = @"C:\very\long\path\" + new string('Z', 30);
        var buf = new ScreenBuffer(30, 10);
        var entries = new List<FileSystemEntry> { MakeSymlink("link", longTarget) };
        PaneRenderer.RenderFileList(buf, FullPane(30), entries, selectedIndex: 0, scrollOffset: 0, isActive: false, showIcons: false);
        string output = Flush(buf);
        Assert.Contains("\u2026", output);
        // The arrow should still be present
        Assert.Contains("→", output);
    }

    // ── Preview rendering ────────────────────────────────────────────────────

    [Fact]
    public void RenderPreview_ShowLineNumbersFalse_SkipsLineNumbers()
    {
        var buf = new ScreenBuffer(40, 5);
        StyledLine[] lines = [new("hello", null)];
        PaneRenderer.RenderPreview(buf, FullPane(40, 5), lines, showLineNumbers: false);
        string output = Flush(buf);
        Assert.Contains("hello", output);
        // With line numbers enabled, "   1 " would precede content. Without, it should not.
        Assert.DoesNotContain("   1", output);
    }

    [Fact]
    public void RenderPreview_CharStyles_UsesPerCharStyles()
    {
        var buf = new ScreenBuffer(40, 5);
        var style = new CellStyle(new Color(255, 0, 0), null);
        CellStyle[] charStyles = [style, style, style];
        StyledLine[] lines = [new("abc", null, charStyles)];
        PaneRenderer.RenderPreview(buf, FullPane(40, 5), lines, showLineNumbers: false);

        // Flush with ANSI — should contain red color escape
        var sb = new StringBuilder();
        buf.Flush(sb);
        string raw = sb.ToString();
        Assert.Contains("abc", StripAnsi(raw));
        // Should contain ANSI sequences for the red FG color
        Assert.Contains("\x1b[", raw);
    }
}
