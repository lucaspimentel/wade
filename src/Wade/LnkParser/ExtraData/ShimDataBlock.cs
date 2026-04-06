namespace Wade.LnkParser.ExtraData;

/// <summary>
/// ShimDataBlock (signature 0xA000000C)
/// Stores shim layer information for application compatibility.
/// </summary>
internal class ShimDataBlock : ExtraDataBlock
{
    public byte[] RawData { get; init; } = [];

    private ShimDataBlock(uint blockSize, uint blockSignature)
        : base(blockSize, blockSignature)
    {
    }

    public static ShimDataBlock Parse(BinaryReader reader, uint blockSize, uint blockSignature)
    {
        var dataSize = (int)(blockSize - 8);
        var rawData = reader.ReadBytes(dataSize);

        return new ShimDataBlock(blockSize, blockSignature)
        {
            RawData = rawData
        };
    }
}
