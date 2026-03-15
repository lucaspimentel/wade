using Wade.FileSystem;
using Wade.Preview;

namespace Wade.Tests;

public class PreviewProviderRegistryTests
{
    private static PreviewContext MakeContext(
        bool isCloudPlaceholder = false,
        GitFileStatus? gitStatus = null,
        string? repoRoot = null,
        bool glowEnabled = false,
        bool zipPreviewEnabled = true,
        bool pdfPreviewEnabled = true,
        bool imagePreviewsEnabled = true) =>
        new(
            PaneWidthCells: 40,
            PaneHeightCells: 30,
            CellPixelWidth: 8,
            CellPixelHeight: 16,
            IsCloudPlaceholder: isCloudPlaceholder,
            GitStatus: gitStatus,
            RepoRoot: repoRoot,
            GlowEnabled: glowEnabled,
            ZipPreviewEnabled: zipPreviewEnabled,
            PdfPreviewEnabled: pdfPreviewEnabled,
            ImagePreviewsEnabled: imagePreviewsEnabled);

    [Fact]
    public void TextFile_ReturnsTextThenHex()
    {
        var providers = PreviewProviderRegistry.GetApplicableProviders("file.cs", MakeContext());

        Assert.Equal(2, providers.Count);
        Assert.IsType<TextPreviewProvider>(providers[0]);
        Assert.IsType<HexPreviewProvider>(providers[1]);
    }

    [Fact]
    public void ZipFile_ReturnsZipContentsThenTextThenHex()
    {
        var providers = PreviewProviderRegistry.GetApplicableProviders("file.zip", MakeContext());

        Assert.Equal(3, providers.Count);
        Assert.IsType<ZipContentsPreviewProvider>(providers[0]);
        Assert.IsType<TextPreviewProvider>(providers[1]);
        Assert.IsType<HexPreviewProvider>(providers[2]);
    }

    [Fact]
    public void ImageFile_ReturnsImageThenTextThenHex()
    {
        var providers = PreviewProviderRegistry.GetApplicableProviders("file.png", MakeContext());

        Assert.Equal(3, providers.Count);
        Assert.IsType<ImagePreviewProvider>(providers[0]);
        Assert.IsType<TextPreviewProvider>(providers[1]);
        Assert.IsType<HexPreviewProvider>(providers[2]);
    }

    [Fact]
    public void CloudPlaceholder_ReturnsEmpty()
    {
        var providers = PreviewProviderRegistry.GetApplicableProviders("file.cs", MakeContext(isCloudPlaceholder: true));

        Assert.Empty(providers);
    }

    [Fact]
    public void GitModifiedTextFile_ReturnsTextThenHexThenDiff()
    {
        var context = MakeContext(gitStatus: GitFileStatus.Modified, repoRoot: "/repo");
        var providers = PreviewProviderRegistry.GetApplicableProviders("file.cs", context);

        Assert.Equal(3, providers.Count);
        Assert.IsType<TextPreviewProvider>(providers[0]);
        Assert.IsType<HexPreviewProvider>(providers[1]);
        Assert.IsType<DiffPreviewProvider>(providers[2]);
    }

    [Fact]
    public void GitModifiedZipFile_ReturnsZipContentsThenTextThenHexThenDiff()
    {
        var context = MakeContext(gitStatus: GitFileStatus.Modified, repoRoot: "/repo");
        var providers = PreviewProviderRegistry.GetApplicableProviders("file.nupkg", context);

        Assert.Equal(5, providers.Count);
        Assert.IsType<NuGetPreviewProvider>(providers[0]);
        Assert.IsType<ZipContentsPreviewProvider>(providers[1]);
        Assert.IsType<TextPreviewProvider>(providers[2]);
        Assert.IsType<HexPreviewProvider>(providers[3]);
        Assert.IsType<DiffPreviewProvider>(providers[4]);
    }

    [Fact]
    public void NupkgFile_ReturnsNuGetThenZipContentsThenTextThenHex()
    {
        var providers = PreviewProviderRegistry.GetApplicableProviders("package.nupkg", MakeContext());

        Assert.Equal(4, providers.Count);
        Assert.IsType<NuGetPreviewProvider>(providers[0]);
        Assert.IsType<ZipContentsPreviewProvider>(providers[1]);
        Assert.IsType<TextPreviewProvider>(providers[2]);
        Assert.IsType<HexPreviewProvider>(providers[3]);
    }

    [Fact]
    public void ZipFile_DoesNotIncludeNuGetProvider()
    {
        var providers = PreviewProviderRegistry.GetApplicableProviders("file.zip", MakeContext());

        Assert.DoesNotContain(providers, p => p is NuGetPreviewProvider);
    }

    [Fact]
    public void ImagePreviewsDisabled_ExcludesImageProvider()
    {
        var providers = PreviewProviderRegistry.GetApplicableProviders("file.png", MakeContext(imagePreviewsEnabled: false));

        Assert.DoesNotContain(providers, p => p is ImagePreviewProvider);
        Assert.Contains(providers, p => p is TextPreviewProvider);
    }

    [Fact]
    public void ZipPreviewDisabled_ExcludesZipContentsProvider()
    {
        var providers = PreviewProviderRegistry.GetApplicableProviders("file.zip", MakeContext(zipPreviewEnabled: false));

        Assert.DoesNotContain(providers, p => p is ZipContentsPreviewProvider);
        Assert.Contains(providers, p => p is TextPreviewProvider);
    }

    [Fact]
    public void StagedFile_IncludesDiffProvider()
    {
        var context = MakeContext(gitStatus: GitFileStatus.Staged, repoRoot: "/repo");
        var providers = PreviewProviderRegistry.GetApplicableProviders("file.cs", context);

        Assert.Contains(providers, p => p is DiffPreviewProvider);
    }

    [Fact]
    public void UntrackedFile_ExcludesDiffProvider()
    {
        var context = MakeContext(gitStatus: GitFileStatus.Untracked, repoRoot: "/repo");
        var providers = PreviewProviderRegistry.GetApplicableProviders("file.cs", context);

        Assert.DoesNotContain(providers, p => p is DiffPreviewProvider);
    }
}
