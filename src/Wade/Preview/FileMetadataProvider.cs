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
            bool isFile = File.Exists(path);

            if (!isFile && !Directory.Exists(path))
            {
                return null;
            }

            var entries = new List<MetadataEntry>();

            if (context.IsCloudPlaceholder)
            {
                entries.Add(new MetadataEntry("Cloud", "not downloaded"));
            }

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
