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
            PdfPreviewEnabled: true,
            PdfMetadataEnabled: true,
            MarkdownPreviewEnabled: true,
            FfprobeEnabled: true,
            MediainfoEnabled: true,
            ZipPreviewEnabled: true,
            ImagePreviewsEnabled: true,
            SixelSupported: true,
            ArchiveMetadataEnabled: true);

    [Theory]
    [InlineData("app.exe")]
    [InlineData("lib.dll")]
    public void ExeOrDll_IncludesExecutableMetadataProvider(string path)
    {
        List<IMetadataProvider> providers = MetadataProviderRegistry.GetApplicableProviders(path, MakeContext());

        Assert.Contains(providers, p => p is ExecutableMetadataProvider);
    }

    [Theory]
    [InlineData("report.docx")]
    [InlineData("data.xlsx")]
    [InlineData("slides.pptx")]
    public void OfficeFile_IncludesOfficeMetadataProvider(string path)
    {
        List<IMetadataProvider> providers = MetadataProviderRegistry.GetApplicableProviders(path, MakeContext());

        Assert.Contains(providers, p => p is OfficeMetadataProvider);
    }

    [Theory]
    [InlineData("package.nupkg")]
    [InlineData("symbols.snupkg")]
    public void NupkgFile_IncludesNuGetMetadataProvider(string path)
    {
        List<IMetadataProvider> providers = MetadataProviderRegistry.GetApplicableProviders(path, MakeContext());

        Assert.Contains(providers, p => p is NuGetMetadataProvider);
    }

    [Theory]
    [InlineData("file.cs")]
    [InlineData("readme.txt")]
    public void NonSpecializedFile_ReturnsOnlyFileMetadataProvider(string path)
    {
        List<IMetadataProvider> providers = MetadataProviderRegistry.GetApplicableProviders(path, MakeContext());

        IMetadataProvider provider = Assert.Single(providers);
        Assert.IsType<FileMetadataProvider>(provider);
    }

    [Theory]
    [InlineData("archive.zip")]
    [InlineData("lib.jar")]
    public void ZipFile_IncludesArchiveMetadataProvider(string path)
    {
        List<IMetadataProvider> providers = MetadataProviderRegistry.GetApplicableProviders(path, MakeContext());

        Assert.Contains(providers, p => p is ArchiveMetadataProvider);
    }

    [Theory]
    [InlineData("photo.png")]
    [InlineData("photo.jpg")]
    [InlineData("photo.gif")]
    public void ImageFile_IncludesImageMetadataProvider(string path)
    {
        List<IMetadataProvider> providers = MetadataProviderRegistry.GetApplicableProviders(path, MakeContext());

        Assert.Contains(providers, p => p is ImageMetadataProvider);
    }

    [Fact]
    public void CloudPlaceholder_ReturnsOnlyFileMetadataProvider()
    {
        List<IMetadataProvider> providers = MetadataProviderRegistry.GetApplicableProviders("app.exe", MakeContext(isCloudPlaceholder: true));

        IMetadataProvider provider = Assert.Single(providers);
        Assert.IsType<FileMetadataProvider>(provider);
    }

    [Fact]
    public void BrokenSymlink_ReturnsEmptyList()
    {
        List<IMetadataProvider> providers = MetadataProviderRegistry.GetApplicableProviders("app.exe", MakeContext(isBrokenSymlink: true));

        Assert.Empty(providers);
    }

    [Fact]
    public void FileMetadataProvider_IsAlwaysFirst()
    {
        // FileMetadataProvider must be first so the filename section appears at the top
        foreach (string path in (string[])["app.exe", "report.docx", "package.nupkg", "photo.png", "file.txt"])
        {
            List<IMetadataProvider> providers = MetadataProviderRegistry.GetApplicableProviders(path, MakeContext());
            Assert.IsType<FileMetadataProvider>(providers[0]);
        }
    }

    [Fact]
    public void RegistryPreservesProviderOrder()
    {
        // image: FileMetadataProvider first, then ImageMetadataProvider
        List<IMetadataProvider> imgProviders = MetadataProviderRegistry.GetApplicableProviders("photo.png", MakeContext());
        Assert.IsType<FileMetadataProvider>(imgProviders[0]);
        Assert.IsType<ImageMetadataProvider>(imgProviders[1]);

        // exe/dll: FileMetadataProvider first, then ExecutableMetadataProvider
        List<IMetadataProvider> exeProviders = MetadataProviderRegistry.GetApplicableProviders("app.exe", MakeContext());
        Assert.IsType<FileMetadataProvider>(exeProviders[0]);
        Assert.IsType<ExecutableMetadataProvider>(exeProviders[1]);

        // docx: FileMetadataProvider, OfficeMetadataProvider, ArchiveMetadataProvider
        List<IMetadataProvider> docxProviders = MetadataProviderRegistry.GetApplicableProviders("report.docx", MakeContext());
        Assert.IsType<FileMetadataProvider>(docxProviders[0]);
        Assert.IsType<OfficeMetadataProvider>(docxProviders[1]);
        Assert.IsType<ArchiveMetadataProvider>(docxProviders[2]);

        // nupkg: FileMetadataProvider, NuGetMetadataProvider, ArchiveMetadataProvider
        List<IMetadataProvider> nupkgProviders = MetadataProviderRegistry.GetApplicableProviders("package.nupkg", MakeContext());
        Assert.IsType<FileMetadataProvider>(nupkgProviders[0]);
        Assert.IsType<NuGetMetadataProvider>(nupkgProviders[1]);
        Assert.IsType<ArchiveMetadataProvider>(nupkgProviders[2]);

        // zip: FileMetadataProvider, ArchiveMetadataProvider
        List<IMetadataProvider> zipProviders = MetadataProviderRegistry.GetApplicableProviders("archive.zip", MakeContext());
        Assert.IsType<FileMetadataProvider>(zipProviders[0]);
        Assert.IsType<ArchiveMetadataProvider>(zipProviders[1]);
    }
}
