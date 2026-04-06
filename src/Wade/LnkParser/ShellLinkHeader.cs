namespace Wade.LnkParser;

internal class ShellLinkHeader
{
    public const uint ExpectedHeaderSize = 0x0000004C;
    public static readonly Guid ExpectedLinkClsid = new("00021401-0000-0000-C000-000000000046");

    public uint HeaderSize { get; init; }
    public Guid LinkClsid { get; init; }
    public LinkFlags LinkFlags { get; init; }
    public FileAttributes FileAttributes { get; init; }
    public DateTime? CreationTime { get; init; }
    public DateTime? AccessTime { get; init; }
    public DateTime? WriteTime { get; init; }
    public uint FileSize { get; init; }
    public int IconIndex { get; init; }
    public ShowCommand ShowCommand { get; init; }
    public ushort HotKey { get; init; }

    public string GetHotKeyDescription()
    {
        return HotKeyHelper.Decode(HotKey);
    }

    public static ShellLinkHeader Parse(BinaryReader reader)
    {
        var headerSize = reader.ReadUInt32();
        if (headerSize != ExpectedHeaderSize)
        {
            throw new InvalidDataException($"Invalid HeaderSize: 0x{headerSize:X8}, expected 0x{ExpectedHeaderSize:X8}");
        }

        var linkClsid = new Guid(reader.ReadBytes(16));
        if (linkClsid != ExpectedLinkClsid)
        {
            throw new InvalidDataException($"Invalid LinkCLSID: {linkClsid}, expected {ExpectedLinkClsid}");
        }

        var linkFlags = (LinkFlags)reader.ReadUInt32();
        var fileAttributes = (FileAttributes)reader.ReadUInt32();
        var creationTime = ReadFileTime(reader);
        var accessTime = ReadFileTime(reader);
        var writeTime = ReadFileTime(reader);
        var fileSize = reader.ReadUInt32();
        var iconIndex = reader.ReadInt32();
        var showCommand = (ShowCommand)reader.ReadUInt32();
        var hotKey = reader.ReadUInt16();

        // Skip reserved fields
        reader.ReadUInt16(); // Reserved1
        reader.ReadUInt32(); // Reserved2
        reader.ReadUInt32(); // Reserved3

        return new ShellLinkHeader
        {
            HeaderSize = headerSize,
            LinkClsid = linkClsid,
            LinkFlags = linkFlags,
            FileAttributes = fileAttributes,
            CreationTime = creationTime,
            AccessTime = accessTime,
            WriteTime = writeTime,
            FileSize = fileSize,
            IconIndex = iconIndex,
            ShowCommand = showCommand,
            HotKey = hotKey
        };
    }

    private static DateTime? ReadFileTime(BinaryReader reader)
    {
        var fileTime = reader.ReadInt64();
        if (fileTime == 0)
        {
            return null;
        }

        try
        {
            return DateTime.FromFileTimeUtc(fileTime);
        }
        catch
        {
            // Invalid FILETIME, return null
            return null;
        }
    }
}
