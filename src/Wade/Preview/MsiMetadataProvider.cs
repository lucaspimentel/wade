namespace Wade.Preview;

internal sealed class MsiMetadataProvider : IMetadataProvider
{
    public string Label => "MSI metadata";

    public bool CanProvideMetadata(string path, PreviewContext context) =>
        Path.GetExtension(path).Equals(".msi", StringComparison.OrdinalIgnoreCase);

    public MetadataResult? GetMetadata(string path, PreviewContext context, CancellationToken ct)
    {
        if (ct.IsCancellationRequested)
        {
            return null;
        }

        if (!OperatingSystem.IsWindows())
        {
            return null;
        }

        try
        {
            nint db = MsiInterop.OpenDatabaseReadOnly(path);
            if (db == 0)
            {
                return null;
            }

            try
            {
                var sections = new List<MetadataSection>();
                Dictionary<string, string> properties = MsiInterop.QueryPropertyTable(db);

                // Installer section from Property table
                var installerEntries = new List<MetadataEntry>();
                AddPropertyIfPresent(installerEntries, "Product", properties, "ProductName");
                AddPropertyIfPresent(installerEntries, "Version", properties, "ProductVersion");
                AddPropertyIfPresent(installerEntries, "Manufacturer", properties, "Manufacturer");
                AddPropertyIfPresent(installerEntries, "ProductCode", properties, "ProductCode");
                AddPropertyIfPresent(installerEntries, "UpgradeCode", properties, "UpgradeCode");

                if (installerEntries.Count > 0)
                {
                    sections.Add(new MetadataSection("Installer", installerEntries.ToArray()));
                }

                if (ct.IsCancellationRequested)
                {
                    return null;
                }

                // Summary section from summary info stream
                MsiSummaryInfo? summary = MsiInterop.GetSummaryInfo(db);
                if (summary is not null)
                {
                    var summaryEntries = new List<MetadataEntry>();

                    if (summary.Subject is not null)
                    {
                        summaryEntries.Add(new MetadataEntry("Subject", summary.Subject));
                    }

                    if (summary.Author is not null)
                    {
                        summaryEntries.Add(new MetadataEntry("Author", summary.Author));
                    }

                    if (summary.Comments is not null)
                    {
                        summaryEntries.Add(new MetadataEntry("Comments", summary.Comments));
                    }

                    if (summary.Template is not null)
                    {
                        summaryEntries.Add(new MetadataEntry("Platform", summary.Template));
                    }

                    // WordCount bit 3 indicates per-user (elevated) vs per-machine
                    if (properties.TryGetValue("ALLUSERS", out string? allUsers))
                    {
                        string scope = allUsers == "1" ? "Per-machine" : "Per-user";
                        summaryEntries.Add(new MetadataEntry("Install scope", scope));
                    }

                    if (summaryEntries.Count > 0)
                    {
                        sections.Add(new MetadataSection("Summary", summaryEntries.ToArray()));
                    }
                }

                if (sections.Count == 0)
                {
                    return null;
                }

                return new MetadataResult
                {
                    Sections = sections.ToArray(),
                    FileTypeLabel = "MSI",
                };
            }
            finally
            {
                MsiInterop.CloseDatabase(db);
            }
        }
        catch
        {
            return null;
        }
    }

    private static void AddPropertyIfPresent(
        List<MetadataEntry> entries,
        string label,
        Dictionary<string, string> properties,
        string propertyName)
    {
        if (properties.TryGetValue(propertyName, out string? value) && !string.IsNullOrWhiteSpace(value))
        {
            entries.Add(new MetadataEntry(label, value));
        }
    }
}
