using System.IO.Compression;
using System.Xml.Linq;
using Wade.Highlighting;

namespace Wade.Preview;

internal sealed class NuGetPreviewProvider : IPreviewProvider
{
    public string Label => "NuGet metadata";

    public bool CanPreview(string path, PreviewContext context)
    {
        string ext = Path.GetExtension(path);
        return ext.Equals(".nupkg", StringComparison.OrdinalIgnoreCase)
            || ext.Equals(".snupkg", StringComparison.OrdinalIgnoreCase);
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
                return new PreviewResult
                {
                    TextLines = [new StyledLine("[no .nuspec found in package]", null)],
                    IsRendered = true,
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

            var lines = new List<StyledLine>();
            XElement root = doc.Root;

            // Find <metadata> element using local name to ignore namespace variations
            XElement? metadata = root.Elements().FirstOrDefault(e => e.Name.LocalName == "metadata");

            if (metadata is null)
            {
                return new PreviewResult
                {
                    TextLines = [new StyledLine("[invalid .nuspec: no metadata element]", null)],
                    IsRendered = true,
                    FileTypeLabel = "NuGet Package",
                };
            }

            lines.Add(new StyledLine("  NuGet Package", null));
            lines.Add(new StyledLine("  \u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500", null));

            AddField(lines, metadata, "Id", "Id");
            AddField(lines, metadata, "Version", "Version");
            AddField(lines, metadata, "Authors", "Authors");
            AddLicense(lines, metadata);
            AddField(lines, metadata, "ProjectUrl", "Project");
            AddRepositoryUrl(lines, metadata);
            AddField(lines, metadata, "Tags", "Tags");

            string? description = GetElementValue(metadata, "description");

            if (!string.IsNullOrWhiteSpace(description))
            {
                lines.Add(new StyledLine("", null));
                lines.Add(new StyledLine("  Description:", null));
                foreach (string descLine in WrapText(description.Trim(), Math.Max(context.PaneWidthCells - 6, 20)))
                {
                    lines.Add(new StyledLine($"    {descLine}", null));
                }
            }

            AddDependencies(lines, metadata);

            return new PreviewResult
            {
                TextLines = lines.ToArray(),
                IsRendered = true,
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

    private static void AddField(List<StyledLine> lines, XElement metadata, string elementName, string label)
    {
        string? value = GetElementValue(metadata, elementName);

        if (!string.IsNullOrWhiteSpace(value))
        {
            lines.Add(new StyledLine($"  {label,-12} {value}", null));
        }
    }

    private static void AddLicense(List<StyledLine> lines, XElement metadata)
    {
        XElement? licenseEl = metadata.Elements().FirstOrDefault(e => e.Name.LocalName == "license");

        if (licenseEl is not null)
        {
            lines.Add(new StyledLine($"  {"License",-12} {licenseEl.Value}", null));
            return;
        }

        string? licenseUrl = GetElementValue(metadata, "licenseUrl");

        if (!string.IsNullOrWhiteSpace(licenseUrl))
        {
            lines.Add(new StyledLine($"  {"License",-12} {licenseUrl}", null));
        }
    }

    private static void AddRepositoryUrl(List<StyledLine> lines, XElement metadata)
    {
        XElement? repoEl = metadata.Elements().FirstOrDefault(e => e.Name.LocalName == "repository");

        if (repoEl is null)
        {
            return;
        }

        string? url = repoEl.Attribute("url")?.Value;

        if (!string.IsNullOrWhiteSpace(url))
        {
            lines.Add(new StyledLine($"  {"Repository",-12} {url}", null));
        }
    }

    private static void AddDependencies(List<StyledLine> lines, XElement metadata)
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

                lines.Add(new StyledLine("", null));
                lines.Add(new StyledLine($"  Dependencies ({framework}):", null));

                foreach (XElement dep in depElements)
                {
                    string? id = dep.Attribute("id")?.Value;
                    string? version = dep.Attribute("version")?.Value;

                    if (id is not null)
                    {
                        string entry = version is not null ? $"    {id} >= {version}" : $"    {id}";
                        lines.Add(new StyledLine(entry, null));
                    }
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

            lines.Add(new StyledLine("", null));
            lines.Add(new StyledLine("  Dependencies:", null));

            foreach (XElement dep in depElements)
            {
                string? id = dep.Attribute("id")?.Value;
                string? version = dep.Attribute("version")?.Value;

                if (id is not null)
                {
                    string entry = version is not null ? $"    {id} >= {version}" : $"    {id}";
                    lines.Add(new StyledLine(entry, null));
                }
            }
        }
    }

    private static string? GetElementValue(XElement parent, string localName)
    {
        XElement? el = parent.Elements().FirstOrDefault(
            e => e.Name.LocalName.Equals(localName, StringComparison.OrdinalIgnoreCase));
        return el?.Value;
    }

    private static List<string> WrapText(string text, int maxWidth)
    {
        var result = new List<string>();

        // Normalize whitespace
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
