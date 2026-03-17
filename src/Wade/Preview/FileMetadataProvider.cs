using System.Globalization;
using Wade.FileSystem;
using Wade.UI;

namespace Wade.Preview;

internal sealed class FileMetadataProvider : IMetadataProvider
{
    public string Label => "File info";

    public bool CanProvideMetadata(string path, PreviewContext context) => true;

    public MetadataResult? GetMetadata(string path, PreviewContext context, CancellationToken ct)
    {
        if (ct.IsCancellationRequested)
        {
            return null;
        }

        try
        {
            var fileInfo = new FileInfo(path);
            bool isFile = fileInfo.Exists;
            var fsInfo = isFile ? (FileSystemInfo)fileInfo : new DirectoryInfo(path);

            if (!fsInfo.Exists)
            {
                return null;
            }

            var entries = new List<MetadataEntry>();

            if (isFile)
            {
                Span<char> sizeBuf = stackalloc char[32];
                int n = FormatHelpers.FormatSize(sizeBuf, fileInfo.Length);
                entries.Add(new MetadataEntry("Size", sizeBuf[..n].ToString()));
            }

            string modified = fsInfo.LastWriteTime.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);
            entries.Add(new MetadataEntry("Modified", modified));

            if (context.GitStatus is { } status && status != GitFileStatus.None)
            {
                entries.Add(new MetadataEntry("Git", PropertiesOverlay.FormatGitStatus(status)));
            }

            string name = Path.GetFileName(path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            return new MetadataResult
            {
                Sections = [new MetadataSection(name, [.. entries])],
            };
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }
}
