using Wade.Preview;

namespace Wade.Tests;

public class ExecutableMetadataProviderTests
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

    [Theory]
    [InlineData("app.exe")]
    [InlineData("app.EXE")]
    [InlineData("lib.dll")]
    [InlineData("lib.DLL")]
    public void CanProvideMetadata_ExecutableExtensions_ReturnsTrue(string path)
    {
        var provider = new ExecutableMetadataProvider();
        Assert.True(provider.CanProvideMetadata(path, MakeContext()));
    }

    [Theory]
    [InlineData("archive.zip")]
    [InlineData("readme.txt")]
    [InlineData("image.png")]
    [InlineData("package.nupkg")]
    public void CanProvideMetadata_NonExecutableExtensions_ReturnsFalse(string path)
    {
        var provider = new ExecutableMetadataProvider();
        Assert.False(provider.CanProvideMetadata(path, MakeContext()));
    }

    [Fact]
    public void GetMetadata_WithRealDotNetAssembly_ReturnsSections()
    {
        string assemblyPath = typeof(ExecutableMetadataProviderTests).Assembly.Location;

        var provider = new ExecutableMetadataProvider();
        MetadataResult? result = provider.GetMetadata(assemblyPath, MakeContext(), CancellationToken.None);

        Assert.NotNull(result);
        Assert.NotEmpty(result.Sections);

        // Flatten all entries for easy assertion
        string allText = FlattenSections(result.Sections);

        // PE headers should always be present
        Assert.Contains("Architecture", allText);
        Assert.Contains("Subsystem", allText);

        // .NET assembly metadata should be present
        Assert.Contains(".NET Assembly", allText);
        Assert.Contains("Wade.Tests", allText);
        Assert.Contains("Referenced Assemblies", allText);
    }

    [Fact]
    public void GetMetadata_NonPeFile_ReturnsNull()
    {
        string tempPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}.exe");

        try
        {
            File.WriteAllText(tempPath, "This is not a PE file");

            var provider = new ExecutableMetadataProvider();
            MetadataResult? result = provider.GetMetadata(tempPath, MakeContext(), CancellationToken.None);

            Assert.Null(result);
        }
        finally
        {
            File.Delete(tempPath);
        }
    }

    [Fact]
    public void GetMetadata_CancelledToken_ReturnsNull()
    {
        string assemblyPath = typeof(ExecutableMetadataProviderTests).Assembly.Location;

        var provider = new ExecutableMetadataProvider();
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        MetadataResult? result = provider.GetMetadata(assemblyPath, MakeContext(), cts.Token);

        Assert.Null(result);
    }

    [Fact]
    public void GetMetadata_ZeroTimestamp_ShowsReproducibleBuild()
    {
        // Build a minimal PE file with zeroed TimeDateStamp.
        // DOS header (64 bytes) with e_lfanew at offset 60 pointing to PE signature,
        // then PE signature + COFF header + minimal optional header.
        byte[] pe = new byte[512];

        // DOS magic
        pe[0] = 0x4D; // 'M'
        pe[1] = 0x5A; // 'Z'

        // e_lfanew at offset 60 → PE signature starts at 64
        int peOffset = 64;
        pe[60] = (byte)peOffset;

        // PE signature "PE\0\0"
        pe[peOffset] = 0x50; // 'P'
        pe[peOffset + 1] = 0x45; // 'E'

        // COFF header starts at peOffset + 4
        int coffOffset = peOffset + 4;

        // Machine = AMD64 (0x8664)
        pe[coffOffset] = 0x64;
        pe[coffOffset + 1] = 0x86;

        // TimeDateStamp at coffOffset + 4 — leave as 0 (already zero-initialized)

        // SizeOfOptionalHeader at coffOffset + 16 — set to a small value
        pe[coffOffset + 16] = 0x70; // 112 bytes (minimum for PE32+)

        // Optional header magic at coffOffset + 20: PE32+ (0x020B)
        int optOffset = coffOffset + 20;
        pe[optOffset] = 0x0B;
        pe[optOffset + 1] = 0x02;

        // Subsystem at optOffset + 68: WINDOWS_CUI (3)
        pe[optOffset + 68] = 3;

        string tempPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}.dll");

        try
        {
            File.WriteAllBytes(tempPath, pe);

            var provider = new ExecutableMetadataProvider();
            MetadataResult? result = provider.GetMetadata(tempPath, MakeContext(), CancellationToken.None);

            Assert.NotNull(result);
            string allText = FlattenSections(result.Sections);
            Assert.Contains("Reproducible build", allText);
        }
        finally
        {
            File.Delete(tempPath);
        }
    }

    [Fact]
    public void Registry_ExeFile_ReturnsExecutableMetadataProvider()
    {
        List<IMetadataProvider> providers = MetadataProviderRegistry.GetApplicableProviders("app.exe", MakeContext());

        Assert.Contains(providers, p => p is ExecutableMetadataProvider);
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
