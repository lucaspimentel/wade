using System.IO.Compression;
using Wade.FileSystem;

namespace Wade.Tests;

public class ZipPreviewTests
{
    [Fact]
    public void IsZipFile_ZipExtension_ReturnsTrue()
    {
        Assert.True(ZipPreview.IsZipFile("archive.zip"));
    }

    [Theory]
    [InlineData(".nupkg")]
    [InlineData(".snupkg")]
    [InlineData(".jar")]
    [InlineData(".war")]
    [InlineData(".ear")]
    [InlineData(".docx")]
    [InlineData(".xlsx")]
    [InlineData(".pptx")]
    [InlineData(".odt")]
    [InlineData(".ods")]
    [InlineData(".odp")]
    [InlineData(".apk")]
    [InlineData(".vsix")]
    [InlineData(".whl")]
    [InlineData(".epub")]
    public void IsZipFile_ZipFormatExtensions_ReturnsTrue(string ext)
    {
        Assert.True(ZipPreview.IsZipFile($"file{ext}"));
    }

    [Theory]
    [InlineData(".tar")]
    [InlineData(".gz")]
    [InlineData(".txt")]
    public void IsZipFile_NonZipExtensions_ReturnsFalse(string ext)
    {
        Assert.False(ZipPreview.IsZipFile($"file{ext}"));
    }

    [Fact]
    public void GetPreviewLines_ValidZip_ReturnsHeaderAndEntries()
    {
        string zipPath = CreateTempZip(("hello.txt", "Hello World"), ("dir/nested.txt", "Nested"));
        try
        {
            string[]? lines = ZipPreview.GetPreviewLines(zipPath, CancellationToken.None);

            Assert.NotNull(lines);
            Assert.Contains("Size", lines[0]);
            Assert.Contains("Compressed", lines[0]);
            Assert.Contains("Ratio", lines[0]);
            Assert.Contains(lines, l => l.Contains("hello.txt"));
            Assert.Contains(lines, l => l.Contains("dir/nested.txt"));
        }
        finally
        {
            File.Delete(zipPath);
        }
    }

    [Fact]
    public void GetPreviewLines_SkipsDirectoryEntries()
    {
        string zipPath = CreateTempZipWithDirs(("dir/", null), ("dir/file.txt", "data"));
        try
        {
            string[]? lines = ZipPreview.GetPreviewLines(zipPath, CancellationToken.None);

            Assert.NotNull(lines);
            Assert.DoesNotContain(lines, l => l.TrimEnd() == "dir/" || l.TrimEnd().EndsWith("  dir/"));
            Assert.Contains(lines, l => l.Contains("dir/file.txt"));
        }
        finally
        {
            File.Delete(zipPath);
        }
    }

    [Fact]
    public void GetPreviewLines_EmptyZip_ReturnsEmptyMessage()
    {
        string zipPath = CreateTempZip();
        try
        {
            string[]? lines = ZipPreview.GetPreviewLines(zipPath, CancellationToken.None);

            Assert.NotNull(lines);
            Assert.Single(lines);
            Assert.Equal("[empty archive]", lines[0]);
        }
        finally
        {
            File.Delete(zipPath);
        }
    }

    [Fact]
    public void GetPreviewLines_CorruptFile_ReturnsInvalidMessage()
    {
        string path = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName() + ".zip");
        File.WriteAllBytes(path, [0x00, 0x01, 0x02, 0x03]);
        try
        {
            string[]? lines = ZipPreview.GetPreviewLines(path, CancellationToken.None);

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
    public void GetPreviewLines_Cancelled_ReturnsNull()
    {
        string zipPath = CreateTempZip(("test.txt", "data"));
        try
        {
            using var cts = new CancellationTokenSource();
            cts.Cancel();

            string[]? lines = ZipPreview.GetPreviewLines(zipPath, cts.Token);

            Assert.Null(lines);
        }
        finally
        {
            File.Delete(zipPath);
        }
    }

    private static string CreateTempZip(params (string Name, string Content)[] entries)
    {
        string path = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName() + ".zip");
        using var stream = File.Create(path);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Create);

        foreach (var (name, content) in entries)
        {
            var entry = archive.CreateEntry(name);
            using var writer = new StreamWriter(entry.Open());
            writer.Write(content);
        }

        return path;
    }

    private static string CreateTempZipWithDirs(params (string Name, string? Content)[] entries)
    {
        string path = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName() + ".zip");
        using var stream = File.Create(path);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Create);

        foreach (var (name, content) in entries)
        {
            var entry = archive.CreateEntry(name);
            if (content is not null)
            {
                using var writer = new StreamWriter(entry.Open());
                writer.Write(content);
            }
        }

        return path;
    }
}
