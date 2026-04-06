using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Wade.FileSystem;

internal static class ReparsePointDetector
{
    private const uint IO_REPARSE_TAG_MOUNT_POINT = 0xA0000003;

    private const uint FileShareRead = 0x00000001;
    private const uint FileShareWrite = 0x00000002;
    private const uint OpenExisting = 3;
    private const uint FILE_FLAG_BACKUP_SEMANTICS = 0x02000000;
    private const uint FILE_FLAG_OPEN_REPARSE_POINT = 0x00200000;
    private const int FileAttributeTagInfo = 9;
    private static readonly nint InvalidHandleValue = -1;

    public static bool IsJunctionPoint(string path)
    {
        if (!OperatingSystem.IsWindows())
        {
            return false;
        }

        return IsJunctionPointWindows(path);
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

    internal static bool IsJunctionTag(uint reparseTag) =>
        reparseTag == IO_REPARSE_TAG_MOUNT_POINT;

    [SupportedOSPlatform("windows")]
    private static uint GetReparseTag(string path)
    {
        nint handle = CreateFileW(
            path,
            0,
            FileShareRead | FileShareWrite,
            nint.Zero,
            OpenExisting,
            FILE_FLAG_BACKUP_SEMANTICS | FILE_FLAG_OPEN_REPARSE_POINT,
            nint.Zero);

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
    private static extern bool CloseHandle(nint hObject);

    [StructLayout(LayoutKind.Sequential)]
    private struct FILE_ATTRIBUTE_TAG_INFO
    {
        public uint FileAttributes;
        public uint ReparseTag;
    }
}
