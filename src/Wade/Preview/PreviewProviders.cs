using Wade.FileSystem;
using Wade.Highlighting;
using Wade.Highlighting.Languages;
using Wade.Imaging;

namespace Wade.Preview;

internal sealed class ImagePreviewProvider : IPreviewProvider
{
    public string Label => "Image";

    public bool CanPreview(string path, PreviewContext context) =>
        context.ImagePreviewsEnabled
        && ImagePreview.IsImageFile(path);

    public PreviewResult? GetPreview(string path, PreviewContext context, CancellationToken ct)
    {
        ImagePreviewResult? result = ImagePreview.Load(
            path,
            context.PaneWidthCells, context.PaneHeightCells,
            context.CellPixelWidth, context.CellPixelHeight,
            ct);

        if (result is null)
        {
            return null;
        }

        return new PreviewResult
        {
            SixelData = result.SixelData,
            SixelPixelWidth = result.PixelWidth,
            SixelPixelHeight = result.PixelHeight,
            FileTypeLabel = result.Label,
        };
    }
}

internal sealed class PdfPreviewProvider : IPreviewProvider
{
    public string Label => "PDF";

    public bool CanPreview(string path, PreviewContext context) =>
        context.PdfPreviewEnabled
        && context.SixelSupported
        && ImageConverter.CanConvert(path);

    public PreviewResult? GetPreview(string path, PreviewContext context, CancellationToken ct)
    {
        string? tempImagePath = ImageConverter.ConvertToImage(path, ct);
        if (tempImagePath is null)
        {
            return null;
        }

        try
        {
            ImagePreviewResult? result = ImagePreview.Load(
                tempImagePath,
                context.PaneWidthCells, context.PaneHeightCells,
                context.CellPixelWidth, context.CellPixelHeight,
                ct);

            if (result is null)
            {
                return null;
            }

            string docExt = Path.GetExtension(path).TrimStart('.').ToUpperInvariant();

            return new PreviewResult
            {
                SixelData = result.SixelData,
                SixelPixelWidth = result.PixelWidth,
                SixelPixelHeight = result.PixelHeight,
                FileTypeLabel = $"{docExt} Document (page 1)",
            };
        }
        finally
        {
            try
            {
                File.Delete(tempImagePath);
            }
            catch
            {
            }
        }
    }
}

internal sealed class MarkdigMarkdownPreviewProvider : IPreviewProvider
{
    public string Label => "Rendered markdown (built-in)";

    public bool CanPreview(string path, PreviewContext context)
    {
        if (!context.MarkdownPreviewEnabled)
        {
            return false;
        }

        string ext = Path.GetExtension(path);
        return ext.Equals(".md", StringComparison.OrdinalIgnoreCase)
               || ext.Equals(".markdown", StringComparison.OrdinalIgnoreCase);
    }

    public PreviewResult? GetPreview(string path, PreviewContext context, CancellationToken ct)
    {
        StyledLine[]? lines = MarkdigRenderer.Render(path, context.PaneWidthCells - 2, ct);

        if (lines is null)
        {
            return null;
        }

        return new PreviewResult
        {
            TextLines = lines,
            IsRendered = true,
            FileTypeLabel = FilePreview.GetFileTypeLabel(path) ?? "Markdown",
        };
    }
}

internal sealed class ZipContentsPreviewProvider : IPreviewProvider
{
    public string Label => "Archive contents";

    public bool CanPreview(string path, PreviewContext context) =>
        context.ZipPreviewEnabled && ZipPreview.IsZipFile(path);

    public PreviewResult? GetPreview(string path, PreviewContext context, CancellationToken ct)
    {
        string[]? zipLines = ZipPreview.GetPreviewLines(path, ct);
        if (zipLines is null)
        {
            return null;
        }

        var styledLines = new StyledLine[zipLines.Length + 2];
        styledLines[0] = new StyledLine("  Archive Contents", null);
        styledLines[1] = new StyledLine("  " + new string('\u2500', 16), null);
        for (int i = 0; i < zipLines.Length; i++)
        {
            styledLines[i + 2] = new StyledLine(zipLines[i], null);
        }

        return new PreviewResult
        {
            TextLines = styledLines,
            IsRendered = true,
            FileTypeLabel = FilePreview.GetFileTypeLabel(path) ?? "Archive",
        };
    }
}

internal sealed class TarContentsPreviewProvider : IPreviewProvider
{
    public string Label => "Archive contents";

    public bool CanPreview(string path, PreviewContext context) =>
        context.ZipPreviewEnabled && (TarPreview.IsTarArchive(path) || TarPreview.IsPlainGzip(path));

