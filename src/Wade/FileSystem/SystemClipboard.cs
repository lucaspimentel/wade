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

    /// <summary>
    /// Copies file paths to the OS clipboard in a format compatible with Explorer paste.
    /// Returns true on success, false if clipboard is unavailable or on non-Windows platforms.
    /// </summary>
    public static bool SetFiles(IReadOnlyList<string> paths, bool isCut)
    {
        if (OperatingSystem.IsWindows())
        {
            return SetFilesWindows(paths, isCut);
        }

        // Unix/macOS file clipboard interop not yet implemented
        return false;
    }

    /// <summary>
    /// Reads file paths from the OS clipboard (e.g. files copied in Explorer).
    /// Returns null if clipboard is unavailable, empty, or on non-Windows platforms.
    /// </summary>
    public static (List<string> Paths, bool IsCut)? GetFiles()
    {
        if (OperatingSystem.IsWindows())
        {
            return GetFilesWindows();
        }

        // Unix/macOS file clipboard interop not yet implemented
        return null;
    }

    [SupportedOSPlatform("windows")]
    private static bool SetFilesWindows(IReadOnlyList<string> paths, bool isCut)
    {
        if (paths.Count == 0)
        {
            return false;
        }

        if (!OpenClipboard(nint.Zero))
        {
            return false;
        }

        try
        {
            EmptyClipboard();

            // Build CF_HDROP (DROPFILES struct + double-null-terminated UTF-16 path list)
            // DROPFILES header: 20 bytes (pFiles=20, pt={0,0}, fNC=0, fWide=1)
            const int headerSize = 20;
            int dataSize = 0;

            foreach (string path in paths)
            {
                dataSize += (path.Length + 1) * 2; // UTF-16 chars + null terminator
            }

            dataSize += 2; // final null terminator (double-null)

            int totalSize = headerSize + dataSize;
            nint hDrop = GlobalAlloc(GMEM_MOVEABLE, (nuint)totalSize);

            if (hDrop == nint.Zero)
            {
                return false;
            }

            nint locked = GlobalLock(hDrop);

            if (locked == nint.Zero)
            {
                GlobalFree(hDrop);
                return false;
            }

            try
            {
                // Write DROPFILES header
                Marshal.WriteInt32(locked, 0, headerSize); // pFiles offset
                Marshal.WriteInt32(locked, 4, 0);          // pt.x
                Marshal.WriteInt32(locked, 8, 0);          // pt.y
                Marshal.WriteInt32(locked, 12, 0);         // fNC
                Marshal.WriteInt32(locked, 16, 1);         // fWide (Unicode)

                // Write paths
                int offset = headerSize;

                foreach (string path in paths)
                {
                    char[] chars = path.ToCharArray();
                    Marshal.Copy(chars, 0, locked + offset, chars.Length);
                    offset += chars.Length * 2;
                    Marshal.WriteInt16(locked + offset, 0); // null terminator
                    offset += 2;
                }

                Marshal.WriteInt16(locked + offset, 0); // double-null terminator
            }
            finally
            {
                GlobalUnlock(hDrop);
            }

            if (SetClipboardData(CF_HDROP, hDrop) == nint.Zero)
            {
                GlobalFree(hDrop);
                return false;
            }

            // Set Preferred DropEffect (copy=1, move/cut=2)
            uint dropEffectFormat = RegisterClipboardFormatW("Preferred DropEffect");

            if (dropEffectFormat != 0)
            {
                nint hEffect = GlobalAlloc(GMEM_MOVEABLE, 4);

                if (hEffect != nint.Zero)
                {
                    nint effectLocked = GlobalLock(hEffect);

                    if (effectLocked != nint.Zero)
                    {
                        Marshal.WriteInt32(effectLocked, isCut ? DROPEFFECT_MOVE : DROPEFFECT_COPY);
                        GlobalUnlock(hEffect);
                        if (SetClipboardData(dropEffectFormat, hEffect) == nint.Zero)
                        {
                            GlobalFree(hEffect);
                        }
                    }
                    else
                    {
                        GlobalFree(hEffect);
                    }
                }
            }

            return true;
        }
        finally
        {
            CloseClipboard();
        }
    }

    [SupportedOSPlatform("windows")]
    private static (List<string> Paths, bool IsCut)? GetFilesWindows()
    {
        if (!OpenClipboard(nint.Zero))
        {
            return null;
        }

        try
        {
            nint hDrop = GetClipboardData(CF_HDROP);

            if (hDrop == nint.Zero)
            {
                return null;
            }

            // Get file count
            uint fileCount = DragQueryFileW(hDrop, 0xFFFFFFFF, null, 0);

            if (fileCount == 0)
            {
                return null;
            }

            var paths = new List<string>((int)fileCount);
            char[] buffer = new char[1024];

            for (uint i = 0; i < fileCount; i++)
            {
                uint charsCopied = DragQueryFileW(hDrop, i, buffer, (uint)buffer.Length);

                if (charsCopied > 0)
                {
                    paths.Add(new string(buffer, 0, (int)charsCopied));
                }
            }

            // Check Preferred DropEffect to determine cut vs copy
            bool isCut = false;
            uint dropEffectFormat = RegisterClipboardFormatW("Preferred DropEffect");

            if (dropEffectFormat != 0)
            {
                nint hEffect = GetClipboardData(dropEffectFormat);

                if (hEffect != nint.Zero)
                {
                    nint effectLocked = GlobalLock(hEffect);

                    if (effectLocked != nint.Zero)
                    {
                        int dropEffect = Marshal.ReadInt32(effectLocked);
                        isCut = (dropEffect & DROPEFFECT_MOVE) != 0;
                        GlobalUnlock(hEffect);
                    }
                }
            }

            return (paths, isCut);
        }
        finally
        {
            CloseClipboard();
        }
    }

    // ── Windows P/Invoke ──────────────────────────────────────────────────

    private const uint CF_UNICODETEXT = 13;
    private const uint CF_HDROP = 15;
    private const uint GMEM_MOVEABLE = 0x0002;
    private const int DROPEFFECT_COPY = 1;
    private const int DROPEFFECT_MOVE = 2;

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

    [SupportedOSPlatform("windows")]
    [DllImport("user32.dll", SetLastError = true)]
    private static extern nint GetClipboardData(uint uFormat);

    [SupportedOSPlatform("windows")]
    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern uint RegisterClipboardFormatW(string lpszFormat);

    [SupportedOSPlatform("windows")]
    [DllImport("shell32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern uint DragQueryFileW(nint hDrop, uint iFile, char[]? lpszFile, uint cch);
}
