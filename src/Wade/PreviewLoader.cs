using Wade.Preview;
using Wade.Terminal;

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
        var token = _cts.Token;

        Task.Run(() => LoadWithProvider(path, provider, context, token), token);
    }

    public void BeginLoad(
        string path,
        IMetadataProvider? metadataProvider,
        IPreviewProvider? previewProvider,
        PreviewContext context)
    {
        _cts?.Cancel();
        _cts?.Dispose();
        _cts = new CancellationTokenSource();
        var token = _cts.Token;

        Task.Run(() => LoadMetadataAndPreview(path, metadataProvider, previewProvider, context, token), token);
    }

    public void Cancel()
    {
        _cts?.Cancel();
        _cts?.Dispose();
        _cts = null;
    }

    private void LoadMetadataAndPreview(
        string path,
        IMetadataProvider? metadataProvider,
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

            // Load metadata first
            if (metadataProvider is not null)
            {
                var metadataResult = metadataProvider.GetMetadata(path, context, ct);

                if (metadataResult is not null && !ct.IsCancellationRequested)
                {
                    _pipeline.Inject(new MetadataReadyEvent(
                        path,
                        metadataResult.Sections,
                        metadataResult.FileTypeLabel));
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

            var result = provider.GetPreview(path, context, ct);
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
                    result.Encoding,
                    result.LineEnding,
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
                    result.Encoding,
                    result.LineEnding,
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
