using System.Formats.Tar;
using System.IO.Compression;
using System.Text;
using Wade.FileSystem;
using Wade.Highlighting;

namespace Wade.Tests;

public class TarPreviewTests
{
    [Theory]
    [InlineData("archive.tar")]
    [InlineData("archive.tar.gz")]
    [InlineData("archive.TAR.GZ")]
    [InlineData("archive.tgz")]
    [InlineData("archive.TGZ")]
    public void IsTarArchive_TarExtensions_ReturnsTrue(string path) => Assert.True(TarPreview.IsTarArchive(path));

    [Theory]
    [InlineData("file.gz")]
    [InlineData("file.zip")]
    [InlineData("file.txt")]
    [InlineData("")]
    public void IsTarArchive_NonTarExtensions_ReturnsFalse(string path) => Assert.False(TarPreview.IsTarArchive(path));

    [Theory]
    [InlineData("foo.log.gz")]
    [InlineData("foo.GZ")]
    public void IsPlainGzip_PlainGzipExtensions_ReturnsTrue(string path) => Assert.True(TarPreview.IsPlainGzip(path));

    [Theory]
    [InlineData("archive.tar.gz")]
    [InlineData("archive.TAR.GZ")]
    [InlineData("file.tar")]
    [InlineData("file.tgz")]
    [InlineData("file.txt")]
    public void IsPlainGzip_NonPlainGzip_ReturnsFalse(string path) => Assert.False(TarPreview.IsPlainGzip(path));

