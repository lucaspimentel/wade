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
    public void ExeOrDll_IncludesExecutableMetadataProvider(string path)
    {
        var providers = MetadataProviderRegistry.GetApplicableProviders(path, MakeContext());

        Assert.Contains(providers, p => p is ExecutableMetadataProvider);
    }

    [Theory]
    [InlineData("report.docx")]
    [InlineData("data.xlsx")]
    [InlineData("slides.pptx")]
    public void OfficeFile_IncludesOfficeMetadataProvider(string path)
    {
        var providers = MetadataProviderRegistry.GetApplicableProviders(path, MakeContext());

        Assert.Contains(providers, p => p is OfficeMetadataProvider);
    }

    [Theory]
    [InlineData("package.nupkg")]
    [InlineData("symbols.snupkg")]
    public void NupkgFile_IncludesNuGetMetadataProvider(string path)
    {
        var providers = MetadataProviderRegistry.GetApplicableProviders(path, MakeContext());

        Assert.Contains(providers, p => p is NuGetMetadataProvider);
    }

    [Theory]
    [InlineData("file.cs")]
    [InlineData("file.zip")]
    [InlineData("readme.txt")]
    public void NonSpecializedFile_ReturnsOnlyFileMetadataProvider(string path)
    {
        var providers = MetadataProviderRegistry.GetApplicableProviders(path, MakeContext());

        var provider = Assert.Single(providers);
        Assert.IsType<FileMetadataProvider>(provider);
    }

    [Theory]
    [InlineData("photo.png")]
    [InlineData("photo.jpg")]
    [InlineData("photo.gif")]
    public void ImageFile_IncludesImageMetadataProvider(string path)
    {
        var providers = MetadataProviderRegistry.GetApplicableProviders(path, MakeContext());

        Assert.Contains(providers, p => p is ImageMetadataProvider);
    }

    [Fact]
    public void CloudPlaceholder_ReturnsOnlyFileMetadataProvider()
    {
        var providers = MetadataProviderRegistry.GetApplicableProviders("app.exe", MakeContext(isCloudPlaceholder: true));

        var provider = Assert.Single(providers);
        Assert.IsType<FileMetadataProvider>(provider);
    }

    [Fact]
    public void BrokenSymlink_ReturnsEmptyList()
    {
        var providers = MetadataProviderRegistry.GetApplicableProviders("app.exe", MakeContext(isBrokenSymlink: true));

        Assert.Empty(providers);
    }

    [Fact]
    public void FileMetadataProvider_IsAlwaysFirst()
    {
        // FileMetadataProvider must be first so the filename section appears at the top
        foreach (string path in (string[])["app.exe", "report.docx", "package.nupkg", "photo.png", "file.txt"])
        {
            var providers = MetadataProviderRegistry.GetApplicableProviders(path, MakeContext());
            Assert.IsType<FileMetadataProvider>(providers[0]);
        }
    }

    [Fact]
    public void RegistryPreservesProviderOrder()
    {
        // image: FileMetadataProvider first, then ImageMetadataProvider
        var imgProviders = MetadataProviderRegistry.GetApplicableProviders("photo.png", MakeContext());
        Assert.IsType<FileMetadataProvider>(imgProviders[0]);
        Assert.IsType<ImageMetadataProvider>(imgProviders[1]);

        // exe/dll: FileMetadataProvider first, then ExecutableMetadataProvider
        var exeProviders = MetadataProviderRegistry.GetApplicableProviders("app.exe", MakeContext());
        Assert.IsType<FileMetadataProvider>(exeProviders[0]);
        Assert.IsType<ExecutableMetadataProvider>(exeProviders[1]);

        // docx: FileMetadataProvider first, then OfficeMetadataProvider
        var docxProviders = MetadataProviderRegistry.GetApplicableProviders("report.docx", MakeContext());
        Assert.IsType<FileMetadataProvider>(docxProviders[0]);
        Assert.IsType<OfficeMetadataProvider>(docxProviders[1]);

        // nupkg: FileMetadataProvider first, then NuGetMetadataProvider
        var nupkgProviders = MetadataProviderRegistry.GetApplicableProviders("package.nupkg", MakeContext());
        Assert.IsType<FileMetadataProvider>(nupkgProviders[0]);
        Assert.IsType<NuGetMetadataProvider>(nupkgProviders[1]);
    }
}
