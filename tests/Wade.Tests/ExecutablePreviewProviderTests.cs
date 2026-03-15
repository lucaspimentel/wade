using Wade.Preview;

namespace Wade.Tests;

public class ExecutablePreviewProviderTests
{
    private static PreviewContext MakeContext() =>
        new(
            PaneWidthCells: 60,
            PaneHeightCells: 30,
            CellPixelWidth: 8,
            CellPixelHeight: 16,
            IsCloudPlaceholder: false,
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
    public void CanPreview_ExecutableExtensions_ReturnsTrue(string path)
    {
        var provider = new ExecutablePreviewProvider();
        Assert.True(provider.CanPreview(path, MakeContext()));
    }

    [Theory]
    [InlineData("archive.zip")]
    [InlineData("readme.txt")]
    [InlineData("image.png")]
    [InlineData("package.nupkg")]
    public void CanPreview_NonExecutableExtensions_ReturnsFalse(string path)
    {
        var provider = new ExecutablePreviewProvider();
        Assert.False(provider.CanPreview(path, MakeContext()));
    }

    [Fact]
    public void GetPreview_WithRealDotNetAssembly_ReturnsMetadata()
    {
        // Use the test assembly itself as the test subject — it's a .NET assembly
        string assemblyPath = typeof(ExecutablePreviewProviderTests).Assembly.Location;

        var provider = new ExecutablePreviewProvider();
        PreviewResult? result = provider.GetPreview(assemblyPath, MakeContext(), CancellationToken.None);

        Assert.NotNull(result);
        Assert.NotNull(result.TextLines);
        Assert.True(result.IsRendered);

        string allText = string.Join('\n', result.TextLines.Select(l => l.Text));

        // PE headers should always be present
        Assert.Contains("Architecture", allText);
        Assert.Contains("Subsystem", allText);

        // .NET assembly metadata should be present
        Assert.Contains(".NET Assembly", allText);
        Assert.Contains("Wade.Tests", allText);
        Assert.Contains("Referenced Assemblies", allText);
    }

    [Fact]
    public void GetPreview_NonPeFile_ReturnsNull()
    {
        // Create a temp file with .exe extension but non-PE content
        string tempPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}.exe");

        try
        {
            File.WriteAllText(tempPath, "This is not a PE file");

            var provider = new ExecutablePreviewProvider();
            PreviewResult? result = provider.GetPreview(tempPath, MakeContext(), CancellationToken.None);

            Assert.Null(result);
        }
        finally
        {
            File.Delete(tempPath);
        }
    }

    [Fact]
    public void GetPreview_CancelledToken_ReturnsNull()
    {
        string assemblyPath = typeof(ExecutablePreviewProviderTests).Assembly.Location;

        var provider = new ExecutablePreviewProvider();
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        PreviewResult? result = provider.GetPreview(assemblyPath, MakeContext(), cts.Token);

        Assert.Null(result);
    }

    [Fact]
    public void Registry_ExeFile_ReturnsExecutableBeforeText()
    {
        var providers = PreviewProviderRegistry.GetApplicableProviders("app.exe", MakeContext());

        int execIndex = providers.FindIndex(p => p is ExecutablePreviewProvider);
        int textIndex = providers.FindIndex(p => p is TextPreviewProvider);

        Assert.True(execIndex >= 0, "ExecutablePreviewProvider should be in the list");
        Assert.True(textIndex >= 0, "TextPreviewProvider should be in the list");
        Assert.True(execIndex < textIndex, "Executable provider should come before Text provider");
    }
}
