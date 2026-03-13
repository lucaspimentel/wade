using System.Collections.Frozen;
using System.IO.Compression;
using Wade.UI;

namespace Wade.FileSystem;

internal static class ZipPreview
{
    private const int MaxEntries = 100;

    private static readonly FrozenSet<string> s_zipExtensions =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ".zip", ".nupkg", ".jar", ".war", ".ear",
        }.ToFrozenSet(StringComparer.OrdinalIgnoreCase);

    public static bool IsZipFile(string path)
    {
        string ext = Path.GetExtension(path);
        return ext.Length > 0 && s_zipExtensions.Contains(ext);
    }

    public static string[]? GetPreviewLines(string path, CancellationToken ct)
    {
        try
        {
            using var archive = ZipFile.OpenRead(path);

            if (ct.IsCancellationRequested)
            {
                return null;
            }

            if (archive.Entries.Count == 0)
            {
                return ["[empty archive]"];
            }

            // Filter to file entries only (skip directory-only entries)
            var files = archive.Entries
                .Where(e => !e.FullName.EndsWith('/'))
                .OrderBy(e => e.FullName, StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (files.Count == 0)
            {
                return ["[empty archive]"];
            }

            int take = Math.Min(files.Count, MaxEntries);
            List<string> lines = new(take + 4);
            Span<char> sizeBuf = stackalloc char[16];
            Span<char> compBuf = stackalloc char[16];

            //         "  nnnnnnnnnn  nnnnnnnnnn  nnnnn  filename"
            lines.Add("        Size  Compressed  Ratio  Name");

            long totalSize = 0;
            long totalCompressed = 0;

            for (int i = 0; i < take; i++)
            {
                if (ct.IsCancellationRequested)
                {
                    return null;
                }

                var entry = files[i];

                int sn = FormatHelpers.FormatSize(sizeBuf, entry.Length);
                string size = sizeBuf[..sn].ToString();

                int cn = FormatHelpers.FormatSize(compBuf, entry.CompressedLength);
                string compressed = compBuf[..cn].ToString();

                string ratio = entry.Length > 0
                    ? $"{(double)entry.CompressedLength / entry.Length:P0}"
                    : "---";

                lines.Add($"  {size,10}  {compressed,10}  {ratio,5}  {entry.FullName}");
            }

            if (files.Count > MaxEntries)
            {
                lines.Add($"... and {files.Count - MaxEntries} more entries");
            }

            // Totals across all files (not just displayed ones)
            foreach (var entry in files)
            {
                totalSize += entry.Length;
                totalCompressed += entry.CompressedLength;
            }

            string totalRatio = totalSize > 0
                ? $"{(double)totalCompressed / totalSize:P0}"
                : "---";

            int tsn = FormatHelpers.FormatSize(sizeBuf, totalSize);
            int tcn = FormatHelpers.FormatSize(compBuf, totalCompressed);

            lines.Add("  ──────────  ──────────  ─────");
            string totalSizeStr = sizeBuf[..tsn].ToString();
            string totalCompStr = compBuf[..tcn].ToString();
            lines.Add($"  {totalSizeStr,10}  {totalCompStr,10}  {totalRatio,5}  {files.Count} files");

            return [.. lines];
        }
        catch (InvalidDataException)
        {
            return ["[invalid archive]"];
        }
        catch (OperationCanceledException)
        {
            return null;
        }
    }
}
