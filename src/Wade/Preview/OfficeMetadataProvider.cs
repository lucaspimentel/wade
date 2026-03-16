using System.IO.Compression;
using System.Xml.Linq;

namespace Wade.Preview;

internal sealed class OfficeMetadataProvider : IMetadataProvider
{
    private static readonly XNamespace DcNs = "http://purl.org/dc/elements/1.1/";
    private static readonly XNamespace DcTermsNs = "http://purl.org/dc/terms/";
    private static readonly XNamespace CpNs = "http://schemas.openxmlformats.org/package/2006/metadata/core-properties";
    private static readonly XNamespace AppNs = "http://schemas.openxmlformats.org/officeDocument/2006/extended-properties";
    private static readonly XNamespace VtNs = "http://schemas.openxmlformats.org/officeDocument/2006/docPropsVTypes";

    public string Label => "Document metadata";

    public bool CanProvideMetadata(string path, PreviewContext context)
    {
        string ext = Path.GetExtension(path);
        return ext.Equals(".docx", StringComparison.OrdinalIgnoreCase)
            || ext.Equals(".xlsx", StringComparison.OrdinalIgnoreCase)
            || ext.Equals(".pptx", StringComparison.OrdinalIgnoreCase)
            || ext.Equals(".dotx", StringComparison.OrdinalIgnoreCase)
            || ext.Equals(".xltx", StringComparison.OrdinalIgnoreCase)
            || ext.Equals(".potx", StringComparison.OrdinalIgnoreCase);
    }

    public MetadataResult? GetMetadata(string path, PreviewContext context, CancellationToken ct)
    {
        if (ct.IsCancellationRequested)
        {
            return null;
        }

        try
        {
            using var zip = ZipFile.OpenRead(path);
            string fileTypeLabel = GetFileTypeLabel(path);
            var sections = new List<MetadataSection>();

            bool hasCoreProps = AddCoreProperties(sections, zip, context);

            if (ct.IsCancellationRequested)
            {
                return null;
            }

            bool hasAppProps = AddAppProperties(sections, zip, path);

            if (!hasCoreProps && !hasAppProps)
            {
                return new MetadataResult
                {
                    Sections = [new MetadataSection(null, [new MetadataEntry("", "[no document metadata found]")])],
                    FileTypeLabel = fileTypeLabel,
                };
            }

            return new MetadataResult
            {
                Sections = sections.ToArray(),
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

    private static bool AddCoreProperties(List<MetadataSection> sections, ZipArchive zip, PreviewContext context)
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
        var entries = new List<MetadataEntry>();

        AddEntryIfPresent(entries, "Title", root.Element(DcNs + "title")?.Value);
        AddEntryIfPresent(entries, "Author", root.Element(DcNs + "creator")?.Value);
        AddEntryIfPresent(entries, "Subject", root.Element(DcNs + "subject")?.Value);
        AddEntryIfPresent(entries, "Keywords", root.Element(CpNs + "keywords")?.Value);
        AddEntryIfPresent(entries, "Category", root.Element(CpNs + "category")?.Value);
        AddDateEntry(entries, "Created", root.Element(DcTermsNs + "created")?.Value);
        AddDateEntry(entries, "Modified", root.Element(DcTermsNs + "modified")?.Value);
        AddEntryIfPresent(entries, "Last author", root.Element(CpNs + "lastModifiedBy")?.Value);
        AddEntryIfPresent(entries, "Revision", root.Element(CpNs + "revision")?.Value);

        string? description = root.Element(DcNs + "description")?.Value;

        if (!string.IsNullOrWhiteSpace(description))
        {
            // Add description as a separate section
            var descEntries = new List<MetadataEntry>();
            foreach (string descLine in TextHelper.WrapText(description.Trim(), Math.Max(context.PaneWidthCells - 6, 20)))
            {
                descEntries.Add(new MetadataEntry("", descLine));
            }

            if (entries.Count > 0)
            {
                string fileTypeLabel = "Document Properties";
                sections.Add(new MetadataSection(fileTypeLabel, entries.ToArray()));
            }

            sections.Add(new MetadataSection("Description", descEntries.ToArray()));
            return true;
        }

        if (entries.Count == 0)
        {
            return false;
        }

        sections.Add(new MetadataSection("Document Properties", entries.ToArray()));
        return true;
    }

    private static bool AddAppProperties(List<MetadataSection> sections, ZipArchive zip, string path)
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
        var entries = new List<MetadataEntry>();
        string ext = Path.GetExtension(path);

        if (ext.Equals(".docx", StringComparison.OrdinalIgnoreCase) || ext.Equals(".dotx", StringComparison.OrdinalIgnoreCase))
        {
            AddFormattedIntEntry(entries, "Pages", root.Element(AppNs + "Pages")?.Value);
            AddFormattedIntEntry(entries, "Words", root.Element(AppNs + "Words")?.Value);
            AddFormattedIntEntry(entries, "Paragraphs", root.Element(AppNs + "Paragraphs")?.Value);
        }
        else if (ext.Equals(".pptx", StringComparison.OrdinalIgnoreCase) || ext.Equals(".potx", StringComparison.OrdinalIgnoreCase))
        {
            AddFormattedIntEntry(entries, "Slides", root.Element(AppNs + "Slides")?.Value);
            AddFormattedIntEntry(entries, "Hidden slides", root.Element(AppNs + "HiddenSlides")?.Value);
        }

        AddEntryIfPresent(entries, "Application", root.Element(AppNs + "Application")?.Value);

        // Sheet/slide names from TitlesOfParts
        XElement? titlesOfParts = root.Element(AppNs + "TitlesOfParts");
        XElement? vector = titlesOfParts?.Element(VtNs + "vector");
        List<MetadataEntry>? partEntries = null;
        string? partLabel = null;

        if (vector is not null)
        {
            var parts = vector.Elements(VtNs + "lpstr")
                .Select(e => e.Value)
                .Where(v => !string.IsNullOrWhiteSpace(v))
                .ToList();

            if (parts.Count > 0)
            {
                partLabel = ext.Equals(".xlsx", StringComparison.OrdinalIgnoreCase)
                    || ext.Equals(".xltx", StringComparison.OrdinalIgnoreCase)
                    ? "Sheets" : "Parts";

                partEntries = new List<MetadataEntry>();
                foreach (string part in parts)
                {
                    partEntries.Add(new MetadataEntry("", part));
                }
            }
        }

        if (entries.Count == 0 && partEntries is null)
        {
            return false;
        }

        if (entries.Count > 0)
        {
            sections.Add(new MetadataSection("Statistics", entries.ToArray()));
        }

        if (partEntries is not null)
        {
            sections.Add(new MetadataSection(partLabel, partEntries.ToArray()));
        }

        return true;
    }

    private static void AddEntryIfPresent(List<MetadataEntry> entries, string label, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            entries.Add(new MetadataEntry(label, value));
        }
    }

    private static void AddDateEntry(List<MetadataEntry> entries, string label, string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        if (DateTimeOffset.TryParse(value, out DateTimeOffset dto))
        {
            entries.Add(new MetadataEntry(label, $"{dto:yyyy-MM-dd HH:mm:ss}"));
        }
        else
        {
            entries.Add(new MetadataEntry(label, value));
        }
    }

    private static void AddFormattedIntEntry(List<MetadataEntry> entries, string label, string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        if (int.TryParse(value, out int num))
        {
            entries.Add(new MetadataEntry(label, $"{num:N0}"));
        }
        else
        {
            entries.Add(new MetadataEntry(label, value));
        }
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

}
