using System.Globalization;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;
using Wade.Terminal;

namespace Wade.Tests;

[SupportedOSPlatform("windows")]
public class InputDecodeGoldenTests
{
    private static readonly string GoldenDir = FindGoldenDir();

    public static TheoryData<string> FixtureFiles =>
        new([.. Directory.GetFiles(GoldenDir, "*.scn").Select(Path.GetFileName).Cast<string>()]);

    [Theory]
    [MemberData(nameof(FixtureFiles))]
    public void DecodeFixture_MatchesCSharpOutput(string fixtureFileName)
    {
        string fixturePath = Path.Combine(GoldenDir, fixtureFileName);
        string goldenPath = Path.ChangeExtension(fixturePath, ".golden.txt");
        (var records, int windowWidth, int windowHeight) = ParseFixture(fixturePath);
        List<InputEvent> events = WindowsInputSource.DecodeRecords(records.ToArray().AsSpan(), windowWidth, windowHeight);
        string actual = FormatEvents(events);

        if (Environment.GetEnvironmentVariable("WADE_UPDATE_GOLDENS") == "1" && !File.Exists(goldenPath))
        {
            File.WriteAllText(goldenPath, actual, new UTF8Encoding(false));
        }

        Assert.True(File.Exists(goldenPath), $"Missing golden file {goldenPath}; run with WADE_UPDATE_GOLDENS=1 to generate");
        string expected = File.ReadAllText(goldenPath);
        Assert.Equal(expected, actual);
    }

    internal static (List<WindowsInputSource.INPUT_RECORD> Records, int WindowWidth, int WindowHeight) ParseFixture(string path)
    {
        var records = new List<WindowsInputSource.INPUT_RECORD>();
        int windowWidth = 80, windowHeight = 25;

        foreach (string rawLine in File.ReadAllLines(path))
        {
            string line = rawLine.Trim();
            if (line.Length == 0 || line.StartsWith('#'))
            {
                continue;
            }

            string[] tokens = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            switch (tokens[0])
            {
                case "size":
                    windowWidth = ParseInt(tokens[1]);
                    windowHeight = ParseInt(tokens[2]);
                    break;
                case "kd":
                case "ku":
                    records.Add(MakeKey(ParseInt(tokens[1]), ParseChar(tokens[2]), ParseMods(tokens[3]), tokens[0] == "kd"));
                    break;
                case "mouse":
                    records.Add(MakeMouse((short)ParseInt(tokens[1]), (short)ParseInt(tokens[2]), (uint)ParseInt(tokens[3]), (uint)ParseInt(tokens[4]), (uint)ParseInt(tokens[5])));
                    break;
                case "buf":
                    records.Add(MakeBufferSize((short)ParseInt(tokens[1]), (short)ParseInt(tokens[2])));
                    break;
                case "other":
                    records.Add(new WindowsInputSource.INPUT_RECORD { EventType = 0 });
                    break;
                default:
                    throw new InvalidDataException($"Unknown record '{tokens[0]}' in {path}");
            }
        }

        return (records, windowWidth, windowHeight);
    }

    internal static WindowsInputSource.INPUT_RECORD MakeKey(int vk, char ch, uint controlKeyState, bool keyDown) =>
        new()
        {
            EventType = 0x0001,
            Event = new WindowsInputSource.INPUT_RECORD_UNION
            {
                KeyEvent = new WindowsInputSource.KEY_EVENT_RECORD
                {
                    bKeyDown = keyDown ? 1 : 0,
                    wVirtualKeyCode = (ushort)vk,
                    UnicodeChar = ch,
                    dwControlKeyState = controlKeyState,
                },
            },
        };

    internal static WindowsInputSource.INPUT_RECORD MakeMouse(short x, short y, uint buttonState, uint controlKeyState, uint eventFlags) =>
        new()
        {
            EventType = 0x0002,
            Event = new WindowsInputSource.INPUT_RECORD_UNION
            {
                MouseEvent = new WindowsInputSource.MOUSE_EVENT_RECORD
                {
                    X = x,
                    Y = y,
                    dwButtonState = buttonState,
                    dwControlKeyState = controlKeyState,
                    dwEventFlags = eventFlags,
                },
            },
        };

    internal static WindowsInputSource.INPUT_RECORD MakeBufferSize(short x, short y) =>
        new()
        {
            EventType = 0x0004,
            Event = new WindowsInputSource.INPUT_RECORD_UNION
            {
                WindowBufferSizeEvent = new WindowsInputSource.WINDOW_BUFFER_SIZE_RECORD { X = x, Y = y },
            },
        };

    // MODS encoding: '-' or subset of "sac"; maps to ControlKeyState bits
    internal static uint ParseMods(string token)
    {
        uint state = 0;
        if (token.Contains('s'))
        {
            state |= 0x0010; // ShiftPressed
        }

        if (token.Contains('a'))
        {
            state |= 0x0002; // LeftAltPressed
        }

        if (token.Contains('c'))
        {
            state |= 0x0008; // LeftCtrlPressed
        }

        return state;
    }

    internal static string FormatEvents(IEnumerable<InputEvent> events)
    {
        var sb = new StringBuilder();
        foreach (InputEvent evt in events)
        {
            switch (evt)
            {
                case KeyEvent k:
                    sb.Append("key ").Append((int)k.Key).Append(' ').Append(FormatChar(k.KeyChar)).Append(' ').Append(FormatMods(k.Shift, k.Alt, k.Control));
                    break;
                case PasteEvent p:
                    sb.Append("paste ").Append(p.Text);
                    break;
                case MouseEvent m:
                    sb.Append("mouse ").Append(FormatButton(m.Button)).Append(' ').Append(m.Row).Append(' ').Append(m.Col).Append(' ').Append(m.IsRelease ? "release" : "press");
                    break;
                case ResizeEvent r:
                    sb.Append("resize ").Append(r.Width).Append(' ').Append(r.Height);
                    break;
            }

            sb.Append('\n');
        }

        return sb.ToString();
    }

    internal static string FormatChar(char ch) => ch switch
    {
        '\0' => "-",
        < ' ' => $"\\u{(int)ch:X4}",
        '\x7f' => "\\u007F",
        _ => ch.ToString(),
    };

    internal static string FormatMods(bool shift, bool alt, bool control)
    {
        string mods = $"{(shift ? "s" : "")}{(alt ? "a" : "")}{(control ? "c" : "")}";
        return mods.Length == 0 ? "-" : mods;
    }

    private static string FormatButton(MouseButton button) => button switch
    {
        MouseButton.Left => "left",
        MouseButton.Right => "right",
        MouseButton.Middle => "middle",
        MouseButton.ScrollUp => "scrollup",
        MouseButton.ScrollDown => "scrolldown",
        _ => "none",
    };

    private static int ParseInt(string token) => int.Parse(token, CultureInfo.InvariantCulture);

    private static char ParseChar(string token) => token switch
    {
        "-" => '\0',
        _ when token.StartsWith("\\u") => (char)Convert.ToInt32(token[2..], 16),
        _ when token.Length == 1 => token[0],
        _ => throw new InvalidDataException($"Invalid char token '{token}'"),
    };

    private static string FindGoldenDir()
    {
        string? dir = AppContext.BaseDirectory;
        while (dir is not null)
        {
            string candidate = Path.Combine(dir, "tests", "golden", "input");
            if (Directory.Exists(candidate))
            {
                return candidate;
            }

            dir = Path.GetDirectoryName(dir.TrimEnd(Path.DirectorySeparatorChar));
        }

        throw new InvalidOperationException("Could not locate tests/golden/input directory");
    }
}
