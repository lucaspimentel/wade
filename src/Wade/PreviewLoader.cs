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
    private bool _glowEnabled;
    private bool _zipPreviewEnabled;
    private bool _hexPreviewEnabled;
    private bool _pdfPreviewEnabled;
    private int _paneWidthCells;
    private int _paneHeightCells;
    private int _cellPixelWidth = 8;
    private int _cellPixelHeight = 16;

#pragma warning disable CSLINT221 // Consider using a primary constructor
    public PreviewLoader(InputPipeline pipeline)
#pragma warning restore CSLINT221
    {
        _pipeline = pipeline;
    }

    public void Configure(bool imagePreviewsEnabled, int paneWidthCells, int paneHeightCells,
        int cellPixelWidth, int cellPixelHeight, bool glowEnabled, bool zipPreviewEnabled, bool hexPreviewEnabled,
        bool pdfPreviewEnabled)
    {
        _imagePreviewsEnabled = imagePreviewsEnabled;
        _glowEnabled = glowEnabled;
        _zipPreviewEnabled = zipPreviewEnabled;
        _hexPreviewEnabled = hexPreviewEnabled;
        _pdfPreviewEnabled = pdfPreviewEnabled;
        _paneWidthCells = paneWidthCells;
        _paneHeightCells = paneHeightCells;
        _cellPixelWidth = cellPixelWidth;
        _cellPixelHeight = cellPixelHeight;
    }

    public void BeginLoad(string path)
    {
        _cts?.Cancel();
        _cts?.Dispose();
        _cts = new CancellationTokenSource();
        var token = _cts.Token;

        bool imageEnabled = _imagePreviewsEnabled;
        bool glowEnabled = _glowEnabled;
        bool zipEnabled = _zipPreviewEnabled;
        bool hexEnabled = _hexPreviewEnabled;
        bool pdfEnabled = _pdfPreviewEnabled;
        int paneW = _paneWidthCells;
        int paneH = _paneHeightCells;
        int cellW = _cellPixelWidth;
        int cellH = _cellPixelHeight;

        Task.Run(() => LoadPreview(path, imageEnabled, glowEnabled, zipEnabled, hexEnabled, pdfEnabled, paneW, paneH, cellW, cellH, token), token);
    }

    public void Cancel()
    {
        _cts?.Cancel();
        _cts?.Dispose();
        _cts = null;
    }

    private void LoadPreview(string path, bool imageEnabled, bool glowEnabled, bool zipEnabled, bool hexEnabled, bool pdfEnabled,
        int paneW, int paneH, int cellW, int cellH, CancellationToken ct)
    {
        try
        {
            if (ct.IsCancellationRequested)
            {
                return;
            }

            // Try image preview first
            if (imageEnabled && ImagePreview.IsImageFile(path))
            {
                var result = ImagePreview.Load(path, paneW, paneH, cellW, cellH, ct);
                if (result is not null)
                {
                    if (ct.IsCancellationRequested)
                    {
                        return;
                    }

                    _pipeline.Inject(new ImagePreviewReadyEvent(path, result.SixelData, result.PixelWidth, result.PixelHeight, result.Label));
                    return;
                }
                // Fall through to text/binary preview on failure
            }

            // Try convert-to-image preview (PDF, etc.)
            if (imageEnabled && pdfEnabled && ImageConverter.CanConvert(path))
            {
                string? tempImagePath = ImageConverter.ConvertToImage(path, ct);
                if (tempImagePath is not null)
                {
                    try
                    {
                        var result = ImagePreview.Load(tempImagePath, paneW, paneH, cellW, cellH, ct);
                        if (result is not null && !ct.IsCancellationRequested)
                        {
                            string docExt = Path.GetExtension(path).TrimStart('.').ToUpperInvariant();
                            string label = $"{docExt} Document (page 1)";
                            _pipeline.Inject(new ImagePreviewReadyEvent(path, result.SixelData,
                                result.PixelWidth, result.PixelHeight, label));
                            return;
                        }
                    }
                    finally
                    {
                        try { File.Delete(tempImagePath); } catch { }
                    }
                }
            }

            // Try glow for markdown files
            var ext = Path.GetExtension(path);
            if (ext.Equals(".md", StringComparison.OrdinalIgnoreCase) ||
                ext.Equals(".markdown", StringComparison.OrdinalIgnoreCase))
            {
                if (glowEnabled && GlowRenderer.IsAvailable)
                {
                    var glowLines = GlowRenderer.Render(path, paneW - 2, ct);
                    if (glowLines is not null)
                    {
                        if (ct.IsCancellationRequested)
                        {
                            return;
                        }

                        string glowLabel = FilePreview.GetFileTypeLabel(path) ?? "Markdown";
                        _pipeline.Inject(new PreviewReadyEvent(path, glowLines, glowLabel, null, null, IsRendered: true));
                        return;
                    }
                }
            }

            // Try zip preview
            if (zipEnabled && ZipPreview.IsZipFile(path))
            {
                var zipLines = ZipPreview.GetPreviewLines(path, ct);
                if (zipLines is not null)
                {
                    if (ct.IsCancellationRequested)
                    {
                        return;
                    }
                    string label = FilePreview.GetFileTypeLabel(path) ?? "Archive";
                    var zipStyled = zipLines.Select(l => new StyledLine(l, null)).ToArray();
                    _pipeline.Inject(new PreviewReadyEvent(path, zipStyled, label, null, null, IsRendered: true));
                    return;
                }
            }

            // Try hex preview for binary files
            if (hexEnabled && FilePreview.IsBinary(path))
            {
                var hexLines = HexPreview.GetPreviewLines(path, ct);
                if (hexLines is not null)
                {
                    if (ct.IsCancellationRequested)
                    {
                        return;
                    }

                    string hexLabel = FilePreview.GetFileTypeLabel(path) ?? "Binary";
                    _pipeline.Inject(new PreviewReadyEvent(path, hexLines, hexLabel, null, null, IsRendered: true));
                    return;
                }
            }

            var rawLines = FilePreview.GetPreviewLines(path, out var metadata);

            if (ct.IsCancellationRequested)
            {
                return;
            }

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
            {
                return;
            }

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
