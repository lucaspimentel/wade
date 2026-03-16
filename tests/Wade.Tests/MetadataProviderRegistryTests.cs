using Wade.Preview;

namespace Wade.Tests;

public class MetadataProviderRegistryTests
{
    private static PreviewContext MakeContext(
        bool isCloudPlaceholder = false,
        bool isBrokenSymlink = false) =>
        new(
            PaneWidthCells: 40,
            PaneHeightCells: 30,
            CellPixelWidth: 8,
            CellPixelHeight: 16,
            IsCloudPlaceholder: isCloudPlaceholder,
            IsBrokenSymlink: isBrokenSymlink,
            GitStatus: null,
            RepoRoot: null,
            GlowEnabled: false,
            ZipPreviewEnabled: true,
            PdfPreviewEnabled: true,
            ImagePreviewsEnabled: true);

    [Theory]
    [InlineData("app.exe")]
    [InlineData("lib.dll")]
    public void ExeOrDll_ReturnsExecutableMetadataProvider(string path)
    {
        var provider = MetadataProviderRegistry.GetProvider(path, MakeContext());

        Assert.NotNull(provider);
        Assert.IsType<ExecutableMetadataProvider>(provider);
    }

    [Theory]
    [InlineData("report.docx")]
    [InlineData("data.xlsx")]
    [InlineData("slides.pptx")]
    public void OfficeFile_ReturnsOfficeMetadataProvider(string path)
    {
        var provider = MetadataProviderRegistry.GetProvider(path, MakeContext());

        Assert.NotNull(provider);
        Assert.IsType<OfficeMetadataProvider>(provider);
    }

    [Theory]
    [InlineData("package.nupkg")]
    [InlineData("symbols.snupkg")]
    public void NupkgFile_ReturnsNuGetMetadataProvider(string path)
    {
        var provider = MetadataProviderRegistry.GetProvider(path, MakeContext());

        Assert.NotNull(provider);
        Assert.IsType<NuGetMetadataProvider>(provider);
    }

    [Theory]
    [InlineData("file.cs")]
    [InlineData("file.zip")]
    [InlineData("file.png")]
    [InlineData("readme.txt")]
    public void NonMetadataFile_ReturnsNull(string path)
    {
        var provider = MetadataProviderRegistry.GetProvider(path, MakeContext());

        Assert.Null(provider);
    }

    [Fact]
    public void CloudPlaceholder_ReturnsNull()
    {
        var provider = MetadataProviderRegistry.GetProvider("app.exe", MakeContext(isCloudPlaceholder: true));

        Assert.Null(provider);
    }

    [Fact]
    public void BrokenSymlink_ReturnsNull()
    {
        var provider = MetadataProviderRegistry.GetProvider("app.exe", MakeContext(isBrokenSymlink: true));

        Assert.Null(provider);
    }
}
