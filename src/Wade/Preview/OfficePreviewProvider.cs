using System.IO.Compression;
using System.Xml.Linq;
using Wade.Highlighting;

namespace Wade.Preview;

internal sealed class OfficePreviewProvider : IPreviewProvider
{
    private static readonly XNamespace DcNs = "http://purl.org/dc/elements/1.1/";
    private static readonly XNamespace DcTermsNs = "http://purl.org/dc/terms/";
    private static readonly XNamespace CpNs = "http://schemas.openxmlformats.org/package/2006/metadata/core-properties";
    private static readonly XNamespace AppNs = "http://schemas.openxmlformats.org/officeDocument/2006/extended-properties";
    private static readonly XNamespace VtNs = "http://schemas.openxmlformats.org/officeDocument/2006/docPropsVTypes";

    public string Label => "Document metadata";

    public bool CanPreview(string path, PreviewContext context)
    {
        string ext = Path.GetExtension(path);
        return ext.Equals(".docx", StringComparison.OrdinalIgnoreCase)
            || ext.Equals(".xlsx", StringComparison.OrdinalIgnoreCase)
            || ext.Equals(".pptx", StringComparison.OrdinalIgnoreCase)
            || ext.Equals(".dotx", StringComparison.OrdinalIgnoreCase)
            || ext.Equals(".xltx", StringComparison.OrdinalIgnoreCase)
            || ext.Equals(".potx", StringComparison.OrdinalIgnoreCase);
    }

    public PreviewResult? GetPreview(string path, PreviewContext context, CancellationToken ct)
    {
        if (ct.IsCancellationRequested)
        {
            return null;
        }

        try
        {
            using var zip = ZipFile.OpenRead(path);
            var lines = new List<StyledLine>();
            string fileTypeLabel = GetFileTypeLabel(path);

            lines.Add(new StyledLine($"  {fileTypeLabel}", null));
            lines.Add(new StyledLine("  \u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500", null));

            bool hasCoreProps = AddCoreProperties(lines, zip, context);

            if (ct.IsCancellationRequested)
            {
                return null;
            }

            bool hasAppProps = AddAppProperties(lines, zip, path);

            if (!hasCoreProps && !hasAppProps)
            {
                return new PreviewResult
                {
                    TextLines = [new StyledLine("[no document metadata found]", null)],
                    IsRendered = true,
                    FileTypeLabel = fileTypeLabel,
                };
            }

            return new PreviewResult
            {
                TextLines = lines.ToArray(),
                IsRendered = true,
                FileTypeLabel = fileTypeLabel,
            };
        }
        catch (InvalidDataException)
        {
            return null;
        }
        catch (IOException)
        {
            return null;
        }
    }

    private static bool AddCoreProperties(List<StyledLine> lines, ZipArchive zip, PreviewContext context)
    {
        ZipArchiveEntry? coreEntry = zip.GetEntry("docProps/core.xml");

        if (coreEntry is null)
        {
            return false;
        }

        using var stream = coreEntry.Open();
        var doc = XDocument.Load(stream);

        if (doc.Root is null)
        {
            return false;
        }

        XElement root = doc.Root;
        bool hasData = false;

        hasData |= AddField(lines, "Title", root.Element(DcNs + "title")?.Value);
        hasData |= AddField(lines, "Author", root.Element(DcNs + "creator")?.Value);
        hasData |= AddField(lines, "Subject", root.Element(DcNs + "subject")?.Value);
        hasData |= AddField(lines, "Keywords", root.Element(CpNs + "keywords")?.Value);
        hasData |= AddField(lines, "Category", root.Element(CpNs + "category")?.Value);
        hasData |= AddDateField(lines, "Created", root.Element(DcTermsNs + "created")?.Value);
        hasData |= AddDateField(lines, "Modified", root.Element(DcTermsNs + "modified")?.Value);
        hasData |= AddField(lines, "Last author", root.Element(CpNs + "lastModifiedBy")?.Value);
        hasData |= AddField(lines, "Revision", root.Element(CpNs + "revision")?.Value);

        string? description = root.Element(DcNs + "description")?.Value;

        if (!string.IsNullOrWhiteSpace(description))
        {
            lines.Add(new StyledLine("", null));
            lines.Add(new StyledLine("  Description:", null));

            foreach (string descLine in WrapText(description.Trim(), Math.Max(context.PaneWidthCells - 6, 20)))
            {
                lines.Add(new StyledLine($"    {descLine}", null));
            }

            hasData = true;
        }

        return hasData;
    }

