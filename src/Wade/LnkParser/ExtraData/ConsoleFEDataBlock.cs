namespace Wade.LnkParser.ExtraData;

/// <summary>
/// ConsoleFEDataBlock (signature 0xA0000004)
/// Stores console font and display properties for Far East languages.
/// </summary>
internal class ConsoleFEDataBlock : ExtraDataBlock
{
    public byte[] RawData { get; init; } = [];

    private ConsoleFEDataBlock(uint blockSize, uint blockSignature)
        : base(blockSize, blockSignature)
    {
    }

    public static ConsoleFEDataBlock Parse(BinaryReader reader, uint blockSize, uint blockSignature)
    {
        var dataSize = (int)(blockSize - 8);
        var rawData = reader.ReadBytes(dataSize);

        return new ConsoleFEDataBlock(blockSize, blockSignature)
        {
            RawData = rawData
        };
    }
}
