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

    public static List<IMetadataProvider> GetApplicableProviders(string path, PreviewContext context)
    {
        if (context.IsBrokenSymlink || context.IsCloudPlaceholder)
        {
            return [];
        }

        var result = new List<IMetadataProvider>();
        foreach (var provider in s_providers)
        {
            if (provider.CanProvideMetadata(path, context))
            {
                result.Add(provider);
            }
        }

        return result;
    }
}
