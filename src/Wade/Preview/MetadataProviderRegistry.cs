namespace Wade.Preview;

internal static class MetadataProviderRegistry
{
    private static readonly IMetadataProvider[] s_providers =
    [
        new ExecutableMetadataProvider(),
        new OfficeMetadataProvider(),
        new MediaMetadataProvider(),
        new NuGetMetadataProvider(),
        new PdfMetadataProvider(),
    ];

    public static IMetadataProvider? GetProvider(string path, PreviewContext context)
    {
        if (context.IsBrokenSymlink || context.IsCloudPlaceholder)
        {
            return null;
        }

        foreach (var provider in s_providers)
        {
            if (provider.CanProvideMetadata(path, context))
            {
                return provider;
            }
        }

        return null;
    }
}
