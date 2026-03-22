using Wade.FileSystem;

namespace Wade.Tests;

public class InlineDirSizeTests
{
    [Theory]
    [InlineData(DriveMediaType.Ssd, true, false, false, true)]
    [InlineData(DriveMediaType.Ssd, false, false, false, false)]
    [InlineData(DriveMediaType.Hdd, true, true, false, true)]
    [InlineData(DriveMediaType.Hdd, true, false, false, false)]
    [InlineData(DriveMediaType.Network, true, false, true, true)]
    [InlineData(DriveMediaType.Network, true, false, false, false)]
    [InlineData(DriveMediaType.Removable, true, false, false, true)]   // follows SSD
    [InlineData(DriveMediaType.Removable, false, false, false, false)] // follows SSD
    [InlineData(DriveMediaType.Unknown, true, true, true, false)]      // Unknown = disabled
    internal void ShouldComputeInlineDirSizes_ReturnsExpected(
        DriveMediaType driveType,
        bool ssdEnabled,
        bool hddEnabled,
        bool networkEnabled,
        bool expected)
    {
        var config = new WadeConfig
        {
            DirSizeSsdEnabled = ssdEnabled,
            DirSizeHddEnabled = hddEnabled,
            DirSizeNetworkEnabled = networkEnabled,
        };

        bool result = App.ShouldComputeInlineDirSizes(driveType, config);
        Assert.Equal(expected, result);
    }
}
