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
    public void CanProvideMetadata_UnrelatedExtensions_ReturnsFalse(string path) => Assert.False(_provider.CanProvideMetadata(path, MakeContext()));

    [Theory]
    [InlineData("file.tar")]
    [InlineData("file.tar.gz")]
    [InlineData("file.tgz")]
    [InlineData("file.gz")]
    public void CanProvideMetadata_TarExtensions_ReturnsTrue(string path) => Assert.True(_provider.CanProvideMetadata(path, MakeContext()));

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

    // ── Tar / gzip metadata ───────────────────────────────────────────────────

    [Fact]
    public void GetMetadata_PlainTar_ReturnsTarFormat()
    {
        string path = CreateTempTar(("a.txt", "abc"), ("b.txt", "1234"));
        try
        {
            MetadataResult? result = _provider.GetMetadata(path, MakeContext(), CancellationToken.None);

            Assert.NotNull(result);
            MetadataSection section = Assert.Single(result.Sections);
            Assert.Equal("Archive", section.Header);

            Assert.Equal("Files", section.Entries[0].Label);
            Assert.Equal("2", section.Entries[0].Value);
            Assert.Equal("Total size", section.Entries[1].Label);
            Assert.Equal("Format", section.Entries[2].Label);
            Assert.Equal("tar", section.Entries[2].Value);

            // Plain tar has no compression metadata.
            Assert.DoesNotContain(section.Entries, e => e.Label == "Compressed");
            Assert.DoesNotContain(section.Entries, e => e.Label == "Ratio");
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void GetMetadata_TarGz_ReturnsCompressedAndRatio()
    {
        string path = CreateTempTarGz(("hello.txt", "hello world hello world"));
        try
        {
            MetadataResult? result = _provider.GetMetadata(path, MakeContext(), CancellationToken.None);

            Assert.NotNull(result);
            MetadataSection section = Assert.Single(result.Sections);

            Assert.Equal("Files", section.Entries[0].Label);
            Assert.Equal("1", section.Entries[0].Value);
            Assert.Equal("Format", section.Entries[2].Label);
            Assert.Equal("tar.gz", section.Entries[2].Value);
            Assert.Contains(section.Entries, e => e.Label == "Compressed");
            Assert.Contains(section.Entries, e => e.Label == "Ratio");
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void GetMetadata_PlainGzip_ReturnsFormatGzipAndFilesEquals1()
    {
        string path = CreateTempGzipOfText("hello world\n");
        try
        {
            MetadataResult? result = _provider.GetMetadata(path, MakeContext(), CancellationToken.None);

            Assert.NotNull(result);
            MetadataSection section = Assert.Single(result.Sections);

            Assert.Equal("Files", section.Entries[0].Label);
            Assert.Equal("1", section.Entries[0].Value);
            Assert.Equal("Format", section.Entries[2].Label);
            Assert.Equal("gzip", section.Entries[2].Value);
            Assert.Contains(section.Entries, e => e.Label == "Compressed");
        }
        finally
        {
            File.Delete(path);
        }
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static string CreateTempTar(params (string Name, string Content)[] entries)
    {
        string path = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName() + ".tar");
        using FileStream fs = File.Create(path);
        using System.Formats.Tar.TarWriter writer = new(fs, System.Formats.Tar.TarEntryFormat.Pax);

        foreach ((string name, string content) in entries)
        {
            System.Formats.Tar.PaxTarEntry entry = new(System.Formats.Tar.TarEntryType.RegularFile, name)
            {
                DataStream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(content)),
            };
            writer.WriteEntry(entry);
        }

        return path;
    }

    private static string CreateTempTarGz(params (string Name, string Content)[] entries)
    {
        string path = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName() + ".tar.gz");
        using FileStream fs = File.Create(path);
        using GZipStream gz = new(fs, CompressionLevel.Fastest);
        using System.Formats.Tar.TarWriter writer = new(gz, System.Formats.Tar.TarEntryFormat.Pax);

        foreach ((string name, string content) in entries)
        {
            System.Formats.Tar.PaxTarEntry entry = new(System.Formats.Tar.TarEntryType.RegularFile, name)
            {
                DataStream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(content)),
            };
            writer.WriteEntry(entry);
        }

        return path;
    }

    private static string CreateTempGzipOfText(string text)
    {
        string path = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName() + ".gz");
        using FileStream fs = File.Create(path);
        using GZipStream gz = new(fs, CompressionLevel.Fastest);
        gz.Write(System.Text.Encoding.UTF8.GetBytes(text));
        return path;
    }

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
