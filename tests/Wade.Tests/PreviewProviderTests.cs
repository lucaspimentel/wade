using System.IO.Compression;
using Wade.FileSystem;
using Wade.Imaging;
using Wade.Preview;

namespace Wade.Tests;

public class PreviewProviderTests
{
    private static PreviewContext DefaultContext(
        bool isCloudPlaceholder = false,
        bool isBrokenSymlink = false,
        GitFileStatus? gitStatus = null,
        string? repoRoot = null,
        bool pdfPreviewEnabled = true,
        bool markdownPreviewEnabled = true,
        bool glowPreviewEnabled = true,
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
            PdfPreviewEnabled: pdfPreviewEnabled,
            PdfMetadataEnabled: true,
            MarkdownPreviewEnabled: markdownPreviewEnabled,
            GlowPreviewEnabled: glowPreviewEnabled,
            FfprobeEnabled: true,
            MediainfoEnabled: true,
            ZipPreviewEnabled: zipPreviewEnabled,
            ImagePreviewsEnabled: imagePreviewsEnabled,
            SixelSupported: sixelSupported,
            ArchiveMetadataEnabled: true);

    // --- Helpers ---

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

    // --- ImagePreviewProvider ---

    public class ImagePreviewProviderTests
    {
        [Theory]
        [InlineData(".png")]
        [InlineData(".jpg")]
        [InlineData(".gif")]
        [InlineData(".bmp")]
        [InlineData(".webp")]
        public void CanPreview_ImageExtensions_ReturnsTrue(string ext)
        {
            var provider = new ImagePreviewProvider();
            Assert.True(provider.CanPreview($"file{ext}", DefaultContext()));
        }

        [Theory]
        [InlineData(".txt")]
        [InlineData(".cs")]
        [InlineData(".zip")]
        public void CanPreview_NonImageExtensions_ReturnsFalse(string ext)
        {
            var provider = new ImagePreviewProvider();
            Assert.False(provider.CanPreview($"file{ext}", DefaultContext()));
        }

        [Fact]
        public void CanPreview_ImagePreviewsDisabled_ReturnsFalse()
        {
            var provider = new ImagePreviewProvider();
            Assert.False(provider.CanPreview("file.png", DefaultContext(imagePreviewsEnabled: false)));
        }

        [Fact]
        public void Label_IsImage() => Assert.Equal("Image", new ImagePreviewProvider().Label);
    }

    // --- PdfPreviewProvider ---

    public class PdfPreviewProviderTests
    {
        [Fact]
        public void CanPreview_PdfDisabled_ReturnsFalse()
        {
            var provider = new PdfPreviewProvider();
            Assert.False(provider.CanPreview("file.pdf", DefaultContext(pdfPreviewEnabled: false)));
        }

        [Fact]
        public void CanPreview_ImagePreviewsDisabled_StillReturnsTrue()
        {
            // Requires pdftopng on PATH — skip if not available
            if (!ImageConverter.CanConvert("file.pdf"))
            {
                return;
            }

            var provider = new PdfPreviewProvider();
            // PDF preview is independent of image previews — only requires Sixel support
            Assert.True(provider.CanPreview("file.pdf", DefaultContext(imagePreviewsEnabled: false)));
        }

        [Fact]
        public void CanPreview_SixelNotSupported_ReturnsFalse()
        {
            // Requires pdftopng on PATH — skip if not available
            if (!ImageConverter.CanConvert("file.pdf"))
            {
                return;
            }

            var provider = new PdfPreviewProvider();
            Assert.False(provider.CanPreview("file.pdf", DefaultContext(sixelSupported: false)));
        }

        [Theory]
        [InlineData(".txt")]
        [InlineData(".cs")]
        [InlineData(".png")]
        public void CanPreview_NonPdfExtensions_ReturnsFalse(string ext)
        {
            var provider = new PdfPreviewProvider();
            Assert.False(provider.CanPreview($"file{ext}", DefaultContext()));
        }

        [Fact]
        public void Label_IsPdf() => Assert.Equal("PDF", new PdfPreviewProvider().Label);
    }

    // --- GlowMarkdownPreviewProvider ---

