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
            DisabledTools: new HashSet<string>(),
            ZipPreviewEnabled: true,
            ImagePreviewsEnabled: true);

    [Theory]
    [InlineData("app.exe")]
    [InlineData("lib.dll")]
    public void ExeOrDll_ReturnsExecutableMetadataProvider(string path)
    {
        var providers = MetadataProviderRegistry.GetApplicableProviders(path, MakeContext());

        var provider = Assert.Single(providers);
        Assert.IsType<ExecutableMetadataProvider>(provider);
    }

    [Theory]
    [InlineData("report.docx")]
    [InlineData("data.xlsx")]
    [InlineData("slides.pptx")]
    public void OfficeFile_ReturnsOfficeMetadataProvider(string path)
    {
        var providers = MetadataProviderRegistry.GetApplicableProviders(path, MakeContext());

        var provider = Assert.Single(providers);
        Assert.IsType<OfficeMetadataProvider>(provider);
    }

    [Theory]
    [InlineData("package.nupkg")]
    [InlineData("symbols.snupkg")]
    public void NupkgFile_ReturnsNuGetMetadataProvider(string path)
    {
        var providers = MetadataProviderRegistry.GetApplicableProviders(path, MakeContext());

        var provider = Assert.Single(providers);
        Assert.IsType<NuGetMetadataProvider>(provider);
    }

    [Theory]
    [InlineData("file.cs")]
    [InlineData("file.zip")]
    [InlineData("file.png")]
    [InlineData("readme.txt")]
    public void NonMetadataFile_ReturnsEmptyList(string path)
    {
        var providers = MetadataProviderRegistry.GetApplicableProviders(path, MakeContext());

        Assert.Empty(providers);
    }

    [Fact]
    public void CloudPlaceholder_ReturnsEmptyList()
    {
        var providers = MetadataProviderRegistry.GetApplicableProviders("app.exe", MakeContext(isCloudPlaceholder: true));

        Assert.Empty(providers);
    }

    [Fact]
    public void BrokenSymlink_ReturnsEmptyList()
    {
        var providers = MetadataProviderRegistry.GetApplicableProviders("app.exe", MakeContext(isBrokenSymlink: true));

        Assert.Empty(providers);
    }

    [Fact]
    public void RegistryPreservesProviderOrder()
    {
        // exe/dll matches ExecutableMetadataProvider (first in registry)
        var exeProviders = MetadataProviderRegistry.GetApplicableProviders("app.exe", MakeContext());
        Assert.IsType<ExecutableMetadataProvider>(exeProviders[0]);

        // docx matches OfficeMetadataProvider (second in registry)
        var docxProviders = MetadataProviderRegistry.GetApplicableProviders("report.docx", MakeContext());
        Assert.IsType<OfficeMetadataProvider>(docxProviders[0]);

        // nupkg matches NuGetMetadataProvider (fourth in registry)
        var nupkgProviders = MetadataProviderRegistry.GetApplicableProviders("package.nupkg", MakeContext());
        Assert.IsType<NuGetMetadataProvider>(nupkgProviders[0]);
    }
}
