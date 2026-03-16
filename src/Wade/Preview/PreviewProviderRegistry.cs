namespace Wade.Preview;

internal static class PreviewProviderRegistry
{
    private static readonly IPreviewProvider[] s_providers =
    [
        new ImagePreviewProvider(),
        new PdfPreviewProvider(),
        new GlowMarkdownPreviewProvider(),
        new NuGetPreviewProvider(),
        new OfficePreviewProvider(),
        new ZipContentsPreviewProvider(),
        new ExecutablePreviewProvider(),
        new TextPreviewProvider(),
        new HexPreviewProvider(),
        new DiffPreviewProvider(),
    ];

    public static List<IPreviewProvider> GetApplicableProviders(string path, PreviewContext context)
    {
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