    [Fact]
    public void GetPreviewLines_ValidTar_ReturnsHeaderAndEntries()
    {
        string path = CreateTempTar(("hello.txt", "Hello World"), ("dir/nested.txt", "Nested"));
        try
        {
            string[]? lines = TarPreview.GetPreviewLines(path, CancellationToken.None);

            Assert.NotNull(lines);
            Assert.Contains("Size", lines[0]);
            Assert.Contains("Name", lines[0]);
            Assert.DoesNotContain("Compressed", lines[0]);
            Assert.Contains(lines, l => l.Contains("hello.txt"));
            Assert.Contains(lines, l => l.Contains("dir/nested.txt"));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void GetPreviewLines_ValidTarGz_ReturnsEntries()
    {
        string path = CreateTempTarGz(("hello.txt", "Hello"), ("other.txt", "data"));
        try
        {
            string[]? lines = TarPreview.GetPreviewLines(path, CancellationToken.None);

            Assert.NotNull(lines);
            Assert.Contains(lines, l => l.Contains("hello.txt"));
            Assert.Contains(lines, l => l.Contains("other.txt"));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void GetPreviewLines_TgzExtension_TreatedAsTarGz()
    {
        string path = CreateTempTarGz(extension: ".tgz", ("a.txt", "a"));
        try
        {
            string[]? lines = TarPreview.GetPreviewLines(path, CancellationToken.None);

            Assert.NotNull(lines);
            Assert.Contains(lines, l => l.Contains("a.txt"));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void GetPreviewLines_EmptyTar_ReturnsEmptyMessage()
    {
        string path = CreateTempTar();
        try
        {
            string[]? lines = TarPreview.GetPreviewLines(path, CancellationToken.None);

            Assert.NotNull(lines);
            Assert.Single(lines);
            Assert.Equal("[empty archive]", lines[0]);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void GetPreviewLines_SkipsDirectoryEntries()
    {
        string path = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName() + ".tar");
        using (FileStream fs = File.Create(path))
        using (TarWriter writer = new(fs, TarEntryFormat.Pax))
        {
            writer.WriteEntry(new PaxTarEntry(TarEntryType.Directory, "dir/"));
            PaxTarEntry file = new(TarEntryType.RegularFile, "dir/file.txt");
            byte[] data = "x"u8.ToArray();
            file.DataStream = new MemoryStream(data);
            writer.WriteEntry(file);
        }

        try
        {
            string[]? lines = TarPreview.GetPreviewLines(path, CancellationToken.None);

            Assert.NotNull(lines);
            Assert.DoesNotContain(lines, l => l.TrimEnd().EndsWith("  dir/"));
            Assert.Contains(lines, l => l.Contains("dir/file.txt"));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void GetPreviewLines_ExceedsCap_ShowsMoreFooter()
    {
        var entries = new (string Name, string Content)[105];
        for (int i = 0; i < entries.Length; i++)
        {
            entries[i] = ($"file{i:D3}.txt", "x");
        }

        string path = CreateTempTar(entries);
        try
        {
            string[]? lines = TarPreview.GetPreviewLines(path, CancellationToken.None);

            Assert.NotNull(lines);
            Assert.Contains(lines, l => l.Contains("... and 5 more entries"));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void GetPreviewLines_TruncatedTar_ReturnsInvalidMessage()
    {
        string path = CreateTempTar(("hello.txt", "Hello World"));
        try
        {
            // Truncate to a partial record, which triggers EndOfStreamException in TarReader.
            using (FileStream fs = new(path, FileMode.Open, FileAccess.ReadWrite))
            {
                fs.SetLength(200);
            }

            string[]? lines = TarPreview.GetPreviewLines(path, CancellationToken.None);

            Assert.NotNull(lines);
            Assert.Single(lines);
            Assert.Equal("[invalid archive]", lines[0]);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void GetPreviewLines_CorruptGzip_ReturnsInvalidMessage()
    {
        string path = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName() + ".gz");
        File.WriteAllBytes(path, [0x00, 0x01, 0x02, 0x03]);
        try
        {
            string[]? lines = TarPreview.GetPreviewLines(path, CancellationToken.None);

            Assert.NotNull(lines);
            // A plain .gz that can't decompress surfaces through the "invalid gzip" path or "invalid archive".
            Assert.Contains(lines, l => l.Contains("invalid", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void GetPreviewLines_SingleMemberGzipOfText_IncludesTextHead()
    {
        string path = CreateTempGzipOfText("line one\nline two\nline three\n");
        try
        {
            string[]? lines = TarPreview.GetPreviewLines(path, CancellationToken.None);

            Assert.NotNull(lines);
            Assert.Contains(lines, l => l.StartsWith("[gzip]"));
            Assert.Contains(lines, l => l == "line one");
            Assert.Contains(lines, l => l == "line two");
            Assert.Contains(lines, l => l == "line three");
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void GetPreviewLines_SingleMemberGzipOfBinary_ShowsBinaryMarker()
    {
        byte[] binary = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x00, 0x00, 0x00, 0x0D];
        string path = CreateTempGzipOfBytes(binary);
        try
        {
            string[]? lines = TarPreview.GetPreviewLines(path, CancellationToken.None);

            Assert.NotNull(lines);
            Assert.Contains(lines, l => l.StartsWith("[gzip]"));
            Assert.Contains(lines, l => l == "[binary content]");
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void GetPreviewLines_GzWrappingTar_DetectedAsTar()
    {
        // Create a tar archive in memory, then gzip it and save with a .gz (not .tar.gz) extension.
        byte[] tarBytes;
        using (MemoryStream ms = new())
        {
            using (TarWriter writer = new(ms, TarEntryFormat.Pax, leaveOpen: true))
            {
                PaxTarEntry entry = new(TarEntryType.RegularFile, "inside-tar.txt")
                {
                    DataStream = new MemoryStream("payload"u8.ToArray()),
                };
                writer.WriteEntry(entry);
            }
            tarBytes = ms.ToArray();
        }

        string path = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName() + ".gz");
        using (FileStream fs = File.Create(path))
        using (GZipStream gz = new(fs, CompressionLevel.Fastest))
        {
            gz.Write(tarBytes);
        }

        try
        {
            string[]? lines = TarPreview.GetPreviewLines(path, CancellationToken.None);

            Assert.NotNull(lines);
            Assert.Contains(lines, l => l.Contains("inside-tar.txt"));
            Assert.DoesNotContain(lines, l => l.StartsWith("[gzip]"));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void GetPreviewLines_Cancelled_ReturnsNull()
    {
        string path = CreateTempTar(("a.txt", "a"));
        try
        {
            using var cts = new CancellationTokenSource();
            cts.Cancel();

            string[]? lines = TarPreview.GetPreviewLines(path, cts.Token);

            Assert.Null(lines);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void GetStats_PlainTar_ReturnsFilesAndTotalSize()
    {
        string path = CreateTempTar(("a.txt", "abc"), ("b.txt", "12345"));
        try
        {
            TarArchiveStats? stats = TarPreview.GetStats(path, CancellationToken.None);

            Assert.NotNull(stats);
            Assert.Equal(2, stats.Value.Files);
            Assert.Equal(8, stats.Value.TotalSize);
            Assert.Equal(TarFormat.Tar, stats.Value.Format);
            Assert.Null(stats.Value.CompressedSize);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void GetStats_TarGz_ReturnsCompressedSize()
    {
        string path = CreateTempTarGz(("a.txt", "hello"));
        try
        {
            TarArchiveStats? stats = TarPreview.GetStats(path, CancellationToken.None);

            Assert.NotNull(stats);
            Assert.Equal(1, stats.Value.Files);
            Assert.Equal(TarFormat.TarGzip, stats.Value.Format);
            Assert.NotNull(stats.Value.CompressedSize);
            Assert.True(stats.Value.CompressedSize > 0);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void GetStats_PlainGzip_ReturnsFormatGzip()
    {
        string path = CreateTempGzipOfText("hello world\n");
        try
        {
            TarArchiveStats? stats = TarPreview.GetStats(path, CancellationToken.None);

            Assert.NotNull(stats);
            Assert.Equal(1, stats.Value.Files);
            Assert.Equal(TarFormat.Gzip, stats.Value.Format);
            Assert.NotNull(stats.Value.CompressedSize);
            Assert.NotNull(stats.Value.UncompressedHint);
        }
        finally
        {
            File.Delete(path);
        }
    }

    // ── GetGzipStyledPreview / syntax highlighting ───────────────────────────

    [Fact]
    public void GetGzipStyledPreview_PythonSource_HighlightsContent()
    {
        string path = CreateTempGzipWithInnerName("script.py", "def foo():\n    return 42\nclass Bar:\n    pass\n");
        try
        {
            StyledLine[]? lines = TarPreview.GetGzipStyledPreview(path, CancellationToken.None);

            Assert.NotNull(lines);

            // Metadata header and separator are plain.
            Assert.StartsWith("[gzip]", lines[0].Text);
            Assert.Null(lines[0].Spans);
            Assert.Null(lines[1].Spans);

            // At least one content line should have been tokenized by PythonLanguage
            // (def / class / return are keywords).
            Assert.Contains(lines, l => l.Spans is { Length: > 0 });

            // Text is preserved.
            Assert.Contains(lines, l => l.Text == "def foo():");
            Assert.Contains(lines, l => l.Text == "class Bar:");
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void GetGzipStyledPreview_UnknownInnerExtension_ReturnsPlainContent()
    {
        string path = CreateTempGzipWithInnerName("access.log", "2026-04-11 INFO hello\n2026-04-11 WARN uh oh\n");
        try
        {
            StyledLine[]? lines = TarPreview.GetGzipStyledPreview(path, CancellationToken.None);

            Assert.NotNull(lines);
            Assert.StartsWith("[gzip]", lines[0].Text);

            // All content lines should be plain (no language for .log).
            Assert.Contains(lines, l => l.Text == "2026-04-11 INFO hello");
            Assert.All(lines, l => Assert.Null(l.Spans));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void GetGzipStyledPreview_BinaryContent_ReturnsBinaryMarker()
    {
        byte[] binary = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x00, 0x00, 0x00, 0x0D];
        string path = CreateTempGzipOfBytes(binary);
        try
        {
            StyledLine[]? lines = TarPreview.GetGzipStyledPreview(path, CancellationToken.None);

            Assert.NotNull(lines);
            Assert.Contains(lines, l => l.Text == "[binary content]");
            Assert.All(lines, l => Assert.Null(l.Spans));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void GetGzipStyledPreview_GzWrappingTar_ReturnsPlainTarListing()
    {
        byte[] tarBytes;
        using (MemoryStream ms = new())
        {
            using (TarWriter writer = new(ms, TarEntryFormat.Pax, leaveOpen: true))
            {
                PaxTarEntry entry = new(TarEntryType.RegularFile, "inside-tar.txt")
                {
                    DataStream = new MemoryStream("payload"u8.ToArray()),
                };
                writer.WriteEntry(entry);
            }
            tarBytes = ms.ToArray();
        }

        string path = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName() + ".gz");
        using (FileStream fs = File.Create(path))
        using (GZipStream gz = new(fs, CompressionLevel.Fastest))
        {
            gz.Write(tarBytes);
        }

        try
        {
            StyledLine[]? lines = TarPreview.GetGzipStyledPreview(path, CancellationToken.None);

            Assert.NotNull(lines);
            Assert.Contains(lines, l => l.Text.Contains("inside-tar.txt"));
            Assert.DoesNotContain(lines, l => l.Text.StartsWith("[gzip]"));
            // Tar file listings carry no source highlighting.
            Assert.All(lines, l => Assert.Null(l.Spans));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void GetGzipStyledPreview_CorruptGzip_ReturnsInvalidMarker()
    {
        string path = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName() + ".gz");
        File.WriteAllBytes(path, [0x00, 0x01, 0x02, 0x03]);
        try
        {
            StyledLine[]? lines = TarPreview.GetGzipStyledPreview(path, CancellationToken.None);

            Assert.NotNull(lines);
            Assert.Contains(lines, l => l.Text.Contains("invalid", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void GetGzipStyledPreview_Cancelled_ReturnsNull()
    {
        string path = CreateTempGzipOfText("hello\n");
        try
        {
            using var cts = new CancellationTokenSource();
            cts.Cancel();

            StyledLine[]? lines = TarPreview.GetGzipStyledPreview(path, cts.Token);

            Assert.Null(lines);
        }
        finally
        {
            File.Delete(path);
        }
    }

    // ── Fixture helpers ───────────────────────────────────────────────────────

    private static string CreateTempTar(params (string Name, string Content)[] entries)
    {
        string path = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName() + ".tar");
        using FileStream fs = File.Create(path);
        using TarWriter writer = new(fs, TarEntryFormat.Pax);

        foreach ((string name, string content) in entries)
        {
            PaxTarEntry entry = new(TarEntryType.RegularFile, name)
            {
                DataStream = new MemoryStream(Encoding.UTF8.GetBytes(content)),
            };
            writer.WriteEntry(entry);
        }

        return path;
    }

    private static string CreateTempTarGz(params (string Name, string Content)[] entries)
        => CreateTempTarGz(".tar.gz", entries);

    private static string CreateTempTarGz(string extension, params (string Name, string Content)[] entries)
    {
        string path = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName() + extension);
        using FileStream fs = File.Create(path);
        using GZipStream gz = new(fs, CompressionLevel.Fastest);
        using TarWriter writer = new(gz, TarEntryFormat.Pax);

        foreach ((string name, string content) in entries)
        {
            PaxTarEntry entry = new(TarEntryType.RegularFile, name)
            {
                DataStream = new MemoryStream(Encoding.UTF8.GetBytes(content)),
            };
            writer.WriteEntry(entry);
        }

        return path;
    }

    private static string CreateTempGzipOfText(string text)
        => CreateTempGzipOfBytes(Encoding.UTF8.GetBytes(text));

    private static string CreateTempGzipOfBytes(byte[] data)
    {
        string path = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName() + ".gz");
        using FileStream fs = File.Create(path);
        using GZipStream gz = new(fs, CompressionLevel.Fastest);
        gz.Write(data);
        return path;
    }

    // Creates a temp file named "<random>.<innerName>.gz" so that
    // Path.GetFileNameWithoutExtension yields "<random>.<innerName>" and
    // LanguageMap picks the language by innerName's extension.
    private static string CreateTempGzipWithInnerName(string innerName, string text)
    {
        string path = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName() + "." + innerName + ".gz");
        using FileStream fs = File.Create(path);
        using GZipStream gz = new(fs, CompressionLevel.Fastest);
        gz.Write(Encoding.UTF8.GetBytes(text));
        return path;
    }
}
