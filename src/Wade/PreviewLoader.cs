using Wade.FileSystem;
using Wade.Highlighting;
using Wade.Preview;
using Wade.Terminal;
using Wade.UI;

namespace Wade;

internal sealed class PreviewLoader
{
    private readonly InputPipeline _pipeline;
    private CancellationTokenSource? _cts;

#pragma warning disable CSLINT221 // Consider using a primary constructor
    public PreviewLoader(InputPipeline pipeline)
#pragma warning restore CSLINT221
    {
        _pipeline = pipeline;
    }

    public void BeginLoad(string path, IPreviewProvider provider, PreviewContext context)
    {
        _cts?.Cancel();
        _cts?.Dispose();
        _cts = new CancellationTokenSource();
        CancellationToken token = _cts.Token;

        Task.Run(() => LoadWithProvider(path, provider, context, token), token);
    }

    public void BeginLoad(
        string path,
        List<IMetadataProvider>? metadataProviders,
        IPreviewProvider? previewProvider,
        PreviewContext context)
    {
        _cts?.Cancel();
        _cts?.Dispose();
        _cts = new CancellationTokenSource();
        CancellationToken token = _cts.Token;

        Task.Run(() => LoadMetadataAndPreview(path, metadataProviders, previewProvider, context, token), token);
    }

    public void Cancel()
    {
        _cts?.Cancel();
        _cts?.Dispose();
        _cts = null;
    }

    private void LoadMetadataAndPreview(
        string path,
        List<IMetadataProvider>? metadataProviders,
        IPreviewProvider? previewProvider,
        PreviewContext context,
        CancellationToken ct)
    {
        try
        {
            if (ct.IsCancellationRequested)
            {
                return;
            }

            // Load metadata first — collect sections from all applicable providers
            if (metadataProviders is { Count: > 0 })
            {
                var allSections = new List<MetadataSection>();
                string? fileTypeLabel = null;

                foreach (IMetadataProvider provider in metadataProviders)
                {
                    if (ct.IsCancellationRequested)
                    {
                        return;
                    }

                    MetadataResult? result = provider.GetMetadata(path, context, ct);
                    if (result is not null)
                    {
                        allSections.AddRange(result.Sections);
                        fileTypeLabel ??= result.FileTypeLabel;
                    }
                }

                if (allSections.Count > 0 && !ct.IsCancellationRequested)
                {
                    // Detect encoding/line ending for non-binary files
                    FileMetadata fileMetadata = FilePreview.DetectFileMetadata(path);
                    string? encoding = fileMetadata.IsBinary ? null : fileMetadata.Encoding;
                    string? lineEnding = fileMetadata.IsBinary ? null : fileMetadata.LineEnding;

                    _pipeline.Inject(new MetadataReadyEvent(path, [.. allSections], fileTypeLabel, encoding, lineEnding));

                    // Reduce available height for preview so image providers don't size to the full pane
                    StyledLine[] renderedMetadata = MetadataRenderer.Render([.. allSections], context.PaneWidthCells);
                    int metadataRows = Math.Min(renderedMetadata.Length + 1, context.PaneHeightCells / 2); // +1 for separator row
                    int availableRows = context.PaneHeightCells - metadataRows;
                    if (availableRows > 0)
                    {
                        context = context with { PaneHeightCells = availableRows };
                    }
                }
            }

            if (ct.IsCancellationRequested)
            {
                return;
            }

            // Then load preview
            if (previewProvider is not null)
            {
                LoadWithProvider(path, previewProvider, context, ct);
            }
            else if (!ct.IsCancellationRequested)
            {
                // No preview provider — signal that loading is complete
                _pipeline.Inject(new PreviewLoadingCompleteEvent(path));
            }
        }
        catch (OperationCanceledException)
        {
            // Normal cancellation
        }
        catch (InvalidOperationException)
        {
            // Pipeline disposed / completed adding
        }
    }

    private void LoadWithProvider(string path, IPreviewProvider provider, PreviewContext context, CancellationToken ct)
    {
        try
        {
            if (ct.IsCancellationRequested)
            {
                return;
            }

            PreviewResult? result = provider.GetPreview(path, context, ct);
            if (result is null || ct.IsCancellationRequested)
            {
                return;
            }

            // Inject combined event if result has both text and image
            if (result.SixelData is not null && result.TextLines is not null)
            {
                _pipeline.Inject(new CombinedPreviewReadyEvent(
                    path,
                    result.TextLines,
                    result.SixelData,
                    result.SixelPixelWidth,
                    result.SixelPixelHeight,
                    result.FileTypeLabel,
                    result.IsRendered));
            }
            else if (result.SixelData is not null)
            {
                _pipeline.Inject(new ImagePreviewReadyEvent(
                    path,
                    result.SixelData,
                    result.SixelPixelWidth,
                    result.SixelPixelHeight,
                    result.FileTypeLabel ?? "Image"));
            }
            else if (result.TextLines is not null)
            {
                _pipeline.Inject(new PreviewReadyEvent(
                    path,
                    result.TextLines,
                    result.FileTypeLabel,
                    result.IsRendered,
                    result.IsPlaceholder));
            }
        }
        catch (OperationCanceledException)
        {
            // Normal cancellation
        }
        catch (InvalidOperationException)
        {
            // Pipeline disposed / completed adding
        }
    }
}
