using Wade.Preview;

namespace Wade.Tests;

public class PdfMetadataProviderTests
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

    // ── Extension matching ────────────────────────────────────────────────────

    [Theory]
    [InlineData("doc.pdf")]
    [InlineData("doc.PDF")]
    public void CanProvideMetadata_PdfExtensions_ReturnsTrueWhenAvailable(string path)
    {
        if (!PdfMetadataProvider.IsAvailable)
        {
            return; // Skip if pdfinfo not installed
        }

        var provider = new PdfMetadataProvider();
        Assert.True(provider.CanProvideMetadata(path, MakeContext()));
    }

    [Theory]
    [InlineData("readme.txt")]
    [InlineData("report.docx")]
    [InlineData("image.png")]
    [InlineData("app.exe")]
    public void CanProvideMetadata_NonPdfExtensions_ReturnsFalse(string path)
    {
        var provider = new PdfMetadataProvider();
        Assert.False(provider.CanProvideMetadata(path, MakeContext()));
    }

    // ── Parse method tests ────────────────────────────────────────────────────

    [Fact]
    public void ParsePdfInfoOutput_FullMetadata_ReturnsAllFields()
    {
        const string output = """
            Title:           Cover Page English
            Author:          John Doe
            Creator:         Windows NT 4.0
            Producer:        Acrobat Distiller 3.01 for Windows
            CreationDate:    Mon May 24 04:42:21 1999
            ModDate:         Mon May 24 04:44:24 1999
            Pages:           57
            Encrypted:       no
            Page size:       595 x 842 pts (A4)
            PDF version:     1.2
            """;

        MetadataSection[]? sections = PdfMetadataProvider.ParsePdfInfoOutput(output);

        Assert.NotNull(sections);
        string allText = FlattenSections(sections!);

        Assert.Contains("Cover Page English", allText);
        Assert.Contains("John Doe", allText);
        Assert.Contains("Windows NT 4.0", allText);
        Assert.Contains("Acrobat Distiller", allText);
        Assert.Contains("57", allText);
        Assert.Contains("595 x 842 pts (A4)", allText);
        Assert.Contains("1.2", allText);
    }

    [Fact]
    public void ParsePdfInfoOutput_MinimalMetadata_ReturnsAvailableFields()
    {
        const string output = """
            Pages:           3
            PDF version:     1.7
            """;

        MetadataSection[]? sections = PdfMetadataProvider.ParsePdfInfoOutput(output);

        Assert.NotNull(sections);
        string allText = FlattenSections(sections!);

        Assert.Contains("3", allText);
        Assert.Contains("1.7", allText);
    }

    [Fact]
    public void ParsePdfInfoOutput_EmptyOutput_ReturnsNull()
    {
        MetadataSection[]? sections = PdfMetadataProvider.ParsePdfInfoOutput("");

        Assert.Null(sections);
    }

    [Fact]
    public void ParsePdfInfoOutput_EncryptedPdf_ShowsEncryptedField()
    {
        const string output = """
            Pages:           10
            Encrypted:       yes (print:yes copy:no change:no addNotes:no algorithm:RC4)
            PDF version:     1.6
            """;

        MetadataSection[]? sections = PdfMetadataProvider.ParsePdfInfoOutput(output);

        Assert.NotNull(sections);
        string allText = FlattenSections(sections!);

        Assert.Contains("Encrypted", allText);
        Assert.Contains("yes", allText);
    }

    // ── Registry ──────────────────────────────────────────────────────────────

    [Fact]
    public void Registry_PdfFile_ReturnsPdfMetadataProviderWhenAvailable()
    {
        if (!PdfMetadataProvider.IsAvailable)
        {
            return; // Skip if pdfinfo not installed
        }

        var provider = MetadataProviderRegistry.GetProvider("doc.pdf", MakeContext());

        Assert.NotNull(provider);
        Assert.IsType<PdfMetadataProvider>(provider);
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
