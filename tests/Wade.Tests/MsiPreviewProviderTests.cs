using Wade.Preview;

namespace Wade.Tests;

public class MsiPreviewProviderTests
{
    private static PreviewContext MakeContext() =>
        new(
            PaneWidthCells: 60,
            PaneHeightCells: 30,
            CellPixelWidth: 8,
            CellPixelHeight: 16,
            IsCloudPlaceholder: false,
            IsBrokenSymlink: false,
            GitStatus: null,
            RepoRoot: null,
            DisabledTools: new HashSet<string>(),
            ZipPreviewEnabled: true,
            ImagePreviewsEnabled: true,
            SixelSupported: true,
            ArchiveMetadataEnabled: true);

    [Theory]
    [InlineData("installer.msi", true)]
    [InlineData("installer.MSI", true)]
    [InlineData("program.exe", false)]
    [InlineData("readme.txt", false)]
    public void CanPreview_ChecksExtensionAndPlatform(string path, bool expected)
    {
        var provider = new MsiPreviewProvider();
        bool result = provider.CanPreview(path, MakeContext());

        if (!OperatingSystem.IsWindows())
        {
            // On non-Windows, CanPreview always returns false
            Assert.False(result);
        }
        else
        {
            Assert.Equal(expected, result);
        }
    }

    [Fact]
    public void GetPreview_ReturnsNull_WhenCancelled()
    {
        var provider = new MsiPreviewProvider();
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        PreviewResult? result = provider.GetPreview("test.msi", MakeContext(), cts.Token);
        Assert.Null(result);
    }

    [Fact]
    public void Registry_MsiFile_OnWindows_ReturnsMsiPreviewProvider()
    {
        List<IPreviewProvider> providers = PreviewProviderRegistry.GetApplicableProviders("installer.msi", MakeContext());

        if (OperatingSystem.IsWindows())
        {
            Assert.Contains(providers, p => p is MsiPreviewProvider);
        }
        else
        {
            Assert.DoesNotContain(providers, p => p is MsiPreviewProvider);
        }
    }
}
