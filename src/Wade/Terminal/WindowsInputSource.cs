using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Wade.Terminal;

[SupportedOSPlatform("windows")]
internal sealed class WindowsInputSource : IInputSource
{
    // Win32 constants
    private const int StdInputHandle = -10;
    private const uint WaitObject0 = 0x00000000;
    private const ushort KeyEventType = 0x0001;
    private const ushort MouseEventType = 0x0002;
    private const ushort WindowBufferSizeEventType = 0x0004;
    private const uint MouseMoved = 0x0001;
    private const uint MouseWheeled = 0x0004;
    private const uint FromLeft1stButtonPressed = 0x0001;
    private const uint RightmostButtonPressed = 0x0002;
    private readonly nint _stdinHandle;

    public WindowsInputSource()
    {
        _stdinHandle = GetStdHandle(StdInputHandle);
    }

    private readonly Queue<InputEvent> _pending = new();

    public InputEvent? ReadNext(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            if (_pending.TryDequeue(out InputEvent? queued))
            {
                return queued;
            }

            uint waitResult = WaitForSingleObject(_stdinHandle, 100);

            if (waitResult != WaitObject0)
            {
                continue; // timeout or error — loop back and check cancellation
            }

            if (!GetNumberOfConsoleInputEvents(_stdinHandle, out uint count) || count == 0)
            {
                continue;
            }

            // Read all pending records at once to detect paste bursts
            var records = new INPUT_RECORD[count];
            if (!ReadConsoleInputBatch(_stdinHandle, records, count, out uint eventsRead) || eventsRead == 0)
            {
                continue;
            }

            ProcessRecords(records.AsSpan(0, (int)eventsRead));
        }