    public class GlowMarkdownPreviewProviderTests
    {
        [Theory]
        [InlineData(".txt")]
        [InlineData(".cs")]
        [InlineData(".html")]
        public void CanPreview_NonMarkdownExtensions_ReturnsFalse(string ext)
        {
            var provider = new GlowMarkdownPreviewProvider();
            Assert.False(provider.CanPreview($"file{ext}", DefaultContext()));
        }

        [Fact]
        public void CanPreview_GlowDisabled_ReturnsFalse()
        {
            var provider = new GlowMarkdownPreviewProvider();
            Assert.False(provider.CanPreview("file.md", DefaultContext(glowPreviewEnabled: false)));
        }

        [Fact]
        public void Label_IsRenderedMarkdown() => Assert.Equal("Rendered markdown (glow)", new GlowMarkdownPreviewProvider().Label);
    }

    // --- MarkdigMarkdownPreviewProvider ---

    public class MarkdigMarkdownPreviewProviderTests
    {
        [Theory]
        [InlineData(".md")]
        [InlineData(".markdown")]
        [InlineData(".MD")]
        public void CanPreview_MarkdownExtensions_ReturnsTrue(string ext)
        {
            var provider = new MarkdigMarkdownPreviewProvider();
            Assert.True(provider.CanPreview($"file{ext}", DefaultContext()));
        }

        [Theory]
        [InlineData(".txt")]
        [InlineData(".cs")]
        [InlineData(".html")]
        public void CanPreview_NonMarkdownExtensions_ReturnsFalse(string ext)
        {
            var provider = new MarkdigMarkdownPreviewProvider();
            Assert.False(provider.CanPreview($"file{ext}", DefaultContext()));
        }

        [Fact]
        public void CanPreview_MarkdownPreviewDisabled_ReturnsFalse()
        {
            var provider = new MarkdigMarkdownPreviewProvider();
            Assert.False(provider.CanPreview("file.md", DefaultContext(markdownPreviewEnabled: false)));
        }

        [Fact]
        public void Label_IsRenderedMarkdown() => Assert.Equal("Rendered markdown (built-in)", new MarkdigMarkdownPreviewProvider().Label);
    }

    // --- ZipContentsPreviewProvider ---

    public class ZipContentsPreviewProviderTests
    {
        [Theory]
        [InlineData(".zip")]
        [InlineData(".nupkg")]
        [InlineData(".jar")]
        [InlineData(".docx")]
        public void CanPreview_ZipExtensions_ReturnsTrue(string ext)
        {
            var provider = new ZipContentsPreviewProvider();
            Assert.True(provider.CanPreview($"file{ext}", DefaultContext()));
        }

        [Theory]
        [InlineData(".txt")]
        [InlineData(".cs")]
        [InlineData(".png")]
        public void CanPreview_NonZipExtensions_ReturnsFalse(string ext)
        {
            var provider = new ZipContentsPreviewProvider();
            Assert.False(provider.CanPreview($"file{ext}", DefaultContext()));
        }

        [Fact]
        public void CanPreview_ZipPreviewDisabled_ReturnsFalse()
        {
            var provider = new ZipContentsPreviewProvider();
            Assert.False(provider.CanPreview("file.zip", DefaultContext(zipPreviewEnabled: false)));
        }

        [Fact]
        public void GetPreview_ValidZip_ReturnsRenderedTextLines()
        {
            string zipPath = CreateTempZip(("hello.txt", "Hello"));
            try
            {
                var provider = new ZipContentsPreviewProvider();
                PreviewResult? result = provider.GetPreview(zipPath, DefaultContext(), CancellationToken.None);

                Assert.NotNull(result);
                Assert.NotNull(result.TextLines);
                Assert.True(result.IsRendered);
                Assert.True(result.TextLines.Length > 0);
                Assert.Contains(result.TextLines, l => l.Text.Contains("hello.txt"));
            }
            finally
            {
                File.Delete(zipPath);
            }
        }

        [Fact]
        public void GetPreview_CorruptZip_ReturnsInvalidMessage()
        {
            string path = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName() + ".zip");
            File.WriteAllBytes(path, [0x00, 0x01, 0x02, 0x03]);
            try
            {
                var provider = new ZipContentsPreviewProvider();
                PreviewResult? result = provider.GetPreview(path, DefaultContext(), CancellationToken.None);

                Assert.NotNull(result);
                Assert.NotNull(result.TextLines);
                Assert.True(result.IsRendered);
                Assert.Contains(result.TextLines, l => l.Text == "[invalid archive]");
            }
            finally
            {
                File.Delete(path);
            }
        }

