using Wade.FileSystem;

namespace Wade.Preview;

internal static class PreviewProviderRegistry
{
    private static readonly IPreviewProvider[] s_providers =
    [
        new ImagePreviewProvider(),
        new PdfPreviewProvider(),
        new GlowMarkdownPreviewProvider(),
        new ZipContentsPreviewProvider(),
        new MsiPreviewProvider(),
        new TextPreviewProvider(),
        new DiffPreviewProvider(),
        new NonePreviewProvider(),
        new HexPreviewProvider(),
    ];

    public static List<IPreviewProvider> GetApplicableProviders(string path, PreviewContext context)
    {
        if (context.IsBrokenSymlink || context.IsCloudPlaceholder)
        {
            return [];
        }

        var result = new List<IPreviewProvider>();

        foreach (IPreviewProvider provider in s_providers)
        {
            if (provider.CanPreview(path, context))
            {
                result.Add(provider);
            }
        }

        // For secondary archive types (e.g. .docx, .nupkg), move archive contents
        // after "None" so it's available via the preview switcher but not the default.
        if (ZipPreview.IsZipFile(path) && !ZipPreview.IsPrimaryArchive(path))
        {
            int zipIndex = result.FindIndex(p => p is ZipContentsPreviewProvider);
            int noneIndex = result.FindIndex(p => p is NonePreviewProvider);

            if (zipIndex >= 0 && noneIndex >= 0 && zipIndex < noneIndex)
            {
                IPreviewProvider zip = result[zipIndex];
                result.RemoveAt(zipIndex);
                // noneIndex shifted left by 1 after removal
                int insertAt = noneIndex; // insert after None (which is now at noneIndex - 1)
                result.Insert(insertAt, zip);
            }
        }

        return result;
    }
}