        return null;
    }

    private void ProcessRecords(ReadOnlySpan<INPUT_RECORD> records)
    {
        int i = 0;

        while (i < records.Length)
        {
            INPUT_RECORD record = records[i];

            switch (record.EventType)
            {
                case KeyEventType:
                    if (record.Event.KeyEvent.bKeyDown == 0)
                    {
                        i++;
                        break; // skip key-up events
                    }

                    KEY_EVENT_RECORD k = record.Event.KeyEvent;

                    // Check for paste burst: consecutive printable key-down events
                    if (k.UnicodeChar >= ' ')
                    {
                        int start = i;
                        var pasteChars = new System.Text.StringBuilder();
                        pasteChars.Append(k.UnicodeChar);
                        i++;

                        while (i < records.Length && records[i].EventType == KeyEventType)
                        {
                            // Skip key-up events (pasted text interleaves key-down/key-up)
                            if (records[i].Event.KeyEvent.bKeyDown == 0)
                            {
                                i++;
                                continue;
                            }

                            if (records[i].Event.KeyEvent.UnicodeChar < ' ')
                            {
                                break;
                            }

                            pasteChars.Append(records[i].Event.KeyEvent.UnicodeChar);
                            i++;
                        }

                        if (pasteChars.Length > 1)
                        {
                            // Multiple chars in one batch = paste
                            _pending.Enqueue(new PasteEvent(pasteChars.ToString()));
                        }
                        else
                        {
                            // Single char = normal keystroke
                            var modifiers = (ControlKeyState)k.dwControlKeyState;
                            bool shift = (modifiers & ControlKeyState.ShiftPressed) != 0;
                            bool alt = (modifiers & (ControlKeyState.LeftAltPressed | ControlKeyState.RightAltPressed)) != 0;
                            bool control = (modifiers & (ControlKeyState.LeftCtrlPressed | ControlKeyState.RightCtrlPressed)) != 0;
                            _pending.Enqueue(new KeyEvent((ConsoleKey)k.wVirtualKeyCode, k.UnicodeChar, shift, alt, control));
                        }

                        break;
                    }

                    {
                        var modifiers = (ControlKeyState)k.dwControlKeyState;
                        bool shift = (modifiers & ControlKeyState.ShiftPressed) != 0;
                        bool alt = (modifiers & (ControlKeyState.LeftAltPressed | ControlKeyState.RightAltPressed)) != 0;
                        bool control = (modifiers & (ControlKeyState.LeftCtrlPressed | ControlKeyState.RightCtrlPressed)) != 0;
                        _pending.Enqueue(new KeyEvent((ConsoleKey)k.wVirtualKeyCode, k.UnicodeChar, shift, alt, control));
                    }

                    i++;
                    break;

                case MouseEventType:
                    MOUSE_EVENT_RECORD m = record.Event.MouseEvent;
                    if (m.dwEventFlags == MouseMoved)
                    {
                        i++;
                        break; // ignore mouse move
                    }

                    if (m.dwEventFlags == MouseWheeled)
                    {
                        short hiWord = (short)(m.dwButtonState >> 16);
                        MouseButton scrollButton = hiWord > 0 ? MouseButton.ScrollUp : MouseButton.ScrollDown;
                        _pending.Enqueue(new MouseEvent(scrollButton, m.Y, m.X, false));
                    }
                    else if (m.dwEventFlags == 0) // button press or release
                    {
                        if ((m.dwButtonState & FromLeft1stButtonPressed) != 0)
                        {
                            _pending.Enqueue(new MouseEvent(MouseButton.Left, m.Y, m.X, false));
                        }
                        else if ((m.dwButtonState & RightmostButtonPressed) != 0)
                        {
                            _pending.Enqueue(new MouseEvent(MouseButton.Right, m.Y, m.X, false));
                        }
                        else if (m.dwButtonState == 0)
                        {
                            _pending.Enqueue(new MouseEvent(MouseButton.Left, m.Y, m.X, true));
                        }
                    }

                    i++;
                    break;

                case WindowBufferSizeEventType:
                    int width = Console.WindowWidth;
                    int height = Console.WindowHeight;
                    _pending.Enqueue(new ResizeEvent(width, height));
                    i++;
                    break;

                default:
                    i++;
                    break;
            }
        }
    }

    public void Dispose()
    {
        // stdin handle is owned by the runtime — do not close it
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern nint GetStdHandle(int nStdHandle);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool ReadConsoleInput(nint hConsoleInput, ref INPUT_RECORD lpBuffer, uint nLength, out uint lpNumberOfEventsRead);

    [DllImport("kernel32.dll", EntryPoint = "ReadConsoleInputW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool ReadConsoleInputBatch(nint hConsoleInput, [Out] INPUT_RECORD[] lpBuffer, uint nLength, out uint lpNumberOfEventsRead);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetNumberOfConsoleInputEvents(nint hConsoleInput, out uint lpcNumberOfEvents);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern uint WaitForSingleObject(nint hHandle, uint dwMilliseconds);

    [Flags]
    private enum ControlKeyState : uint
    {
        ShiftPressed = 0x0010,
        LeftCtrlPressed = 0x0008,
        RightCtrlPressed = 0x0004,
        LeftAltPressed = 0x0002,
        RightAltPressed = 0x0001,
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct INPUT_RECORD
    {
        [FieldOffset(0)] public ushort EventType;
        [FieldOffset(4)] public INPUT_RECORD_UNION Event;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct INPUT_RECORD_UNION
    {
        [FieldOffset(0)] public KEY_EVENT_RECORD KeyEvent;
        [FieldOffset(0)] public MOUSE_EVENT_RECORD MouseEvent;
        [FieldOffset(0)] public WINDOW_BUFFER_SIZE_RECORD WindowBufferSizeEvent;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KEY_EVENT_RECORD
    {
        public int bKeyDown; // BOOL
        public ushort wRepeatCount;
        public ushort wVirtualKeyCode;
        public ushort wVirtualScanCode;
        public char UnicodeChar;
        public uint dwControlKeyState;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MOUSE_EVENT_RECORD
    {
        public short X; // dwMousePosition.X (column)
        public short Y; // dwMousePosition.Y (row)
        public uint dwButtonState;
        public uint dwControlKeyState;
        public uint dwEventFlags;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WINDOW_BUFFER_SIZE_RECORD
    {
        public short X;
        public short Y;
    }
}
