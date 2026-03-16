using System.Diagnostics;

namespace Wade.Preview;

internal sealed class PdfMetadataProvider : IMetadataProvider
{
    private static readonly Lazy<bool> s_isAvailable = new(CheckAvailability);

    public string Label => "PDF metadata";

    internal static bool IsAvailable => s_isAvailable.Value;

    public bool CanProvideMetadata(string path, PreviewContext context) =>
        string.Equals(Path.GetExtension(path), ".pdf", StringComparison.OrdinalIgnoreCase) && IsAvailable;

    public MetadataResult? GetMetadata(string path, PreviewContext context, CancellationToken ct)
    {
        if (ct.IsCancellationRequested)
        {
            return null;
        }

        try
        {
            string? output = RunPdfInfo(path);

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

    // ── Process execution ─────────────────────────────────────────────────────

    private static string? RunPdfInfo(string path)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "pdfinfo",
            ArgumentList = { path },
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        return RunProcess(psi);
    }

    private static string? RunProcess(ProcessStartInfo psi)
    {
        using var process = Process.Start(psi);

        if (process is null)
        {
            return null;
        }

        string output = process.StandardOutput.ReadToEnd();

        if (!process.WaitForExit(5000))
        {
            try { process.Kill(); }
            catch { /* best effort */ }

            return null;
        }

        return process.ExitCode == 0 ? output : null;
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

    // ── Availability check ────────────────────────────────────────────────────

    private static bool CheckAvailability()
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "pdfinfo",
                Arguments = "-v",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };

            using var process = Process.Start(psi);

            if (process is null)
            {
                return false;
            }

            process.StandardOutput.ReadToEnd();
            process.StandardError.ReadToEnd();
            return process.WaitForExit(3000);
        }
        catch
        {
            return false;
        }
    }
}
