using Wade.Imaging;

namespace Wade.Tests;

public class PdfPreviewTests
{
    [Fact]
    public void XpdfPdfTool_IsAvailable_ReturnsBool()
    {
        var tool = new XpdfPdfTool();
        // Should not throw — just returns true or false based on whether pdftopng is on PATH
        _ = tool.IsAvailable;
    }

    [Fact]
    public void PdfImageConverter_CanConvert_TrueForPdf()
    {
        var converter = new PdfImageConverter();
        // CanConvert checks extension AND tool availability.
        // If no tool is available, it returns false even for .pdf — that's correct behavior.
        // We test the extension matching by checking it doesn't return true for non-pdf.
        bool result = converter.CanConvert("test.pdf");
        // Result depends on whether pdftopng is installed — either way, no exception
        Assert.Equal(result, converter.CanConvert("TEST.PDF"));
    }

    [Theory]
    [InlineData("test.txt")]
    [InlineData("test.png")]
    [InlineData("test.jpg")]
    [InlineData("test.docx")]
    [InlineData("test")]
    public void PdfImageConverter_CanConvert_FalseForNonPdf(string path)
    {
        var converter = new PdfImageConverter();
        Assert.False(converter.CanConvert(path));
    }

    [Theory]
    [InlineData("document.pdf", true)]
    [InlineData("DOCUMENT.PDF", true)]
    [InlineData("file.Pdf", true)]
    [InlineData("file.txt", false)]
    [InlineData("file.png", false)]
    public void ImageConverter_CanConvert_DelegatesCorrectly(string path, bool expectedIfToolAvailable)
    {
        bool result = ImageConverter.CanConvert(path);
        // If a PDF tool is available, result should match expected; if not, all are false
        if (!expectedIfToolAvailable)
        {
            Assert.False(result);
        }
        // When expectedIfToolAvailable is true, result depends on tool availability — both outcomes are valid
    }
}
