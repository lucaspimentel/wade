using System.Text;
using Wade.Terminal;

namespace Wade.Tests;

/// <summary>
/// Executes ScreenBuffer golden-frame scenario files (.scn format) and captures
/// the output of each flush op. The same format is consumed by the Rust port's
/// harness in src/wade-rs/tests/golden_frames.rs.
/// </summary>
internal static class ScreenBufferScenarioRunner
{
    public sealed record Scenario(string Name, List<string> FlushOutputs);

    public static Scenario Run(string scenarioPath)
    {
        var buffer = default(ScreenBuffer)!;
        bool started = false;
        var sb = new StringBuilder();
        var flushes = new List<string>();

        foreach (string rawLine in File.ReadAllLines(scenarioPath))
        {
            string line = rawLine.Trim();
            if (line.Length == 0 || line.StartsWith('#'))
            {
                continue;
            }

            string[] tokens = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            switch (tokens[0])
            {
                case "resize":
                    if (started)
                    {
                        buffer.Resize(ParseInt(tokens[1]), ParseInt(tokens[2]));
                    }
                    else
                    {
                        buffer = new ScreenBuffer(ParseInt(tokens[1]), ParseInt(tokens[2]));
                        started = true;
                    }

                    break;
                case "put":
                    buffer.Put(ParseInt(tokens[1]), ParseInt(tokens[2]), ParseChar(tokens[3]), ParseStyle(tokens[4..]));
                    break;
                case "string":
                    int maxWidth = tokens.Length > 6 ? ParseInt(tokens[6]) : int.MaxValue;
                    buffer.WriteString(ParseInt(tokens[1]), ParseInt(tokens[2]), tokens[3], ParseStyle(tokens[4..5]), maxWidth);
                    break;
                case "fillrow":
                    buffer.FillRow(ParseInt(tokens[1]), ParseInt(tokens[2]), ParseInt(tokens[3]), ParseChar(tokens[4]), ParseStyle(tokens[5..]));
                    break;
                case "clear":
                    buffer.Clear();
                    break;
                case "fullredraw":
                    buffer.ForceFullRedraw();
                    break;
                case "flush":
                    buffer.Serialize(sb);
                    flushes.Add(sb.ToString());
                    break;
                default:
                    throw new InvalidDataException($"Unknown op '{tokens[0]}' in {scenarioPath}");
            }
        }

        if (!started)
        {
            throw new InvalidDataException($"Scenario {scenarioPath} has no resize op");
        }

        return new Scenario(Path.GetFileName(scenarioPath), flushes);
    }

    private static int ParseInt(string token) => int.Parse(token, System.Globalization.CultureInfo.InvariantCulture);

    private static char ParseChar(string token)
    {
        if (token.Length == 1)
        {
            return token[0];
        }

        // Escaped forms: \uXXXX
        if (token.StartsWith("\\u"))
        {
            return (char)Convert.ToInt32(token[2..], 16);
        }

        throw new InvalidDataException($"Invalid char token '{token}'");
    }

    private static CellStyle ParseStyle(ReadOnlySpan<string> tokens)
    {
        // tokens: FGR FGG FGB BGR BGG BGB FLAGS (put/fillrow) or FLAGS only (string op)
        Color? fg = null, bg = null;
        string flags;
        if (tokens.Length >= 7)
        {
            int fgr = ParseInt(tokens[0]);
            int fgg = ParseInt(tokens[1]);
            int fgb = ParseInt(tokens[2]);
            int bgr = ParseInt(tokens[3]);
            int bgg = ParseInt(tokens[4]);
            int bgb = ParseInt(tokens[5]);
            fg = fgr < 0 ? null : new Color((byte)fgr, (byte)fgg, (byte)fgb);
            bg = bgr < 0 ? null : new Color((byte)bgr, (byte)bgg, (byte)bgb);
            flags = tokens[6];
        }
        else
        {
            flags = tokens[0];
        }

        return new CellStyle(fg, bg, flags.Contains('b'), flags.Contains('d'), flags.Contains('i'), flags.Contains('u'), flags.Contains('s'));
    }

    public static string FormatGolden(IReadOnlyList<string> flushes)
    {
        var sb = new StringBuilder();
        for (int i = 0; i < flushes.Count; i++)
        {
            sb.Append("=== flush ").Append(i).Append(" ===").Append('\n');
            sb.Append(flushes[i]).Append('\n');
        }

        return sb.ToString();
    }
}
