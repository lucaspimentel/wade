namespace Wade.Preview;

internal sealed class PdfMetadataProvider : IMetadataProvider
{
    internal static bool IsAvailable => CliTool.IsAvailable("pdfinfo", "-v");

    public string Label => "PDF metadata";

    public bool CanProvideMetadata(string path, PreviewContext context) =>
        !context.DisabledTools.Contains("pdfinfo")
        && string.Equals(Path.GetExtension(path), ".pdf", StringComparison.OrdinalIgnoreCase)
        && IsAvailable;

    public MetadataResult? GetMetadata(string path, PreviewContext context, CancellationToken ct)
    {
        if (ct.IsCancellationRequested)
        {
            return null;
        }

        try
        {
            string? output = CliTool.Run("pdfinfo", [path], ct: ct);

            if (output is null)
            {
                return null;
            }

            ct.ThrowIfCancellationRequested();

            MetadataSection[]? sections = ParsePdfInfoOutput(output);

            if (sections is null || sections.Length == 0)
            {
                return null;
            }

            return new MetadataResult
            {
                Sections = sections,
                FileTypeLabel = "PDF",
            };
        }
        catch (OperationCanceledException)
        {
            return null;
        }
        catch (IOException)
        {
            return null;
        }
    }

    // ── Output parsing ────────────────────────────────────────────────────────

    internal static MetadataSection[]? ParsePdfInfoOutput(string output)
    {
        if (string.IsNullOrWhiteSpace(output))
        {
            return null;
        }

        var entries = new List<MetadataEntry>();

        foreach (string line in output.Split('\n'))
        {
            int colonIndex = line.IndexOf(':');

            if (colonIndex < 0)
            {
                continue;
            }

            string key = line[..colonIndex].Trim();
            string value = line[(colonIndex + 1)..].Trim();

            if (string.IsNullOrEmpty(value))
            {
                continue;
            }

            string? label = key switch
            {
                "Title" => "Title",
                "Subject" => "Subject",
                "Keywords" => "Keywords",
                "Author" => "Author",
                "Creator" => "Creator",
                "Producer" => "Producer",
                "CreationDate" => "Created",
                "ModDate" => "Modified",
                "Pages" => "Pages",
                "Page size" => "Page size",
                "PDF version" => "PDF version",
                "Encrypted" => "Encrypted",
                _ => null,
            };

            if (label is null)
            {
                continue;
            }

            // Attempt to parse dates into a cleaner format
            if (key is "CreationDate" or "ModDate")
            {
                if (DateTimeOffset.TryParse(value, out DateTimeOffset dto))
                {
                    value = dto.ToString("yyyy-MM-dd HH:mm:ss zzz");
                }
            }

            entries.Add(new MetadataEntry(label, value));
        }

        return entries.Count > 0
            ? [new MetadataSection("PDF Document", entries.ToArray())]
            : null;
    }
}
