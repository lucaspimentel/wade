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
            GlowEnabled: false,
            ZipPreviewEnabled: true,
            PdfPreviewEnabled: true,
            ImagePreviewsEnabled: true);

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
    public void Registry_ExeFile_ReturnsExecutableMetadataProvider()
    {
        var provider = MetadataProviderRegistry.GetProvider("app.exe", MakeContext());

        Assert.NotNull(provider);
        Assert.IsType<ExecutableMetadataProvider>(provider);
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
