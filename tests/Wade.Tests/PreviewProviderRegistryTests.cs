using Wade.FileSystem;
using Wade.Preview;

namespace Wade.Tests;

public class PreviewProviderRegistryTests : IDisposable
{
    private readonly List<string> _tempFiles = [];

    public void Dispose()
    {
        foreach (string path in _tempFiles)
        {
            try
            {
                File.Delete(path);
            }
            catch
            {
            }
        }
    }

    private string CreateTempFile(string extension, string content = "Hello World")
    {
        string path = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName() + extension);
        File.WriteAllText(path, content);
        _tempFiles.Add(path);
        return path;
    }

    private string CreateTempBinaryFile(string extension, byte[]? content = null)
    {
        string path = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName() + extension);
        File.WriteAllBytes(path, content ?? [0x4D, 0x5A, 0x00, 0x00]);
        _tempFiles.Add(path);
        return path;
    }

    private static PreviewContext MakeContext(
        bool isCloudPlaceholder = false,
        bool isBrokenSymlink = false,
        GitFileStatus? gitStatus = null,
        string? repoRoot = null,
        HashSet<string>? disabledTools = null,
        bool zipPreviewEnabled = true,
        bool imagePreviewsEnabled = true,
        bool sixelSupported = true) =>
        new(
            PaneWidthCells: 40,
            PaneHeightCells: 30,
            CellPixelWidth: 8,
            CellPixelHeight: 16,
            IsCloudPlaceholder: isCloudPlaceholder,
            IsBrokenSymlink: isBrokenSymlink,
            GitStatus: gitStatus,
            RepoRoot: repoRoot,
            DisabledTools: disabledTools ?? new HashSet<string>(),
            ZipPreviewEnabled: zipPreviewEnabled,
            ImagePreviewsEnabled: imagePreviewsEnabled,
            SixelSupported: sixelSupported,
            ArchiveMetadataEnabled: true);

    [Fact]
    public void TextFile_DefaultsToText_WithNoneAndHex()
    {
        string path = CreateTempFile(".cs");
        List<IPreviewProvider> providers = PreviewProviderRegistry.GetApplicableProviders(path, MakeContext());

        Assert.Equal(3, providers.Count);
        Assert.IsType<TextPreviewProvider>(providers[0]);
        Assert.IsType<NonePreviewProvider>(providers[1]);
        Assert.IsType<HexPreviewProvider>(providers[2]);
    }

    [Fact]
    public void ZipFile_ReturnsZipContentsThenNoneThenHex()
    {
        List<IPreviewProvider> providers = PreviewProviderRegistry.GetApplicableProviders("file.zip", MakeContext());

        Assert.Equal(3, providers.Count);
        Assert.IsType<ZipContentsPreviewProvider>(providers[0]);
        Assert.IsType<NonePreviewProvider>(providers[1]);
        Assert.IsType<HexPreviewProvider>(providers[2]);
    }

    [Fact]
    public void ImageFile_ReturnsImageThenNoneThenHex()
    {
        List<IPreviewProvider> providers = PreviewProviderRegistry.GetApplicableProviders("file.png", MakeContext());

        Assert.Equal(3, providers.Count);
        Assert.IsType<ImagePreviewProvider>(providers[0]);
        Assert.IsType<NonePreviewProvider>(providers[1]);
        Assert.IsType<HexPreviewProvider>(providers[2]);
    }

    [Fact]
    public void CloudPlaceholder_ReturnsEmpty()
    {
        List<IPreviewProvider> providers = PreviewProviderRegistry.GetApplicableProviders("file.cs", MakeContext(isCloudPlaceholder: true));

        Assert.Empty(providers);
    }

    [Fact]
    public void BrokenSymlink_ReturnsEmpty()
    {
        List<IPreviewProvider> providers = PreviewProviderRegistry.GetApplicableProviders("file.cs", MakeContext(isBrokenSymlink: true));

        Assert.Empty(providers);
    }

    [Fact]
    public void GitModifiedTextFile_ReturnsTextThenDiffThenNoneThenHex()
    {
        string path = CreateTempFile(".cs");
        PreviewContext context = MakeContext(gitStatus: GitFileStatus.Modified, repoRoot: "/repo");
        List<IPreviewProvider> providers = PreviewProviderRegistry.GetApplicableProviders(path, context);

        Assert.Equal(4, providers.Count);
        Assert.IsType<TextPreviewProvider>(providers[0]);
        Assert.IsType<DiffPreviewProvider>(providers[1]);
        Assert.IsType<NonePreviewProvider>(providers[2]);
        Assert.IsType<HexPreviewProvider>(providers[3]);
    }

    [Fact]
    public void GitModifiedNupkgFile_DefaultsToNone_ArchiveContentsAvailable()
    {
        PreviewContext context = MakeContext(gitStatus: GitFileStatus.Modified, repoRoot: "/repo");
        List<IPreviewProvider> providers = PreviewProviderRegistry.GetApplicableProviders("file.nupkg", context);

        Assert.Equal(4, providers.Count);
        Assert.IsType<DiffPreviewProvider>(providers[0]);
        Assert.IsType<NonePreviewProvider>(providers[1]);
        Assert.IsType<ZipContentsPreviewProvider>(providers[2]);
        Assert.IsType<HexPreviewProvider>(providers[3]);
    }

    [Fact]
    public void NupkgFile_DefaultsToNone_ArchiveContentsAvailable()
    {
        List<IPreviewProvider> providers = PreviewProviderRegistry.GetApplicableProviders("package.nupkg", MakeContext());

        Assert.Equal(3, providers.Count);
        Assert.IsType<NonePreviewProvider>(providers[0]);
        Assert.IsType<ZipContentsPreviewProvider>(providers[1]);
        Assert.IsType<HexPreviewProvider>(providers[2]);
    }

    [Fact]
    public void DocxFile_DefaultsToNone_ArchiveContentsAvailable()
    {
        List<IPreviewProvider> providers = PreviewProviderRegistry.GetApplicableProviders("report.docx", MakeContext());

        Assert.Equal(3, providers.Count);
        Assert.IsType<NonePreviewProvider>(providers[0]);
        Assert.IsType<ZipContentsPreviewProvider>(providers[1]);
        Assert.IsType<HexPreviewProvider>(providers[2]);
    }

    [Fact]
    public void ExeFile_DefaultsToNone_WithHexAvailable()
    {
        List<IPreviewProvider> providers = PreviewProviderRegistry.GetApplicableProviders("app.exe", MakeContext());

        Assert.Equal(2, providers.Count);
        Assert.IsType<NonePreviewProvider>(providers[0]);
        Assert.IsType<HexPreviewProvider>(providers[1]);
    }

    [Fact]
    public void ImagePreviewsDisabled_ExcludesImageProvider()
    {
        List<IPreviewProvider> providers = PreviewProviderRegistry.GetApplicableProviders("file.png", MakeContext(imagePreviewsEnabled: false));

        Assert.DoesNotContain(providers, p => p is ImagePreviewProvider);
        Assert.Contains(providers, p => p is NonePreviewProvider);
        Assert.Contains(providers, p => p is HexPreviewProvider);
    }

    [Fact]
    public void ZipPreviewDisabled_ExcludesZipContentsProvider()
    {
        List<IPreviewProvider> providers = PreviewProviderRegistry.GetApplicableProviders("file.zip", MakeContext(zipPreviewEnabled: false));

        Assert.DoesNotContain(providers, p => p is ZipContentsPreviewProvider);
        Assert.Contains(providers, p => p is NonePreviewProvider);
        Assert.Contains(providers, p => p is HexPreviewProvider);
    }

    [Fact]
    public void StagedFile_IncludesDiffProvider()
    {
        string path = CreateTempFile(".cs");
        PreviewContext context = MakeContext(gitStatus: GitFileStatus.Staged, repoRoot: "/repo");
        List<IPreviewProvider> providers = PreviewProviderRegistry.GetApplicableProviders(path, context);

        Assert.Contains(providers, p => p is DiffPreviewProvider);
        Assert.Contains(providers, p => p is NonePreviewProvider);
        Assert.Contains(providers, p => p is HexPreviewProvider);
    }

    [Fact]
    public void UntrackedFile_ExcludesDiffProvider()
    {
        string path = CreateTempFile(".cs");
        PreviewContext context = MakeContext(gitStatus: GitFileStatus.Untracked, repoRoot: "/repo");
        List<IPreviewProvider> providers = PreviewProviderRegistry.GetApplicableProviders(path, context);

        Assert.DoesNotContain(providers, p => p is DiffPreviewProvider);
        Assert.Contains(providers, p => p is NonePreviewProvider);
        Assert.Contains(providers, p => p is HexPreviewProvider);
    }

    [Fact]
    public void NoneAndHex_AlwaysPresent()
    {
        string path = CreateTempFile(".cs");
        List<IPreviewProvider> providers = PreviewProviderRegistry.GetApplicableProviders(path, MakeContext());

        Assert.Contains(providers, p => p is NonePreviewProvider);
        Assert.Contains(providers, p => p is HexPreviewProvider);
        // None comes before Hex
        int noneIdx = providers.FindIndex(p => p is NonePreviewProvider);
        int hexIdx = providers.FindIndex(p => p is HexPreviewProvider);
        Assert.True(noneIdx < hexIdx);
    }
}
