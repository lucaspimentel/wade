using Wade.Highlighting;
using Wade.Terminal;

namespace Wade.FileSystem;

internal static class HexPreview
{
    private const int MaxBytes = 64 * 1024; // 64 KB
    private const int BytesPerRow = 16;

    // Colors
    private static readonly CellStyle s_offsetStyle = new(new Color(100, 120, 150), null, Dim: true);   // dim blue-gray
    private static readonly CellStyle s_hexStyle = new(new Color(180, 180, 180), null);                  // light gray
    private static readonly CellStyle s_nullByteStyle = new(new Color(100, 100, 100), null, Dim: true);  // dimmer for 0x00
    private static readonly CellStyle s_asciiStyle = new(new Color(120, 200, 120), null);                // green
    private static readonly CellStyle s_dotStyle = new(new Color(100, 100, 100), null, Dim: true);       // dim non-printable
    private static readonly CellStyle s_separatorStyle = new(new Color(80, 80, 80), null, Dim: true);    // dim separators

    public static StyledLine[]? GetPreviewLines(string path, CancellationToken ct)
    {
        try
        {
            byte[] data;
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            int toRead = (int)Math.Min(fs.Length, MaxBytes);
            data = new byte[toRead];
            int totalRead = 0;
            while (totalRead < toRead)
            {
                if (ct.IsCancellationRequested)
                {
                    return null;
                }

                int n = fs.Read(data, totalRead, toRead - totalRead);
                if (n == 0)
                {
                    break;
                }

                totalRead += n;
            }

            if (totalRead < toRead)
            {
                Array.Resize(ref data, totalRead);
            }

            if (data.Length == 0)
            {
                return [new StyledLine("[empty file]", null)];
            }

            int rowCount = (data.Length + BytesPerRow - 1) / BytesPerRow;
            var lines = new StyledLine[rowCount];

            for (int row = 0; row < rowCount; row++)
            {
                if (ct.IsCancellationRequested)
                {
                    return null;
                }

                int offset = row * BytesPerRow;
                int count = Math.Min(BytesPerRow, data.Length - offset);
                lines[row] = FormatRow(data, offset, count);
            }

            return lines;
        }
        catch (OperationCanceledException)
        {
            return null;
        }
        catch
        {
            return null;
        }
    }

    private static StyledLine FormatRow(byte[] data, int offset, int count)
    {
        // Format: "00000000  48 65 6C 6C 6F 20 57 6F  72 6C 64 21 0A 00 FF FE  |Hello World!....|"
        // Offset(8) + "  " + hex left(24) + " " + hex right(24) + " " + "|" + ascii(16) + "|"
        // Total max: 8 + 2 + 24 + 1 + 24 + 1 + 1 + 16 + 1 = 78

        var chars = new char[78];
        var styles = new CellStyle[78];

        // Fill with spaces
        Array.Fill(chars, ' ');

        // Offset (8 hex digits)
        string offsetStr = offset.ToString("X8");
        for (int i = 0; i < 8; i++)
        {
            chars[i] = offsetStr[i];
            styles[i] = s_offsetStyle;
        }

        // Two-space separator
        styles[8] = s_separatorStyle;
        styles[9] = s_separatorStyle;

        // Hex bytes
        for (int i = 0; i < count; i++)
        {
            byte b = data[offset + i];
            int hexPos = 10 + (i * 3);
            if (i >= 8)
            {
                hexPos++; // extra space between groups
            }

            string hex = b.ToString("X2");
            chars[hexPos] = hex[0];
            chars[hexPos + 1] = hex[1];

            var style = b == 0 ? s_nullByteStyle : s_hexStyle;
            styles[hexPos] = style;
            styles[hexPos + 1] = style;
        }

        // Middle separator space at position 34 (between groups)
        styles[34] = s_separatorStyle;

        // "|" + ASCII + "|"
        int asciiStart = 60; // 10 + 48 + 1 + 1 = after hex section
        chars[asciiStart] = '|';
        styles[asciiStart] = s_separatorStyle;

        for (int i = 0; i < BytesPerRow; i++)
        {
            int pos = asciiStart + 1 + i;
            if (i < count)
            {
                byte b = data[offset + i];
                if (b is >= 0x20 and <= 0x7E)
                {
                    chars[pos] = (char)b;
                    styles[pos] = s_asciiStyle;
                }
                else
                {
                    chars[pos] = '.';
                    styles[pos] = b == 0 ? s_nullByteStyle : s_dotStyle;
                }
            }
            else
            {
                chars[pos] = ' ';
                styles[pos] = s_separatorStyle;
            }
        }

        chars[asciiStart + 1 + BytesPerRow] = '|';
        styles[asciiStart + 1 + BytesPerRow] = s_separatorStyle;

        // Trim trailing spaces for partial rows
        int len = asciiStart + 1 + BytesPerRow + 1; // always full width
        return new StyledLine(new string(chars, 0, len), null, styles[..len]);
    }
}
