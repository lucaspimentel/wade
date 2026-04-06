namespace Wade.LnkParser;

/// <summary>
/// LinkTargetIDList stores a sequence of ItemID structures representing the shell namespace path.
/// Each ItemID can represent different types of shell items (Root Folder, Volume, File Entry, etc.).
/// </summary>
internal class LinkTargetIdList
{
    public ushort IdListSize { get; init; }
    public List<TargetItem> Items { get; init; } = [];

    // Legacy: Keep for backward compatibility
    public List<string> ExtractedStringList { get; init; } = [];
    public List<Guid> ExtractedClsidList { get; init; } = [];

    public static LinkTargetIdList? Parse(BinaryReader reader)
    {
        var idListSize = reader.ReadUInt16();
        if (idListSize == 0)
        {
            return new LinkTargetIdList();
        }

        var startPosition = reader.BaseStream.Position;
        var endPosition = startPosition + idListSize;
        var items = new List<TargetItem>();

        // Parse ItemID list
        while (reader.BaseStream.Position < endPosition)
        {
            var itemIdSize = reader.ReadUInt16();
            if (itemIdSize == 0) break; // Terminal ItemID

            var itemStartPosition = reader.BaseStream.Position;
            var itemData = reader.ReadBytes(itemIdSize - 2); // -2 for the size field itself

            var item = ParseTargetItem(itemData);
            items.Add(item);
        }

        // Also extract strings and CLSIDs for backward compatibility
        reader.BaseStream.Position = startPosition;
        var rawData = reader.ReadBytes(idListSize);
        var extractedStringList = ExtractStringList(rawData);
        var extractedClsidList = ExtractClsidList(rawData);

        return new LinkTargetIdList
        {
            IdListSize = idListSize,
            Items = items,
            ExtractedStringList = extractedStringList,
            ExtractedClsidList = extractedClsidList
        };
    }

    private static TargetItem ParseTargetItem(byte[] data)
    {
        if (data.Length < 1) return new UnknownItem { RawData = data };

        var type = data[0];

        // Root folder detection (type 0x1F with GUID at offset 2)
        if (type == 0x1F && data.Length >= 18)
        {
            var guid = new Guid(data.Skip(2).Take(16).ToArray());
            return new RootFolderItem
            {
                Guid = guid,
                RawData = data
            };
        }

        // Volume detection (type typically 0x2F or has drive letter pattern)
        if ((type == 0x2F || type == 0x2E) && data.Length >= 3)
        {
            // Try to extract drive letter
            string? drivePath = null;
            for (int i = 0; i < data.Length - 2; i++)
            {
                if (data[i] >= 0x41 && data[i] <= 0x5A && // A-Z
                    data[i + 1] == 0x3A && data[i + 2] == 0x5C) // :\
                {
                    drivePath = System.Text.Encoding.ASCII.GetString(data, i, 3);
                    break;
                }
            }

            return new VolumeItem
            {
                Flags = type,
                Data = drivePath ?? string.Empty,
                RawData = data
            };
        }

        // File entry detection (type typically 0x31, 0x32, or has file metadata)
        if (type >= 0x30 && type <= 0x35 && data.Length >= 14)
        {
            try
            {
                using var ms = new MemoryStream(data);
                using var br = new BinaryReader(ms);

                br.ReadByte(); // type
                var fileSize = br.ReadUInt32();

                // Try to read timestamp (MS-DOS format)
                DateTime? modifiedTime = null;
                if (ms.Position + 4 <= data.Length)
                {
                    var dosDate = br.ReadUInt16();
                    var dosTime = br.ReadUInt16();
                    try
                    {
                        if (dosDate != 0)
                        {
                            int year = ((dosDate >> 9) & 0x7F) + 1980;
                            int month = (dosDate >> 5) & 0x0F;
                            int day = dosDate & 0x1F;
                            int hour = (dosTime >> 11) & 0x1F;
                            int minute = (dosTime >> 5) & 0x3F;
                            int second = (dosTime & 0x1F) * 2;
                            modifiedTime = new DateTime(year, month, day, hour, minute, second, DateTimeKind.Utc);
                        }
                    }
                    catch { }
                }

                var fileAttributeFlags = br.ReadUInt16();

                // Read primary name (short name)
                var nameBytes = new List<byte>();
                while (ms.Position < data.Length)
                {
                    var b = br.ReadByte();
                    if (b == 0) break;
                    nameBytes.Add(b);
                }
                var primaryName = System.Text.Encoding.ASCII.GetString(nameBytes.ToArray());

                bool isDirectory = (fileAttributeFlags & 0x10) != 0;

                return new FileEntryItem
                {
                    Flags = type,
                    FileSize = fileSize,
                    FileAttributeFlags = fileAttributeFlags,
                    PrimaryName = primaryName,
                    IsDirectory = isDirectory,
                    ModifiedTime = modifiedTime,
                    RawData = data
                };
            }
            catch
            {
                // Fall through to unknown item
            }
        }

        return new UnknownItem { RawData = data };
    }

