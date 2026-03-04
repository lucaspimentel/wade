using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;

namespace Wade.Terminal;

[UnsupportedOSPlatform("windows")]
internal sealed class UnixInputSource : IInputSource
{
    private readonly Stream _stdin;
    private readonly byte[] _buf = new byte[64];
    private readonly Queue<InputEvent> _pending = new();
    private PosixSignalRegistration? _sigwinchReg;

    public UnixInputSource()
    {
        _stdin = Console.OpenStandardInput();
        _sigwinchReg = PosixSignalRegistration.Create(PosixSignal.SIGWINCH, ctx =>
        {
            ctx.Cancel = true;
            _pending.Enqueue(new ResizeEvent(Console.WindowWidth, Console.WindowHeight));
        });
    }

    public InputEvent? ReadNext(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            if (_pending.TryDequeue(out var queued))
                return queued;

            int bytesRead;
            try
            {
                // ReadByte blocks; we read one byte at a time for the leader,
                // then drain available bytes for sequences.
                bytesRead = _stdin.Read(_buf, 0, 1);
            }
            catch (OperationCanceledException)
            {
                return null;
            }

            if (bytesRead == 0)
            {
                // EOF on stdin
                Thread.Sleep(10);
                continue;
            }

            byte b = _buf[0];

            if (b == 0x1B) // ESC
            {
                // Wait briefly for more bytes (sequence vs standalone Escape)
                Thread.Sleep(50);
                int available = DrainAvailable(_buf, 1);
                int total = 1 + available;

                var events = VtParser.Parse(_buf.AsSpan(0, total));
                foreach (var evt in events)
                    _pending.Enqueue(evt);
            }
            else
            {
                var events = VtParser.Parse(_buf.AsSpan(0, 1));
                foreach (var evt in events)
                    _pending.Enqueue(evt);
            }
        }

        return null;
    }

    public void Dispose()
    {
        _sigwinchReg?.Dispose();
        _sigwinchReg = null;
    }

    private int DrainAvailable(byte[] buf, int offset)
    {
        int total = 0;
        // Non-blocking read: stdin.Read may block, so we use a polling approach
        while (offset + total < buf.Length)
        {
            // Use a short timeout to check if data is available
            var readTask = _stdin.ReadAsync(buf, offset + total, 1);
            if (!readTask.Wait(10))
                break;
            if (readTask.Result == 0)
                break;
            total++;
        }
        return total;
    }
}

