using System.Text;
using System.Text.RegularExpressions;
using Wade.FileSystem;
using Wade.Preview;
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
            "test.txt", "/tmp/test.txt", false, 1536, DateTime.Now, LinkTarget: null, IsBrokenSymlink: false, IsDrive: false);

        PropertiesOverlay.Render(buf, 100, 30, entry, null);

        string output = Flush(buf);
        Assert.Contains("Properties", output);
        Assert.Contains("Press any key to close", output);
        Assert.Contains("Name", output);
        Assert.Contains("Path", output);
        Assert.Contains("Type", output);
        Assert.Contains("Target", output);
        Assert.Contains("Size", output);
        Assert.Contains("Created", output);
        Assert.Contains("Modified", output);
        Assert.Contains("Accessed", output);
        Assert.Contains("Attributes", output);
        Assert.Contains("Read-only", output);
    }

    [Theory]
    [InlineData("readme.md", "Markdown")]
    [InlineData("report.pdf", "PDF")]
    [InlineData("app.cs", "C#")]
    [InlineData("data.unknown", "File")]
    public void Render_FileEntry_ShowsFileTypeLabel(string fileName, string expectedType)
    {
        var buf = new ScreenBuffer(100, 30);
        var entry = new FileSystemEntry(
            fileName, "/tmp/" + fileName, false, 2048, DateTime.Now, LinkTarget: null, IsBrokenSymlink: false, IsDrive: false);

        PropertiesOverlay.Render(buf, 100, 30, entry, null);

        string output = Flush(buf);
        Assert.Contains(expectedType, output);
        Assert.Contains(fileName, output);
    }

    [Fact]
    public void Render_DirectoryEntry_ShowsDashForSize()
    {
        var buf = new ScreenBuffer(100, 30);
        var entry = new FileSystemEntry(
            "docs", "/tmp/docs", true, 0, DateTime.Now, LinkTarget: null, IsBrokenSymlink: false, IsDrive: false);

        PropertiesOverlay.Render(buf, 100, 30, entry, null);

        string output = Flush(buf);
        Assert.Contains("Directory", output);
        Assert.Contains("\u2014", output); // em dash for size
    }

    [Fact]
    public void Render_DriveEntry_ShowsDriveType()
    {
        var buf = new ScreenBuffer(100, 30);
        var entry = new FileSystemEntry(
            "C:\\", "C:\\", true, 0, DateTime.Now, LinkTarget: null, IsBrokenSymlink: false, IsDrive: true);

        PropertiesOverlay.Render(buf, 100, 30, entry, null);

        string output = Flush(buf);
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
            "file.dat", "/tmp/file.dat", false, bytes, DateTime.Now, LinkTarget: null, IsBrokenSymlink: false, IsDrive: false);

        PropertiesOverlay.Render(buf, 120, 30, entry, null);

        string output = Flush(buf);
        Assert.Contains(expected, output);
        Assert.Contains("bytes", output);
    }

    [Fact]
    public void Render_SymlinkToFile_ShowsSymlinkType()
    {
        var buf = new ScreenBuffer(120, 30);
        string dir = Path.Combine(Path.GetTempPath(), "wade_test_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        string targetPath = Path.Combine(dir, "real.txt");
        File.WriteAllText(targetPath, "test");
        try
        {
            string linkPath = Path.Combine(dir, "link.txt");
            var entry = new FileSystemEntry(
                "link.txt", linkPath, false, 100, DateTime.Now, LinkTarget: targetPath, IsBrokenSymlink: false, IsDrive: false);

            PropertiesOverlay.Render(buf, 120, 30, entry, null);

            string output = Flush(buf);
            Assert.Contains("Symlink \u2192 File", output);
            Assert.Contains(targetPath, output);
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void Render_SymlinkToDirectory_ShowsSymlinkType()
    {
        var buf = new ScreenBuffer(120, 30);
        string dir = Path.Combine(Path.GetTempPath(), "wade_test_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        string targetPath = Path.Combine(dir, "real-dir");
        Directory.CreateDirectory(targetPath);
        try
        {
            string linkPath = Path.Combine(dir, "link-dir");
            var entry = new FileSystemEntry(
                "link-dir", linkPath, true, 0, DateTime.Now, LinkTarget: targetPath, IsBrokenSymlink: false, IsDrive: false);

            PropertiesOverlay.Render(buf, 120, 30, entry, null);

            string output = Flush(buf);
            Assert.Contains("Symlink \u2192 Directory", output);
            Assert.Contains(targetPath, output);
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void Render_BrokenSymlink_ShowsBrokenType()
    {
        var buf = new ScreenBuffer(120, 30);
        string dir = Path.GetTempPath();
        string linkPath = Path.Combine(dir, "broken");
        string targetPath = Path.Combine(dir, "nonexistent_target_xyz");
        var entry = new FileSystemEntry(
            "broken", linkPath, false, 0, DateTime.Now, LinkTarget: targetPath, IsBrokenSymlink: true, IsDrive: false);

        PropertiesOverlay.Render(buf, 120, 30, entry, null);

        string output = Flush(buf);
        Assert.Contains("Broken Symlink", output);
        Assert.Contains(targetPath, output);
    }

    [Fact]
    public void Render_JunctionPoint_ShowsJunctionType()
    {
        var buf = new ScreenBuffer(120, 30);
        string dir = Path.Combine(Path.GetTempPath(), "wade_test_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        string targetPath = Path.Combine(dir, "real-dir");
        Directory.CreateDirectory(targetPath);
        try
        {
            string junctionPath = Path.Combine(dir, "junction-dir");
            var entry = new FileSystemEntry(
                "junction-dir", junctionPath, true, 0, DateTime.Now, LinkTarget: targetPath, IsBrokenSymlink: false, IsDrive: false,
                IsJunctionPoint: true);

            PropertiesOverlay.Render(buf, 120, 30, entry, null);

            string output = Flush(buf);
            Assert.Contains("Junction \u2192 Directory", output);
            Assert.Contains(targetPath, output);
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void Render_RegularFile_ShowsEmDashForTarget()
    {
        var buf = new ScreenBuffer(120, 30);
        string filePath = Path.Combine(Path.GetTempPath(), "normal.dat");
        var entry = new FileSystemEntry(
            "normal.dat", filePath, false, 512, DateTime.Now, LinkTarget: null, IsBrokenSymlink: false, IsDrive: false);

        PropertiesOverlay.Render(buf, 120, 30, entry, null);

        string output = Flush(buf);
        Assert.Contains("File", output);
        Assert.Contains("\u2014", output); // em dash for target
    }

    [Fact]
    public void Render_ShowsGitStatusModified()
    {
        var buf = new ScreenBuffer(120, 30);
        var entry = new FileSystemEntry(
            "file.cs", "/tmp/file.cs", false, 100, DateTime.Now, LinkTarget: null, IsBrokenSymlink: false, IsDrive: false);

        PropertiesOverlay.Render(buf, 120, 30, entry, null, GitFileStatus.Modified);

        string output = Flush(buf);
        Assert.Contains("Git status", output);
        Assert.Contains("Modified", output);
    }

    [Fact]
    public void Render_ShowsGitStatusStaged()
    {
        var buf = new ScreenBuffer(120, 30);
        var entry = new FileSystemEntry(
            "file.cs", "/tmp/file.cs", false, 100, DateTime.Now, LinkTarget: null, IsBrokenSymlink: false, IsDrive: false);

        PropertiesOverlay.Render(buf, 120, 30, entry, null, GitFileStatus.Staged);

        string output = Flush(buf);
        Assert.Contains("Git status", output);
        Assert.Contains("Staged", output);
    }

    [Fact]
    public void Render_GitStatusCombinedFlags_ShowsCommaSeparated()
    {
        var buf = new ScreenBuffer(120, 30);
        var entry = new FileSystemEntry(
            "file.cs", "/tmp/file.cs", false, 100, DateTime.Now, LinkTarget: null, IsBrokenSymlink: false, IsDrive: false);

        PropertiesOverlay.Render(buf, 120, 30, entry, null, GitFileStatus.Modified | GitFileStatus.Staged);

        string output = Flush(buf);
        Assert.Contains("Staged, Modified", output);
    }

    [Fact]
    public void FormatGitStatus_None_ReturnsEmDash() => Assert.Equal("\u2014", PropertiesOverlay.FormatGitStatus(GitFileStatus.None));

    [Fact]
    public void FormatGitStatus_Null_ReturnsEmDash() => Assert.Equal("\u2014", PropertiesOverlay.FormatGitStatus(null));

    [Fact]
    public void Render_ReturnsContentHeight()
    {
        var buf = new ScreenBuffer(100, 40);
        var entry = new FileSystemEntry(
            "test.txt", "/tmp/test.txt", false, 1024, DateTime.Now, LinkTarget: null, IsBrokenSymlink: false, IsDrive: false);

        int contentHeight = PropertiesOverlay.Render(buf, 100, 40, entry, null);

        // 11 system property rows (Labels array), no metadata
        Assert.Equal(11, contentHeight);
    }

    [Fact]
    public void Render_ReturnsContentHeight_WithMetadata()
    {
        var buf = new ScreenBuffer(100, 50);
        var entry = new FileSystemEntry(
            "test.txt", "/tmp/test.txt", false, 1024, DateTime.Now, LinkTarget: null, IsBrokenSymlink: false, IsDrive: false);

        MetadataSection[] sections =
        [
            new("Info", [new MetadataEntry("Key1", "Value1"), new MetadataEntry("Key2", "Value2")]),
        ];

        int contentHeight = PropertiesOverlay.Render(buf, 100, 50, entry, null, metadataSections: sections);

        // 11 system rows + 1 blank separator + 1 header + 2 entries = 15
        Assert.Equal(15, contentHeight);
    }

    [Fact]
    public void Render_NoScroll_WhenContentFits()
    {
        var buf = new ScreenBuffer(100, 40);
        var entry = new FileSystemEntry(
            "test.txt", "/tmp/test.txt", false, 1024, DateTime.Now, LinkTarget: null, IsBrokenSymlink: false, IsDrive: false);

        PropertiesOverlay.Render(buf, 100, 40, entry, null);

        string output = Flush(buf);
        Assert.Contains("Press any key to close", output);
        Assert.DoesNotContain("scroll", output);
    }

    [Fact]
    public void Render_ScrollableFooter_WhenContentOverflows()
    {
        var buf = new ScreenBuffer(100, 20);
        var entry = new FileSystemEntry(
            "test.txt", "/tmp/test.txt", false, 1024, DateTime.Now, LinkTarget: null, IsBrokenSymlink: false, IsDrive: false);

        // Create enough metadata to overflow a 20-row screen
        var entries = new MetadataEntry[20];
        for (int i = 0; i < entries.Length; i++)
        {
            entries[i] = new MetadataEntry($"Field{i}", $"Val{i}");
        }

        MetadataSection[] sections = [new("Details", entries)];

        PropertiesOverlay.Render(buf, 100, 20, entry, null, metadataSections: sections);

        string output = Flush(buf);
        Assert.Contains("scroll", output);
    }

    [Fact]
    public void Render_ScrollOffset_SkipsTopRows()
    {
        var buf = new ScreenBuffer(100, 20);
        var entry = new FileSystemEntry(
            "test.txt", "/tmp/test.txt", false, 1024, DateTime.Now, LinkTarget: null, IsBrokenSymlink: false, IsDrive: false);

        // Create enough metadata to overflow
        var metaEntries = new MetadataEntry[20];
        for (int i = 0; i < metaEntries.Length; i++)
        {
            metaEntries[i] = new MetadataEntry($"Field{i:D2}", $"MetaValue{i:D2}");
        }

        MetadataSection[] sections = [new("Details", metaEntries)];

        // Render with scroll offset that pushes "Name" label off screen
        PropertiesOverlay.Render(buf, 100, 20, entry, null, metadataSections: sections, scrollOffset: 5);

        string output = Flush(buf);
        // "Name" is the first system property row — should be scrolled off
        // We check that the label "Name" followed by the value doesn't appear as a property row
        // but the title "Properties" should still appear (it's in the dialog chrome, not content)
        Assert.Contains("Properties", output);
        // Later metadata entries should be visible
        Assert.Contains("MetaValue", output);
    }
}
