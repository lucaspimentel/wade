using System.Globalization;
using System.Text;
using Wade.FileSystem;
using Wade.Highlighting;
using Wade.Preview;
using Wade.Terminal;
using Wade.UI;

namespace Wade.Tests;

/// <summary>
/// Preview parity golden: for every fixture in tests/golden/preview/files this
/// writes FilePreview's metadata and lines, both registries' provider order,
/// the text and hex provider output, and the rendered file metadata to
/// tests/golden/preview/preview.golden.txt. src/wade-rs/tests/preview_golden.rs
/// reproduces it.
/// </summary>
public class PreviewGoldenTests
{
    private const int HexHeadRows = 40;
    private const int HexTailRows = 3;

    [Fact]
    public void PreviewFixtures_MatchGolden()
    {
        string dir = FindGoldenDir();
        string filesDir = Path.Combine(dir, "files");
        var sb = new StringBuilder();

        string[] files = Directory.GetFiles(filesDir);
        Array.Sort(files, StringComparer.Ordinal);

        foreach (string path in files)
        {
            string name = Path.GetFileName(path);
            sb.Append("=== ").Append(name).Append('\n');

            FileMetadata metadata = FilePreview.DetectFileMetadata(path);
            sb.Append("metadata binary=").Append(metadata.IsBinary ? "true" : "false")
                .Append(" encoding=").Append(metadata.Encoding.Length > 0 ? metadata.Encoding : "-")
                .Append(" line-ending=").Append(metadata.LineEnding ?? "-").Append('\n');
            sb.Append("label ").Append(FilePreview.GetFileTypeLabel(path) ?? "-").Append('\n');

            string[] lines = FilePreview.GetPreviewLines(path, out FileMetadata previewMetadata);
            sb.Append("lines ").Append(lines.Length).Append(" placeholder=")
                .Append(previewMetadata.PlaceholderMessage ?? "-").Append('\n');
            foreach (string line in lines)
            {
                sb.Append("  ").Append(Escape(line)).Append('\n');
            }

            foreach ((string contextName, PreviewContext context) in Contexts(dir))
            {
                sb.Append("preview-providers[").Append(contextName).Append("] ")
                    .Append(string.Join(" | ", PreviewProviderRegistry.GetApplicableProviders(path, context).Select(p => p.Label)))
                    .Append('\n');
                sb.Append("metadata-providers[").Append(contextName).Append("] ")
                    .Append(string.Join(" | ", MetadataProviderRegistry.GetApplicableProviders(path, context).Select(p => p.Label)))
                    .Append('\n');
            }

            PreviewContext defaultContext = Contexts(dir)[0].Context;

            var text = new TextPreviewProvider();
            if (text.CanPreview(path, defaultContext))
            {
                AppendResult(sb, "text", text.GetPreview(path, defaultContext, CancellationToken.None), int.MaxValue);
            }

            AppendResult(sb, "hex", new HexPreviewProvider().GetPreview(path, defaultContext, CancellationToken.None), HexHeadRows);

            MetadataResult? fileInfo = new FileMetadataProvider().GetMetadata(path, defaultContext, CancellationToken.None);
            if (fileInfo is not null)
            {
                sb.Append("file-metadata\n");
                foreach (StyledLine line in MetadataRenderer.Render(fileInfo.Sections, 40))
                {
                    AppendStyledLine(sb, line);
                }
            }
        }

        string goldenPath = Path.Combine(dir, "preview.golden.txt");
        string actual = sb.ToString();

        if (Environment.GetEnvironmentVariable("WADE_UPDATE_GOLDENS") == "1" && !File.Exists(goldenPath))
        {
            File.WriteAllText(goldenPath, actual, new UTF8Encoding(false));
        }

        Assert.True(File.Exists(goldenPath), $"Missing golden file {goldenPath}; run with WADE_UPDATE_GOLDENS=1 to generate");
        Assert.Equal(File.ReadAllText(goldenPath).ReplaceLineEndings("\n"), actual);
    }

    [Fact]
    public void MetadataRendererCases_MatchGolden()
    {
        // Mixed headers, label widths, list items and divider widths
        MetadataSection[] sections =
        [
            new("photo.jpg", [new("Size", "1.2 MB"), new("Git", "Modified")]),
            new(null, [new("Resolution", "4000 x 3000"), new("", "list item value")]),
            new("Empty section", []),
            new("A much longer section header than the pane", [new("K", "V")]),
        ];

        var sb = new StringBuilder();
        foreach (int width in new[] { 0, 3, 4, 12, 40 })
        {
            sb.Append("=== width ").Append(width).Append('\n');
            foreach (StyledLine line in MetadataRenderer.Render(sections, width))
            {
                AppendStyledLine(sb, line);
            }
        }

        string goldenPath = Path.Combine(FindGoldenDir(), "metadata-renderer.golden.txt");
        string actual = sb.ToString();

        if (Environment.GetEnvironmentVariable("WADE_UPDATE_GOLDENS") == "1" && !File.Exists(goldenPath))
        {
            File.WriteAllText(goldenPath, actual, new UTF8Encoding(false));
        }

        Assert.True(File.Exists(goldenPath), $"Missing golden file {goldenPath}; run with WADE_UPDATE_GOLDENS=1 to generate");
        Assert.Equal(File.ReadAllText(goldenPath).ReplaceLineEndings("\n"), actual);
    }

