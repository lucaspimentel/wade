namespace Wade.Preview;

internal static class PreviewProviderRegistry
{
    private static readonly IPreviewProvider[] s_providers =
    [
        new ImagePreviewProvider(),
        new PdfPreviewProvider(),
        new GlowMarkdownPreviewProvider(),
        new ZipContentsPreviewProvider(),
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

        foreach (var provider in s_providers)
        {
            if (provider.CanPreview(path, context))
            {
                result.Add(provider);
            }
        }

        return result;
    }
}
