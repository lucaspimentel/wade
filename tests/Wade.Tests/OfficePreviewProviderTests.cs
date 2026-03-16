using System.IO.Compression;
using System.Text;
using Wade.Preview;

namespace Wade.Tests;

public class OfficePreviewProviderTests
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
    [InlineData("report.docx")]
    [InlineData("report.DOCX")]
    [InlineData("data.xlsx")]
    [InlineData("slides.pptx")]
    [InlineData("template.dotx")]
    [InlineData("template.xltx")]
    [InlineData("template.potx")]
    public void CanPreview_OfficeExtensions_ReturnsTrue(string path)
    {
        var provider = new OfficePreviewProvider();
        Assert.True(provider.CanPreview(path, MakeContext()));
    }

    [Theory]
    [InlineData("archive.zip")]
    [InlineData("readme.txt")]
    [InlineData("document.odt")]
    [InlineData("package.nupkg")]
    [InlineData("document.doc")]
    public void CanPreview_NonOfficeExtensions_ReturnsFalse(string path)
    {
        var provider = new OfficePreviewProvider();
        Assert.False(provider.CanPreview(path, MakeContext()));
    }

    [Fact]
    public void GetPreview_WithCoreAndAppProperties_ReturnsMetadata()
    {
        string tempPath = CreateTestDocx(
            coreXml: """
                <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
                <cp:coreProperties
                    xmlns:cp="http://schemas.openxmlformats.org/package/2006/metadata/core-properties"
                    xmlns:dc="http://purl.org/dc/elements/1.1/"
                    xmlns:dcterms="http://purl.org/dc/terms/"
                    xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance">
                  <dc:title>Quarterly Report</dc:title>
                  <dc:creator>Jane Smith</dc:creator>
                  <dc:subject>Q1 2025 Results</dc:subject>
                  <cp:keywords>quarterly, finance</cp:keywords>
                  <dcterms:created xsi:type="dcterms:W3CDTF">2025-01-15T09:30:00Z</dcterms:created>
                  <dcterms:modified xsi:type="dcterms:W3CDTF">2025-03-10T14:22:03Z</dcterms:modified>
                </cp:coreProperties>
                """,
            appXml: """
                <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
                <Properties xmlns="http://schemas.openxmlformats.org/officeDocument/2006/extended-properties">
                  <Pages>12</Pages>
                  <Words>3450</Words>
                  <Paragraphs>85</Paragraphs>
                  <Application>Microsoft Office Word</Application>
                </Properties>
                """);

        try
        {
            var provider = new OfficePreviewProvider();
            PreviewResult? result = provider.GetPreview(tempPath, MakeContext(), CancellationToken.None);

            Assert.NotNull(result);
            Assert.NotNull(result.TextLines);
            Assert.True(result.IsRendered);
            Assert.Equal("Word Document", result.FileTypeLabel);

            string allText = string.Join('\n', result.TextLines.Select(l => l.Text));
            Assert.Contains("Quarterly Report", allText);
            Assert.Contains("Jane Smith", allText);
            Assert.Contains("Q1 2025 Results", allText);
            Assert.Contains("quarterly, finance", allText);
            Assert.Contains("2025-01-15", allText);
            Assert.Contains("2025-03-10", allText);
            Assert.Contains("12", allText);
            Assert.Contains("3,450", allText);
            Assert.Contains("Microsoft Office Word", allText);
        }
        finally
        {
            File.Delete(tempPath);
        }
    }

    [Fact]
    public void GetPreview_ExcelWithSheetNames_ShowsSheets()
    {
        string tempPath = CreateTestOfficeFile(
            ".xlsx",
            coreXml: null,
            appXml: """
                <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
                <Properties xmlns="http://schemas.openxmlformats.org/officeDocument/2006/extended-properties"
                            xmlns:vt="http://schemas.openxmlformats.org/officeDocument/2006/docPropsVTypes">
                  <Application>Microsoft Excel</Application>
                  <TitlesOfParts>
                    <vt:vector size="3" baseType="lpstr">
                      <vt:lpstr>Sales</vt:lpstr>
                      <vt:lpstr>Expenses</vt:lpstr>
                      <vt:lpstr>Summary</vt:lpstr>
                    </vt:vector>
                  </TitlesOfParts>
                </Properties>
                """);

        try
        {
            var provider = new OfficePreviewProvider();
            PreviewResult? result = provider.GetPreview(tempPath, MakeContext(), CancellationToken.None);

            Assert.NotNull(result);
            Assert.Equal("Excel Workbook", result!.FileTypeLabel);

            string allText = string.Join('\n', result.TextLines!.Select(l => l.Text));
            Assert.Contains("Sheets:", allText);
            Assert.Contains("Sales", allText);
            Assert.Contains("Expenses", allText);
            Assert.Contains("Summary", allText);
        }
        finally
        {
            File.Delete(tempPath);
        }
    }

    [Fact]
    public void GetPreview_PowerPointWithSlides_ShowsSlideCount()
    {
        string tempPath = CreateTestOfficeFile(
            ".pptx",
            coreXml: null,
            appXml: """
                <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
                <Properties xmlns="http://schemas.openxmlformats.org/officeDocument/2006/extended-properties">
                  <Slides>24</Slides>
                  <HiddenSlides>2</HiddenSlides>
                  <Application>Microsoft PowerPoint</Application>
                </Properties>
                """);

        try
        {
            var provider = new OfficePreviewProvider();
            PreviewResult? result = provider.GetPreview(tempPath, MakeContext(), CancellationToken.None);

            Assert.NotNull(result);
            Assert.Equal("PowerPoint Presentation", result!.FileTypeLabel);

            string allText = string.Join('\n', result.TextLines!.Select(l => l.Text));
            Assert.Contains("24", allText);
            Assert.Contains("Hidden slides", allText);
            Assert.Contains("Microsoft PowerPoint", allText);
        }
        finally
        {
            File.Delete(tempPath);
        }
    }

    [Fact]
    public void GetPreview_NoMetadata_ReturnsFallbackMessage()
    {
        string tempPath = CreateTestDocx(coreXml: null, appXml: null);

        try
        {
            var provider = new OfficePreviewProvider();
            PreviewResult? result = provider.GetPreview(tempPath, MakeContext(), CancellationToken.None);

            Assert.NotNull(result);
            Assert.Contains("[no document metadata found]", result!.TextLines![0].Text);
        }
        finally
        {
            File.Delete(tempPath);
        }
    }

    [Fact]
    public void GetPreview_CancelledToken_ReturnsNull()
    {
        string tempPath = CreateTestDocx(
            coreXml: """
                <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
                <cp:coreProperties xmlns:cp="http://schemas.openxmlformats.org/package/2006/metadata/core-properties"
                                   xmlns:dc="http://purl.org/dc/elements/1.1/">
                  <dc:title>Test</dc:title>
                </cp:coreProperties>
                """,
            appXml: null);

        try
        {
            var provider = new OfficePreviewProvider();
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
    public void Registry_DocxFile_ReturnsOfficeBeforeZipContents()
    {
        var providers = PreviewProviderRegistry.GetApplicableProviders("report.docx", MakeContext());

        int officeIndex = providers.FindIndex(p => p is OfficePreviewProvider);
        int zipIndex = providers.FindIndex(p => p is ZipContentsPreviewProvider);

        Assert.True(officeIndex >= 0, "OfficePreviewProvider should be in the list");
        Assert.True(zipIndex >= 0, "ZipContentsPreviewProvider should be in the list");
        Assert.True(officeIndex < zipIndex, "Office provider should come before ZipContents provider");
    }

    private static string CreateTestDocx(string? coreXml, string? appXml) =>
        CreateTestOfficeFile(".docx", coreXml, appXml);

    private static string CreateTestOfficeFile(string extension, string? coreXml, string? appXml)
    {
        string tempPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}{extension}");

        using (var fs = new FileStream(tempPath, FileMode.Create))
        using (var archive = new ZipArchive(fs, ZipArchiveMode.Create))
        {
            if (coreXml is not null)
            {
                ZipArchiveEntry entry = archive.CreateEntry("docProps/core.xml");
                using Stream stream = entry.Open();
                byte[] bytes = Encoding.UTF8.GetBytes(coreXml);
                stream.Write(bytes, 0, bytes.Length);
            }

            if (appXml is not null)
            {
                ZipArchiveEntry entry = archive.CreateEntry("docProps/app.xml");
                using Stream stream = entry.Open();
                byte[] bytes = Encoding.UTF8.GetBytes(appXml);
                stream.Write(bytes, 0, bytes.Length);
            }
        }

        return tempPath;
    }
}