    [Fact]
    public void ArchiveFixtures_MatchGolden()
    {
        // The app runs with InvariantGlobalization; ratios use P0 ("45 %")
        CultureInfo previousCulture = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;

        try
        {
            string dir = FindGoldenDir();
            var sb = new StringBuilder();

            string[] files = Directory.GetFiles(Path.Combine(dir, "archives"));
            Array.Sort(files, StringComparer.Ordinal);

            foreach (string path in files)
            {
                sb.Append("=== ").Append(Path.GetFileName(path)).Append('\n');
                sb.Append("kind zip=").Append(ZipPreview.IsZipFile(path) ? "true" : "false")
                    .Append(" primary=").Append(ZipPreview.IsPrimaryArchive(path) ? "true" : "false")
                    .Append(" tar=").Append(TarPreview.IsTarArchive(path) ? "true" : "false")
                    .Append(" gzip=").Append(TarPreview.IsPlainGzip(path) ? "true" : "false").Append('\n');

                foreach ((string contextName, PreviewContext context) in Contexts(dir))
                {
                    if (contextName is "default" or "archive-metadata-off")
                    {
                        sb.Append("preview-providers[").Append(contextName).Append("] ")
                            .Append(string.Join(" | ", PreviewProviderRegistry.GetApplicableProviders(path, context).Select(p => p.Label)))
                            .Append('\n');
                        sb.Append("metadata-providers[").Append(contextName).Append("] ")
                            .Append(string.Join(" | ", MetadataProviderRegistry.GetApplicableProviders(path, context).Select(p => p.Label)))
                            .Append('\n');
                    }
                }

                PreviewContext defaultContext = Contexts(dir)[0].Context;

                foreach (IPreviewProvider provider in new IPreviewProvider[] { new ZipContentsPreviewProvider(), new TarContentsPreviewProvider() })
                {
                    if (provider.CanPreview(path, defaultContext))
                    {
                        AppendResult(sb, "archive", provider.GetPreview(path, defaultContext, CancellationToken.None), int.MaxValue);
                    }
                }

                var archiveMetadata = new ArchiveMetadataProvider();
                if (archiveMetadata.CanProvideMetadata(path, defaultContext))
                {
                    MetadataResult? result = archiveMetadata.GetMetadata(path, defaultContext, CancellationToken.None);
                    if (result is null)
                    {
                        sb.Append("archive-metadata null\n");
                    }
                    else
                    {
                        sb.Append("archive-metadata label=").Append(result.FileTypeLabel ?? "-").Append('\n');
                        foreach (MetadataSection section in result.Sections)
                        {
                            sb.Append("  [").Append(section.Header ?? "-").Append("]\n");
                            foreach (MetadataEntry entry in section.Entries)
                            {
                                sb.Append("  ").Append(entry.Label).Append(": ").Append(entry.Value).Append('\n');
                            }
                        }
                    }
                }
            }

            // The P0 ratio format shared by the zip listing and archive metadata
            sb.Append("=== ratios\n");
            foreach ((long compressed, long total) in RatioCases)
            {
                sb.Append(compressed).Append('/').Append(total).Append(' ')
                    .Append($"{(double)compressed / total:P0}").Append('\n');
            }

            string goldenPath = Path.Combine(dir, "archives.golden.txt");
            string actual = sb.ToString();

            if (Environment.GetEnvironmentVariable("WADE_UPDATE_GOLDENS") == "1" && !File.Exists(goldenPath))
            {
                File.WriteAllText(goldenPath, actual, new UTF8Encoding(false));
            }

            Assert.True(File.Exists(goldenPath), $"Missing golden file {goldenPath}; run with WADE_UPDATE_GOLDENS=1 to generate");
            Assert.Equal(File.ReadAllText(goldenPath).ReplaceLineEndings("\n"), actual);
        }
        finally
        {
            CultureInfo.CurrentCulture = previousCulture;
        }
    }

    private static readonly (long Compressed, long Total)[] RatioCases =
    [
        (0, 5), (1, 8), (3, 8), (5, 8), (7, 8), (1, 200), (3, 200), (1, 3), (2, 3), (5, 5), (7, 5),
        (1, 1000), (5, 1000), (15, 1000), (25, 1000), (1005, 100000), (99995, 100000), (123456789, 1000000),
        (4999, 1000000), (5, 1000000000), (long.MaxValue / 3, long.MaxValue / 7),
    ];

