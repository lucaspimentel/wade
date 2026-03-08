using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Wade.Terminal;

[SupportedOSPlatform("windows")]
internal sealed class WindowsInputSource : IInputSource
{
    private readonly nint _stdinHandle;

    public WindowsInputSource()
    {
        _stdinHandle = GetStdHandle(StdInputHandle);
    }

    public InputEvent? ReadNext(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            uint waitResult = WaitForSingleObject(_stdinHandle, 100);

            if (waitResult != WaitObject0)
            {
                continue; // timeout or error — loop back and check cancellation
            }

            if (!GetNumberOfConsoleInputEvents(_stdinHandle, out uint count) || count == 0)
            {
                continue;
            }

            var record = new INPUT_RECORD();
            if (!ReadConsoleInput(_stdinHandle, ref record, 1, out uint eventsRead) || eventsRead == 0)
            {
                continue;
            }

            switch (record.EventType)
            {
                case KeyEventType:
                    if (record.Event.KeyEvent.bKeyDown == 0)
                    {
                        break; // skip key-up events
                    }

                    var k = record.Event.KeyEvent;
                    var modifiers = (ControlKeyState)k.dwControlKeyState;
                    bool shift = (modifiers & (ControlKeyState.ShiftPressed)) != 0;
                    bool alt = (modifiers & (ControlKeyState.LeftAltPressed | ControlKeyState.RightAltPressed)) != 0;
                    bool control = (modifiers & (ControlKeyState.LeftCtrlPressed | ControlKeyState.RightCtrlPressed)) != 0;

                    return new KeyEvent((ConsoleKey)k.wVirtualKeyCode, k.UnicodeChar, shift, alt, control);

                case MouseEventType:
                    var m = record.Event.MouseEvent;
                    if (m.dwEventFlags == MouseMoved)
                    {
                        break; // ignore mouse move
                    }

                    if (m.dwEventFlags == MouseWheeled)
                    {
                        // High word of dwButtonState determines direction
                        short hiWord = (short)(m.dwButtonState >> 16);
                        var scrollButton = hiWord > 0 ? MouseButton.ScrollUp : MouseButton.ScrollDown;
                        return new MouseEvent(scrollButton, m.Y, m.X, false);
                    }

                    if (m.dwEventFlags == 0) // button press or release
                    {
                        if ((m.dwButtonState & FromLeft1stButtonPressed) != 0)
                        {
                            return new MouseEvent(MouseButton.Left, m.Y, m.X, false);
                        }

                        if (m.dwButtonState == 0)
                        {
                            return new MouseEvent(MouseButton.Left, m.Y, m.X, true);
                        }
                    }
                    break;

                case WindowBufferSizeEventType:
                    var size = record.Event.WindowBufferSizeEvent;
                    // Query actual window size instead of buffer size
                    int width = Console.WindowWidth;
                    int height = Console.WindowHeight;
                    return new ResizeEvent(width, height);
            }
        }

        return null;
    }

    public void Dispose()
    {
        // stdin handle is owned by the runtime — do not close it
    }

    // Win32 constants
    private const int StdInputHandle = -10;
    private const uint WaitObject0 = 0x00000000;
    private const ushort KeyEventType = 0x0001;
    private const ushort MouseEventType = 0x0002;
    private const ushort WindowBufferSizeEventType = 0x0004;
    private const uint MouseMoved = 0x0001;
    private const uint MouseWheeled = 0x0004;
    private const uint FromLeft1stButtonPressed = 0x0001;

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

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern nint GetStdHandle(int nStdHandle);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool ReadConsoleInput(nint hConsoleInput, ref INPUT_RECORD lpBuffer, uint nLength, out uint lpNumberOfEventsRead);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetNumberOfConsoleInputEvents(nint hConsoleInput, out uint lpcNumberOfEvents);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern uint WaitForSingleObject(nint hHandle, uint dwMilliseconds);
}
