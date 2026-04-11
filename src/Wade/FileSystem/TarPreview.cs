using System.Formats.Tar;
using System.IO.Compression;
using System.Text;
using Wade.Highlighting;
using Wade.UI;

namespace Wade.FileSystem;

internal enum TarFormat
{
    Tar,
    TarGzip,
    Gzip,
}

internal readonly record struct TarArchiveStats(
    int Files,
    long TotalSize,
    TarFormat Format,
    long? CompressedSize,
    long? UncompressedHint);

internal static class TarPreview
{
    private const int MaxEntries = 100;
    private const int TextHeadLines = 30;
    private const int BinaryCheckSize = 4096;
    private const int TarRecordSize = 512;
    private const int UstarMagicOffset = 257;
    private const long IsizeSafetyLimit = (long)3584 * 1024 * 1024; // ~3.5 GiB

    public static bool IsTarArchive(string path)
    {
        if (string.IsNullOrEmpty(path))
        {
            return false;
        }

        return path.EndsWith(".tar", StringComparison.OrdinalIgnoreCase)
            || path.EndsWith(".tar.gz", StringComparison.OrdinalIgnoreCase)
            || path.EndsWith(".tgz", StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsPlainGzip(string path)
    {
        if (string.IsNullOrEmpty(path))
        {
            return false;
        }

        return path.EndsWith(".gz", StringComparison.OrdinalIgnoreCase)
            && !path.EndsWith(".tar.gz", StringComparison.OrdinalIgnoreCase);
    }

    public static string[]? GetPreviewLines(string path, CancellationToken ct)
    {
        try
        {
            if (path.EndsWith(".tar", StringComparison.OrdinalIgnoreCase))
            {
                using FileStream fs = File.OpenRead(path);
                return RenderTarEntries(fs, ct);
            }

            if (path.EndsWith(".tar.gz", StringComparison.OrdinalIgnoreCase)
                || path.EndsWith(".tgz", StringComparison.OrdinalIgnoreCase))
            {
                using FileStream fs = File.OpenRead(path);
                using GZipStream gz = new(fs, CompressionMode.Decompress);
                return RenderTarEntries(gz, ct);
            }

            if (path.EndsWith(".gz", StringComparison.OrdinalIgnoreCase))
            {
                return RenderGzip(path, ct);
            }

            return null;
        }
        catch (InvalidDataException)
        {
            return ["[invalid archive]"];
        }
        catch (EndOfStreamException)
        {
            return ["[invalid archive]"];
        }
        catch (OperationCanceledException)
        {
            return null;
        }
        catch (IOException)
        {
            return ["[invalid archive]"];
        }
    }

    public static TarArchiveStats? GetStats(string path, CancellationToken ct)
    {
        try
        {
            long compressedSize = new FileInfo(path).Length;

            if (path.EndsWith(".tar", StringComparison.OrdinalIgnoreCase))
            {
                using FileStream fs = File.OpenRead(path);
                (int files, long total) = CountTarEntries(fs, ct);
                return new TarArchiveStats(files, total, TarFormat.Tar, CompressedSize: null, UncompressedHint: null);
            }

            if (path.EndsWith(".tar.gz", StringComparison.OrdinalIgnoreCase)
                || path.EndsWith(".tgz", StringComparison.OrdinalIgnoreCase))
            {
                using FileStream fs = File.OpenRead(path);
                using GZipStream gz = new(fs, CompressionMode.Decompress);
                (int files, long total) = CountTarEntries(gz, ct);
                return new TarArchiveStats(files, total, TarFormat.TarGzip, compressedSize, UncompressedHint: null);
            }

            if (path.EndsWith(".gz", StringComparison.OrdinalIgnoreCase))
            {
                // Probe for tar payload first.
                if (LooksLikeTarGzip(path, ct))
                {
                    using FileStream fs = File.OpenRead(path);
                    using GZipStream gz = new(fs, CompressionMode.Decompress);
                    (int files, long total) = CountTarEntries(gz, ct);
                    return new TarArchiveStats(files, total, TarFormat.TarGzip, compressedSize, UncompressedHint: null);
                }

                long? uncompressed = null;
                uint? isize = ReadGzipIsize(path);
                if (isize is not null && compressedSize < IsizeSafetyLimit)
                {
                    uncompressed = isize.Value;
                }

                return new TarArchiveStats(
                    Files: 1,
                    TotalSize: uncompressed ?? 0,
                    Format: TarFormat.Gzip,
                    CompressedSize: compressedSize,
                    UncompressedHint: uncompressed);
            }

            return null;
        }
        catch (InvalidDataException)
        {
            return null;
        }
        catch (EndOfStreamException)
        {
            return null;
        }
        catch (OperationCanceledException)
        {
            return null;
        }
        catch (IOException)
        {
            return null;
        }
    }

    private static string[] RenderTarEntries(Stream tarStream, CancellationToken ct)
    {
        using TarReader reader = new(tarStream);

        List<(string Name, long Size)> entries = [];
        int totalFiles = 0;

        while (true)
        {
            if (ct.IsCancellationRequested)
            {
                throw new OperationCanceledException(ct);
            }

            TarEntry? entry = reader.GetNextEntry(copyData: false);
            if (entry is null)
            {
                break;
            }

            if (entry.EntryType == TarEntryType.Directory)
            {
                continue;
            }

            totalFiles++;

            if (entries.Count < MaxEntries)
            {
                entries.Add((entry.Name, entry.Length));
            }
        }

        if (totalFiles == 0)
        {
            return ["[empty archive]"];
        }

        entries.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));

        List<string> lines = new(entries.Count + 2);
        Span<char> sizeBuf = stackalloc char[16];

        //         "  nnnnnnnnnn  filename"
        lines.Add("        Size  Name");

        foreach ((string name, long size) in entries)
        {
            if (ct.IsCancellationRequested)
            {
                throw new OperationCanceledException(ct);
            }

            int sn = FormatHelpers.FormatSize(sizeBuf, size);
            string sizeText = sizeBuf[..sn].ToString();
            lines.Add($"  {sizeText,10}  {name}");
        }

        if (totalFiles > MaxEntries)
        {
            lines.Add($"... and {totalFiles - MaxEntries} more entries");
        }

        return [.. lines];
    }

