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
            var (lines, _) = FilePreview.GetPreviewLines(tempFile);

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
    public void GetPreviewLines_TextFile_WithoutFileCommand_ReturnsZeroHeaderLineCount()
    {
        // Initialize() is not called, so file command is unavailable — header count must be 0
        string tempFile = Path.GetTempFileName();
        try
        {
            File.WriteAllText(tempFile, "line1\nline2\n");
            var (_, headerLineCount) = FilePreview.GetPreviewLines(tempFile);
            Assert.Equal(0, headerLineCount);
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Fact]
    public void GetPreviewLines_BinaryFile_ReturnsZeroHeaderLineCount()
    {
        string tempFile = Path.GetTempFileName();
        try
        {
            File.WriteAllBytes(tempFile, [0x48, 0x65, 0x6C, 0x00, 0x6F]);
            var (_, headerLineCount) = FilePreview.GetPreviewLines(tempFile);
            Assert.Equal(0, headerLineCount);
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
            var (lines, _) = FilePreview.GetPreviewLines(tempFile);
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
            var (lines, _) = FilePreview.GetPreviewLines(tempFile);
            Assert.Single(lines);
            Assert.Equal("[binary file]", lines[0]);
        }
        finally
        {
            File.Delete(tempFile);
        }
    }
}