    private static List<string> ExtractStringList(byte[] data)
    {
        var strings = new List<string>();

        // Try to extract Unicode strings (minimum 4 chars, 8 bytes)
        for (int i = 0; i < data.Length - 8; i++)
        {
            if (IsLikelyUnicodeString(data, i, out var length))
            {
                var str = System.Text.Encoding.Unicode.GetString(data, i, length);
                if (!string.IsNullOrWhiteSpace(str))
                {
                    strings.Add(str);
                    i += length - 1; // Skip ahead (loop will add 1)
                }
            }
        }

        // Also try ASCII strings
        for (int i = 0; i < data.Length - 4; i++)
        {
            if (IsLikelyAsciiString(data, i, out var length))
            {
                var str = System.Text.Encoding.ASCII.GetString(data, i, length);
                if (!string.IsNullOrWhiteSpace(str) && !strings.Contains(str))
                {
                    strings.Add(str);
                    i += length - 1; // Skip ahead (loop will add 1)
                }
            }
        }

        return strings;
    }

    private static bool IsLikelyUnicodeString(byte[] data, int offset, out int length)
    {
        length = 0;
        int nullTerminatorCount = 0;
        int charCount = 0;

        for (int i = offset; i < data.Length - 1; i += 2)
        {
            byte b1 = data[i];
            byte b2 = data[i + 1];

            // Check for null terminator
            if (b1 == 0 && b2 == 0)
            {
                nullTerminatorCount++;
                length = i - offset + 2;
                break;
            }

            // Check if it's a printable Unicode character (ASCII range + null high byte)
            if (b2 == 0 && b1 >= 0x20 && b1 <= 0x7E)
            {
                charCount++;
            }
            else if (b1 == 0x3A && b2 == 0x00) // ':' - common in URIs
            {
                charCount++;
            }
            else if (b1 == 0x2F && b2 == 0x00) // '/' - common in URIs
            {
                charCount++;
            }
            else
            {
                // Not a valid Unicode string
                break;
            }

            if (charCount >= 4 && i - offset > 20)
            {
                // Look ahead for null terminator
                bool foundTerminator = false;
                for (int j = i + 2; j < Math.Min(data.Length - 1, i + 200); j += 2)
                {
                    if (data[j] == 0 && data[j + 1] == 0)
                    {
                        length = j - offset + 2;
                        foundTerminator = true;
                        break;
                    }
                }
                return foundTerminator;
            }
        }

        return nullTerminatorCount > 0 && charCount >= 4;
    }

    private static bool IsLikelyAsciiString(byte[] data, int offset, out int length)
    {
        length = 0;
        int charCount = 0;

        for (int i = offset; i < Math.Min(data.Length, offset + 200); i++)
        {
            byte b = data[i];

            // Null terminator
            if (b == 0)
            {
                length = i - offset + 1;
                return charCount >= 4;
            }

            // Printable ASCII
            if (b is >= 0x20 and <= 0x7E)
            {
                charCount++;
            }
            else
            {
                break;
            }
        }

        return false;
    }

