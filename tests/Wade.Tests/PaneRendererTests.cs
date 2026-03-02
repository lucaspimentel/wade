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

    private static FileSystemEntry MakeFile(string name) =>
        new(name, $@"C:\{name}", IsDirectory: false, Size: 0, LastModified: default);

    private static FileSystemEntry MakeDir(string name) =>
        new(name, $@"C:\{name}", IsDirectory: true, Size: 0, LastModified: default);

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
}
