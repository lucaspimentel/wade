using System.Text;
using Wade.FileSystem;

namespace Wade.Tests;

public class ReparsePointDetectorTests
{
    private static readonly byte[] NullTerminator = [0, 0];

    [Theory]
    [InlineData(0xA0000003u, true)]  // IO_REPARSE_TAG_MOUNT_POINT (junction)
    [InlineData(0xA000000Cu, false)] // IO_REPARSE_TAG_SYMLINK
    [InlineData(0x8000001Bu, false)] // IO_REPARSE_TAG_APPEXECLINK
    [InlineData(0u, false)]          // No tag
    [InlineData(0x80000017u, false)] // IO_REPARSE_TAG_WCI
    public void IsJunctionTag_ReturnsExpected(uint tag, bool expected) =>
        Assert.Equal(expected, ReparsePointDetector.IsJunctionTag(tag));

    [Theory]
    [InlineData(0x8000001Bu, true)]  // IO_REPARSE_TAG_APPEXECLINK
    [InlineData(0xA0000003u, false)] // IO_REPARSE_TAG_MOUNT_POINT
    [InlineData(0xA000000Cu, false)] // IO_REPARSE_TAG_SYMLINK
    [InlineData(0u, false)]          // No tag
    public void IsAppExecLinkTag_ReturnsExpected(uint tag, bool expected) =>
        Assert.Equal(expected, ReparsePointDetector.IsAppExecLinkTag(tag));

    [Fact]
    public void ParseAppExecLinkTarget_ValidBuffer_ReturnsTargetPath()
    {
        string packageId = "Microsoft.WindowsTerminal_8wekyb3d8bbwe";
        string appUserModelId = "Microsoft.WindowsTerminal_8wekyb3d8bbwe!App";
        string targetPath = @"C:\Program Files\WindowsApps\Microsoft.WindowsTerminal\wt.exe";

        byte[] buffer = BuildAppExecLinkBuffer(packageId, appUserModelId, targetPath);

        string? result = ReparsePointDetector.ParseAppExecLinkTarget(buffer);

        Assert.Equal(targetPath, result);
    }

    [Fact]
    public void ParseAppExecLinkTarget_EmptyBuffer_ReturnsNull()
    {
        string? result = ReparsePointDetector.ParseAppExecLinkTarget([]);
        Assert.Null(result);
    }

    [Fact]
    public void ParseAppExecLinkTarget_TruncatedBuffer_ReturnsNull()
    {
        byte[] buffer = new byte[12];
        string? result = ReparsePointDetector.ParseAppExecLinkTarget(buffer);
        Assert.Null(result);
    }

    [Fact]
    public void ParseAppExecLinkTarget_MissingThirdString_ReturnsNull()
    {
        string packageId = "SomePackage";

        using var ms = new MemoryStream();
        ms.Write(new byte[8]); // Header
        ms.Write(BitConverter.GetBytes(3u)); // Version
        ms.Write(Encoding.Unicode.GetBytes(packageId));
        ms.Write(NullTerminator);

        string? result = ReparsePointDetector.ParseAppExecLinkTarget(ms.ToArray());
        Assert.Null(result);
    }

    private static byte[] BuildAppExecLinkBuffer(string packageId, string appUserModelId, string targetPath)
    {
        using var ms = new MemoryStream();

        // Reparse header (8 bytes)
        ms.Write(BitConverter.GetBytes(0x8000001Bu)); // ReparseTag
        ms.Write(NullTerminator); // ReparseDataLength (not used by parser)
        ms.Write(NullTerminator); // Reserved

        // Version
        ms.Write(BitConverter.GetBytes(3u));

        // 3 null-terminated UTF-16 strings
        ms.Write(Encoding.Unicode.GetBytes(packageId));
        ms.Write(NullTerminator);
        ms.Write(Encoding.Unicode.GetBytes(appUserModelId));
        ms.Write(NullTerminator);
        ms.Write(Encoding.Unicode.GetBytes(targetPath));
        ms.Write(NullTerminator);

        return ms.ToArray();
    }
}
