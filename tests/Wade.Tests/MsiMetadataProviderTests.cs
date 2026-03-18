using Wade.Preview;

namespace Wade.Tests;

public class MsiMetadataProviderTests
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
    [InlineData("installer.Msi", true)]
    [InlineData("program.exe", false)]
    [InlineData("library.dll", false)]
    [InlineData("readme.txt", false)]
    [InlineData("archive.zip", false)]
    public void CanProvideMetadata_ChecksExtension(string path, bool expected)
    {
        var provider = new MsiMetadataProvider();
        Assert.Equal(expected, provider.CanProvideMetadata(path, MakeContext()));
    }

    [Fact]
    public void GetMetadata_ReturnsNull_WhenCancelled()
    {
        var provider = new MsiMetadataProvider();
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        MetadataResult? result = provider.GetMetadata("test.msi", MakeContext(), cts.Token);
        Assert.Null(result);
    }

    [Fact]
    public void Registry_MsiFile_ReturnsMsiMetadataProvider()
    {
        var providers = MetadataProviderRegistry.GetApplicableProviders("installer.msi", MakeContext());
        Assert.Contains(providers, p => p is MsiMetadataProvider);
    }
}
