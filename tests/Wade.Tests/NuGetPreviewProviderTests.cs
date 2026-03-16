using System.IO.Compression;
using System.Text;
using Wade.Preview;

namespace Wade.Tests;

public class NuGetPreviewProviderTests
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
    [InlineData("package.nupkg")]
    [InlineData("package.NUPKG")]
    [InlineData("symbols.snupkg")]
    [InlineData("symbols.SNUPKG")]
    public void CanPreview_NuGetExtensions_ReturnsTrue(string path)
    {
        var provider = new NuGetPreviewProvider();
        Assert.True(provider.CanPreview(path, MakeContext()));
    }

    [Theory]
    [InlineData("archive.zip")]
    [InlineData("library.jar")]
    [InlineData("readme.txt")]
    public void CanPreview_NonNuGetExtensions_ReturnsFalse(string path)
    {
        var provider = new NuGetPreviewProvider();
        Assert.False(provider.CanPreview(path, MakeContext()));
    }

    [Fact]
    public void GetPreview_WithNuspec_ReturnsMetadataLines()
    {
        string tempPath = CreateTestNupkg(
            """
            <?xml version="1.0" encoding="utf-8"?>
            <package xmlns="http://schemas.microsoft.com/packaging/2013/05/nuspec.xsd">
              <metadata>
                <id>TestPackage</id>
                <version>1.2.3</version>
                <authors>Test Author</authors>
                <description>A test package for unit testing.</description>
                <tags>test unit</tags>
                <license type="expression">MIT</license>
                <projectUrl>https://example.com</projectUrl>
                <repository type="git" url="https://github.com/test/repo" />
                <dependencies>
                  <group targetFramework="net8.0">
                    <dependency id="Newtonsoft.Json" version="13.0.3" />
                  </group>
                </dependencies>
              </metadata>
            </package>
            """);

        try
        {
            var provider = new NuGetPreviewProvider();
            PreviewResult? result = provider.GetPreview(tempPath, MakeContext(), CancellationToken.None);

            Assert.NotNull(result);
            Assert.NotNull(result.TextLines);
            Assert.True(result.IsRendered);
            Assert.Equal("NuGet Package", result.FileTypeLabel);

            string allText = string.Join('\n', result.TextLines.Select(l => l.Text));
            Assert.Contains("TestPackage", allText);
            Assert.Contains("1.2.3", allText);
            Assert.Contains("Test Author", allText);
            Assert.Contains("MIT", allText);
            Assert.Contains("https://example.com", allText);
            Assert.Contains("https://github.com/test/repo", allText);
            Assert.Contains("test unit", allText);
            Assert.Contains("A test package for unit testing.", allText);
            Assert.Contains("Newtonsoft.Json", allText);
            Assert.Contains("net8.0", allText);
        }
        finally
        {
            File.Delete(tempPath);
        }
    }

    [Fact]
    public void GetPreview_MissingNuspec_ReturnsFallbackMessage()
    {
        string tempPath = CreateTestNupkg(nuspecXml: null);

        try
        {
            var provider = new NuGetPreviewProvider();
            PreviewResult? result = provider.GetPreview(tempPath, MakeContext(), CancellationToken.None);

            Assert.NotNull(result);
            Assert.NotNull(result.TextLines);
            Assert.Contains("[no .nuspec found in package]", result.TextLines[0].Text);
        }
        finally
        {
            File.Delete(tempPath);
        }
    }

    [Fact]
    public void GetPreview_CancelledToken_ReturnsNull()
    {
        string tempPath = CreateTestNupkg(
            """
            <?xml version="1.0" encoding="utf-8"?>
            <package xmlns="http://schemas.microsoft.com/packaging/2013/05/nuspec.xsd">
              <metadata><id>Test</id><version>1.0.0</version></metadata>
            </package>
            """);

        try
        {
            var provider = new NuGetPreviewProvider();
            using var cts = new CancellationTokenSource();
            cts.Cancel();
            PreviewResult? result = provider.GetPreview(tempPath, MakeContext(), cts.Token);

            Assert.Null(result);
        }
        finally
        {
            File.Delete(tempPath);
        }
    }

    [Fact]
    public void Registry_NupkgFile_ReturnsNuGetBeforeZipContents()
    {
        var providers = PreviewProviderRegistry.GetApplicableProviders("package.nupkg", MakeContext());

        Assert.True(providers.Count >= 2);

        int nugetIndex = providers.FindIndex(p => p is NuGetPreviewProvider);
        int zipIndex = providers.FindIndex(p => p is ZipContentsPreviewProvider);

        Assert.True(nugetIndex >= 0, "NuGetPreviewProvider should be in the list");
        Assert.True(zipIndex >= 0, "ZipContentsPreviewProvider should be in the list");
        Assert.True(nugetIndex < zipIndex, "NuGet provider should come before ZipContents provider");
    }

    [Fact]
    public void GetPreview_FlatDependencies_ShowsDependencies()
    {
        string tempPath = CreateTestNupkg(
            """
            <?xml version="1.0" encoding="utf-8"?>
            <package xmlns="http://schemas.microsoft.com/packaging/2010/07/nuspec.xsd">
              <metadata>
                <id>FlatDeps</id>
                <version>2.0.0</version>
                <dependencies>
                  <dependency id="SomeLib" version="1.0.0" />
                  <dependency id="OtherLib" version="2.0.0" />
                </dependencies>
              </metadata>
            </package>
            """);

        try
        {
            var provider = new NuGetPreviewProvider();
            PreviewResult? result = provider.GetPreview(tempPath, MakeContext(), CancellationToken.None);

            Assert.NotNull(result);
            string allText = string.Join('\n', result!.TextLines!.Select(l => l.Text));
            Assert.Contains("SomeLib", allText);
            Assert.Contains("OtherLib", allText);
        }
        finally
        {
            File.Delete(tempPath);
        }
    }

    private static string CreateTestNupkg(string? nuspecXml)
    {
        string tempPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}.nupkg");

        using (var fs = new FileStream(tempPath, FileMode.Create))
        using (var archive = new ZipArchive(fs, ZipArchiveMode.Create))
        {
            if (nuspecXml is not null)
            {
                ZipArchiveEntry entry = archive.CreateEntry("Test.nuspec");

                using Stream stream = entry.Open();
                byte[] bytes = Encoding.UTF8.GetBytes(nuspecXml);
                stream.Write(bytes, 0, bytes.Length);
            }
        }

        return tempPath;
    }
}
