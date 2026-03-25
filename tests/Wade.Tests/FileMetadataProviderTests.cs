using Wade.FileSystem;
using Wade.Preview;

namespace Wade.Tests;

public class FileMetadataProviderTests
{
    private static PreviewContext MakeContext(GitFileStatus? gitStatus = null) =>
        new(
            PaneWidthCells: 60,
            PaneHeightCells: 30,
            CellPixelWidth: 8,
            CellPixelHeight: 16,
            IsCloudPlaceholder: false,
            IsBrokenSymlink: false,
            GitStatus: gitStatus,
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
    [InlineData("some/file.txt")]
    [InlineData("C:\\some\\path.exe")]
    [InlineData("C:\\some\\directory")]
    public void CanProvideMetadata_AnyPath_ReturnsTrue(string path)
    {
        var provider = new FileMetadataProvider();
        Assert.True(provider.CanProvideMetadata(path, MakeContext()));
    }

    [Fact]
    public void GetMetadata_Directory_ReturnsNameWithoutSizeOrModified()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(tempDir);
        try
        {
            var provider = new FileMetadataProvider();
            MetadataResult? result = provider.GetMetadata(tempDir, MakeContext(), CancellationToken.None);

            Assert.NotNull(result);
            MetadataSection section = Assert.Single(result.Sections);
            Assert.Equal(Path.GetFileName(tempDir), section.Header);
            Assert.DoesNotContain(section.Entries, e => e.Label == "Size");
            Assert.DoesNotContain(section.Entries, e => e.Label == "Modified");
        }
        finally
        {
            Directory.Delete(tempDir);
        }
    }

    [Fact]
    public void GetMetadata_ReturnsFilenameAsSectionHeader()
    {
        string tempPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}.txt");
        try
        {
            File.WriteAllText(tempPath, "hello");

            var provider = new FileMetadataProvider();
            MetadataResult? result = provider.GetMetadata(tempPath, MakeContext(), CancellationToken.None);

            Assert.NotNull(result);
            MetadataSection section = Assert.Single(result.Sections);
            Assert.Equal(Path.GetFileName(tempPath), section.Header);
        }
        finally
        {
            File.Delete(tempPath);
        }
    }

    [Fact]
    public void GetMetadata_File_DoesNotIncludeSizeOrModified()
    {
        string tempPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}.txt");
        try
        {
            File.WriteAllText(tempPath, "hello world");

            var provider = new FileMetadataProvider();
            MetadataResult? result = provider.GetMetadata(tempPath, MakeContext(), CancellationToken.None);

            Assert.NotNull(result);
            string flat = FlattenSections(result.Sections);
            Assert.DoesNotContain("Size", flat);
            Assert.DoesNotContain("Modified", flat);
        }
        finally
        {
            File.Delete(tempPath);
        }
    }

    [Fact]
    public void GetMetadata_WithGitStatus_IncludesGitEntry()
    {
        string tempPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}.txt");
        try
        {
            File.WriteAllText(tempPath, "hello");

            var provider = new FileMetadataProvider();
            MetadataResult? result = provider.GetMetadata(tempPath, MakeContext(gitStatus: GitFileStatus.Modified), CancellationToken.None);

            Assert.NotNull(result);
            string flat = FlattenSections(result.Sections);
            Assert.Contains("Git", flat);
        }
        finally
        {
            File.Delete(tempPath);
        }
    }

    [Fact]
    public void GetMetadata_CloudPlaceholder_IncludesCloudEntry()
    {
        string tempPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}.txt");
        try
        {
            File.WriteAllText(tempPath, "hello");

            var context = new PreviewContext(
                PaneWidthCells: 60,
                PaneHeightCells: 30,
                CellPixelWidth: 8,
                CellPixelHeight: 16,
                IsCloudPlaceholder: true,
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

            var provider = new FileMetadataProvider();
            MetadataResult? result = provider.GetMetadata(tempPath, context, CancellationToken.None);

            Assert.NotNull(result);
            string flat = FlattenSections(result.Sections);
            Assert.Contains("Cloud", flat);
        }
        finally
        {
            File.Delete(tempPath);
        }
    }

    [Fact]
    public void GetMetadata_NoGitStatus_OmitsGitEntry()
    {
        string tempPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}.txt");
        try
        {
            File.WriteAllText(tempPath, "hello");

            var provider = new FileMetadataProvider();
            MetadataResult? result = provider.GetMetadata(tempPath, MakeContext(gitStatus: null), CancellationToken.None);

            Assert.NotNull(result);
            string flat = FlattenSections(result.Sections);
            Assert.DoesNotContain("Git", flat);
        }
        finally
        {
            File.Delete(tempPath);
        }
    }

    [Fact]
    public void GetMetadata_FileTypeLabel_IsNull()
    {
        string tempPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}.txt");
        try
        {
            File.WriteAllText(tempPath, "hello");

            var provider = new FileMetadataProvider();
            MetadataResult? result = provider.GetMetadata(tempPath, MakeContext(), CancellationToken.None);

            Assert.NotNull(result);
            Assert.Null(result.FileTypeLabel);
        }
        finally
        {
            File.Delete(tempPath);
        }
    }

    [Fact]
    public void GetMetadata_CancelledToken_ReturnsNull()
    {
        string tempPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}.txt");
        try
        {
            File.WriteAllText(tempPath, "hello");

            var provider = new FileMetadataProvider();
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
    public void Registry_PlainTextFile_IncludesFileMetadataProvider()
    {
        string tempPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}.txt");
        try
        {
            File.WriteAllText(tempPath, "hello");
            List<IMetadataProvider> providers = MetadataProviderRegistry.GetApplicableProviders(tempPath, MakeContext());

            Assert.NotEmpty(providers);
            Assert.IsType<FileMetadataProvider>(providers[0]);
        }
        finally
        {
            File.Delete(tempPath);
        }
    }

    private static string FlattenSections(MetadataSection[] sections)
    {
        var parts = new List<string>();
        foreach (MetadataSection s in sections)
        {
            if (s.Header is not null)
            {
                parts.Add(s.Header);
            }

            foreach (MetadataEntry e in s.Entries)
            {
                parts.Add($"{e.Label} {e.Value}");
            }
        }

        return string.Join('\n', parts);
    }
}
