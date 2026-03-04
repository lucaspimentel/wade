using Wade.FileSystem;
using Wade.Highlighting;
using Wade.Terminal;

namespace Wade;

internal sealed class PreviewLoader
{
    private readonly InputPipeline _pipeline;
    private CancellationTokenSource? _cts;

    public PreviewLoader(InputPipeline pipeline)
    {
        _pipeline = pipeline;
    }

    public void BeginLoad(string path)
    {
        _cts?.Cancel();
        _cts?.Dispose();
        _cts = new CancellationTokenSource();
        var token = _cts.Token;

        Task.Run(() => LoadPreview(path, token), token);
    }

    public void Cancel()
    {
        _cts?.Cancel();
        _cts?.Dispose();
        _cts = null;
    }

    private void LoadPreview(string path, CancellationToken ct)
    {
        try
        {
            if (ct.IsCancellationRequested)
                return;

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
