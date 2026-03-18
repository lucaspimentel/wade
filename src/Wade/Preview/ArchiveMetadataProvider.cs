using System.IO.Compression;
using Wade.FileSystem;
using Wade.UI;

namespace Wade.Preview;

internal sealed class ArchiveMetadataProvider : IMetadataProvider
{
    public string Label => "Archive metadata";

    public bool CanProvideMetadata(string path, PreviewContext context) =>
        ZipPreview.IsZipFile(path);

    public MetadataResult? GetMetadata(string path, PreviewContext context, CancellationToken ct)
    {
        if (ct.IsCancellationRequested)
        {
            return null;
        }

        try
        {
            using var archive = ZipFile.OpenRead(path);

            if (ct.IsCancellationRequested)
            {
                return null;
            }

            // Count files only (skip directory-only entries)
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
}