        [Fact]
        public void GetPreview_ValidZip_StartsWithArchiveContentsHeader()
        {
            string zipPath = CreateTempZip(("hello.txt", "Hello"));
            try
            {
                var provider = new ZipContentsPreviewProvider();
                PreviewResult? result = provider.GetPreview(zipPath, DefaultContext(), CancellationToken.None);

                Assert.NotNull(result);
                Assert.NotNull(result.TextLines);
                Assert.True(result.TextLines.Length >= 2);
                Assert.Contains("Archive Contents", result.TextLines[0].Text);
            }
            finally
            {
                File.Delete(zipPath);
            }
        }

        [Fact]
        public void Label_IsArchiveContents() => Assert.Equal("Archive contents", new ZipContentsPreviewProvider().Label);
    }

    // --- TextPreviewProvider ---

    public class TextPreviewProviderTests
    {
        [Fact]
        public void CanPreview_TextFile_ReturnsTrue()
        {
            string path = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName() + ".txt");
            File.WriteAllText(path, "Hello World");
            try
            {
                var provider = new TextPreviewProvider();
                Assert.True(provider.CanPreview(path, DefaultContext()));
            }
            finally
            {
                File.Delete(path);
            }
        }

        [Fact]
        public void CanPreview_BinaryFile_ReturnsFalse()
        {
            string path = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName() + ".exe");
            File.WriteAllBytes(path, [0x4D, 0x5A, 0x00, 0x00]);
            try
            {
                var provider = new TextPreviewProvider();
                Assert.False(provider.CanPreview(path, DefaultContext()));
            }
            finally
            {
                File.Delete(path);
            }
        }

        [Fact]
        public void GetPreview_TextFile_ReturnsLinesWithMetadata()
        {
            string path = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName() + ".txt");
            File.WriteAllText(path, "Hello\nWorld\n");
            try
            {
                var provider = new TextPreviewProvider();
                PreviewResult? result = provider.GetPreview(path, DefaultContext(), CancellationToken.None);

                Assert.NotNull(result);
                Assert.NotNull(result.TextLines);
                Assert.False(result.IsRendered);
                Assert.False(result.IsPlaceholder);
                Assert.NotNull(result.Encoding);
                Assert.NotNull(result.FileTypeLabel);
                Assert.True(result.TextLines.Length >= 2);
            }
            finally
            {
                File.Delete(path);
            }
        }

        [Fact]
        public void GetPreview_BinaryFile_ReturnsBinaryMessage()
        {
            string path = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName() + ".exe");
            File.WriteAllBytes(path, [0x4D, 0x5A, 0x00, 0x00]); // MZ header
            try
            {
                var provider = new TextPreviewProvider();
                PreviewResult? result = provider.GetPreview(path, DefaultContext(), CancellationToken.None);

                Assert.NotNull(result);
                Assert.NotNull(result.TextLines);
                Assert.True(result.IsRendered);
                Assert.True(result.IsPlaceholder);
                Assert.Single(result.TextLines);
                Assert.Equal("[binary file]", result.TextLines[0].Text);
            }
            finally
            {
                File.Delete(path);
            }
        }

        [Fact]
        public void GetPreview_EmptyFile_IsPlaceholder()
        {
            string path = Path.GetTempFileName();
            try
            {
                var provider = new TextPreviewProvider();
                PreviewResult? result = provider.GetPreview(path, DefaultContext(), CancellationToken.None);

                Assert.NotNull(result);
                Assert.True(result.IsPlaceholder);
            }
            finally
            {
                File.Delete(path);
            }
        }

        [Fact]
        public void Label_IsText() => Assert.Equal("Text", new TextPreviewProvider().Label);
    }

    // --- HexPreviewProvider ---

    public class HexPreviewProviderTests
    {
        [Fact]
        public void CanPreview_BinaryFile_ReturnsTrue()
        {
            string path = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName() + ".bin");
            File.WriteAllBytes(path, [0x4D, 0x5A, 0x00, 0x00]);
            try
            {
                var provider = new HexPreviewProvider();
                Assert.True(provider.CanPreview(path, DefaultContext()));
            }
            finally
            {
                File.Delete(path);
            }
        }

        [Fact]
        public void CanPreview_TextFile_ReturnsTrue()
        {
            string path = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName() + ".txt");
            File.WriteAllText(path, "Hello World");
            try
            {
                var provider = new HexPreviewProvider();
                Assert.True(provider.CanPreview(path, DefaultContext()));
            }
            finally
            {
                File.Delete(path);
            }
        }

        [Fact]
        public void GetPreview_File_ReturnsRenderedHexLines()
        {
            string path = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName() + ".bin");
            File.WriteAllBytes(path, "Hello World"u8.ToArray());
            try
            {
                var provider = new HexPreviewProvider();
                PreviewResult? result = provider.GetPreview(path, DefaultContext(), CancellationToken.None);

                Assert.NotNull(result);
                Assert.NotNull(result.TextLines);
                Assert.True(result.IsRendered);
                Assert.True(result.TextLines.Length > 0);
            }
            finally
            {
                File.Delete(path);
            }
        }

        [Fact]
        public void Label_IsHexDump() => Assert.Equal("Hex dump", new HexPreviewProvider().Label);
    }

    // --- NonePreviewProvider ---

    public class NonePreviewProviderTests
    {
        [Fact]
        public void CanPreview_AnyFile_ReturnsTrue()
        {
            var provider = new NonePreviewProvider();
            Assert.True(provider.CanPreview("file.cs", DefaultContext()));
            Assert.True(provider.CanPreview("file.exe", DefaultContext()));
            Assert.True(provider.CanPreview("file.png", DefaultContext()));
        }

        [Fact]
        public void GetPreview_ReturnsPlaceholder()
        {
            var provider = new NonePreviewProvider();
            PreviewResult? result = provider.GetPreview("file.cs", DefaultContext(), CancellationToken.None);

            Assert.NotNull(result);
            Assert.True(result.IsPlaceholder);
        }

        [Fact]
        public void Label_IsNone() => Assert.Equal("None", new NonePreviewProvider().Label);
    }

    // --- DiffPreviewProvider ---

    public class DiffPreviewProviderTests
    {
        [Fact]
        public void CanPreview_ModifiedFile_ReturnsTrue()
        {
            var provider = new DiffPreviewProvider();
            PreviewContext context = DefaultContext(gitStatus: GitFileStatus.Modified, repoRoot: "/repo");
            Assert.True(provider.CanPreview("file.cs", context));
        }

        [Fact]
        public void CanPreview_StagedFile_ReturnsTrue()
        {
            var provider = new DiffPreviewProvider();
            PreviewContext context = DefaultContext(gitStatus: GitFileStatus.Staged, repoRoot: "/repo");
            Assert.True(provider.CanPreview("file.cs", context));
        }

        [Fact]
        public void CanPreview_UntrackedFile_ReturnsFalse()
        {
            var provider = new DiffPreviewProvider();
            PreviewContext context = DefaultContext(gitStatus: GitFileStatus.Untracked, repoRoot: "/repo");
            Assert.False(provider.CanPreview("file.cs", context));
        }

        [Fact]
        public void CanPreview_NoRepoRoot_ReturnsFalse()
        {
            var provider = new DiffPreviewProvider();
            PreviewContext context = DefaultContext(gitStatus: GitFileStatus.Modified, repoRoot: null);
            Assert.False(provider.CanPreview("file.cs", context));
        }

        [Fact]
        public void CanPreview_NoGitStatus_ReturnsFalse()
        {
            var provider = new DiffPreviewProvider();
            PreviewContext context = DefaultContext(gitStatus: null, repoRoot: "/repo");
            Assert.False(provider.CanPreview("file.cs", context));
        }

        [Fact]
        public void CanPreview_CleanFile_ReturnsFalse()
        {
            var provider = new DiffPreviewProvider();
            PreviewContext context = DefaultContext(gitStatus: GitFileStatus.None, repoRoot: "/repo");
            Assert.False(provider.CanPreview("file.cs", context));
        }

        [Fact]
        public void Label_IsGitDiff() => Assert.Equal("Git diff", new DiffPreviewProvider().Label);
    }
}
