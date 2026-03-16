namespace Wade.Preview;

internal interface IMetadataProvider
{
    string Label { get; }

    bool CanProvideMetadata(string path, PreviewContext context);

    MetadataResult? GetMetadata(string path, PreviewContext context, CancellationToken ct);
}

internal record MetadataResult
{
    public required MetadataSection[] Sections { get; init; }
    public string? FileTypeLabel { get; init; }
}

internal record MetadataSection(string? Header, MetadataEntry[] Entries);

internal record MetadataEntry(string Label, string Value);