internal static class VtParser
{
    public static List<InputEvent> Parse(ReadOnlySpan<byte> data)
    {
        var events = new List<InputEvent>();
        int i = 0;

        while (i < data.Length)
        {
            byte b = data[i];

            if (b == 0x1B) // ESC
            {
                if (i + 1 < data.Length && data[i + 1] == '[')
                {
                    // CSI sequence: ESC [ ...
                    i += 2;
                    i += ParseCsi(data[i..], events);
                }
                else if (i + 1 < data.Length && data[i + 1] == 'O')
                {
                    // SS3 sequence: ESC O ...
                    i += 2;
                    if (i < data.Length)
                    {
                        var key = data[i] switch
                        {
                            (byte)'P' => ConsoleKey.F1,
                            (byte)'Q' => ConsoleKey.F2,
                            (byte)'R' => ConsoleKey.F3,
                            (byte)'S' => ConsoleKey.F4,
                            (byte)'H' => ConsoleKey.Home,
                            (byte)'F' => ConsoleKey.End,
                            _ => (ConsoleKey)0,
                        };
                        if (key != 0)
                            events.Add(new KeyEvent(key, '\0', false, false, false));
                        i++;
                    }
                }
                else
                {
                    // Standalone Escape
                    events.Add(new KeyEvent(ConsoleKey.Escape, '\x1B', false, false, false));
                    i++;
                }
            }
            else if (b == 0x0D) // CR → Enter
            {
                events.Add(new KeyEvent(ConsoleKey.Enter, '\r', false, false, false));
                i++;
            }
            else if (b == 0x0A) // LF → Enter (some terminals)
            {
                events.Add(new KeyEvent(ConsoleKey.Enter, '\n', false, false, false));
                i++;
            }
            else if (b == 0x09) // Tab
            {
                events.Add(new KeyEvent(ConsoleKey.Tab, '\t', false, false, false));
                i++;
            }
            else if (b == 0x7F) // DEL → Backspace
            {
                events.Add(new KeyEvent(ConsoleKey.Backspace, '\x7F', false, false, false));
                i++;
            }
            else if (b == 0x08) // BS → Backspace
            {
                events.Add(new KeyEvent(ConsoleKey.Backspace, '\b', false, false, false));
                i++;
            }
            else if (b >= 0x01 && b <= 0x1A) // Ctrl+A through Ctrl+Z
            {
                char letter = (char)('A' + b - 1);
                var consoleKey = (ConsoleKey)letter;
                events.Add(new KeyEvent(consoleKey, (char)b, false, false, true));
                i++;
            }
            else if (b >= 0x20 && b <= 0x7E) // Printable ASCII
            {
                char c = (char)b;
                var consoleKey = CharToConsoleKey(c);
                events.Add(new KeyEvent(consoleKey, c, false, false, false));
                i++;
            }
            else if (b >= 0x80) // UTF-8 multibyte
            {
                int seqLen = Utf8SequenceLength(b);
                if (i + seqLen <= data.Length)
                {
                    var charSpan = data.Slice(i, seqLen);
                    char decoded = DecodeUtf8Char(charSpan);
                    events.Add(new KeyEvent((ConsoleKey)0, decoded, false, false, false));
                }
                i += seqLen;
            }
            else
            {
                i++; // skip unknown byte
            }
        }

        return events;
    }

    private static int ParseCsi(ReadOnlySpan<byte> data, List<InputEvent> events)
    {
        // Parse parameter bytes (0x30-0x3F) and intermediate bytes (0x20-0x2F),
        // then a final byte (0x40-0x7E)
        int i = 0;
        int paramStart = i;

        // Collect parameter and intermediate bytes
        while (i < data.Length && data[i] >= 0x20 && data[i] <= 0x3F)
            i++;

        if (i >= data.Length)
            return i;

        byte finalByte = data[i];
        i++;

        var paramSpan = data[paramStart..(i - 1)];

        // SGR mouse sequence: ESC [ < Cb ; Cx ; Cy M/m
        if (paramSpan.Length > 0 && paramSpan[0] == '<')
        {
            ParseSgrMouse(paramSpan[1..], finalByte, events);
            return i;
        }

        ParseCsiParams(paramSpan, out int param1, out int param2);

        switch (finalByte)
        {
            case (byte)'A': // Cursor Up
                events.Add(new KeyEvent(ConsoleKey.UpArrow, '\0', false, false, false));
                break;
            case (byte)'B': // Cursor Down
                events.Add(new KeyEvent(ConsoleKey.DownArrow, '\0', false, false, false));
                break;
            case (byte)'C': // Cursor Right
                events.Add(new KeyEvent(ConsoleKey.RightArrow, '\0', false, false, false));
                break;
            case (byte)'D': // Cursor Left
                events.Add(new KeyEvent(ConsoleKey.LeftArrow, '\0', false, false, false));
                break;
            case (byte)'H': // Home
                events.Add(new KeyEvent(ConsoleKey.Home, '\0', false, false, false));
                break;
            case (byte)'F': // End
                events.Add(new KeyEvent(ConsoleKey.End, '\0', false, false, false));
                break;
            case (byte)'Z': // Shift+Tab
                events.Add(new KeyEvent(ConsoleKey.Tab, '\0', true, false, false));
                break;
            case (byte)'~': // Extended keys with parameter
                var key = param1 switch
                {
                    1 => ConsoleKey.Home,
                    2 => ConsoleKey.Insert,
                    3 => ConsoleKey.Delete,
                    4 => ConsoleKey.End,
                    5 => ConsoleKey.PageUp,
                    6 => ConsoleKey.PageDown,
                    15 => ConsoleKey.F5,
                    17 => ConsoleKey.F6,
                    18 => ConsoleKey.F7,
                    19 => ConsoleKey.F8,
                    20 => ConsoleKey.F9,
                    21 => ConsoleKey.F10,
                    23 => ConsoleKey.F11,
                    24 => ConsoleKey.F12,
                    _ => (ConsoleKey)0,
                };
                if (key != 0)
                    events.Add(new KeyEvent(key, '\0', false, false, false));
                break;
        }

        return i;
    }

