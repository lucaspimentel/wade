using System.Runtime.InteropServices;

namespace Wade.Terminal;

internal sealed class TerminalSetup : IDisposable
{
    private readonly nint _stdoutHandle;
    private readonly nint _stdinHandle;
    private readonly uint _originalOutputMode;
    private readonly uint _originalInputMode;
    private bool _disposed;

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
                             | EnableWindowInput;
            SetConsoleMode(_stdinHandle, inputMode);
        }

        Console.Write(AnsiCodes.EnterAlternateScreen);
        Console.Write(AnsiCodes.HideCursor);
        Console.Write(AnsiCodes.ClearScreen);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        Console.Write(AnsiCodes.ResetAttributes);
        Console.Write(AnsiCodes.ShowCursor);
        Console.Write(AnsiCodes.LeaveAlternateScreen);

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            SetConsoleMode(_stdoutHandle, _originalOutputMode);
            SetConsoleMode(_stdinHandle, _originalInputMode);
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

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern nint GetStdHandle(int nStdHandle);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetConsoleMode(nint hConsoleHandle, out uint lpMode);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool SetConsoleMode(nint hConsoleHandle, uint dwMode);
}
