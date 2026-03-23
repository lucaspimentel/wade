namespace Wade.Preview;

internal interface IMetadataProvider
{
    public string Label { get; }

    public bool CanProvideMetadata(string path, PreviewContext context);

    public MetadataResult? GetMetadata(string path, PreviewContext context, CancellationToken ct);
}

internal sealed record MetadataResult
{
    public required MetadataSection[] Sections { get; init; }

    public string? FileTypeLabel { get; init; }
}

internal sealed record MetadataSection(string? Header, MetadataEntry[] Entries);

internal sealed record MetadataEntry(string Label, string Value);