    private static void ParseCsiParams(ReadOnlySpan<byte> data, out int param1, out int param2)
    {
        param1 = 0;
        param2 = 0;
        int current = 0;
        bool inSecond = false;

        foreach (byte b in data)
        {
            if (b == ';')
            {
                param1 = current;
                current = 0;
                inSecond = true;
            }
            else if (b >= '0' && b <= '9')
            {
                current = current * 10 + (b - '0');
            }
        }

        if (inSecond)
            param2 = current;
        else
            param1 = current;
    }

    private static void ParseSgrMouse(ReadOnlySpan<byte> paramData, byte finalByte, List<InputEvent> events)
    {
        // paramData contains "Cb;Cx;Cy" as ASCII digits/semicolons
        int cb = 0, cx = 0, cy = 0;
        int field = 0;
        int current = 0;

        foreach (byte b in paramData)
        {
            if (b == ';')
            {
                if (field == 0) cb = current;
                else if (field == 1) cx = current;
                current = 0;
                field++;
            }
            else if (b >= '0' && b <= '9')
            {
                current = current * 10 + (b - '0');
            }
        }

        if (field == 2) cy = current;

        bool isRelease = finalByte == 'm';

        // Decode button: bits 0-1 for button, bit 6 for scroll
        MouseButton button;
        if (cb >= 64)
        {
            button = cb == 64 ? MouseButton.ScrollUp : MouseButton.ScrollDown;
        }
        else
        {
            button = (cb & 0x03) switch
            {
                0 => MouseButton.Left,
                1 => MouseButton.Middle,
                2 => MouseButton.Right,
                _ => MouseButton.None,
            };
        }

        // Cx, Cy are 1-based → convert to 0-based
        events.Add(new MouseEvent(button, cy - 1, cx - 1, isRelease));
    }

    private static ConsoleKey CharToConsoleKey(char c)
    {
        return c switch
        {
            >= 'a' and <= 'z' => (ConsoleKey)(c - 'a' + 'A'),
            >= 'A' and <= 'Z' => (ConsoleKey)c,
            >= '0' and <= '9' => (ConsoleKey)c,
            ' ' => ConsoleKey.Spacebar,
            '/' => ConsoleKey.Divide,
            '*' => ConsoleKey.Multiply,
            '-' => ConsoleKey.OemMinus,
            '+' => ConsoleKey.OemPlus,
            '.' => ConsoleKey.OemPeriod,
            ',' => ConsoleKey.OemComma,
            _ => (ConsoleKey)0,
        };
    }

    private static int Utf8SequenceLength(byte leadByte)
    {
        if ((leadByte & 0xE0) == 0xC0) return 2;
        if ((leadByte & 0xF0) == 0xE0) return 3;
        if ((leadByte & 0xF8) == 0xF0) return 4;
        return 1; // invalid, consume one byte
    }

    private static char DecodeUtf8Char(ReadOnlySpan<byte> bytes)
    {
        Span<char> chars = stackalloc char[2];
        int charCount = Encoding.UTF8.GetChars(bytes, chars);
        return charCount > 0 ? chars[0] : '\uFFFD';
    }
}
