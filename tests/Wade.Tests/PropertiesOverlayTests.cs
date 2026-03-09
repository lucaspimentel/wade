using System.Text;
using System.Text.RegularExpressions;
using Wade.FileSystem;
using Wade.Terminal;
using Wade.UI;

namespace Wade.Tests;

public class PropertiesOverlayTests
{
    private static string StripAnsi(string s) =>
        Regex.Replace(s, @"\x1b\[[^a-zA-Z]*[a-zA-Z]", "");

    private static string Flush(ScreenBuffer buf)
    {
        var sb = new StringBuilder();
        buf.Flush(sb);
        return StripAnsi(sb.ToString());
    }

    [Fact]
    public void Render_ShowsLabelsAndTitle()
    {
        var buf = new ScreenBuffer(100, 30);
        var entry = new FileSystemEntry(
            "test.txt", "/tmp/test.txt", false, 1536, DateTime.Now);

        PropertiesOverlay.Render(buf, 100, 30, entry);

        var output = Flush(buf);
        Assert.Contains("Properties", output);
        Assert.Contains("Press any key to close", output);
        Assert.Contains("Name", output);
        Assert.Contains("Path", output);
        Assert.Contains("Type", output);
        Assert.Contains("Size", output);
        Assert.Contains("Created", output);
        Assert.Contains("Modified", output);
        Assert.Contains("Accessed", output);
        Assert.Contains("Attributes", output);
        Assert.Contains("Read-only", output);
    }

    [Fact]
    public void Render_FileEntry_ShowsFileType()
    {
        var buf = new ScreenBuffer(100, 30);
        var entry = new FileSystemEntry(
            "readme.md", "/tmp/readme.md", false, 2048, DateTime.Now);

        PropertiesOverlay.Render(buf, 100, 30, entry);

        var output = Flush(buf);
        Assert.Contains("File", output);
        Assert.Contains("readme.md", output);
    }

    [Fact]
    public void Render_DirectoryEntry_ShowsDashForSize()
    {
        var buf = new ScreenBuffer(100, 30);
        var entry = new FileSystemEntry(
            "docs", "/tmp/docs", true, 0, DateTime.Now);

        PropertiesOverlay.Render(buf, 100, 30, entry);

        var output = Flush(buf);
        Assert.Contains("Directory", output);
        Assert.Contains("\u2014", output); // em dash for size
    }

    [Fact]
    public void Render_DriveEntry_ShowsDriveType()
    {
        var buf = new ScreenBuffer(100, 30);
        var entry = new FileSystemEntry(
            "C:\\", "C:\\", true, 0, DateTime.Now, IsDrive: true);

        PropertiesOverlay.Render(buf, 100, 30, entry);

        var output = Flush(buf);
        Assert.Contains("Drive", output);
    }

    [Theory]
    [InlineData(512, "512 B")]
    [InlineData(1536, "1.5 KB")]
    [InlineData(1_572_864, "1.5 MB")]
    public void Render_FileEntry_ShowsFormattedSize(long bytes, string expected)
    {
        var buf = new ScreenBuffer(120, 30);
        var entry = new FileSystemEntry(
            "file.dat", "/tmp/file.dat", false, bytes, DateTime.Now);

        PropertiesOverlay.Render(buf, 120, 30, entry);

        var output = Flush(buf);
        Assert.Contains(expected, output);
        Assert.Contains("bytes", output);
    }
}
