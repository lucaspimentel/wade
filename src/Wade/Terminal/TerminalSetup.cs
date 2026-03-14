using System.Runtime.InteropServices;

namespace Wade.Terminal;

internal sealed class TerminalSetup : IDisposable
{
    private readonly nint _stdoutHandle;
    private readonly nint _stdinHandle;
    private readonly uint _originalOutputMode;
    private readonly uint _originalInputMode;
    private readonly int _ttyFd = -1;
    private readonly byte[]? _savedTermios;
    private bool _disposed;

    public TerminalCapabilities Capabilities { get; } = TerminalCapabilities.Default;

    public TerminalSetup()
    {
        Console.OutputEncoding = System.Text.Encoding.UTF8;
        Console.InputEncoding = System.Text.Encoding.UTF8;

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            _stdoutHandle = GetStdHandle(StdOutputHandle);
            _stdinHandle = GetStdHandle(StdInputHandle);

            GetConsoleMode(_stdoutHandle, out _originalOutputMode);
            GetConsoleMode(_stdinHandle, out _originalInputMode);

            // Enable VT processing on output
            SetConsoleMode(_stdoutHandle, _originalOutputMode | EnableVirtualTerminalProcessing | DisableNewlineAutoReturn);

            // Disable line input and echo for raw mode, but do NOT enable
            // ENABLE_VIRTUAL_TERMINAL_INPUT — input uses Win32 ReadConsoleInput
            // which gives us structured key records, not VT sequences.
            uint inputMode = _originalInputMode
                             & ~EnableLineInput
                             & ~EnableEchoInput
                             & ~EnableProcessedInput
                             & ~EnableQuickEditMode
                             | EnableWindowInput
                             | EnableMouseInput
                             | EnableExtendedFlags;
            SetConsoleMode(_stdinHandle, inputMode);

            // Windows Terminal supports Sixel since v1.22; detect via WT_SESSION.
            // Cell pixel size uses defaults (8×16) — Windows Terminal doesn't deliver
            // ESC[16t responses through ReadConsoleInput since VT input is disabled.
            bool wtSession = Environment.GetEnvironmentVariable("WT_SESSION") is not null;
            Capabilities = new TerminalCapabilities(wtSession, 8, 16);
        }

        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            _ttyFd = LibC.Open("/dev/tty", LibC.O_RDWR);
            if (_ttyFd >= 0)
            {
                _savedTermios = new byte[LibC.TermiosSize];
                if (LibC.Tcgetattr(_ttyFd, _savedTermios) == 0)
                {
                    byte[] raw = (byte[])_savedTermios.Clone();
                    LibC.Cfmakeraw(raw);
                    LibC.Tcsetattr(_ttyFd, LibC.TCSAFLUSH, raw);
                }
                else
                {
                    _savedTermios = null;
                }
            }

            // Query terminal capabilities BEFORE entering alternate screen
            Capabilities = DetectCapabilitiesUnix(_ttyFd);
        }

        Console.Write(AnsiCodes.SaveTitle);
        Console.Write(AnsiCodes.EnterAlternateScreen);
        Console.Write(AnsiCodes.HideCursor);
        Console.Write(AnsiCodes.ClearScreen);

        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            Console.Write(AnsiCodes.EnableMouseReporting);
            Console.Write(AnsiCodes.EnableSgrMouseMode);
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            Console.Write(AnsiCodes.DisableSgrMouseMode);
            Console.Write(AnsiCodes.DisableMouseReporting);

            if (_savedTermios != null && _ttyFd >= 0)
            {
                LibC.Tcsetattr(_ttyFd, LibC.TCSAFLUSH, _savedTermios);
            }
        }

        Console.Write(AnsiCodes.ResetAttributes);
        Console.Write(AnsiCodes.ShowCursor);
        Console.Write(AnsiCodes.RestoreTitle);
        Console.Write(AnsiCodes.LeaveAlternateScreen);

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            SetConsoleMode(_stdoutHandle, _originalOutputMode);
            SetConsoleMode(_stdinHandle, _originalInputMode);
        }

        if (_ttyFd >= 0)
        {
            LibC.Close(_ttyFd);
        }
    }

    private static TerminalCapabilities DetectCapabilitiesUnix(int ttyFd)
    {
        if (ttyFd < 0)
        {
            return TerminalCapabilities.Default;
        }

        try
        {
            // Send DA1 query and cell size query
            Console.Write("\x1b[c\x1b[16t");
            Console.Out.Flush();

            var buf = new byte[256];
            int total = 0;

            // Read responses with a 200ms timeout using poll
            while (total < buf.Length)
            {
                var pfd = new LibC.PollFd { fd = ttyFd, events = LibC.POLLIN, revents = 0 };
                int pollResult = LibC.Poll(ref pfd, 1, 200);
                if (pollResult <= 0)
                {
                    break;
                }

                nint n = LibC.Read(ttyFd, buf, total, buf.Length - total);
                if (n <= 0)
                {
                    break;
                }

                total += (int)n;
            }

            if (total == 0)
            {
                return TerminalCapabilities.Default;
            }

            return TerminalCapabilities.ParseQueryResponses(buf.AsSpan(0, total));
        }
        catch
        {
            return TerminalCapabilities.Default;
        }
    }

    // Windows console API constants
    private const int StdOutputHandle = -11;
    private const int StdInputHandle = -10;
    private const uint EnableVirtualTerminalProcessing = 0x0004;
    private const uint DisableNewlineAutoReturn = 0x0008;
    private const uint EnableVirtualTerminalInput = 0x0200;
    private const uint EnableLineInput = 0x0002;
    private const uint EnableEchoInput = 0x0004;
    private const uint EnableProcessedInput = 0x0001;
    private const uint EnableWindowInput = 0x0008;
    private const uint EnableMouseInput = 0x0010;
    private const uint EnableQuickEditMode = 0x0040;
    private const uint EnableExtendedFlags = 0x0080;

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern nint GetStdHandle(int nStdHandle);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetConsoleMode(nint hConsoleHandle, out uint lpMode);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool SetConsoleMode(nint hConsoleHandle, uint dwMode);
}
