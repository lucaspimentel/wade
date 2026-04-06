namespace Wade.LnkParser;

/// <summary>
/// VolumeID structure containing information about a volume/drive
/// </summary>
internal class VolumeID
{
    public uint VolumeIdSize { get; init; }
    public VolumeType VolumeType { get; init; }
    public uint DriveSerialNumber { get; init; }
    public string VolumeLabel { get; init; } = string.Empty;

    public static VolumeID Parse(BinaryReader reader)
    {
        var startPosition = reader.BaseStream.Position;
        var volumeIdSize = reader.ReadUInt32();
        var driveType = (VolumeType)reader.ReadUInt32();
        var driveSerialNumber = reader.ReadUInt32();
        var volumeLabelOffset = reader.ReadUInt32();

        string volumeLabel = string.Empty;

        // If volumeLabelOffset is 0x00000014, the volume label is at offset 0x14 (Unicode)
        // Otherwise it's at the specified offset (ANSI)
        if (volumeLabelOffset == 0x00000014 && volumeIdSize > 0x14)
        {
            // Unicode volume label
            reader.BaseStream.Position = startPosition + volumeLabelOffset;
            volumeLabel = ReadNullTerminatedUnicodeString(reader);
        }
        else if (volumeLabelOffset < volumeIdSize)
        {
            // ANSI volume label
            reader.BaseStream.Position = startPosition + volumeLabelOffset;
            volumeLabel = ReadNullTerminatedString(reader);
        }

        // Move to end of VolumeID structure
        reader.BaseStream.Position = startPosition + volumeIdSize;

        return new VolumeID
        {
            VolumeIdSize = volumeIdSize,
            VolumeType = driveType,
            DriveSerialNumber = driveSerialNumber,
            VolumeLabel = volumeLabel
        };
    }

    private static string ReadNullTerminatedString(BinaryReader reader)
    {
        var bytes = new List<byte>();
        byte b;
        while ((b = reader.ReadByte()) != 0)
        {
            bytes.Add(b);
        }
        return System.Text.Encoding.Default.GetString(bytes.ToArray());
    }

    private static string ReadNullTerminatedUnicodeString(BinaryReader reader)
    {
        var bytes = new List<byte>();
        while (true)
        {
            var b1 = reader.ReadByte();
            var b2 = reader.ReadByte();
            if (b1 == 0 && b2 == 0)
            {
                break;
            }
            bytes.Add(b1);
            bytes.Add(b2);
        }
        return System.Text.Encoding.Unicode.GetString(bytes.ToArray());
    }
}

/// <summary>
/// Drive types as defined in MS-SHLLINK specification
/// </summary>
internal enum VolumeType : uint
{
    DRIVE_UNKNOWN = 0x00000000,
    DRIVE_NO_ROOT_DIR = 0x00000001,
    DRIVE_REMOVABLE = 0x00000002,
    DRIVE_FIXED = 0x00000003,
    DRIVE_REMOTE = 0x00000004,
    DRIVE_CDROM = 0x00000005,
    DRIVE_RAMDISK = 0x00000006
}
