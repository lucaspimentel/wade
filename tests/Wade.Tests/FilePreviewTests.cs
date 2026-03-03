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

}
