namespace Wade.Preview;

internal static class CliToolHints
{
    private static readonly HashSet<string> s_mediaExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".mp3", ".flac", ".wav", ".ogg", ".aac", ".wma", ".m4a", ".opus", ".aiff",
        ".mp4", ".mkv", ".avi", ".mov", ".wmv", ".webm", ".flv", ".m4v", ".ts", ".mpg", ".mpeg",
    };

    /// <summary>
    /// Returns an install hint for a missing CLI tool that could provide preview/metadata for the given file,
    /// or null if no hint applies.
    /// </summary>
    public static string? GetHint(string path)
    {
        string ext = Path.GetExtension(path);

        if (ext.Equals(".pdf", StringComparison.OrdinalIgnoreCase))
        {
            bool hasPdftopng = CliTool.IsAvailable("pdftopng");
            bool hasPdfinfo = CliTool.IsAvailable("pdfinfo", "-v");

            if (!hasPdftopng && !hasPdfinfo)
            {
                return "Install xpdf CLI tools for PDF preview and metadata (pdftopng, pdfinfo)";
            }

            if (!hasPdftopng)
            {
                return "Install pdftopng for PDF image preview (xpdf)";
            }

            if (!hasPdfinfo)
            {
                return "Install pdfinfo for PDF metadata (xpdf or poppler-utils)";
            }
        }

        if (s_mediaExtensions.Contains(ext))
        {
            bool hasFfprobe = CliTool.IsAvailable("ffprobe", "-version", requireZeroExitCode: true);
            bool hasMediainfo = CliTool.IsAvailable("mediainfo", "--version");

            if (!hasFfprobe && !hasMediainfo)
            {
                return "Install ffprobe or mediainfo for media metadata";
            }
        }

        return null;
    }
}
