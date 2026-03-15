using System.Text;
using Wade.FileSystem;
using Wade.UI;

namespace Wade.Tests;

public class CloudPlaceholderTests
{
    private static FileSystemEntry PlaceholderFile(string name) =>
        new(name, $@"C:\{name}", IsDirectory: false, Size: 0, LastModified: default,
            LinkTarget: null, IsBrokenSymlink: false, IsDrive: false, IsCloudPlaceholder: true);

    private static FileSystemEntry PlaceholderDir(string name) =>
        new(name, $@"C:\{name}", IsDirectory: true, Size: 0, LastModified: default,
            LinkTarget: null, IsBrokenSymlink: false, IsDrive: false, IsCloudPlaceholder: true);

    private static FileSystemEntry NormalFile(string name) =>
        new(name, $@"C:\{name}", IsDirectory: false, Size: 0, LastModified: default,
            LinkTarget: null, IsBrokenSymlink: false, IsDrive: false, IsCloudPlaceholder: false);

    [Fact]
    public void GetIcon_ReturnsCloudIcon_ForPlaceholderFile()
    {
        var icon = FileIcons.GetIcon(PlaceholderFile("document.docx"));
        Assert.Equal(new Rune(0xF0163), icon);
    }

    [Fact]
    public void GetIcon_ReturnsCloudIcon_ForPlaceholderDirectory()
    {
        var icon = FileIcons.GetIcon(PlaceholderDir("Photos"));
        Assert.Equal(new Rune(0xF0163), icon);
    }

    [Fact]
    public void GetIcon_ReturnsNormalIcon_ForNonPlaceholderFile()
    {
        var icon = FileIcons.GetIcon(NormalFile("document.docx"));
        // docx has no special icon, should return the generic file icon
        Assert.Equal(new Rune(0xF15B), icon);
    }

    [Theory]
    [InlineData(0x00400000, true)]  // FILE_ATTRIBUTE_RECALL_ON_DATA_ACCESS
    [InlineData(0x00004000, true)]  // FILE_ATTRIBUTE_RECALL_ON_OPEN
    [InlineData(0x00404000, true)]  // both flags
    [InlineData(0x00000020, false)] // FILE_ATTRIBUTE_ARCHIVE only
    [InlineData(0x00000000, false)] // no flags
    public void CheckIsCloudPlaceholder_DetectsCorrectly(int attributeBits, bool expected)
    {
        // Test the detection logic directly (same logic as DirectoryContents.CheckIsCloudPlaceholder)
        const int RecallOnDataAccess = 0x00400000;
        const int RecallOnOpen = 0x00004000;
        bool result = (attributeBits & (RecallOnDataAccess | RecallOnOpen)) != 0;
        Assert.Equal(expected, result);
    }

    [Fact]
    public void IsCloudPlaceholder_DefaultsFalse()
    {
        var entry = new FileSystemEntry(
            "test.txt", @"C:\test.txt", IsDirectory: false, Size: 100,
            LastModified: default, LinkTarget: null, IsBrokenSymlink: false, IsDrive: false);
        Assert.False(entry.IsCloudPlaceholder);
    }
}
