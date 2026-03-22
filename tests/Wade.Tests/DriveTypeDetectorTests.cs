using Wade.FileSystem;

namespace Wade.Tests;

public class DriveTypeDetectorTests
{
    [Theory]
    [InlineData("0", DriveMediaType.Ssd)]
    [InlineData("1", DriveMediaType.Hdd)]
    [InlineData(null, DriveMediaType.Unknown)]
    [InlineData("", DriveMediaType.Unknown)]
    [InlineData("2", DriveMediaType.Unknown)]
    [InlineData("abc", DriveMediaType.Unknown)]
    internal void ParseRotationalValue_ReturnsExpected(string? content, DriveMediaType expected)
    {
        DriveMediaType result = DriveTypeDetector.ParseRotationalValue(content);
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData(true, DriveMediaType.Hdd)]
    [InlineData(false, DriveMediaType.Ssd)]
    internal void ParseSeekPenaltyResult_ReturnsExpected(bool incursSeekPenalty, DriveMediaType expected)
    {
        DriveMediaType result = DriveTypeDetector.ParseSeekPenaltyResult(incursSeekPenalty);
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("/dev/sda1", "sda")]
    [InlineData("/dev/sda", "sda")]
    [InlineData("/dev/vdb2", "vdb")]
    [InlineData("/dev/nvme0n1p1", "nvme0n1")]
    [InlineData("/dev/nvme0n1", "nvme0n1")]
    [InlineData("/dev/nvme1n1p3", "nvme1n1")]
    [InlineData("/dev/xvda1", "xvda")]
    [InlineData("", null)]
    public void ExtractBaseDevice_ReturnsExpected(string devicePath, string? expected)
    {
        string? result = DriveTypeDetector.ExtractBaseDevice(devicePath);
        Assert.Equal(expected, result);
    }
}
