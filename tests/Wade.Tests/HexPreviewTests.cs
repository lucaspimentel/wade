using Wade.FileSystem;
using Wade.Highlighting;
using Wade.Terminal;

namespace Wade.Tests;

public class HexPreviewTests
{
    [Fact]
    public void GetPreviewLines_SmallFile_ReturnsCorrectRowCount()
    {
        string path = CreateTempFile([0x48, 0x65, 0x6C, 0x6C, 0x6F]); // "Hello"
        try
        {
            StyledLine[]? lines = HexPreview.GetPreviewLines(path, CancellationToken.None);

            Assert.NotNull(lines);
            Assert.Single(lines); // 5 bytes = 1 row
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void GetPreviewLines_ExactlyOneRow_Returns1Line()
    {
        byte[] data = new byte[16];
        for (int i = 0; i < 16; i++)
        {
            data[i] = (byte)(0x30 + i); // '0' through '?'
        }

        string path = CreateTempFile(data);
        try
        {
            StyledLine[]? lines = HexPreview.GetPreviewLines(path, CancellationToken.None);

            Assert.NotNull(lines);
            Assert.Single(lines);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void GetPreviewLines_17Bytes_Returns2Lines()
    {
        byte[] data = new byte[17];
        string path = CreateTempFile(data);
        try
        {
            StyledLine[]? lines = HexPreview.GetPreviewLines(path, CancellationToken.None);

            Assert.NotNull(lines);
            Assert.Equal(2, lines.Length);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void GetPreviewLines_ContainsOffset()
    {
        string path = CreateTempFile([0x41]); // 'A'
        try
        {
            StyledLine[]? lines = HexPreview.GetPreviewLines(path, CancellationToken.None);

            Assert.NotNull(lines);
            Assert.StartsWith("00000000", lines[0].Text);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void GetPreviewLines_SecondRowOffset_Is00000010()
    {
        byte[] data = new byte[17];
        string path = CreateTempFile(data);
        try
        {
            StyledLine[]? lines = HexPreview.GetPreviewLines(path, CancellationToken.None);

            Assert.NotNull(lines);
            Assert.Equal(2, lines.Length);
            Assert.StartsWith("00000010", lines[1].Text);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void GetPreviewLines_ContainsHexBytes()
    {
        string path = CreateTempFile([0x48, 0x65, 0x6C]); // "Hel"
        try
        {
            StyledLine[]? lines = HexPreview.GetPreviewLines(path, CancellationToken.None);

            Assert.NotNull(lines);
            Assert.Contains("48", lines[0].Text);
            Assert.Contains("65", lines[0].Text);
            Assert.Contains("6C", lines[0].Text);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void GetPreviewLines_ContainsAsciiColumn()
    {
        string path = CreateTempFile([0x48, 0x65, 0x6C, 0x6C, 0x6F]); // "Hello"
        try
        {
            StyledLine[]? lines = HexPreview.GetPreviewLines(path, CancellationToken.None);

            Assert.NotNull(lines);
            Assert.Contains("Hello", lines[0].Text);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void GetPreviewLines_NonPrintableBytes_ShowAsDots()
    {
        string path = CreateTempFile([0x01, 0x02, 0x03]);
        try
        {
            StyledLine[]? lines = HexPreview.GetPreviewLines(path, CancellationToken.None);

            Assert.NotNull(lines);
            // ASCII column should contain dots for non-printable bytes
            Assert.Contains("|...", lines[0].Text);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void GetPreviewLines_HasCharStyles()
    {
        string path = CreateTempFile([0x41, 0x00, 0x42]); // A, null, B
        try
        {
            StyledLine[]? lines = HexPreview.GetPreviewLines(path, CancellationToken.None);

            Assert.NotNull(lines);
            CellStyle[]? charStyles = lines[0].CharStyles;
            Assert.NotNull(charStyles);
            Assert.Equal(lines[0].Text.Length, charStyles.Length);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void GetPreviewLines_AsciiPipeDelimiters()
    {
        string path = CreateTempFile([0x41]); // 'A'
        try
        {
            StyledLine[]? lines = HexPreview.GetPreviewLines(path, CancellationToken.None);

            Assert.NotNull(lines);
            string text = lines[0].Text;
            // Should contain pipe-delimited ASCII section
            Assert.Contains("|A", text);
            Assert.EndsWith("|", text);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void GetPreviewLines_EmptyFile_ReturnsEmptyMessage()
    {
        string path = CreateTempFile([]);
        try
        {
            StyledLine[]? lines = HexPreview.GetPreviewLines(path, CancellationToken.None);

            Assert.NotNull(lines);
            Assert.Single(lines);
            Assert.Equal("[empty file]", lines[0].Text);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void GetPreviewLines_Cancelled_ReturnsNull()
    {
        string path = CreateTempFile([0x41, 0x42, 0x43]);
        try
        {
            using var cts = new CancellationTokenSource();
            cts.Cancel();

            StyledLine[]? lines = HexPreview.GetPreviewLines(path, cts.Token);

            Assert.Null(lines);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void GetPreviewLines_NonexistentFile_ReturnsNull()
    {
        string path = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName() + ".bin");

        StyledLine[]? lines = HexPreview.GetPreviewLines(path, CancellationToken.None);

        Assert.Null(lines);
    }

    [Fact]
    public void GetPreviewLines_FullRow_Has78CharWidth()
    {
        byte[] data = new byte[16];
        for (int i = 0; i < 16; i++)
        {
            data[i] = (byte)(0x41 + i);
        }

        string path = CreateTempFile(data);
        try
        {
            StyledLine[]? lines = HexPreview.GetPreviewLines(path, CancellationToken.None);

            Assert.NotNull(lines);
            Assert.Equal(78, lines[0].Text.Length);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Theory]
    [InlineData(0x00, '.')] // null → dot
    [InlineData(0x01, '.')] // control → dot
    [InlineData(0x1F, '.')] // control → dot
    [InlineData(0x20, ' ')] // space → space
    [InlineData(0x41, 'A')] // printable → literal
    [InlineData(0x7E, '~')] // printable → literal
    [InlineData(0x7F, '.')] // DEL → dot
    [InlineData(0xFF, '.')] // high byte → dot
    public void GetPreviewLines_AsciiColumn_MapsCorrectly(byte input, char expected)
    {
        string path = CreateTempFile([input]);
        try
        {
            StyledLine[]? lines = HexPreview.GetPreviewLines(path, CancellationToken.None);

            Assert.NotNull(lines);
            // ASCII column starts at position 61 (after "|")
            Assert.Equal(expected, lines[0].Text[61]);
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static string CreateTempFile(byte[] data)
    {
        string path = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName() + ".bin");
        File.WriteAllBytes(path, data);
        return path;
    }
}
