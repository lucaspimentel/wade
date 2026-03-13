using System.Collections.Frozen;
using System.Text;
using Wade.FileSystem;

namespace Wade.UI;

internal static class FileIcons
{
    private static readonly Rune FolderIcon      = new(0xF114);  // nf-fa-folder
    private static readonly Rune DriveIcon       = new(0xF0A0);  // nf-fa-hdd_o
    private static readonly Rune FileIcon        = new(0xF15B);  // nf-fa-file
    private static readonly Rune SymlinkDirIcon  = new(0xF482);  // nf-oct-file_symlink_directory
    private static readonly Rune SymlinkFileIcon = new(0xF481);  // nf-oct-file_symlink_file

    private static readonly FrozenDictionary<string, Rune> ExtensionIcons =
        new Dictionary<string, Rune>(StringComparer.OrdinalIgnoreCase)
        {
            // .NET / C#
            [".cs"]          = new(0xF031B), // nf-md-language_csharp
            [".csx"]         = new(0xF031B),
            [".sln"]         = new(0xF0610), // nf-md-microsoft_visual_studio
            [".slnx"]        = new(0xF0610),
            [".csproj"]      = new(0xF0610),
            [".fsproj"]      = new(0xF0610),
            [".vbproj"]      = new(0xF0610),

            // Web
            [".html"]        = new(0xF13B),  // nf-fa-html5
            [".htm"]         = new(0xF13B),
            [".css"]         = new(0xF031C), // nf-md-language_css3
            [".scss"]        = new(0xF031C),
            [".sass"]        = new(0xF031C),

            // JavaScript / TypeScript
            [".js"]          = new(0xF031E), // nf-md-language_javascript
            [".mjs"]         = new(0xF031E),
            [".cjs"]         = new(0xF031E),
            [".ts"]          = new(0xF06E6), // nf-md-language_typescript
            [".tsx"]         = new(0xF06E6),
            [".jsx"]         = new(0xF031E),

            // Python
            [".py"]          = new(0xF0320), // nf-md-language_python

            // Go
            [".go"]          = new(0xF07D3), // nf-md-language_go

            // Rust
            [".rs"]          = new(0xF1617), // nf-md-language_rust

            // Java / JVM
            [".java"]        = new(0xF0B37), // nf-md-language_java
            [".kt"]          = new(0xF1219), // nf-md-language_kotlin
            [".gradle"]      = new(0xF0AD),  // nf-fa-wrench

            // Data / Config
            [".json"]        = new(0xF1C9),  // nf-fa-file_code_o (close enough)
            [".toml"]        = new(0xF1C9),
            [".yaml"]        = new(0xF1C9),
            [".yml"]         = new(0xF1C9),
            [".xml"]         = new(0xF1C9),
            [".ini"]         = new(0xF1C9),
            [".env"]         = new(0xF462),  // nf-oct-key

            // Docs
            [".md"]          = new(0xF48A),  // nf-oct-markdown
            [".markdown"]    = new(0xF48A),
            [".txt"]         = new(0xF15C),  // nf-fa-file_text_o
            [".rst"]         = new(0xF15C),
            [".pdf"]         = new(0xF1C1),  // nf-fa-file_pdf_o

            // Shell / Scripts
            [".sh"]          = new(0xF489),  // nf-oct-terminal
            [".bash"]        = new(0xF489),
            [".zsh"]         = new(0xF489),
            [".fish"]        = new(0xF489),
            [".ps1"]         = new(0xF489),
            [".psm1"]        = new(0xF489),
            [".psd1"]        = new(0xF489),
            [".bat"]         = new(0xF17A),  // nf-fa-windows
            [".cmd"]         = new(0xF17A),

            // Docker / CI
            [".dockerfile"]  = new(0xF308),  // nf-linux-docker
            [".dockerignore"] = new(0xF308),
            [".gitignore"]   = new(0xF1D3),  // nf-fa-git
            [".gitattributes"] = new(0xF1D3),

            // Images
            [".png"]         = new(0xF1C5),  // nf-fa-file_image_o
            [".jpg"]         = new(0xF1C5),
            [".jpeg"]        = new(0xF1C5),
            [".gif"]         = new(0xF1C5),
            [".svg"]         = new(0xF1C5),
            [".webp"]        = new(0xF1C5),
            [".ico"]         = new(0xF1C5),

            // Archives
            [".zip"]         = new(0xF1C6),  // nf-fa-file_archive_o
            [".tar"]         = new(0xF1C6),
            [".gz"]          = new(0xF1C6),
            [".bz2"]         = new(0xF1C6),
            [".xz"]          = new(0xF1C6),
            [".7z"]          = new(0xF1C6),
            [".rar"]         = new(0xF1C6),

            // Binaries
            [".exe"]         = new(0xF17A),  // nf-fa-windows
            [".dll"]         = new(0xF17A),
            [".so"]          = new(0xF17A),
            [".dylib"]       = new(0xF17A),
            [".pdb"]         = new(0xF188),  // nf-fa-bug
        }.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);

    public static Rune GetIcon(FileSystemEntry entry)
    {
        if (entry.IsDrive)
        {
            return DriveIcon;
        }

        if (entry.IsSymlink)
        {
            return entry.IsDirectory ? SymlinkDirIcon : SymlinkFileIcon;
        }

        if (entry.IsDirectory)
        {
            return FolderIcon;
        }

        var ext = Path.GetExtension(entry.Name);
        if (ext.Length > 0 && ExtensionIcons.TryGetValue(ext, out var icon))
        {
            return icon;
        }

        // Special filenames without extension
        var name = entry.Name;
        if (name.Equals("Dockerfile", StringComparison.OrdinalIgnoreCase))
        {
            return new Rune(0xF308);
        }

        if (name.Equals(".gitignore", StringComparison.OrdinalIgnoreCase) ||
            name.Equals(".gitattributes", StringComparison.OrdinalIgnoreCase))
        {
            return new Rune(0xF1D3);
        }

        return FileIcon;
    }
}
