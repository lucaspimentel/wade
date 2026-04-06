namespace Wade.LnkParser.ExtraData;

/// <summary>
/// UnknownExtraDataBlock - used for unrecognized block signatures
/// </summary>
internal class UnknownExtraDataBlock : ExtraDataBlock
{
    public byte[] RawData { get; init; } = [];

    private UnknownExtraDataBlock(uint blockSize, uint blockSignature)
        : base(blockSize, blockSignature)
    {
    }

    public static UnknownExtraDataBlock Parse(BinaryReader reader, uint blockSize, uint blockSignature)
    {
        var dataSize = (int)(blockSize - 8);
        var rawData = reader.ReadBytes(dataSize);

        return new UnknownExtraDataBlock(blockSize, blockSignature)
        {
            RawData = rawData
        };
    }
}
