using System.IO.Compression;
using Wade.FileSystem;
using Wade.UI;

namespace Wade.Preview;

internal sealed class ArchiveMetadataProvider : IMetadataProvider
{
    public string Label => "Archive metadata";

    public bool CanProvideMetadata(string path, PreviewContext context) =>
        ZipPreview.IsZipFile(path)
        || TarPreview.IsTarArchive(path)
        || TarPreview.IsPlainGzip(path);

    public MetadataResult? GetMetadata(string path, PreviewContext context, CancellationToken ct)
    {
        if (ct.IsCancellationRequested)
        {
            return null;
        }

        if (ZipPreview.IsZipFile(path))
        {
            return GetZipMetadata(path, ct);
        }

        if (TarPreview.IsTarArchive(path) || TarPreview.IsPlainGzip(path))
        {
            return GetTarMetadata(path, ct);
        }

        return null;
    }

    private static MetadataResult? GetZipMetadata(string path, CancellationToken ct)
    {
        try
        {
            using ZipArchive archive = ZipFile.OpenRead(path);

            if (ct.IsCancellationRequested)
            {
                return null;
            }

            int fileCount = 0;
            long totalSize = 0;
            long totalCompressed = 0;

            foreach (ZipArchiveEntry entry in archive.Entries)
            {
                if (entry.FullName.EndsWith('/'))
                {
                    continue;
                }

                fileCount++;
                totalSize += entry.Length;
                totalCompressed += entry.CompressedLength;
            }

            Span<char> buf = stackalloc char[16];

            var entries = new List<MetadataEntry>
            {
                new("Files", $"{fileCount:N0}"),
            };

            int n = FormatHelpers.FormatSize(buf, totalSize);
            entries.Add(new MetadataEntry("Total size", buf[..n].ToString()));

            n = FormatHelpers.FormatSize(buf, totalCompressed);
            entries.Add(new MetadataEntry("Compressed", buf[..n].ToString()));

            string ratio = totalSize > 0
                ? $"{(double)totalCompressed / totalSize:P0}"
                : "---";
            entries.Add(new MetadataEntry("Ratio", ratio));

            return new MetadataResult
            {
                Sections = [new MetadataSection("Archive", entries.ToArray())],
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

    private static MetadataResult? GetTarMetadata(string path, CancellationToken ct)
    {
        TarArchiveStats? maybeStats = TarPreview.GetStats(path, ct);
        if (maybeStats is null)
        {
            return null;
        }

        TarArchiveStats stats = maybeStats.Value;
        Span<char> buf = stackalloc char[16];

        var entries = new List<MetadataEntry>
        {
            new("Files", $"{stats.Files:N0}"),
        };

        int n = FormatHelpers.FormatSize(buf, stats.TotalSize);
        entries.Add(new MetadataEntry("Total size", buf[..n].ToString()));

        string formatLabel = stats.Format switch
        {
            TarFormat.Tar => "tar",
            TarFormat.TarGzip => "tar.gz",
            TarFormat.Gzip => "gzip",
            _ => "archive",
        };
        entries.Add(new MetadataEntry("Format", formatLabel));

        if (stats.CompressedSize is long compressed)
        {
            n = FormatHelpers.FormatSize(buf, compressed);
            entries.Add(new MetadataEntry("Compressed", buf[..n].ToString()));

            if (stats.TotalSize > 0)
            {
                string ratio = $"{(double)compressed / stats.TotalSize:P0}";
                entries.Add(new MetadataEntry("Ratio", ratio));
            }
        }

        return new MetadataResult
        {
            Sections = [new MetadataSection("Archive", entries.ToArray())],
        };
    }
}