    /// <summary>
    /// Registry contexts: config defaults with image/sixel off (as on a
    /// terminal without Sixel), plus a git-modified file and a cloud
    /// placeholder.
    /// </summary>
    private static List<(string Name, PreviewContext Context)> Contexts(string repoRoot)
    {
        var baseContext = new PreviewContext(
            PaneWidthCells: 80,
            PaneHeightCells: 24,
            CellPixelWidth: 8,
            CellPixelHeight: 16,
            IsCloudPlaceholder: false,
            IsBrokenSymlink: false,
            GitStatus: null,
            RepoRoot: null,
            PdfPreviewEnabled: true,
            PdfMetadataEnabled: true,
            MarkdownPreviewEnabled: true,
            FfprobeEnabled: true,
            MediainfoEnabled: true,
            ZipPreviewEnabled: true,
            ImagePreviewsEnabled: false,
            SixelSupported: false,
            ArchiveMetadataEnabled: true);

        return
        [
            ("default", baseContext),
            ("git-modified", baseContext with { GitStatus = GitFileStatus.Modified, RepoRoot = repoRoot }),
            ("cloud", baseContext with { IsCloudPlaceholder = true }),
            ("archive-metadata-off", baseContext with { ArchiveMetadataEnabled = false, ZipPreviewEnabled = false }),
        ];
    }

    private static void AppendResult(StringBuilder sb, string name, PreviewResult? result, int headRows)
    {
        if (result is null)
        {
            sb.Append(name).Append(" null\n");
            return;
        }

        StyledLine[] lines = result.TextLines ?? [];
        sb.Append(name).Append(" label=").Append(result.FileTypeLabel ?? "-")
            .Append(" rendered=").Append(result.IsRendered ? "true" : "false")
            .Append(" placeholder=").Append(result.IsPlaceholder ? "true" : "false")
            .Append(" lines=").Append(lines.Length).Append('\n');

        for (int i = 0; i < lines.Length; i++)
        {
            // Long outputs (hex dumps of big files): head and tail only
            if (i >= headRows && i < lines.Length - HexTailRows)
            {
                continue;
            }

            AppendStyledLine(sb, lines[i]);
        }
    }

    private static void AppendStyledLine(StringBuilder sb, StyledLine line)
    {
        sb.Append("text ").Append(Escape(line.Text)).Append('\n');

        if (line.Spans is { } spans)
        {
            sb.Append("spans");
            foreach (StyledSpan span in spans)
            {
                sb.Append(' ').Append(span.Start).Append('+').Append(span.Length).Append(':').Append(span.Kind);
            }

            sb.Append('\n');
        }

        if (line.CharStyles is { } styles)
        {
            sb.Append("chars");
            int i = 0;
            while (i < styles.Length)
            {
                int j = i + 1;
                while (j < styles.Length && styles[j] == styles[i])
                {
                    j++;
                }

                sb.Append(' ').Append(i).Append('+').Append(j - i).Append(':').Append(FormatStyle(styles[i]));
                i = j;
            }

            sb.Append('\n');
        }
    }

    private static string FormatStyle(CellStyle style)
    {
        static string FormatColor(Color? color) =>
            color is { } c ? string.Create(CultureInfo.InvariantCulture, $"{c.R},{c.G},{c.B}") : "-";

        var flags = new StringBuilder();
        if (style.Bold) flags.Append('b');
        if (style.Dim) flags.Append('d');
        if (style.Inverse) flags.Append('i');
        if (style.Underline) flags.Append('u');
        if (style.Strikethrough) flags.Append('s');

        return $"{FormatColor(style.Fg)}/{FormatColor(style.Bg)}/{(flags.Length > 0 ? flags : "-")}";
    }

    /// <summary>Escapes backslash and non-printable characters as \uXXXX.</summary>
    private static string Escape(string text)
    {
        var sb = new StringBuilder();
        foreach (char ch in text)
        {
            if (ch == '\\')
            {
                sb.Append(@"\\");
            }
            else if (ch < ' ' || ch == '\u007f')
            {
                sb.Append(string.Create(CultureInfo.InvariantCulture, $"\\u{(int)ch:x4}"));
            }
            else
            {
                sb.Append(ch);
            }
        }

        return sb.ToString();
    }

    private static string FindGoldenDir()
    {
        string? dir = AppContext.BaseDirectory;
        while (dir is not null)
        {
            string candidate = Path.Combine(dir, "tests", "golden", "preview");
            if (Directory.Exists(candidate))
            {
                return candidate;
            }

            dir = Path.GetDirectoryName(dir.TrimEnd(Path.DirectorySeparatorChar));
        }

        throw new InvalidOperationException("Could not locate tests/golden/preview directory");
    }
}
