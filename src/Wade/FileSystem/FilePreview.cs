using System.Collections.Frozen;
using System.Text;

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
            ".zip", ".tar", ".gz", ".7z", ".rar",
            ".nupkg", ".snupkg", ".jar", ".war", ".ear",
            ".docx", ".xlsx", ".pptx", ".odt", ".ods", ".odp",
            ".apk", ".vsix", ".whl", ".epub",
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
            [".jsx"] = "React JSX",
            [".tsx"] = "React TSX",
            [".vue"] = "Vue",
            [".svelte"] = "Svelte",
            // Data/Config
            [".json"] = "JSON",
            [".toml"] = "TOML",
            [".yaml"] = "YAML",
            [".yml"] = "YAML",
            [".xml"] = "XML",
            [".xsd"] = "XML Schema Definition",
            [".ini"] = "INI",
            [".url"] = "URL Shortcut",
            [".env"] = "Environment Variables",
            [".csv"] = "Comma-Separated Values",
            [".tsv"] = "Tab-Separated Values",
            [".sql"] = "SQL Script",
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
            [".svg"] = "SVG Image",
            [".webp"] = "Image",
            [".ico"] = "Icon",
            [".bmp"] = "Image",
            [".pdn"] = "Paint.NET Image",
            // Archives
            [".zip"] = "Archive",
            [".tar"] = "Archive",
            [".gz"] = "Archive",
            [".7z"] = "Archive",
            [".rar"] = "Archive",
            [".nupkg"] = "NuGet Package",
            [".snupkg"] = "NuGet Symbols Package",
            [".jar"] = "Java Archive",
            [".war"] = "Java Web Archive",
            [".ear"] = "Java Enterprise Archive",
            [".docx"] = "Word Document",
            [".xlsx"] = "Excel Spreadsheet",
            [".pptx"] = "PowerPoint",
            [".odt"] = "OpenDocument Text",
            [".ods"] = "OpenDocument Spreadsheet",
            [".odp"] = "OpenDocument Presentation",
            [".apk"] = "Android Package",
            [".vsix"] = "VS Extension",
            [".whl"] = "Python Wheel",
            [".epub"] = "eBook",
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
                metadata = metadata with { PlaceholderMessage = "[binary file]" };
                return ["[binary file]"];
            }

            var lines = new List<string>(MaxPreviewLines);
            Encoding? readerEncoding = metadata.Encoding switch
            {
                "UTF-16 LE" => Encoding.Unicode,
                "UTF-16 BE" => Encoding.BigEndianUnicode,
                _ => null,
            };

            using var reader = readerEncoding is not null
                ? new StreamReader(filePath, readerEncoding)
                : new StreamReader(filePath);

            while (lines.Count < MaxPreviewLines && reader.ReadLine() is { } line)
            {
                // Replace tabs with spaces for display
                lines.Add(line.Replace("\t", "    "));
            }

            if (lines.Count == 0)
            {
                metadata = metadata with { PlaceholderMessage = "[empty file]" };
                return ["[empty file]"];
            }

            return [.. lines];
        }
        catch (UnauthorizedAccessException)
        {
            metadata = new FileMetadata(IsBinary: false, Encoding: "UTF-8", LineEnding: null, PlaceholderMessage: "[access denied]");
            return ["[access denied]"];
        }
        catch (IOException ex)
        {
            string message = $"[error: {ex.Message}]";
            metadata = new FileMetadata(IsBinary: false, Encoding: "UTF-8", LineEnding: null, PlaceholderMessage: message);
            return [message];
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
            using FileStream stream = File.OpenRead(filePath);
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
                // UTF-16 LE BOM — unambiguously text; skip null-byte scan
                // (UTF-16 content naturally contains 0x00 bytes)
                return new FileMetadata(IsBinary: false, Encoding: "UTF-16 LE", LineEnding: null);
            }
            else if (bytesRead >= 2 && buffer[0] == 0xFE && buffer[1] == 0xFF)
            {
                // UTF-16 BE BOM — unambiguously text; skip null-byte scan
                return new FileMetadata(IsBinary: false, Encoding: "UTF-16 BE", LineEnding: null);
            }

            // BOM-less UTF-16 heuristic: only when no BOM was detected
            if (dataStart == 0 && TryDetectBomlessUtf16(buffer, out string bomlessEncoding))
            {
                return new FileMetadata(IsBinary: false, Encoding: bomlessEncoding, LineEnding: null);
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
                _ => "Mixed",
            };

            return new FileMetadata(IsBinary: false, Encoding: encoding, LineEnding: lineEnding);
        }
        catch
        {
            return new FileMetadata(IsBinary: true, Encoding: "", LineEnding: null);
        }
    }

    internal static bool IsBinary(string filePath) => DetectFileMetadata(filePath).IsBinary;

    private static bool TryDetectBomlessUtf16(ReadOnlySpan<byte> buffer, out string encoding)
    {
        encoding = "";

        // Need at least 4 code units (8 bytes) for a reliable heuristic
        if (buffer.Length < 8)
            return false;

        // Analyze only complete code units (even byte count)
        int analysisLength = buffer.Length & ~1;
        int pairCount = analysisLength / 2;

        int evenNullCount = 0; // nulls at positions 0, 2, 4, ...
        int oddNullCount = 0;  // nulls at positions 1, 3, 5, ...

        for (int i = 0; i < analysisLength; i += 2)
        {
            if (buffer[i] == 0) evenNullCount++;
            if (buffer[i + 1] == 0) oddNullCount++;
        }

        int highThreshold = pairCount * 80 / 100;
        int lowThreshold = pairCount * 10 / 100;

        // UTF-16 LE: high byte (odd position) is mostly null, low byte (even) mostly non-null
        if (oddNullCount >= highThreshold && evenNullCount <= lowThreshold)
        {
            encoding = "UTF-16 LE";
            return true;
        }

        // UTF-16 BE: high byte (even position) is mostly null, low byte (odd) mostly non-null
        if (evenNullCount >= highThreshold && oddNullCount <= lowThreshold)
        {
            encoding = "UTF-16 BE";
            return true;
        }

        return false;
    }
}

internal readonly record struct FileMetadata(bool IsBinary, string Encoding, string? LineEnding, string? PlaceholderMessage = null);
