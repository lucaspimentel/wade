using System.IO.Compression;
using System.Text;
using Wade.Preview;

namespace Wade.Tests;

public class NuGetMetadataProviderTests
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
            DisabledTools: new HashSet<string>(),
            ZipPreviewEnabled: true,
            ImagePreviewsEnabled: true);

    [Theory]
    [InlineData("package.nupkg")]
    [InlineData("package.NUPKG")]
    [InlineData("symbols.snupkg")]
    [InlineData("symbols.SNUPKG")]
    public void CanProvideMetadata_NuGetExtensions_ReturnsTrue(string path)
    {
        var provider = new NuGetMetadataProvider();
        Assert.True(provider.CanProvideMetadata(path, MakeContext()));
    }

    [Theory]
    [InlineData("archive.zip")]
    [InlineData("library.jar")]
    [InlineData("readme.txt")]
    public void CanProvideMetadata_NonNuGetExtensions_ReturnsFalse(string path)
    {
        var provider = new NuGetMetadataProvider();
        Assert.False(provider.CanProvideMetadata(path, MakeContext()));
    }

    [Fact]
    public void GetMetadata_WithNuspec_ReturnsSections()
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
            var provider = new NuGetMetadataProvider();
            MetadataResult? result = provider.GetMetadata(tempPath, MakeContext(), CancellationToken.None);

            Assert.NotNull(result);
            Assert.NotEmpty(result.Sections);
            Assert.Equal("NuGet Package", result.FileTypeLabel);

            string allText = FlattenSections(result.Sections);
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
    public void GetMetadata_MissingNuspec_ReturnsFallbackMessage()
    {
        string tempPath = CreateTestNupkg(nuspecXml: null);

        try
        {
            var provider = new NuGetMetadataProvider();
            MetadataResult? result = provider.GetMetadata(tempPath, MakeContext(), CancellationToken.None);

            Assert.NotNull(result);
            string allText = FlattenSections(result!.Sections);
            Assert.Contains("[no .nuspec found in package]", allText);
        }
        finally
        {
            File.Delete(tempPath);
        }
    }

    [Fact]
    public void GetMetadata_CancelledToken_ReturnsNull()
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
            var provider = new NuGetMetadataProvider();
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
    public void Registry_NupkgFile_ReturnsNuGetMetadataProvider()
    {
        var providers = MetadataProviderRegistry.GetApplicableProviders("package.nupkg", MakeContext());

        Assert.Contains(providers, p => p is NuGetMetadataProvider);
    }

    [Fact]
    public void GetMetadata_FlatDependencies_ShowsDependencies()
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
            var provider = new NuGetMetadataProvider();
            MetadataResult? result = provider.GetMetadata(tempPath, MakeContext(), CancellationToken.None);

            Assert.NotNull(result);
            string allText = FlattenSections(result!.Sections);
            Assert.Contains("SomeLib", allText);
            Assert.Contains("OtherLib", allText);
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
