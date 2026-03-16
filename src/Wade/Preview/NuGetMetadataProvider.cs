using System.IO.Compression;
using System.Xml.Linq;

namespace Wade.Preview;

internal sealed class NuGetMetadataProvider : IMetadataProvider
{
    public string Label => "NuGet metadata";

    public bool CanProvideMetadata(string path, PreviewContext context)
    {
        string ext = Path.GetExtension(path);
        return ext.Equals(".nupkg", StringComparison.OrdinalIgnoreCase)
            || ext.Equals(".snupkg", StringComparison.OrdinalIgnoreCase);
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

            ZipArchiveEntry? nuspecEntry = null;

            foreach (ZipArchiveEntry entry in zip.Entries)
            {
                if (entry.FullName.EndsWith(".nuspec", StringComparison.OrdinalIgnoreCase)
                    && !entry.FullName.Contains('/'))
                {
                    nuspecEntry = entry;
                    break;
                }
            }

            if (nuspecEntry is null)
            {
                return new MetadataResult
                {
                    Sections = [new MetadataSection(null, [new MetadataEntry("", "[no .nuspec found in package]")])],
                    FileTypeLabel = "NuGet Package",
                };
            }

            if (ct.IsCancellationRequested)
            {
                return null;
            }

            using var stream = nuspecEntry.Open();
            var doc = XDocument.Load(stream);

            if (doc.Root is null)
            {
                return null;
            }

            XElement root = doc.Root;

            // Find <metadata> element using local name to ignore namespace variations
            XElement? metadata = root.Elements().FirstOrDefault(e => e.Name.LocalName == "metadata");

            if (metadata is null)
            {
                return new MetadataResult
                {
                    Sections = [new MetadataSection(null, [new MetadataEntry("", "[invalid .nuspec: no metadata element]")])],
                    FileTypeLabel = "NuGet Package",
                };
            }

            var sections = new List<MetadataSection>();

            // Main metadata section
            var mainEntries = new List<MetadataEntry>();
            AddField(mainEntries, metadata, "Id", "Id");
            AddField(mainEntries, metadata, "Version", "Version");
            AddField(mainEntries, metadata, "Authors", "Authors");
            AddLicense(mainEntries, metadata);
            AddField(mainEntries, metadata, "ProjectUrl", "Project");
            AddRepositoryUrl(mainEntries, metadata);
            AddField(mainEntries, metadata, "Tags", "Tags");

            if (mainEntries.Count > 0)
            {
                sections.Add(new MetadataSection("NuGet Package", mainEntries.ToArray()));
            }

            // Description section
            string? description = GetElementValue(metadata, "description");

            if (!string.IsNullOrWhiteSpace(description))
            {
                var descEntries = new List<MetadataEntry>();
                foreach (string descLine in TextHelper.WrapText(description.Trim(), Math.Max(context.PaneWidthCells - 6, 20)))
                {
                    descEntries.Add(new MetadataEntry("", descLine));
                }

                sections.Add(new MetadataSection("Description", descEntries.ToArray()));
            }

            // Dependencies sections
            AddDependencies(sections, metadata);

            return new MetadataResult
            {
                Sections = sections.ToArray(),
                FileTypeLabel = "NuGet Package",
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

    private static void AddField(List<MetadataEntry> entries, XElement metadata, string elementName, string label)
    {
        string? value = GetElementValue(metadata, elementName);

        if (!string.IsNullOrWhiteSpace(value))
        {
            entries.Add(new MetadataEntry(label, value));
        }
    }

    private static void AddLicense(List<MetadataEntry> entries, XElement metadata)
    {
        XElement? licenseEl = metadata.Elements().FirstOrDefault(e => e.Name.LocalName == "license");

        if (licenseEl is not null)
        {
            entries.Add(new MetadataEntry("License", licenseEl.Value));
            return;
        }

        string? licenseUrl = GetElementValue(metadata, "licenseUrl");

        if (!string.IsNullOrWhiteSpace(licenseUrl))
        {
            entries.Add(new MetadataEntry("License", licenseUrl));
        }
    }

    private static void AddRepositoryUrl(List<MetadataEntry> entries, XElement metadata)
    {
        XElement? repoEl = metadata.Elements().FirstOrDefault(e => e.Name.LocalName == "repository");

        if (repoEl is null)
        {
            return;
        }

        string? url = repoEl.Attribute("url")?.Value;

        if (!string.IsNullOrWhiteSpace(url))
        {
            entries.Add(new MetadataEntry("Repository", url));
        }
    }

    private static void AddDependencies(List<MetadataSection> sections, XElement metadata)
    {
        XElement? deps = metadata.Elements().FirstOrDefault(e => e.Name.LocalName == "dependencies");

        if (deps is null)
        {
            return;
        }

        var groups = deps.Elements().Where(e => e.Name.LocalName == "group").ToList();

        if (groups.Count > 0)
        {
            foreach (XElement group in groups)
            {
                string framework = group.Attribute("targetFramework")?.Value ?? "any";
                var depElements = group.Elements().Where(e => e.Name.LocalName == "dependency").ToList();

                if (depElements.Count == 0)
                {
                    continue;
                }

                var entries = new List<MetadataEntry>();
                foreach (XElement dep in depElements)
                {
                    string? id = dep.Attribute("id")?.Value;
                    string? version = dep.Attribute("version")?.Value;

                    if (id is not null)
                    {
                        string entry = version is not null ? $"{id} >= {version}" : id;
                        entries.Add(new MetadataEntry("", entry));
                    }
                }

                if (entries.Count > 0)
                {
                    sections.Add(new MetadataSection($"Dependencies ({framework})", entries.ToArray()));
                }
            }
        }
        else
        {
            // Flat dependency list (no groups)
            var depElements = deps.Elements().Where(e => e.Name.LocalName == "dependency").ToList();

            if (depElements.Count == 0)
            {
                return;
            }

            var entries = new List<MetadataEntry>();
            foreach (XElement dep in depElements)
            {
                string? id = dep.Attribute("id")?.Value;
                string? version = dep.Attribute("version")?.Value;

                if (id is not null)
                {
                    string entry = version is not null ? $"{id} >= {version}" : id;
                    entries.Add(new MetadataEntry("", entry));
                }
            }

            if (entries.Count > 0)
            {
                sections.Add(new MetadataSection("Dependencies", entries.ToArray()));
            }
        }
    }

    private static string? GetElementValue(XElement parent, string localName)
    {
        XElement? el = parent.Elements().FirstOrDefault(
            e => e.Name.LocalName.Equals(localName, StringComparison.OrdinalIgnoreCase));
        return el?.Value;
    }

}
