using System.Text;
using Wade.FileSystem;
using Wade.UI;

namespace Wade.Tests;

public class FileIconsTests
{
    private static FileSystemEntry Dir(string name) =>
        new(name, $@"C:\{name}", IsDirectory: true, Size: 0, LastModified: default);

    private static FileSystemEntry Drive(string name) =>
        new(name, $@"{name}\", IsDirectory: true, Size: 0, LastModified: default, IsDrive: true);

    private static FileSystemEntry File(string name) =>
        new(name, $@"C:\{name}", IsDirectory: false, Size: 0, LastModified: default);

    [Fact]
    public void Directory_ReturnsFolderIcon()
    {
        Assert.Equal(new Rune(0xF114), FileIcons.GetIcon(Dir("src")));
    }

    [Fact]
    public void Drive_ReturnsDriveIcon()
    {
        Assert.Equal(new Rune(0xF0A0), FileIcons.GetIcon(Drive("C:")));
    }

    [Theory]
    // MDI language icons — supplementary plane (NF v3 codepoints)
    [InlineData("Program.cs",      0xF031B)] // nf-md-language_csharp
    [InlineData("app.js",          0xF031E)] // nf-md-language_javascript
    [InlineData("app.ts",          0xF06E6)] // nf-md-language_typescript
    [InlineData("main.py",         0xF0320)] // nf-md-language_python
    [InlineData("main.go",         0xF07D3)] // nf-md-language_go
    [InlineData("lib.rs",          0xF1617)] // nf-md-language_rust
    [InlineData("Main.java",       0xF0B37)] // nf-md-language_java
    [InlineData("Main.kt",         0xF1219)] // nf-md-language_kotlin
    // BMP icons unchanged
    [InlineData("index.html",      0xF13B)]  // nf-fa-html5
    [InlineData("styles.css",      0xF031C)] // nf-md-language_css3
    [InlineData("data.json",       0xF1C9)]
    [InlineData("config.toml",     0xF1C9)]
    [InlineData("pipeline.yml",    0xF1C9)]
    [InlineData("README.md",       0xF48A)]  // nf-oct-markdown
    [InlineData("notes.txt",       0xF15C)]
    [InlineData("report.pdf",      0xF1C1)]
    [InlineData("deploy.sh",       0xF489)]
    [InlineData("build.ps1",       0xF489)]
    [InlineData("setup.bat",       0xF17A)]
    [InlineData("App.sln",         0xF0610)] // nf-md-microsoft_visual_studio
    [InlineData("Wade.csproj",     0xF0610)] // nf-md-microsoft_visual_studio
    [InlineData("photo.png",       0xF1C5)]
    [InlineData("image.jpg",       0xF1C5)]
    [InlineData("archive.zip",     0xF1C6)]
    [InlineData("release.tar",     0xF1C6)]
    [InlineData("app.exe",         0xF17A)]
    [InlineData("library.dll",     0xF17A)]
    public void KnownExtension_ReturnsExpectedIcon(string filename, int expectedCodepoint)
    {
        Assert.Equal(new Rune(expectedCodepoint), FileIcons.GetIcon(File(filename)));
    }

    [Theory]
    [InlineData("CS",   0xF031B)] // case-insensitive: .CS → nf-md-language_csharp
    [InlineData("JSON", 0xF1C9)]
    public void Extension_IsCaseInsensitive(string ext, int expectedCodepoint)
    {
        Assert.Equal(new Rune(expectedCodepoint), FileIcons.GetIcon(File($"file.{ext}")));
    }

    [Theory]
    [InlineData("file.xyz")]
    [InlineData("file.abc")]
    [InlineData("noextension")]
    public void UnknownExtension_ReturnsFallbackFileIcon(string filename)
    {
        Assert.Equal(new Rune(0xF15B), FileIcons.GetIcon(File(filename)));
    }

    [Theory]
    [InlineData("Dockerfile")]
    [InlineData("dockerfile")]
    public void Dockerfile_WithoutExtension_ReturnsDockerIcon(string filename)
    {
        Assert.Equal(new Rune(0xF308), FileIcons.GetIcon(File(filename)));
    }

    [Theory]
    [InlineData(".gitignore")]
    [InlineData(".gitattributes")]
    public void GitFiles_ReturnGitIcon(string filename)
    {
        Assert.Equal(new Rune(0xF1D3), FileIcons.GetIcon(File(filename)));
    }
}