    public PreviewResult? GetPreview(string path, PreviewContext context, CancellationToken ct)
    {
        StyledLine[]? body;

        if (TarPreview.IsPlainGzip(path))
        {
            body = TarPreview.GetGzipStyledPreview(path, ct);
        }
        else
        {
            string[]? tarLines = TarPreview.GetPreviewLines(path, ct);
            if (tarLines is null)
            {
                return null;
            }

            body = new StyledLine[tarLines.Length];
            for (int i = 0; i < tarLines.Length; i++)
            {
                body[i] = new StyledLine(tarLines[i], null);
            }
        }

        if (body is null)
        {
            return null;
        }

        var styledLines = new StyledLine[body.Length + 2];
        styledLines[0] = new StyledLine("  Archive Contents", null);
        styledLines[1] = new StyledLine("  " + new string('\u2500', 16), null);
        Array.Copy(body, 0, styledLines, 2, body.Length);

        return new PreviewResult
        {
            TextLines = styledLines,
            IsRendered = true,
            FileTypeLabel = FilePreview.GetFileTypeLabel(path) ?? "Archive",
        };
    }
}

internal sealed class TextPreviewProvider : IPreviewProvider
{
    public string Label => "Text";

    public bool CanPreview(string path, PreviewContext context) => !FilePreview.IsBinary(path);

    public PreviewResult? GetPreview(string path, PreviewContext context, CancellationToken ct)
    {
        string[] rawLines = FilePreview.GetPreviewLines(path, out FileMetadata metadata);

        if (ct.IsCancellationRequested)
        {
            return null;
        }

        if (metadata.IsBinary)
        {
            return new PreviewResult
            {
                TextLines = [new StyledLine("[binary file]", null)],
                IsRendered = true,
                IsPlaceholder = true,
                FileTypeLabel = FilePreview.GetFileTypeLabel(path) ?? "Binary",
            };
        }

        StyledLine[] styledLines = SyntaxHighlighter.Highlight(rawLines, path);

        return new PreviewResult
        {
            TextLines = styledLines,
            IsPlaceholder = metadata.PlaceholderMessage is not null,
            FileTypeLabel = FilePreview.GetFileTypeLabel(path) ?? "Text",
        };
    }
}

internal sealed class HexPreviewProvider : IPreviewProvider
{
    public string Label => "Hex dump";

    public bool CanPreview(string path, PreviewContext context) => true;

    public PreviewResult? GetPreview(string path, PreviewContext context, CancellationToken ct)
    {
        StyledLine[]? hexLines = HexPreview.GetPreviewLines(path, ct);
        if (hexLines is null)
        {
            return null;
        }

        return new PreviewResult
        {
            TextLines = hexLines,
            IsRendered = true,
            FileTypeLabel = FilePreview.GetFileTypeLabel(path) ?? "Binary",
        };
    }
}

internal sealed class NonePreviewProvider : IPreviewProvider
{
    public string Label => "None";

    public bool CanPreview(string path, PreviewContext context) => true;

    public PreviewResult? GetPreview(string path, PreviewContext context, CancellationToken ct) =>
        new()
        {
            TextLines = [],
            IsPlaceholder = true,
        };
}

internal sealed class DiffPreviewProvider : IPreviewProvider
{
    public string Label => "Git diff";

    public bool CanPreview(string path, PreviewContext context) =>
        context.RepoRoot is not null
        && context.GitStatus.HasValue
        && (context.GitStatus.Value.HasFlag(GitFileStatus.Modified)
            || context.GitStatus.Value.HasFlag(GitFileStatus.Staged));

    public PreviewResult? GetPreview(string path, PreviewContext context, CancellationToken ct)
    {
        bool hasModified = context.GitStatus!.Value.HasFlag(GitFileStatus.Modified);
        bool staged = !hasModified && context.GitStatus.Value.HasFlag(GitFileStatus.Staged);

        string[]? diffLines = GitUtils.GetDiff(context.RepoRoot!, path, staged, ct);
        if (diffLines is null || diffLines.Length == 0)
        {
            return null;
        }

        var lang = new DiffLanguage();
        byte state = 0;
        var styledLines = new StyledLine[diffLines.Length];

        for (int i = 0; i < diffLines.Length; i++)
        {
            styledLines[i] = lang.TokenizeLine(diffLines[i], ref state);
        }

        return new PreviewResult
        {
            TextLines = styledLines,
            IsRendered = true,
            FileTypeLabel = "Diff",
        };
    }
}
