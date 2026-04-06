using Wade.FileSystem;

namespace Wade.Tests;

public class ReparsePointDetectorTests
{
    [Theory]
    [InlineData(0xA0000003u, true)]  // IO_REPARSE_TAG_MOUNT_POINT (junction)
    [InlineData(0xA000000Cu, false)] // IO_REPARSE_TAG_SYMLINK
    [InlineData(0x8000001Bu, false)] // IO_REPARSE_TAG_APPEXECLINK
    [InlineData(0u, false)]          // No tag
    [InlineData(0x80000017u, false)] // IO_REPARSE_TAG_WCI
    public void IsJunctionTag_ReturnsExpected(uint tag, bool expected) =>
        Assert.Equal(expected, ReparsePointDetector.IsJunctionTag(tag));
}
