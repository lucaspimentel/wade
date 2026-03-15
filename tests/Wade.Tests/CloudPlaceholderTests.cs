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
    public void GetIcon_ReturnsOriginalIcon_ForPlaceholderFile()
    {
        // Cloud placeholders keep their original file type icon
        var icon = FileIcons.GetIcon(PlaceholderFile("document.docx"));
        // docx has a Word document icon (nf-fa-file_text_o)
        Assert.Equal(new Rune(0xF1C2), icon);
    }

    [Fact]
    public void GetIcon_ReturnsFolderIcon_ForPlaceholderDirectory()
    {
        var icon = FileIcons.GetIcon(PlaceholderDir("Photos"));
        Assert.Equal(new Rune(0xF114), icon);
    }

    [Fact]
    public void GetCloudIcon_ReturnsCloudIcon()
    {
        Assert.Equal(new Rune(0xF0163), FileIcons.GetCloudIcon());
    }

    [Fact]
    public void GetIcon_ReturnsNormalIcon_ForNonPlaceholderFile()
    {
        var icon = FileIcons.GetIcon(NormalFile("document.docx"));
        // docx has a Word document icon (nf-fa-file_text_o)
        Assert.Equal(new Rune(0xF1C2), icon);
    }

    [Theory]
    [InlineData(0x00400000, true)]  // FILE_ATTRIBUTE_RECALL_ON_DATA_ACCESS
    [InlineData(0x00004000, true)]  // FILE_ATTRIBUTE_RECALL_ON_OPEN
    [InlineData(0x00404000, true)]  // both flags
    [InlineData(0x00000020, false)] // FILE_ATTRIBUTE_ARCHIVE only
    [InlineData(0x00000000, false)] // no flags
    public void CheckIsCloudPlaceholder_DetectsCorrectly(int attributeBits, bool expected)
    {
        bool result = DirectoryContents.IsCloudPlaceholderAttributes(attributeBits);
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
