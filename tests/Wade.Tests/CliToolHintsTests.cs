using Wade.Preview;

namespace Wade.Tests;

public class CliToolHintsTests
{
    [Fact]
    public void GetHint_TextFile_ReturnsNull()
    {
        string? hint = CliToolHints.GetHint("document.txt");
        Assert.Null(hint);
    }

    [Fact]
    public void GetHint_CSharpFile_ReturnsNull()
    {
        string? hint = CliToolHints.GetHint("Program.cs");
        Assert.Null(hint);
    }

    [Fact]
    public void GetHint_UnknownExtension_ReturnsNull()
    {
        string? hint = CliToolHints.GetHint("file.xyz123");
        Assert.Null(hint);
    }

    [Fact]
    public void GetHint_PdfFile_ReturnsNonNullOrNullDependingOnAvailability()
    {
        // If both pdfinfo and pdftopng are available, hint should be null.
        // If either is missing, hint should be non-null.
        string? hint = CliToolHints.GetHint("document.pdf");
        bool pdftopngAvailable = CliTool.IsAvailable("pdftopng");
        bool pdfinfoAvailable = CliTool.IsAvailable("pdfinfo", "-v");

        if (pdftopngAvailable && pdfinfoAvailable)
        {
            Assert.Null(hint);
        }
        else
        {
            Assert.NotNull(hint);
        }
    }

    [Fact]
    public void GetHint_MediaFile_ReturnsNonNullOrNullDependingOnAvailability()
    {
        string? hint = CliToolHints.GetHint("video.mp4");
        bool ffprobeAvailable = CliTool.IsAvailable("ffprobe", "-version", requireZeroExitCode: true);
        bool mediainfoAvailable = CliTool.IsAvailable("mediainfo", "--version");

        if (ffprobeAvailable || mediainfoAvailable)
        {
            Assert.Null(hint);
        }
        else
        {
            Assert.NotNull(hint);
        }
    }

    [Fact]
    public void GetHint_MarkdownFile_ReturnsNonNullOrNullDependingOnAvailability()
    {
        string? hint = CliToolHints.GetHint("README.md");
        bool glowAvailable = CliTool.IsAvailable("glow", "--version", requireZeroExitCode: true);

        if (glowAvailable)
        {
            Assert.Null(hint);
        }
        else
        {
            Assert.NotNull(hint);
        }
    }
}