    private static (int Files, long TotalSize) CountTarEntries(Stream tarStream, CancellationToken ct)
    {
        using TarReader reader = new(tarStream);

        int files = 0;
        long total = 0;

        while (true)
        {
            if (ct.IsCancellationRequested)
            {
                throw new OperationCanceledException(ct);
            }

            TarEntry? entry = reader.GetNextEntry(copyData: false);
            if (entry is null)
            {
                break;
            }

            if (entry.EntryType == TarEntryType.Directory)
            {
                continue;
            }

            files++;
            total += entry.Length;
        }

        return (files, total);
    }

    private static bool LooksLikeTarGzip(string path, CancellationToken ct)
    {
        try
        {
            using FileStream fs = File.OpenRead(path);
            using GZipStream gz = new(fs, CompressionMode.Decompress);

            Span<byte> buffer = stackalloc byte[TarRecordSize];
            int read = ReadFully(gz, buffer);

            if (ct.IsCancellationRequested)
            {
                return false;
            }

            if (read < UstarMagicOffset + 5)
            {
                return false;
            }

            return LooksLikeUstar(buffer[..read]);
        }
        catch (InvalidDataException)
        {
            return false;
        }
        catch (IOException)
        {
            return false;
        }
    }

    private static bool LooksLikeUstar(ReadOnlySpan<byte> header)
    {
        if (header.Length < UstarMagicOffset + 5)
        {
            return false;
        }

        ReadOnlySpan<byte> magic = header.Slice(UstarMagicOffset, 5);
        return magic[0] == (byte)'u'
            && magic[1] == (byte)'s'
            && magic[2] == (byte)'t'
            && magic[3] == (byte)'a'
            && magic[4] == (byte)'r';
    }

    private static string[] RenderGzip(string path, CancellationToken ct)
    {
        if (LooksLikeTarGzip(path, ct))
        {
            using FileStream fs = File.OpenRead(path);
            using GZipStream gz = new(fs, CompressionMode.Decompress);
            return RenderTarEntries(gz, ct);
        }

        List<string> lines =
        [
            BuildGzipMetadataLine(path),
            new string('\u2500', 16),
        ];

        GzipContent content = ReadGzipContent(path, ct);
        lines.AddRange(content.Lines);
        return [.. lines];
    }

