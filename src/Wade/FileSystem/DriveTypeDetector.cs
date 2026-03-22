using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Wade.FileSystem;

internal static class DriveTypeDetector
{
    public static DriveMediaType Detect(DriveInfo drive)
    {
        return drive.DriveType switch
        {
            DriveType.Network => DriveMediaType.Network,
            DriveType.Removable => DriveMediaType.Removable,
            DriveType.Fixed => DetectFixedDriveMediaType(drive),
            _ => DriveMediaType.Unknown,
        };
    }

    private static DriveMediaType DetectFixedDriveMediaType(DriveInfo drive)
    {
        if (OperatingSystem.IsWindows())
        {
            return DetectWindowsMediaType(drive);
        }

        if (OperatingSystem.IsLinux())
        {
            return DetectLinuxMediaType(drive);
        }

        return DriveMediaType.Unknown;
    }

    // --- Linux detection via /sys/block/<device>/queue/rotational ---

    private static DriveMediaType DetectLinuxMediaType(DriveInfo drive)
    {
        try
        {
            string? device = ResolveLinuxBlockDevice(drive.RootDirectory.FullName);

            if (device == null)
            {
                return DriveMediaType.Unknown;
            }

            string rotationalPath = $"/sys/block/{device}/queue/rotational";

            if (!File.Exists(rotationalPath))
            {
                return DriveMediaType.Unknown;
            }

            string? content = File.ReadAllText(rotationalPath).Trim();
            return ParseRotationalValue(content);
        }
        catch
        {
            return DriveMediaType.Unknown;
        }
    }

    internal static DriveMediaType ParseRotationalValue(string? content)
    {
        return content switch
        {
            "0" => DriveMediaType.Ssd,
            "1" => DriveMediaType.Hdd,
            _ => DriveMediaType.Unknown,
        };
    }

    internal static string? ResolveLinuxBlockDevice(string mountPoint)
    {
        // Read /proc/mounts to find the device for this mount point
        try
        {
            if (!File.Exists("/proc/mounts"))
            {
                return null;
            }

            foreach (string line in File.ReadLines("/proc/mounts"))
            {
                // Format: device mountpoint fstype options dump pass
                string[] parts = line.Split(' ', 3);

                if (parts.Length < 2)
                {
                    continue;
                }

                if (parts[1] == mountPoint || parts[1] == mountPoint.TrimEnd('/'))
                {
                    string devicePath = parts[0]; // e.g., /dev/sda1, /dev/nvme0n1p1

                    return ExtractBaseDevice(devicePath);
                }
            }
        }
        catch
        {
            // Ignore errors reading /proc/mounts
        }

        return null;
    }

    internal static string? ExtractBaseDevice(string devicePath)
    {
        // Extract device name from path: /dev/sda1 → sda, /dev/nvme0n1p1 → nvme0n1
        string device = Path.GetFileName(devicePath);

        if (string.IsNullOrEmpty(device))
        {
            return null;
        }

        // NVMe: nvme0n1p1 → nvme0n1 (strip pN partition suffix)
        if (device.StartsWith("nvme", StringComparison.Ordinal))
        {
            int pIdx = device.LastIndexOf('p');

            // Ensure the 'p' is a partition separator (after the 'n' namespace part)
            if (pIdx > 0 && pIdx > device.LastIndexOf('n') && pIdx < device.Length - 1 &&
                char.IsAsciiDigit(device[pIdx + 1]))
            {
                return device[..pIdx];
            }

            return device;
        }

        // Traditional: sda1 → sda, vdb2 → vdb (strip trailing digits)
        int i = device.Length;

        while (i > 0 && char.IsAsciiDigit(device[i - 1]))
        {
            i--;
        }

        return i > 0 ? device[..i] : null;
    }

    // --- Windows detection via DeviceIoControl (seek penalty query) ---

    [SupportedOSPlatform("windows")]
    private static DriveMediaType DetectWindowsMediaType(DriveInfo drive)
    {
        try
        {
            return QuerySeekPenalty(drive.Name[0]);
        }
        catch
        {
            return DriveMediaType.Unknown;
        }
    }

    [SupportedOSPlatform("windows")]
    private static DriveMediaType QuerySeekPenalty(char driveLetter)
    {
        // Open the volume handle \\.\C:
        string volumePath = $@"\\.\{driveLetter}:";
        nint handle = CreateFileW(
            volumePath,
            0, // No access required for this IOCTL
            FileShareRead | FileShareWrite,
            nint.Zero,
            OpenExisting,
            0,
            nint.Zero);

        if (handle == InvalidHandleValue)
        {
            return DriveMediaType.Unknown;
        }

        try
        {
            return QuerySeekPenaltyFromHandle(handle);
        }
        finally
        {
            CloseHandle(handle);
        }
    }

    [SupportedOSPlatform("windows")]
    internal static DriveMediaType QuerySeekPenaltyFromHandle(nint handle)
    {
        var query = new STORAGE_PROPERTY_QUERY
        {
            PropertyId = StorageDeviceSeekPenaltyProperty,
            QueryType = PropertyStandardQuery,
        };

        bool success = DeviceIoControl(
            handle,
            IOCTL_STORAGE_QUERY_PROPERTY,
            ref query,
            (uint)Marshal.SizeOf<STORAGE_PROPERTY_QUERY>(),
            out DEVICE_SEEK_PENALTY_DESCRIPTOR descriptor,
            (uint)Marshal.SizeOf<DEVICE_SEEK_PENALTY_DESCRIPTOR>(),
            out _,
            nint.Zero);

        if (!success)
        {
            return DriveMediaType.Unknown;
        }

        return ParseSeekPenaltyResult(descriptor.IncursSeekPenalty);
    }

    internal static DriveMediaType ParseSeekPenaltyResult(bool incursSeekPenalty) =>
        incursSeekPenalty ? DriveMediaType.Hdd : DriveMediaType.Ssd;

    // --- Windows P/Invoke declarations ---

    private const uint IOCTL_STORAGE_QUERY_PROPERTY = 0x002D1400;
    private const int StorageDeviceSeekPenaltyProperty = 7;
    private const int PropertyStandardQuery = 0;

    private const uint FileShareRead = 0x00000001;
    private const uint FileShareWrite = 0x00000002;
    private const uint OpenExisting = 3;
    private static readonly nint InvalidHandleValue = -1;

    [StructLayout(LayoutKind.Sequential)]
    private struct STORAGE_PROPERTY_QUERY
    {
        public int PropertyId;
        public int QueryType;
        public byte AdditionalParameters;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DEVICE_SEEK_PENALTY_DESCRIPTOR
    {
        public uint Version;
        public uint Size;
        [MarshalAs(UnmanagedType.U1)]
        public bool IncursSeekPenalty;
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
    private static extern bool DeviceIoControl(
        nint hDevice,
        uint dwIoControlCode,
        ref STORAGE_PROPERTY_QUERY lpInBuffer,
        uint nInBufferSize,
        out DEVICE_SEEK_PENALTY_DESCRIPTOR lpOutBuffer,
        uint nOutBufferSize,
        out uint lpBytesReturned,
        nint lpOverlapped);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(nint hObject);
}
