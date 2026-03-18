namespace Wade.Preview;

internal static class MetadataProviderRegistry
{
    private static readonly IMetadataProvider[] s_providers =
    [
        new FileMetadataProvider(),
        new ImageMetadataProvider(),
        new ExecutableMetadataProvider(),
        new OfficeMetadataProvider(),
        new MediaMetadataProvider(),
        new NuGetMetadataProvider(),
        new ArchiveMetadataProvider(),
        new PdfMetadataProvider(),
    ];

    public static List<IMetadataProvider> GetApplicableProviders(string path, PreviewContext context)
    {
        if (context.IsBrokenSymlink)
        {
            return [];
        }

        var result = new List<IMetadataProvider>();
        foreach (var provider in s_providers)
        {
            // Cloud placeholders: only FileMetadataProvider (reads fs attrs only, no file I/O)
            if (context.IsCloudPlaceholder && provider is not FileMetadataProvider)
            {
                continue;
            }

            if (provider.CanProvideMetadata(path, context))
            {
                result.Add(provider);
            }
        }

        return result;
    }
}
