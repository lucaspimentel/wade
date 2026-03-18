using Wade.Highlighting;
using Wade.UI;

namespace Wade.Preview;

internal sealed class MsiPreviewProvider : IPreviewProvider
{
    private const int MaxEntries = 100;

    public string Label => "Installer files";

    public bool CanPreview(string path, PreviewContext context) =>
        OperatingSystem.IsWindows()
        && Path.GetExtension(path).Equals(".msi", StringComparison.OrdinalIgnoreCase);

    public PreviewResult? GetPreview(string path, PreviewContext context, CancellationToken ct)
    {
        if (ct.IsCancellationRequested || !OperatingSystem.IsWindows())
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

            List<MsiFileEntry> files;

            try
            {
                files = MsiInterop.QueryFileTable(db);
            }
            finally
            {
                MsiInterop.CloseDatabase(db);
            }

            if (ct.IsCancellationRequested)
            {
                return null;
            }

            if (files.Count == 0)
            {
                return new PreviewResult
                {
                    TextLines = [new StyledLine("[empty installer]", null)],
                    IsRendered = true,
                    IsPlaceholder = true,
                    FileTypeLabel = "MSI",
                };
            }

            files.Sort((a, b) => string.Compare(a.FileName, b.FileName, StringComparison.OrdinalIgnoreCase));

            int take = Math.Min(files.Count, MaxEntries);
            var lines = new List<StyledLine>(take + 3);
            Span<char> sizeBuf = stackalloc char[16];

            lines.Add(new StyledLine("  Installer Files", null));
            lines.Add(new StyledLine("  " + new string('\u2500', 16), null));
            lines.Add(new StyledLine("        Size  Name", null));

            for (int i = 0; i < take; i++)
            {
                if (ct.IsCancellationRequested)
                {
                    return null;
                }

                MsiFileEntry entry = files[i];
                int sn = FormatHelpers.FormatSize(sizeBuf, entry.FileSize);
                string size = sizeBuf[..sn].ToString();

                lines.Add(new StyledLine($"  {size,10}  {entry.FileName}", null));
            }

            if (files.Count > MaxEntries)
            {
                lines.Add(new StyledLine($"... and {files.Count - MaxEntries} more files", null));
            }

            return new PreviewResult
            {
                TextLines = [.. lines],
                IsRendered = true,
                FileTypeLabel = "MSI",
            };
        }
        catch
        {
            return null;
        }
    }
}
