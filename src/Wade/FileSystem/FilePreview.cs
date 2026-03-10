using System.Collections.Frozen;

namespace Wade.FileSystem;

internal static class FilePreview
{
    private const int MaxPreviewLines = 100;
    private const int BinaryCheckSize = 512;

    private static readonly FrozenSet<string> s_binaryExtensions =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            // Images
            ".png", ".jpg", ".jpeg", ".gif", ".svg", ".webp", ".ico", ".bmp", ".tga", ".tiff", ".pbm", ".pdn",
            // Documents
            ".pdf",
            // Archives
            ".zip", ".tar", ".gz", ".7z", ".rar", ".nupkg",
            // Binaries
            ".exe", ".dll", ".so", ".dylib", ".pdb", ".wasm",
        }.ToFrozenSet(StringComparer.OrdinalIgnoreCase);

    private static readonly FrozenDictionary<string, string> s_extensionLabels =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            // Programming
            [".cs"] = "C#",
            [".py"] = "Python",
            [".js"] = "JavaScript",
            [".ts"] = "TypeScript",
            [".go"] = "Go",
            [".rs"] = "Rust",
            [".java"] = "Java",
            [".kt"] = "Kotlin",
            [".cpp"] = "C++",
            [".c"] = "C",
            [".h"] = "C/C++ Header",
            [".rb"] = "Ruby",
            [".php"] = "PHP",
            [".swift"] = "Swift",
            [".fs"] = "F#",
            [".vb"] = "Visual Basic",
            [".lua"] = "Lua",
            [".r"] = "R",
            [".scala"] = "Scala",
            [".ex"] = "Elixir",
            [".exs"] = "Elixir",
            [".erl"] = "Erlang",
            [".hs"] = "Haskell",
            [".ml"] = "OCaml",
            // Web
            [".html"] = "HTML",
            [".htm"] = "HTML",
            [".css"] = "CSS",
            [".scss"] = "SCSS",
            [".sass"] = "Sass",
            [".jsx"] = "JSX",
            [".tsx"] = "TSX",
            [".vue"] = "Vue",
            [".svelte"] = "Svelte",
            // Data/Config
            [".json"] = "JSON",
            [".toml"] = "TOML",
            [".yaml"] = "YAML",
            [".yml"] = "YAML",
            [".xml"] = "XML",
            [".ini"] = "INI",
            [".env"] = "Env",
            [".csv"] = "CSV",
            [".tsv"] = "TSV",
            [".sql"] = "SQL",
            // Docs
            [".md"] = "Markdown",
            [".txt"] = "Text",
            [".rst"] = "reStructuredText",
            [".pdf"] = "PDF",
            [".tex"] = "LaTeX",
            // Shell
            [".sh"] = "Shell",
            [".bash"] = "Shell",
            [".zsh"] = "Shell",
            [".fish"] = "Shell",
            [".ps1"] = "PowerShell",
            [".psm1"] = "PowerShell",
            [".psd1"] = "PowerShell",
            [".bat"] = "Batch",
            [".cmd"] = "Batch",
            // Project
            [".sln"] = "Visual Studio Solution",
            [".slnx"] = "Visual Studio Solution",
            [".csproj"] = "C# Project",
            [".fsproj"] = "F# Project",
            [".vbproj"] = "VB Project",
            [".props"] = "MSBuild",
            [".targets"] = "MSBuild",
            // Images
            [".png"] = "Image",
            [".jpg"] = "Image",
            [".jpeg"] = "Image",
            [".gif"] = "Image",
            [".svg"] = "Image",
            [".webp"] = "Image",
            [".ico"] = "Image",
            [".bmp"] = "Image",
            [".pdn"] = "Image",
            // Archives
            [".zip"] = "Archive",
            [".tar"] = "Archive",
            [".gz"] = "Archive",
            [".7z"] = "Archive",
            [".rar"] = "Archive",
            [".nupkg"] = "Archive",
            // Binaries
            [".exe"] = "Executable",
            [".dll"] = "Library",
            [".so"] = "Shared Library",
            [".dylib"] = "Shared Library",
            [".pdb"] = "Debug Symbols",
            [".wasm"] = "WebAssembly",
        }.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);

    private static readonly FrozenDictionary<string, string> s_filenameLabels =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["Dockerfile"] = "Docker",
            ["Makefile"] = "Makefile",
            ["Jenkinsfile"] = "Jenkinsfile",
            ["Brewfile"] = "Brewfile",
            ["Gemfile"] = "Ruby",
            ["Rakefile"] = "Ruby",
            ["Procfile"] = "Procfile",
        }.ToFrozenDictionary(StringComparer.Ordinal);

    internal static string? GetFileTypeLabel(string filePath)
    {
        string filename = Path.GetFileName(filePath);
        if (s_filenameLabels.TryGetValue(filename, out string? filenameLabel))
        {
            return filenameLabel;
        }

        string ext = Path.GetExtension(filePath);
        if (ext.Length > 0 && s_extensionLabels.TryGetValue(ext, out string? extLabel))
        {
            return extLabel;
        }

        return null;
    }

    public static string[] GetPreviewLines(string filePath) => GetPreviewLines(filePath, out _);

    public static string[] GetPreviewLines(string filePath, out FileMetadata metadata)
    {
        try
        {
            metadata = DetectFileMetadata(filePath);
            if (metadata.IsBinary)
            {
                return ["[binary file]"];
            }

            var lines = new List<string>(MaxPreviewLines);
            using var reader = new StreamReader(filePath);

            while (lines.Count < MaxPreviewLines && reader.ReadLine() is { } line)
            {
                // Replace tabs with spaces for display
                lines.Add(line.Replace("\t", "    "));
            }

            return lines.Count > 0 ? [.. lines] : ["[empty file]"];
        }
        catch (UnauthorizedAccessException)
        {
            metadata = new FileMetadata(IsBinary: false, Encoding: "UTF-8", LineEnding: null);
            return ["[access denied]"];
        }
        catch (IOException ex)
        {
            metadata = new FileMetadata(IsBinary: false, Encoding: "UTF-8", LineEnding: null);
            return [$"[error: {ex.Message}]"];
        }
    }

    internal static FileMetadata DetectFileMetadata(string filePath)
    {
        try
        {
            // Known binary extensions — skip byte scan
            string ext = Path.GetExtension(filePath);
            if (ext.Length > 0 && s_binaryExtensions.Contains(ext))
            {
                return new FileMetadata(IsBinary: true, Encoding: "", LineEnding: null);
            }

            Span<byte> buffer = stackalloc byte[BinaryCheckSize];
            using var stream = File.OpenRead(filePath);
            int bytesRead = stream.Read(buffer);

            if (bytesRead == 0)
            {
                return new FileMetadata(IsBinary: false, Encoding: "UTF-8", LineEnding: null);
            }

            buffer = buffer[..bytesRead];

            // Detect encoding via BOM
            string encoding = "UTF-8";
            int dataStart = 0;
            if (bytesRead >= 3 && buffer[0] == 0xEF && buffer[1] == 0xBB && buffer[2] == 0xBF)
            {
                encoding = "UTF-8 BOM";
                dataStart = 3;
            }
            else if (bytesRead >= 2 && buffer[0] == 0xFF && buffer[1] == 0xFE)
            {
                encoding = "UTF-16 LE";
                dataStart = 2;
            }
            else if (bytesRead >= 2 && buffer[0] == 0xFE && buffer[1] == 0xFF)
            {
                encoding = "UTF-16 BE";
                dataStart = 2;
            }

            // Scan for null bytes (binary) and line endings
            bool hasCrLf = false;
            bool hasLf = false;
            bool hasCr = false;

            for (int i = dataStart; i < bytesRead; i++)
            {
                if (buffer[i] == 0)
                {
                    return new FileMetadata(IsBinary: true, Encoding: "", LineEnding: null);
                }

                if (buffer[i] == '\r')
                {
                    if (i + 1 < bytesRead && buffer[i + 1] == '\n')
                    {
                        hasCrLf = true;
                        i++; // skip the \n
                    }
                    else
                    {
                        hasCr = true;
                    }
                }
                else if (buffer[i] == '\n')
                {
                    hasLf = true;
                }
            }

            string? lineEnding = (hasCrLf, hasLf, hasCr) switch
            {
                (true, false, false) => "CRLF",
                (false, true, false) => "LF",
                (false, false, true) => "CR",
                (false, false, false) => null,
                _ => "Mixed"
            };

            return new FileMetadata(IsBinary: false, Encoding: encoding, LineEnding: lineEnding);
        }
        catch
        {
            return new FileMetadata(IsBinary: true, Encoding: "", LineEnding: null);
        }
    }

    internal static bool IsBinary(string filePath) => DetectFileMetadata(filePath).IsBinary;
}

internal readonly record struct FileMetadata(bool IsBinary, string Encoding, string? LineEnding);