    public static StyledLine[]? GetGzipStyledPreview(string path, CancellationToken ct)
    {
        try
        {
            if (LooksLikeTarGzip(path, ct))
            {
                using FileStream fs = File.OpenRead(path);
                using GZipStream gz = new(fs, CompressionMode.Decompress);
                string[] tarLines = RenderTarEntries(gz, ct);
                return WrapPlain(tarLines);
            }

            List<StyledLine> result =
            [
                new StyledLine(BuildGzipMetadataLine(path), null),
                new StyledLine(new string('\u2500', 16), null),
            ];

            GzipContent content = ReadGzipContent(path, ct);

            if (content.IsText && content.Lines.Length > 0)
            {
                string innerName = Path.GetFileNameWithoutExtension(path);
                StyledLine[] highlighted = SyntaxHighlighter.Highlight(content.Lines, innerName);
                result.AddRange(highlighted);
            }
            else
            {
                foreach (string line in content.Lines)
                {
                    result.Add(new StyledLine(line, null));
                }
            }

            return [.. result];
        }
        catch (InvalidDataException)
        {
            return [new StyledLine("[invalid archive]", null)];
        }
        catch (EndOfStreamException)
        {
            return [new StyledLine("[invalid archive]", null)];
        }
        catch (OperationCanceledException)
        {
            return null;
        }
        catch (IOException)
        {
            return [new StyledLine("[invalid archive]", null)];
        }
    }

    private static StyledLine[] WrapPlain(string[] lines)
    {
        var result = new StyledLine[lines.Length];
        for (int i = 0; i < lines.Length; i++)
        {
            result[i] = new StyledLine(lines[i], null);
        }

        return result;
    }

    private static string BuildGzipMetadataLine(string path)
    {
        string innerName = Path.GetFileNameWithoutExtension(path);
        long compressed = new FileInfo(path).Length;
        uint? isize = ReadGzipIsize(path);
        long? uncompressed = (isize is not null && compressed < IsizeSafetyLimit) ? isize.Value : null;

        Span<char> sizeBuf = stackalloc char[16];
        int sn = FormatHelpers.FormatSize(sizeBuf, compressed);
        string compressedText = sizeBuf[..sn].ToString();

        if (uncompressed is long uSize)
        {
            int un = FormatHelpers.FormatSize(sizeBuf, uSize);
            string uText = sizeBuf[..un].ToString();
            return $"[gzip] original: {innerName}  compressed: {compressedText}  uncompressed: ~{uText}";
        }

        return $"[gzip] original: {innerName}  compressed: {compressedText}";
    }

    private readonly record struct GzipContent(string[] Lines, bool IsText);

    private static GzipContent ReadGzipContent(string path, CancellationToken ct)
    {
        try
        {
            using FileStream fs = File.OpenRead(path);
            using GZipStream gz = new(fs, CompressionMode.Decompress);

            byte[] buffer = new byte[BinaryCheckSize];
            int read = ReadFully(gz, buffer);

            if (ct.IsCancellationRequested)
            {
                throw new OperationCanceledException(ct);
            }

            if (read == 0)
            {
                return new GzipContent(["[empty]"], IsText: false);
            }

            if (HasNullByte(buffer.AsSpan(0, read)))
            {
                return new GzipContent(["[binary content]"], IsText: false);
            }

            string text = Encoding.UTF8.GetString(buffer, 0, read);
            using StringReader sr = new(text);
            List<string> textLines = [];
            int shown = 0;
            while (shown < TextHeadLines)
            {
                string? line = sr.ReadLine();
                if (line is null)
                {
                    break;
                }

                textLines.Add(line);
                shown++;
            }

            return new GzipContent([.. textLines], IsText: true);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (InvalidDataException)
        {
            return new GzipContent(["[invalid gzip content]"], IsText: false);
        }
        catch (IOException)
        {
            return new GzipContent(["[read error]"], IsText: false);
        }
    }

    private static int ReadFully(Stream stream, Span<byte> buffer)
    {
        int total = 0;
        while (total < buffer.Length)
        {
            int read = stream.Read(buffer[total..]);
            if (read == 0)
            {
                break;
            }

            total += read;
        }

        return total;
    }

    private static bool HasNullByte(ReadOnlySpan<byte> buffer)
    {
        for (int i = 0; i < buffer.Length; i++)
        {
            if (buffer[i] == 0)
            {
                return true;
            }
        }

        return false;
    }

    private static uint? ReadGzipIsize(string path)
    {
        try
        {
            using FileStream fs = File.OpenRead(path);
            if (fs.Length < 4)
            {
                return null;
            }

            fs.Seek(-4, SeekOrigin.End);
            Span<byte> buf = stackalloc byte[4];
            int read = ReadFully(fs, buf);
            if (read < 4)
            {
                return null;
            }

            return (uint)buf[0]
                | ((uint)buf[1] << 8)
                | ((uint)buf[2] << 16)
                | ((uint)buf[3] << 24);
        }
        catch (IOException)
        {
            return null;
        }
    }
}