    private static bool AddAppProperties(List<StyledLine> lines, ZipArchive zip, string path)
    {
        ZipArchiveEntry? appEntry = zip.GetEntry("docProps/app.xml");

        if (appEntry is null)
        {
            return false;
        }

        using var stream = appEntry.Open();
        var doc = XDocument.Load(stream);

        if (doc.Root is null)
        {
            return false;
        }

        XElement root = doc.Root;
        bool hasData = false;

        lines.Add(new StyledLine("", null));
        lines.Add(new StyledLine("  Document Properties", null));
        lines.Add(new StyledLine("  \u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500", null));

        string ext = Path.GetExtension(path);

        if (ext.Equals(".docx", StringComparison.OrdinalIgnoreCase) || ext.Equals(".dotx", StringComparison.OrdinalIgnoreCase))
        {
            hasData |= AddFormattedIntField(lines, "Pages", root.Element(AppNs + "Pages")?.Value);
            hasData |= AddFormattedIntField(lines, "Words", root.Element(AppNs + "Words")?.Value);
            hasData |= AddFormattedIntField(lines, "Paragraphs", root.Element(AppNs + "Paragraphs")?.Value);
        }
        else if (ext.Equals(".pptx", StringComparison.OrdinalIgnoreCase) || ext.Equals(".potx", StringComparison.OrdinalIgnoreCase))
        {
            hasData |= AddFormattedIntField(lines, "Slides", root.Element(AppNs + "Slides")?.Value);
            hasData |= AddFormattedIntField(lines, "Hidden slides", root.Element(AppNs + "HiddenSlides")?.Value);
        }

        // Sheet/slide names from TitlesOfParts
        XElement? titlesOfParts = root.Element(AppNs + "TitlesOfParts");
        XElement? vector = titlesOfParts?.Element(VtNs + "vector");

        if (vector is not null)
        {
            var parts = vector.Elements(VtNs + "lpstr")
                .Select(e => e.Value)
                .Where(v => !string.IsNullOrWhiteSpace(v))
                .ToList();

            if (parts.Count > 0)
            {
                string partLabel = ext.Equals(".xlsx", StringComparison.OrdinalIgnoreCase)
                    || ext.Equals(".xltx", StringComparison.OrdinalIgnoreCase)
                    ? "Sheets" : "Parts";

                lines.Add(new StyledLine("", null));
                lines.Add(new StyledLine($"  {partLabel}:", null));

                foreach (string part in parts)
                {
                    lines.Add(new StyledLine($"    {part}", null));
                }

                hasData = true;
            }
        }

        hasData |= AddField(lines, "Application", root.Element(AppNs + "Application")?.Value);

        if (!hasData)
        {
            // Remove the section header we added speculatively
            lines.RemoveRange(lines.Count - 3, 3);
        }

        return hasData;
    }

    private static bool AddField(List<StyledLine> lines, string label, string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        lines.Add(new StyledLine($"  {label,-14} {value}", null));
        return true;
    }

    private static bool AddDateField(List<StyledLine> lines, string label, string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        if (DateTimeOffset.TryParse(value, out DateTimeOffset dto))
        {
            lines.Add(new StyledLine($"  {label,-14} {dto:yyyy-MM-dd HH:mm:ss}", null));
            return true;
        }

        // Fall back to raw value if parsing fails
        lines.Add(new StyledLine($"  {label,-14} {value}", null));
        return true;
    }

    private static bool AddFormattedIntField(List<StyledLine> lines, string label, string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        if (int.TryParse(value, out int num))
        {
            lines.Add(new StyledLine($"  {label,-14} {num:N0}", null));
            return true;
        }

        lines.Add(new StyledLine($"  {label,-14} {value}", null));
        return true;
    }

    private static string GetFileTypeLabel(string path)
    {
        string ext = Path.GetExtension(path);

        return ext.ToUpperInvariant() switch
        {
            ".DOCX" or ".DOTX" => "Word Document",
            ".XLSX" or ".XLTX" => "Excel Workbook",
            ".PPTX" or ".POTX" => "PowerPoint Presentation",
            _ => "Office Document",
        };
    }

    private static List<string> WrapText(string text, int maxWidth)
    {
        var result = new List<string>();
        string normalized = string.Join(' ', text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

        if (normalized.Length <= maxWidth)
        {
            result.Add(normalized);
            return result;
        }

        int pos = 0;

        while (pos < normalized.Length)
        {
            if (pos + maxWidth >= normalized.Length)
            {
                result.Add(normalized[pos..]);
                break;
            }

            int breakAt = normalized.LastIndexOf(' ', pos + maxWidth, maxWidth);

            if (breakAt <= pos)
            {
                breakAt = pos + maxWidth;
            }

            result.Add(normalized[pos..breakAt]);
            pos = breakAt + 1;
        }

        return result;
    }
}
