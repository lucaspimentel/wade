using Wade.FileSystem;

namespace Wade.Tests;

public class FilePreviewTests
{
    [Fact]
    public void GetPreviewLines_TextFile_ReturnsLines()
    {
        string tempFile = Path.GetTempFileName();
        try
        {
            File.WriteAllText(tempFile, "line1\nline2\nline3\n");
            var lines = FilePreview.GetPreviewLines(tempFile);

            Assert.Equal(3, lines.Length);
            Assert.Equal("line1", lines[0]);
            Assert.Equal("line2", lines[1]);
            Assert.Equal("line3", lines[2]);
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Fact]
    public void GetPreviewLines_KnownExtension_ReturnsContentWithoutHeader()
    {
        // Known extension (.cs) — no header in preview; label goes to status bar
        string tempFile = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}.cs");
        try
        {
            File.WriteAllText(tempFile, "public class Foo { }");
            var lines = FilePreview.GetPreviewLines(tempFile);

            Assert.Equal("public class Foo { }", lines[0]);
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Fact]
    public void GetPreviewLines_EmptyFile_ReturnsEmptyMessage()
    {
        string tempFile = Path.GetTempFileName();
        try
        {
            var lines = FilePreview.GetPreviewLines(tempFile);
            Assert.Single(lines);
            Assert.Equal("[empty file]", lines[0]);
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Fact]
    public void IsBinary_FileWithNullBytes_ReturnsTrue()
    {
        string tempFile = Path.GetTempFileName();
        try
        {
            File.WriteAllBytes(tempFile, [0x48, 0x65, 0x6C, 0x00, 0x6F]);
            Assert.True(FilePreview.IsBinary(tempFile));
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Fact]
    public void IsBinary_TextFile_ReturnsFalse()
    {
        string tempFile = Path.GetTempFileName();
        try
        {
            File.WriteAllText(tempFile, "Hello, world!");
            Assert.False(FilePreview.IsBinary(tempFile));
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Fact]
    public void GetPreviewLines_BinaryFile_ReturnsBinaryMessage()
    {
        string tempFile = Path.GetTempFileName();
        try
        {
            File.WriteAllBytes(tempFile, [0x48, 0x65, 0x6C, 0x00, 0x6F]);
            var lines = FilePreview.GetPreviewLines(tempFile);
            Assert.Single(lines);
            Assert.Equal("[binary file]", lines[0]);
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Fact]
    public void GetPreviewLines_BinaryFile_KnownExtension_ReturnsBinaryMessage()
    {
        // Label is in the status bar now, not in the binary message
        string tempFile = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}.exe");
        try
        {
            File.WriteAllBytes(tempFile, [0x4D, 0x5A, 0x00, 0x00]); // MZ header + null bytes
            var lines = FilePreview.GetPreviewLines(tempFile);
            Assert.Single(lines);
            Assert.Equal("[binary file]", lines[0]);
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Theory]
    [InlineData(".cs", "C#")]
    [InlineData(".py", "Python")]
    [InlineData(".json", "JSON")]
    [InlineData(".md", "Markdown")]
    [InlineData(".ts", "TypeScript")]
    [InlineData(".go", "Go")]
    [InlineData(".rs", "Rust")]
    [InlineData(".html", "HTML")]
    [InlineData(".yaml", "YAML")]
    [InlineData(".yml", "YAML")]
    [InlineData(".sh", "Shell")]
    [InlineData(".ps1", "PowerShell")]
    public void GetFileTypeLabel_KnownExtension_ReturnsLabel(string extension, string expectedLabel)
    {
        string path = $"file{extension}";
        Assert.Equal(expectedLabel, FilePreview.GetFileTypeLabel(path));
    }

    [Theory]
    [InlineData(".tmp")]
    [InlineData(".xyz")]
    [InlineData(".unknown")]
    public void GetFileTypeLabel_UnknownExtension_ReturnsNull(string extension)
    {
        string path = $"file{extension}";
        Assert.Null(FilePreview.GetFileTypeLabel(path));
    }

    [Theory]
    [InlineData("Dockerfile", "Docker")]
    [InlineData("Makefile", "Makefile")]
    [InlineData("Jenkinsfile", "Jenkinsfile")]
    public void GetFileTypeLabel_SpecialFilename_ReturnsLabel(string filename, string expectedLabel)
    {
        Assert.Equal(expectedLabel, FilePreview.GetFileTypeLabel(filename));
    }

    [Fact]
    public void DetectFileMetadata_CrlfText_ReturnsCrlfAndUtf8()
    {
        string tempFile = Path.GetTempFileName();
        try
        {
            File.WriteAllBytes(tempFile, "line1\r\nline2\r\nline3"u8.ToArray());
            var metadata = FilePreview.DetectFileMetadata(tempFile);
            Assert.False(metadata.IsBinary);
            Assert.Equal("UTF-8", metadata.Encoding);
            Assert.Equal("CRLF", metadata.LineEnding);
        }
        finally { File.Delete(tempFile); }
    }

    [Fact]
    public void DetectFileMetadata_LfText_ReturnsLfAndUtf8()
    {
        string tempFile = Path.GetTempFileName();
        try
        {
            File.WriteAllBytes(tempFile, "line1\nline2\nline3"u8.ToArray());
            var metadata = FilePreview.DetectFileMetadata(tempFile);
            Assert.False(metadata.IsBinary);
            Assert.Equal("UTF-8", metadata.Encoding);
            Assert.Equal("LF", metadata.LineEnding);
        }
        finally { File.Delete(tempFile); }
    }

    [Fact]
    public void DetectFileMetadata_MixedLineEndings_ReturnsMixed()
    {
        string tempFile = Path.GetTempFileName();
        try
        {
            File.WriteAllBytes(tempFile, "line1\r\nline2\nline3"u8.ToArray());
            var metadata = FilePreview.DetectFileMetadata(tempFile);
            Assert.False(metadata.IsBinary);
            Assert.Equal("Mixed", metadata.LineEnding);
        }
        finally { File.Delete(tempFile); }
    }

    [Fact]
    public void DetectFileMetadata_CrOnly_ReturnsCr()
    {
        string tempFile = Path.GetTempFileName();
        try
        {
            File.WriteAllBytes(tempFile, "line1\rline2\rline3"u8.ToArray());
            var metadata = FilePreview.DetectFileMetadata(tempFile);
            Assert.False(metadata.IsBinary);
            Assert.Equal("CR", metadata.LineEnding);
        }
        finally { File.Delete(tempFile); }
    }

    [Fact]
    public void DetectFileMetadata_SingleLine_ReturnsNullLineEnding()
    {
        string tempFile = Path.GetTempFileName();
        try
        {
            File.WriteAllBytes(tempFile, "no line endings here"u8.ToArray());
            var metadata = FilePreview.DetectFileMetadata(tempFile);
            Assert.False(metadata.IsBinary);
            Assert.Null(metadata.LineEnding);
        }
        finally { File.Delete(tempFile); }
    }

    [Fact]
    public void DetectFileMetadata_Utf8Bom_ReturnsUtf8BomEncoding()
    {
        string tempFile = Path.GetTempFileName();
        try
        {
            byte[] bom = [0xEF, 0xBB, 0xBF];
            byte[] content = "hello world"u8.ToArray();
            File.WriteAllBytes(tempFile, [.. bom, .. content]);
            var metadata = FilePreview.DetectFileMetadata(tempFile);
            Assert.False(metadata.IsBinary);
            Assert.Equal("UTF-8 BOM", metadata.Encoding);
        }
        finally { File.Delete(tempFile); }
    }

    [Fact]
    public void DetectFileMetadata_BinaryFile_ReturnsIsBinaryTrue()
    {
        string tempFile = Path.GetTempFileName();
        try
        {
            File.WriteAllBytes(tempFile, [0x48, 0x65, 0x6C, 0x00, 0x6F]);
            var metadata = FilePreview.DetectFileMetadata(tempFile);
            Assert.True(metadata.IsBinary);
            Assert.Null(metadata.LineEnding);
        }
        finally { File.Delete(tempFile); }
    }

}
