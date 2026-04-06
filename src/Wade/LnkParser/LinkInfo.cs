namespace Wade.LnkParser;

internal class LinkInfo
{
    public uint LinkInfoSize { get; init; }
    public LinkInfoFlags LinkInfoFlags { get; init; }
    public VolumeID? VolumeId { get; init; }
    public string? LocalBasePath { get; init; }
    public string? CommonPathSuffix { get; init; }
    public string? LocalBasePathUnicode { get; init; }
    public string? CommonPathSuffixUnicode { get; init; }

    public string GetLocationType()
    {
        if (LinkInfoFlags.HasFlag(LinkInfoFlags.VolumeIDAndLocalBasePath))
        {
            return "Local";
        }
        if (LinkInfoFlags.HasFlag(LinkInfoFlags.CommonNetworkRelativeLinkAndPathSuffix))
        {
            return "Network";
        }
        return "Unknown";
    }

    public static LinkInfo? Parse(BinaryReader reader)
    {
        var startPosition = reader.BaseStream.Position;
        var linkInfoSize = reader.ReadUInt32();
        var linkInfoHeaderSize = reader.ReadUInt32();
        var linkInfoFlags = (LinkInfoFlags)reader.ReadUInt32();
        var volumeIdOffset = reader.ReadUInt32();
        var localBasePathOffset = reader.ReadUInt32();
        var commonNetworkRelativeLinkOffset = reader.ReadUInt32();
        var commonPathSuffixOffset = reader.ReadUInt32();

        uint localBasePathOffsetUnicode = 0;
        uint commonPathSuffixOffsetUnicode = 0;

        if (linkInfoHeaderSize >= 0x00000024)
        {
            localBasePathOffsetUnicode = reader.ReadUInt32();
            commonPathSuffixOffsetUnicode = reader.ReadUInt32();
        }

        VolumeID? volumeId = null;
        string? localBasePath = null;
        string? commonPathSuffix = null;
        string? localBasePathUnicode = null;
        string? commonPathSuffixUnicode = null;

        if (linkInfoFlags.HasFlag(LinkInfoFlags.VolumeIDAndLocalBasePath))
        {
            if (volumeIdOffset > 0)
            {
                reader.BaseStream.Position = startPosition + volumeIdOffset;
                volumeId = VolumeID.Parse(reader);
            }

            if (localBasePathOffset > 0)
            {
                reader.BaseStream.Position = startPosition + localBasePathOffset;
                localBasePath = ReadNullTerminatedString(reader);
            }

            if (localBasePathOffsetUnicode > 0)
            {
                reader.BaseStream.Position = startPosition + localBasePathOffsetUnicode;
                localBasePathUnicode = ReadNullTerminatedUnicodeString(reader);
            }
        }

        if (commonPathSuffixOffset > 0)
        {
            reader.BaseStream.Position = startPosition + commonPathSuffixOffset;
            commonPathSuffix = ReadNullTerminatedString(reader);
        }

        if (commonPathSuffixOffsetUnicode > 0)
        {
            reader.BaseStream.Position = startPosition + commonPathSuffixOffsetUnicode;
            commonPathSuffixUnicode = ReadNullTerminatedUnicodeString(reader);
        }

        // Move to end of LinkInfo structure
        reader.BaseStream.Position = startPosition + linkInfoSize;

        return new LinkInfo
        {
            LinkInfoSize = linkInfoSize,
            LinkInfoFlags = linkInfoFlags,
            VolumeId = volumeId,
            LocalBasePath = localBasePath,
            CommonPathSuffix = commonPathSuffix,
            LocalBasePathUnicode = localBasePathUnicode,
            CommonPathSuffixUnicode = commonPathSuffixUnicode
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

    public string GetFullPath()
    {
        var basePath = LocalBasePathUnicode ?? LocalBasePath ?? string.Empty;
        var suffix = CommonPathSuffixUnicode ?? CommonPathSuffix ?? string.Empty;

        if (string.IsNullOrEmpty(suffix))
        {
            return basePath;
        }

        return basePath + suffix;
    }
}
