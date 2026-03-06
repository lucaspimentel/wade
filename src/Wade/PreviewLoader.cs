using Wade.FileSystem;
using Wade.Highlighting;
using Wade.Imaging;
using Wade.Terminal;

namespace Wade;

internal sealed class PreviewLoader
{
    private readonly InputPipeline _pipeline;
    private CancellationTokenSource? _cts;
    private bool _imagePreviewsEnabled;
    private int _paneWidthCells;
    private int _paneHeightCells;

    public PreviewLoader(InputPipeline pipeline)
    {
        _pipeline = pipeline;
    }

    public void Configure(bool imagePreviewsEnabled, int paneWidthCells, int paneHeightCells)
    {
        _imagePreviewsEnabled = imagePreviewsEnabled;
        _paneWidthCells = paneWidthCells;
        _paneHeightCells = paneHeightCells;
    }

    public void BeginLoad(string path)
    {
        _cts?.Cancel();
        _cts?.Dispose();
        _cts = new CancellationTokenSource();
        var token = _cts.Token;

        bool imageEnabled = _imagePreviewsEnabled;
        int paneW = _paneWidthCells;
        int paneH = _paneHeightCells;

        Task.Run(() => LoadPreview(path, imageEnabled, paneW, paneH, token), token);
    }

    public void Cancel()
    {
        _cts?.Cancel();
        _cts?.Dispose();
        _cts = null;
    }

    private void LoadPreview(string path, bool imageEnabled, int paneW, int paneH, CancellationToken ct)
    {
        try
        {
            if (ct.IsCancellationRequested)
                return;

            // Try image preview first
            if (imageEnabled && ImagePreview.IsImageFile(path))
            {
                var result = ImagePreview.Load(path, paneW, paneH, ct);
                if (result is not null)
                {
                    if (ct.IsCancellationRequested)
                        return;

                    _pipeline.Inject(new ImagePreviewReadyEvent(path, result.SixelData, result.PixelWidth, result.PixelHeight, result.Label));
                    return;
                }
                // Fall through to text/binary preview on failure
            }

            var rawLines = FilePreview.GetPreviewLines(path, out var metadata);

            if (ct.IsCancellationRequested)
                return;

            string? fileTypeLabel;
            string? encoding;
            string? lineEnding;

            if (metadata.IsBinary)
            {
                fileTypeLabel = FilePreview.GetFileTypeLabel(path) ?? "Binary";
                encoding = null;
                lineEnding = null;
            }
            else
            {
                fileTypeLabel = FilePreview.GetFileTypeLabel(path) ?? "Text";
                encoding = metadata.Encoding;
                lineEnding = metadata.LineEnding;
            }

            var styledLines = SyntaxHighlighter.Highlight(rawLines, path);

            if (ct.IsCancellationRequested)
                return;

            _pipeline.Inject(new PreviewReadyEvent(path, styledLines, fileTypeLabel, encoding, lineEnding));
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
