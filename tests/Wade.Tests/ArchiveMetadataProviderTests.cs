using System.IO.Compression;
using Wade.Preview;

namespace Wade.Tests;

public class ArchiveMetadataProviderTests
{
    private readonly ArchiveMetadataProvider _provider = new();

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
            PdfPreviewEnabled: true,
            PdfMetadataEnabled: true,
            MarkdownPreviewEnabled: true,
            GlowPreviewEnabled: true,
            FfprobeEnabled: true,
            MediainfoEnabled: true,
            ZipPreviewEnabled: true,
            ImagePreviewsEnabled: true,
            SixelSupported: true,
            ArchiveMetadataEnabled: true);

    // ── Extension matching ────────────────────────────────────────────────────

    [Theory]
    [InlineData("archive.zip")]
    [InlineData("lib.jar")]
    [InlineData("package.nupkg")]
    [InlineData("doc.docx")]
    public void CanProvideMetadata_ZipExtensions_ReturnsTrue(string path) => Assert.True(_provider.CanProvideMetadata(path, MakeContext()));

    [Theory]
    [InlineData("file.txt")]
    [InlineData("file.tar")]
    [InlineData("file.gz")]
    public void CanProvideMetadata_NonZipExtensions_ReturnsFalse(string path) => Assert.False(_provider.CanProvideMetadata(path, MakeContext()));

    // ── Metadata extraction ───────────────────────────────────────────────────

    [Fact]
    public void GetMetadata_ValidZip_ReturnsArchiveSection()
    {
        string zipPath = CreateTempZip(("hello.txt", "Hello World"), ("dir/nested.txt", "Nested"));
        try
        {
            MetadataResult? result = _provider.GetMetadata(zipPath, MakeContext(), CancellationToken.None);

            Assert.NotNull(result);
            MetadataSection section = Assert.Single(result.Sections);
            Assert.Equal("Archive", section.Header);

            Assert.Equal("Files", section.Entries[0].Label);
            Assert.Equal("2", section.Entries[0].Value);

            Assert.Equal("Total size", section.Entries[1].Label);
            Assert.Equal("Compressed", section.Entries[2].Label);
            Assert.Equal("Ratio", section.Entries[3].Label);
        }
        finally
        {
            File.Delete(zipPath);
        }
    }

    [Fact]
    public void GetMetadata_EmptyZip_ReturnsZeroFiles()
    {
        string zipPath = CreateTempZip();
        try
        {
            MetadataResult? result = _provider.GetMetadata(zipPath, MakeContext(), CancellationToken.None);

            Assert.NotNull(result);
            MetadataSection section = Assert.Single(result.Sections);
            Assert.Equal("0", section.Entries[0].Value);
        }
        finally
        {
            File.Delete(zipPath);
        }
    }

    [Fact]
    public void GetMetadata_SkipsDirectoryEntries()
    {
        string zipPath = CreateTempZipWithDirs(("dir/", null), ("dir/file.txt", "data"));
        try
        {
            MetadataResult? result = _provider.GetMetadata(zipPath, MakeContext(), CancellationToken.None);

            Assert.NotNull(result);
            MetadataSection section = Assert.Single(result.Sections);
            Assert.Equal("1", section.Entries[0].Value);
        }
        finally
        {
            File.Delete(zipPath);
        }
    }

    [Fact]
    public void GetMetadata_CorruptFile_ReturnsNull()
    {
        string path = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName() + ".zip");
        File.WriteAllBytes(path, [0x00, 0x01, 0x02, 0x03]);
        try
        {
            MetadataResult? result = _provider.GetMetadata(path, MakeContext(), CancellationToken.None);

            Assert.Null(result);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void GetMetadata_Cancelled_ReturnsNull()
    {
        string zipPath = CreateTempZip(("test.txt", "data"));
        try
        {
            using var cts = new CancellationTokenSource();
            cts.Cancel();

            MetadataResult? result = _provider.GetMetadata(zipPath, MakeContext(), cts.Token);

            Assert.Null(result);
        }
        finally
        {
            File.Delete(zipPath);
        }
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static string CreateTempZip(params (string Name, string Content)[] entries)
    {
        string path = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName() + ".zip");
        using FileStream stream = File.Create(path);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Create);

        foreach ((string name, string content) in entries)
        {
            ZipArchiveEntry entry = archive.CreateEntry(name);
            using var writer = new StreamWriter(entry.Open());
            writer.Write(content);
        }

        return path;
    }

    private static string CreateTempZipWithDirs(params (string Name, string? Content)[] entries)
    {
        string path = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName() + ".zip");
        using FileStream stream = File.Create(path);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Create);

        foreach ((string name, string? content) in entries)
        {
            ZipArchiveEntry entry = archive.CreateEntry(name);
            if (content is not null)
            {
                using var writer = new StreamWriter(entry.Open());
                writer.Write(content);
            }
        }

        return path;
    }
}
