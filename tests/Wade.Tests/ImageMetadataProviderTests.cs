using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Wade.Preview;

namespace Wade.Tests;

public class ImageMetadataProviderTests
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

    private static string CreateTempPng(int width, int height)
    {
        string path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}.png");
        using var image = new Image<Rgba32>(width, height);
        image.SaveAsPng(path);
        return path;
    }

    // ── Extension matching ────────────────────────────────────────────────────

    [Theory]
    [InlineData("photo.png")]
    [InlineData("photo.jpg")]
    [InlineData("photo.jpeg")]
    [InlineData("photo.gif")]
    [InlineData("photo.bmp")]
    [InlineData("photo.webp")]
    public void CanProvideMetadata_ImageExtensions_ReturnsTrue(string path)
    {
        var provider = new ImageMetadataProvider();
        Assert.True(provider.CanProvideMetadata(path, MakeContext()));
    }

    [Theory]
    [InlineData("readme.txt")]
    [InlineData("doc.pdf")]
    [InlineData("app.exe")]
    public void CanProvideMetadata_NonImageExtension_ReturnsFalse(string path)
    {
        var provider = new ImageMetadataProvider();
        Assert.False(provider.CanProvideMetadata(path, MakeContext()));
    }

    // ── GetMetadata ───────────────────────────────────────────────────────────

    [Fact]
    public void GetMetadata_PngFile_ReturnsDimensions()
    {
        string tempPath = CreateTempPng(2, 3);

        try
        {
            var provider = new ImageMetadataProvider();
            MetadataResult? result = provider.GetMetadata(tempPath, MakeContext(), CancellationToken.None);

            Assert.NotNull(result);
            Assert.Contains(result!.Sections, s => s.Header == "Image");

            MetadataSection imageSection = Assert.Single(result.Sections, s => s.Header == "Image");
            Assert.Contains(imageSection.Entries, e => e.Label == "Resolution" && e.Value == "2 \u00d7 3");
            Assert.Contains(imageSection.Entries, e => e.Label == "Format" && e.Value == "PNG");
            Assert.Contains(imageSection.Entries, e => e.Label == "Color depth");
            Assert.Equal("PNG (2 \u00d7 3)", result.FileTypeLabel);
        }
        finally
        {
            File.Delete(tempPath);
        }
    }

    [Fact]
    public void GetMetadata_CancelledToken_ReturnsNull()
    {
        string tempPath = CreateTempPng(1, 1);

        try
        {
            var provider = new ImageMetadataProvider();
            using var cts = new CancellationTokenSource();
            cts.Cancel();

            MetadataResult? result = provider.GetMetadata(tempPath, MakeContext(), cts.Token);

            Assert.Null(result);
        }
        finally
        {
            File.Delete(tempPath);
        }
    }

    [Fact]
    public void GetMetadata_InvalidFile_ReturnsNull()
    {
        string tempPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}.png");

        try
        {
            File.WriteAllBytes(tempPath, [0xFF, 0x00, 0xDE, 0xAD]);

            var provider = new ImageMetadataProvider();
            MetadataResult? result = provider.GetMetadata(tempPath, MakeContext(), CancellationToken.None);

            Assert.Null(result);
        }
        finally
        {
            File.Delete(tempPath);
        }
    }

    [Fact]
    public void Registry_ImageFile_ReturnsProvider()
    {
        var providers = MetadataProviderRegistry.GetApplicableProviders("photo.jpg", MakeContext());

        Assert.Contains(providers, p => p is ImageMetadataProvider);
    }

    // ── Helper formatting ─────────────────────────────────────────────────────

    [Theory]
    [InlineData("Canon", "EOS R5", "Canon EOS R5")]
    [InlineData("Canon", "Canon EOS R5", "Canon EOS R5")]
    [InlineData(null, "EOS R5", "EOS R5")]
    [InlineData("Canon", null, "Canon")]
    [InlineData(null, null, null)]
    public void FormatCamera_CombinesMakeAndModel(string? make, string? model, string? expected)
    {
        Assert.Equal(expected, ImageMetadataProvider.FormatCamera(make, model));
    }
}
