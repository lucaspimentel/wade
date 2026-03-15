using Wade.FileSystem;
using Wade.Highlighting;
using Wade.Imaging;
using Wade.Preview;
using Wade.Terminal;

namespace Wade;

internal sealed class PreviewLoader
{
    private readonly InputPipeline _pipeline;
    private CancellationTokenSource? _cts;
    private bool _imagePreviewsEnabled;
    private bool _glowEnabled;
    private bool _zipPreviewEnabled;
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
        int cellPixelWidth, int cellPixelHeight, bool glowEnabled, bool zipPreviewEnabled,
        bool pdfPreviewEnabled)
    {
        _imagePreviewsEnabled = imagePreviewsEnabled;
        _glowEnabled = glowEnabled;
        _zipPreviewEnabled = zipPreviewEnabled;
        _pdfPreviewEnabled = pdfPreviewEnabled;
        _paneWidthCells = paneWidthCells;
        _paneHeightCells = paneHeightCells;
        _cellPixelWidth = cellPixelWidth;
        _cellPixelHeight = cellPixelHeight;
    }

    public void BeginLoad(string path, bool isCloudPlaceholder = false)
    {
        _cts?.Cancel();
        _cts?.Dispose();
        _cts = new CancellationTokenSource();
        var token = _cts.Token;

        if (isCloudPlaceholder)
        {
            Task.Run(() =>
            {
                if (token.IsCancellationRequested)
                {
                    return;
                }

                string label = FilePreview.GetFileTypeLabel(path) ?? "File";
                var line = new StyledLine("[cloud file — not downloaded]", null);
                _pipeline.Inject(new PreviewReadyEvent(path, [line], label, null, null, IsRendered: true));
            }, token);
            return;
        }

        bool imageEnabled = _imagePreviewsEnabled;
        bool glowEnabled = _glowEnabled;
        bool zipEnabled = _zipPreviewEnabled;
        bool pdfEnabled = _pdfPreviewEnabled;
        int paneW = _paneWidthCells;
        int paneH = _paneHeightCells;
        int cellW = _cellPixelWidth;
        int cellH = _cellPixelHeight;

        Task.Run(() => LoadPreview(path, imageEnabled, glowEnabled, zipEnabled, pdfEnabled, paneW, paneH, cellW, cellH, token), token);
    }

    public void BeginLoadDiff(string path, string repoRoot, bool staged)
    {
        _cts?.Cancel();
        _cts?.Dispose();
        _cts = new CancellationTokenSource();
        var token = _cts.Token;

        Task.Run(() => LoadDiff(path, repoRoot, staged, token), token);
    }

    public void BeginLoadHex(string path)
    {
        _cts?.Cancel();
        _cts?.Dispose();
        _cts = new CancellationTokenSource();
        var token = _cts.Token;

        Task.Run(() => LoadHex(path, token), token);
    }

    public void BeginLoad(string path, IPreviewProvider provider, PreviewContext context)
    {
        _cts?.Cancel();
        _cts?.Dispose();
        _cts = new CancellationTokenSource();
        var token = _cts.Token;

        Task.Run(() => LoadWithProvider(path, provider, context, token), token);
    }

    public void Cancel()
    {
        _cts?.Cancel();
        _cts?.Dispose();
        _cts = null;
    }

    private void LoadPreview(string path, bool imageEnabled, bool glowEnabled, bool zipEnabled, bool pdfEnabled,
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

    private void LoadHex(string path, CancellationToken ct)
    {
        try
        {
            if (ct.IsCancellationRequested)
            {
                return;
            }

            var hexLines = HexPreview.GetPreviewLines(path, ct);
            if (hexLines is null || ct.IsCancellationRequested)
            {
                return;
            }

            string hexLabel = FilePreview.GetFileTypeLabel(path) ?? "Binary";
            _pipeline.Inject(new PreviewReadyEvent(path, hexLines, hexLabel, null, null, IsRendered: true));
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

            // Inject image event if result has Sixel data
            if (result.SixelData is not null)
            {
                _pipeline.Inject(new ImagePreviewReadyEvent(
                    path,
                    result.SixelData,
                    result.SixelPixelWidth,
                    result.SixelPixelHeight,
                    result.FileTypeLabel ?? "Image"));
            }

            // Inject text event if result has text lines
            if (result.TextLines is not null)
            {
                _pipeline.Inject(new PreviewReadyEvent(
                    path,
                    result.TextLines,
                    result.FileTypeLabel,
                    result.Encoding,
                    result.LineEnding,
                    result.IsRendered));
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

    private void LoadDiff(string path, string repoRoot, bool staged, CancellationToken ct)
    {
        try
        {
            if (ct.IsCancellationRequested)
            {
                return;
            }

            var diffLines = GitUtils.GetDiff(repoRoot, path, staged, ct);
            if (diffLines is null || diffLines.Length == 0 || ct.IsCancellationRequested)
            {
                return;
            }

            var lang = new Highlighting.Languages.DiffLanguage();
            byte state = 0;
            var styledLines = new Highlighting.StyledLine[diffLines.Length];
            for (int i = 0; i < diffLines.Length; i++)
            {
                styledLines[i] = lang.TokenizeLine(diffLines[i], ref state);
            }

            if (ct.IsCancellationRequested)
            {
                return;
            }

            _pipeline.Inject(new PreviewReadyEvent(path, styledLines, "Diff", null, null, IsRendered: true));
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