    private static List<Guid> ExtractClsidList(byte[] data)
    {
        var clsids = new List<Guid>();

        // GUIDs are 16 bytes, scan through the data looking for valid GUID patterns
        for (int i = 0; i <= data.Length - 16; i++)
        {
            try
            {
                var guidBytes = new byte[16];
                Array.Copy(data, i, guidBytes, 0, 16);
                var guid = new Guid(guidBytes);

                // Filter out all-zero or all-FF GUIDs (likely not real CLSIDs)
                if (guid != Guid.Empty && !IsAllSameValue(guidBytes))
                {
                    clsids.Add(guid);
                }
            }
            catch
            {
                // Invalid GUID, continue scanning
            }
        }

        return clsids.Distinct().ToList();
    }

    private static bool IsAllSameValue(byte[] bytes)
    {
        if (bytes.Length == 0) return true;
        var first = bytes[0];
        return bytes.All(b => b == first);
    }

    public string? GetLaunchUri()
    {
        // Look for URIs like msgamelaunch://, ms-xbl-*, etc.
        return ExtractedStringList.FirstOrDefault(s =>
            s.StartsWith("msgamelaunch://", StringComparison.OrdinalIgnoreCase) ||
            s.StartsWith("ms-xbl-", StringComparison.OrdinalIgnoreCase) ||
            s.Contains("://"));
    }

    public Guid? GetShellFolderClsid()
    {
        // Check structured items first
        var rootFolder = Items.OfType<RootFolderItem>().FirstOrDefault();
        if (rootFolder != null)
        {
            return rootFolder.Guid;
        }

        // Fall back to extracted CLSIDs
        return ExtractedClsidList.FirstOrDefault();
    }
}

/// <summary>
/// Base class for items in the LinkTargetIDList
/// </summary>
internal abstract class TargetItem
{
    public byte[] RawData { get; init; } = [];
}

/// <summary>
/// Root folder item - represents shell namespace roots like My Computer, Network, etc.
/// </summary>
internal class RootFolderItem : TargetItem
{
    public Guid Guid { get; init; }

    public string GetSortIndexName()
    {
        return Guid.ToString("D").ToUpperInvariant() switch
        {
            "20D04FE0-3AEA-1069-A2D8-08002B30309D" => "My Computer",
            "871C5380-42A0-1069-A2EA-08002B30309D" => "Internet Explorer",
            "208D2C60-3AEA-1069-A2D7-08002B30309D" => "Network",
            "450D8FBA-AD25-11D0-98A8-0800361B1103" => "My Documents",
            "52205FD8-5DFB-447D-801A-D0B52F2E83E1" => "My Games",
            _ => "Unknown"
        };
    }

    public int GetSortIndexValue()
    {
        return Guid.ToString("D").ToUpperInvariant() switch
        {
            "20D04FE0-3AEA-1069-A2D8-08002B30309D" => 80,  // My Computer
            "871C5380-42A0-1069-A2EA-08002B30309D" => 104, // Internet Explorer
            "208D2C60-3AEA-1069-A2D7-08002B30309D" => 72,  // Network
            "450D8FBA-AD25-11D0-98A8-0800361B1103" => 64,  // My Documents
            "52205FD8-5DFB-447D-801A-D0B52F2E83E1" => 128, // My Games
            _ => 0
        };
    }
}

/// <summary>
/// Volume item - represents a drive or volume
/// </summary>
internal class VolumeItem : TargetItem
{
    public byte Flags { get; init; }
    public string Data { get; init; } = string.Empty;
}

/// <summary>
/// File entry item - represents a file or directory in the path
/// </summary>
internal class FileEntryItem : TargetItem
{
    public byte Flags { get; init; }
    public uint FileSize { get; init; }
    public ushort FileAttributeFlags { get; init; }
    public string PrimaryName { get; init; } = string.Empty;
    public bool IsDirectory { get; init; }
    public DateTime? ModifiedTime { get; init; }

    public string GetFlagsDescription()
    {
        return IsDirectory ? "Is directory" : "Is file";
    }
}

/// <summary>
/// Unknown item type
/// </summary>
internal class UnknownItem : TargetItem
{
}
