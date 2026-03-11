using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Wade.FileSystem;

internal static class SystemClipboard
{
    /// <summary>
    /// Copies the specified text to the OS clipboard.
    /// Returns true on success, false if no clipboard mechanism is available.
    /// </summary>
    public static bool SetText(string text)
    {
        if (OperatingSystem.IsWindows())
        {
            return SetTextWindows(text);
        }

        return SetTextUnix(text);
    }

    [SupportedOSPlatform("windows")]
    private static bool SetTextWindows(string text)
    {
        if (!OpenClipboard(nint.Zero))
        {
            return false;
        }

        try
        {
            EmptyClipboard();

            int byteCount = (text.Length + 1) * 2; // UTF-16 + null terminator
            nint hGlobal = GlobalAlloc(GMEM_MOVEABLE, (nuint)byteCount);

            if (hGlobal == nint.Zero)
            {
                return false;
            }

            nint locked = GlobalLock(hGlobal);

            if (locked == nint.Zero)
            {
                GlobalFree(hGlobal);
                return false;
            }

            try
            {
                // Copy UTF-16 string including null terminator
                Marshal.Copy(text.ToCharArray(), 0, locked, text.Length);
                Marshal.WriteInt16(locked + text.Length * 2, 0); // null terminator
            }
            finally
            {
                GlobalUnlock(hGlobal);
            }

            if (SetClipboardData(CF_UNICODETEXT, hGlobal) == nint.Zero)
            {
                GlobalFree(hGlobal);
                return false;
            }

            // After successful SetClipboardData, the system owns the memory — do not free it
            return true;
        }
        finally
        {
            CloseClipboard();
        }
    }

    private static bool SetTextUnix(string text)
    {
        // Try clipboard tools in order of preference
        string[][] tools =
        [
            ["pbcopy"],                                  // macOS
            ["wl-copy"],                                 // Wayland
            ["xclip", "-selection", "clipboard"],        // X11
            ["xsel", "--clipboard", "--input"],          // X11 fallback
        ];

        foreach (string[] tool in tools)
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = tool[0],
                    RedirectStandardInput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                };

                for (int i = 1; i < tool.Length; i++)
                {
                    psi.ArgumentList.Add(tool[i]);
                }

                using var process = Process.Start(psi);

                if (process is null)
                {
                    continue;
                }

                process.StandardInput.Write(text);
                process.StandardInput.Close();
                process.WaitForExit(3000);

                if (process.ExitCode == 0)
                {
                    return true;
                }
            }
            catch
            {
                // Tool not available, try next
            }
        }

        return false;
    }

    // ── Windows P/Invoke ──────────────────────────────────────────────────

    private const uint CF_UNICODETEXT = 13;
    private const uint GMEM_MOVEABLE = 0x0002;

    [SupportedOSPlatform("windows")]
    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool OpenClipboard(nint hWndNewOwner);

    [SupportedOSPlatform("windows")]
    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EmptyClipboard();

    [SupportedOSPlatform("windows")]
    [DllImport("user32.dll", SetLastError = true)]
    private static extern nint SetClipboardData(uint uFormat, nint hMem);

    [SupportedOSPlatform("windows")]
    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseClipboard();

    [SupportedOSPlatform("windows")]
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern nint GlobalAlloc(uint uFlags, nuint dwBytes);

    [SupportedOSPlatform("windows")]
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern nint GlobalLock(nint hMem);

    [SupportedOSPlatform("windows")]
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalUnlock(nint hMem);

    [SupportedOSPlatform("windows")]
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern nint GlobalFree(nint hMem);
}
