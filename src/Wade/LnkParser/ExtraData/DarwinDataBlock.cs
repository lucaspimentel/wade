namespace Wade.LnkParser.ExtraData;

/// <summary>
/// DarwinDataBlock (signature 0xA0000003)
/// Stores Darwin (Windows Installer) application identifiers.
/// </summary>
internal class DarwinDataBlock : ExtraDataBlock
{
    public byte[] RawData { get; init; } = [];

    private DarwinDataBlock(uint blockSize, uint blockSignature)
        : base(blockSize, blockSignature)
    {
    }

    public static DarwinDataBlock Parse(BinaryReader reader, uint blockSize, uint blockSignature)
    {
        var dataSize = (int)(blockSize - 8);
        var rawData = reader.ReadBytes(dataSize);

        return new DarwinDataBlock(blockSize, blockSignature)
        {
            RawData = rawData
        };
    }
}
