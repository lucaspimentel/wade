using System.Diagnostics;
using Wade.Terminal;

namespace Wade.Highlighting;

internal static class GlowRenderer
{
    public static bool IsAvailable => CliTool.IsAvailable("glow", "--version", requireZeroExitCode: true);

    // Standard dark terminal 16-color palette (indices 0-15)
    private static readonly Color[] Palette =
    [
        new(0, 0, 0),       // 0  Black
        new(187, 0, 0),     // 1  Red
        new(0, 187, 0),     // 2  Green
        new(187, 187, 0),   // 3  Yellow
        new(0, 0, 187),     // 4  Blue
        new(187, 0, 187),   // 5  Magenta
        new(0, 187, 187),   // 6  Cyan
        new(187, 187, 187), // 7  White
        new(85, 85, 85),    // 8  Bright Black
        new(255, 85, 85),   // 9  Bright Red
        new(85, 255, 85),   // 10 Bright Green
        new(255, 255, 85),  // 11 Bright Yellow
        new(85, 85, 255),   // 12 Bright Blue
        new(255, 85, 255),  // 13 Bright Magenta
        new(85, 255, 255),  // 14 Bright Cyan
        new(255, 255, 255), // 15 Bright White
    ];

    public static StyledLine[]? Render(string filePath, int width, CancellationToken ct)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "glow",
                ArgumentList = { "--style", "dark", "--width", width.ToString(), filePath },
            };
            psi.Environment["CLICOLOR_FORCE"] = "1";

            string? output = CliTool.Run(psi, 5000, ct);

            if (output is null)
            {
                return null;
            }

            ct.ThrowIfCancellationRequested();
            return ParseAnsiOutput(output);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            return null;
        }
    }

    internal static StyledLine[] ParseAnsiOutput(string output)
    {
        var lines = new List<StyledLine>();
        int pos = 0;

        while (pos <= output.Length)
        {
            int lineEnd = output.IndexOf('\n', pos);
            if (lineEnd < 0)
            {
                lineEnd = output.Length;
            }

            // Strip \r if present
            int lineEndTrim = lineEnd > pos && output[lineEnd - 1] == '\r' ? lineEnd - 1 : lineEnd;
            var lineSpan = output.AsSpan(pos, lineEndTrim - pos);

            var (text, charStyles) = ParseAnsiLine(lineSpan);

            // Right-trim trailing spaces
            int trimEnd = text.Length;
            while (trimEnd > 0 && text[trimEnd - 1] == ' ')
            {
                trimEnd--;
            }

            if (trimEnd < text.Length)
            {
                text = text[..trimEnd];
                if (charStyles is not null && trimEnd < charStyles.Length)
                {
                    charStyles = charStyles[..trimEnd];
                }
            }

            lines.Add(new StyledLine(text, null, charStyles));

            pos = lineEnd + 1;
            if (lineEnd == output.Length)
            {
                break;
            }
        }

        // Remove trailing empty lines
        while (lines.Count > 0 && lines[^1].Text.Length == 0)
        {
            lines.RemoveAt(lines.Count - 1);
        }

        return lines.ToArray();
    }

    private static (string Text, CellStyle[]? CharStyles) ParseAnsiLine(ReadOnlySpan<char> line)
    {
        var textBuf = new List<char>();
        var styles = new List<CellStyle>();
        var currentStyle = CellStyle.Default;
        int i = 0;

        while (i < line.Length)
        {
            if (line[i] == '\x1b' && i + 1 < line.Length && line[i + 1] == '[')
            {
                // Parse CSI sequence
                i += 2;
                var parsedStyle = ParseSgr(line, ref i, currentStyle);
                currentStyle = parsedStyle;
            }
            else
            {
                textBuf.Add(line[i]);
                styles.Add(currentStyle);
                i++;
            }
        }

        string text = new(textBuf.ToArray());
        bool hasAnyStyle = false;
        foreach (var s in styles)
        {
            if (s != CellStyle.Default)
            {
                hasAnyStyle = true;
                break;
            }
        }

        return (text, hasAnyStyle ? styles.ToArray() : null);
    }

    private static CellStyle ParseSgr(ReadOnlySpan<char> line, ref int i, CellStyle current)
    {
        // Collect parameters
        var parameters = new List<int>();
        int num = 0;
        bool hasNum = false;

        while (i < line.Length)
        {
            char c = line[i];
            if (c is >= '0' and <= '9')
            {
                num = num * 10 + (c - '0');
                hasNum = true;
                i++;
            }
            else if (c == ';')
            {
                parameters.Add(hasNum ? num : 0);
                num = 0;
                hasNum = false;
                i++;
            }
            else
            {
                // End of sequence
                parameters.Add(hasNum ? num : 0);
                i++; // skip the terminator
                if (c != 'm')
                {
                    // Not an SGR sequence, ignore
                    return current;
                }

                break;
            }
        }

        Color? fg = current.Fg;
        Color? bg = current.Bg;
        bool bold = current.Bold;
        bool dim = current.Dim;
        bool underline = current.Underline;
        bool inverse = current.Inverse;
        bool strikethrough = current.Strikethrough;

        for (int p = 0; p < parameters.Count; p++)
        {
            int code = parameters[p];
            switch (code)
            {
                case 0: // reset
                    fg = null;
                    bg = null;
                    bold = false;
                    dim = false;
                    underline = false;
                    inverse = false;
                    strikethrough = false;
                    break;
                case 1:
                    bold = true;
                    break;
                case 2:
                    dim = true;
                    break;
                case 4:
                    underline = true;
                    break;
                case 7:
                    inverse = true;
                    break;
                case 9:
                    strikethrough = true;
                    break;
                case 22:
                    bold = false;
                    dim = false;
                    break;
                case 24:
                    underline = false;
                    break;
                case 27:
                    inverse = false;
                    break;
                case 29:
                    strikethrough = false;
                    break;

                // FG standard colors 30-37
                case >= 30 and <= 37:
                    fg = Palette[code - 30];
                    break;
                case 39: // default fg
                    fg = null;
                    break;

                // BG standard colors 40-47
                case >= 40 and <= 47:
                    bg = Palette[code - 40];
                    break;
                case 49: // default bg
                    bg = null;
                    break;

                // FG bright colors 90-97
                case >= 90 and <= 97:
                    fg = Palette[code - 90 + 8];
                    break;

                // BG bright colors 100-107
                case >= 100 and <= 107:
                    bg = Palette[code - 100 + 8];
                    break;

                // 256-color: 38;5;N or 48;5;N
                case 38 when p + 2 < parameters.Count && parameters[p + 1] == 5:
                    fg = Color256(parameters[p + 2]);
                    p += 2;
                    break;
                case 48 when p + 2 < parameters.Count && parameters[p + 1] == 5:
                    bg = Color256(parameters[p + 2]);
                    p += 2;
                    break;

                // 24-bit color: 38;2;R;G;B or 48;2;R;G;B
                case 38 when p + 4 < parameters.Count && parameters[p + 1] == 2:
                    fg = new Color((byte)parameters[p + 2], (byte)parameters[p + 3], (byte)parameters[p + 4]);
                    p += 4;
                    break;
                case 48 when p + 4 < parameters.Count && parameters[p + 1] == 2:
                    bg = new Color((byte)parameters[p + 2], (byte)parameters[p + 3], (byte)parameters[p + 4]);
                    p += 4;
                    break;
            }
        }

        return new CellStyle(fg, bg, bold, dim, inverse, underline, strikethrough);
    }

    private static Color? Color256(int index)
    {
        if (index is >= 0 and <= 15)
        {
            return Palette[index];
        }

        if (index is >= 16 and <= 231)
        {
            // 6x6x6 color cube
            int n = index - 16;
            int b = n % 6;
            int g = (n / 6) % 6;
            int r = n / 36;
            return new Color(
                (byte)(r == 0 ? 0 : 55 + r * 40),
                (byte)(g == 0 ? 0 : 55 + g * 40),
                (byte)(b == 0 ? 0 : 55 + b * 40));
        }

        if (index is >= 232 and <= 255)
        {
            // Grayscale ramp
            byte v = (byte)(8 + (index - 232) * 10);
            return new Color(v, v, v);
        }

        return null;
    }
}
