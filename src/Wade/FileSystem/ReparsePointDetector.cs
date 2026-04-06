using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;

namespace Wade.FileSystem;

internal static class ReparsePointDetector
{
    private const uint IO_REPARSE_TAG_MOUNT_POINT = 0xA0000003;
    private const uint IO_REPARSE_TAG_APPEXECLINK = 0x8000001B;
    private const uint FSCTL_GET_REPARSE_POINT = 0x000900A8;

    private const uint FileShareRead = 0x00000001;
    private const uint FileShareWrite = 0x00000002;
    private const uint OpenExisting = 3;
    private const uint FILE_FLAG_BACKUP_SEMANTICS = 0x02000000;
    private const uint FILE_FLAG_OPEN_REPARSE_POINT = 0x00200000;
    private const int FileAttributeTagInfo = 9;
    private static readonly nint InvalidHandleValue = -1;

    // Reparse data buffer header: ReparseTag(4) + ReparseDataLength(2) + Reserved(2) = 8 bytes
    private const int ReparseDataHeaderSize = 8;
    // AppExecLink data starts with a Version DWORD
    private const int AppExecLinkVersionSize = 4;

    public static bool IsJunctionPoint(string path)
    {
        if (!OperatingSystem.IsWindows())
        {
            return false;
        }

        return IsJunctionPointWindows(path);
    }

    public static bool IsAppExecLink(string path)
    {
        if (!OperatingSystem.IsWindows())
        {
            return false;
        }

        return IsAppExecLinkWindows(path);
    }

    public static string? GetAppExecLinkTarget(string path)
    {
        if (!OperatingSystem.IsWindows())
        {
            return null;
        }

        return GetAppExecLinkTargetWindows(path);
    }

    [SupportedOSPlatform("windows")]
    private static bool IsJunctionPointWindows(string path)
    {
        try
        {
            uint reparseTag = GetReparseTag(path);
            return IsJunctionTag(reparseTag);
        }
        catch
        {
            return false;
        }
    }

    [SupportedOSPlatform("windows")]
    private static bool IsAppExecLinkWindows(string path)
    {
        try
        {
            uint reparseTag = GetReparseTag(path);
            return IsAppExecLinkTag(reparseTag);
        }
        catch
        {
            return false;
        }
    }

    [SupportedOSPlatform("windows")]
    private static string? GetAppExecLinkTargetWindows(string path)
    {
        try
        {
            byte[]? buffer = GetReparseDataBuffer(path);

            if (buffer == null)
            {
                return null;
            }

            return ParseAppExecLinkTarget(buffer);
        }
        catch
        {
            return null;
        }
    }

    internal static bool IsJunctionTag(uint reparseTag) =>
        reparseTag == IO_REPARSE_TAG_MOUNT_POINT;

    internal static bool IsAppExecLinkTag(uint reparseTag) =>
        reparseTag == IO_REPARSE_TAG_APPEXECLINK;

    /// <summary>
    /// Parses the target executable path from an AppExecLink reparse data buffer.
    /// Buffer layout: ReparseTag(4) + ReparseDataLength(2) + Reserved(2) + Version(4) + 3 null-terminated UTF-16 strings.
    /// The 3rd string is the target executable path.
    /// </summary>
    internal static string? ParseAppExecLinkTarget(ReadOnlySpan<byte> buffer)
    {
        int offset = ReparseDataHeaderSize + AppExecLinkVersionSize;

        if (buffer.Length <= offset)
        {
            return null;
        }

        // Skip first two null-terminated UTF-16 strings (package ID, app user model ID)
        for (int skip = 0; skip < 2; skip++)
        {
            int nullPos = FindNullTerminator(buffer, offset);

            if (nullPos < 0)
            {
                return null;
            }

            offset = nullPos + 2; // skip past the null terminator (2 bytes for UTF-16)
        }

        // Read the 3rd string (target executable path)
        int endPos = FindNullTerminator(buffer, offset);

        if (endPos < 0 || endPos <= offset)
        {
            return null;
        }

        return Encoding.Unicode.GetString(buffer[offset..endPos]);
    }

    private static int FindNullTerminator(ReadOnlySpan<byte> buffer, int offset)
    {
        for (int i = offset; i + 1 < buffer.Length; i += 2)
        {
            if (buffer[i] == 0 && buffer[i + 1] == 0)
            {
                return i;
            }
        }

        return -1;
    }

    [SupportedOSPlatform("windows")]
    private static uint GetReparseTag(string path)
    {
        nint handle = OpenReparsePoint(path);

        if (handle == InvalidHandleValue)
        {
            return 0;
        }

        try
        {
            bool success = GetFileInformationByHandleEx(
                handle,
                FileAttributeTagInfo,
                out FILE_ATTRIBUTE_TAG_INFO tagInfo,
                (uint)Marshal.SizeOf<FILE_ATTRIBUTE_TAG_INFO>());

            return success ? tagInfo.ReparseTag : 0;
        }
        finally
        {
            CloseHandle(handle);
        }
    }

    [SupportedOSPlatform("windows")]
    private static byte[]? GetReparseDataBuffer(string path)
    {
        nint handle = OpenReparsePoint(path);

        if (handle == InvalidHandleValue)
        {
            return null;
        }

        try
        {
            byte[] buffer = new byte[16 * 1024]; // 16 KB — max reparse data is 16 KB

            bool success = DeviceIoControl(
                handle,
                FSCTL_GET_REPARSE_POINT,
                nint.Zero,
                0,
                buffer,
                (uint)buffer.Length,
                out _,
                nint.Zero);

            return success ? buffer : null;
        }
        finally
        {
            CloseHandle(handle);
        }
    }

    [SupportedOSPlatform("windows")]
    private static nint OpenReparsePoint(string path) =>
        CreateFileW(
            path,
            0,
            FileShareRead | FileShareWrite,
            nint.Zero,
            OpenExisting,
            FILE_FLAG_BACKUP_SEMANTICS | FILE_FLAG_OPEN_REPARSE_POINT,
            nint.Zero);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern nint CreateFileW(
        string lpFileName,
        uint dwDesiredAccess,
        uint dwShareMode,
        nint lpSecurityAttributes,
        uint dwCreationDisposition,
        uint dwFlagsAndAttributes,
        nint hTemplateFile);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetFileInformationByHandleEx(
        nint hFile,
        int fileInformationClass,
        out FILE_ATTRIBUTE_TAG_INFO lpFileInformation,
        uint dwBufferSize);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool DeviceIoControl(
        nint hDevice,
        uint dwIoControlCode,
        nint lpInBuffer,
        uint nInBufferSize,
        [Out] byte[] lpOutBuffer,
        uint nOutBufferSize,
        out uint lpBytesReturned,
        nint lpOverlapped);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(nint hObject);

    [StructLayout(LayoutKind.Sequential)]
    private struct FILE_ATTRIBUTE_TAG_INFO
    {
        public uint FileAttributes;
        public uint ReparseTag;
    }
}
