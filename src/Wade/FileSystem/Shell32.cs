using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Wade.FileSystem;

[SupportedOSPlatform("windows")]
internal static class Shell32
{
    private const int FO_DELETE = 0x0003;
    private const int FOF_ALLOWUNDO = 0x0040;
    private const int FOF_NOCONFIRMATION = 0x0010;
    private const int FOF_SILENT = 0x0004;
    private const int FOF_NOERRORUI = 0x0400;

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHFileOperation(ref SHFILEOPSTRUCT lpFileOp);

    /// <summary>
    /// Sends the specified paths to the Recycle Bin via SHFileOperation.
    /// Returns 0 on success, non-zero on failure.
    /// </summary>
    public static int RecycleFiles(IReadOnlyList<string> paths)
    {
        // pFrom requires double-null-terminated string (paths separated by \0, ending with \0\0)
        string pFrom = string.Join('\0', paths) + "\0\0";

        var op = new SHFILEOPSTRUCT
        {
            wFunc = FO_DELETE,
            pFrom = pFrom,
            fFlags = FOF_ALLOWUNDO | FOF_NOCONFIRMATION | FOF_SILENT | FOF_NOERRORUI,
        };

        return SHFileOperation(ref op);
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct SHFILEOPSTRUCT
    {
        public nint hwnd;
        public int wFunc;
        public string pFrom;
        public string pTo;
        public int fFlags;
        [MarshalAs(UnmanagedType.Bool)] public bool fAnyOperationsAborted;
        public nint hNameMappings;
        public string lpszProgressTitle;
    }
}
