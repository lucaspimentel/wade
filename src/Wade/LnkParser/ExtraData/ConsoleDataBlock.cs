namespace Wade.LnkParser.ExtraData;

/// <summary>
/// ConsoleDataBlock (signature 0xA0000001)
/// Stores console properties for console applications.
/// </summary>
internal class ConsoleDataBlock : ExtraDataBlock
{
    public byte[] RawData { get; init; } = [];

    private ConsoleDataBlock(uint blockSize, uint blockSignature)
        : base(blockSize, blockSignature)
    {
    }

    public static ConsoleDataBlock Parse(BinaryReader reader, uint blockSize, uint blockSignature)
    {
        var dataSize = (int)(blockSize - 8); // -8 for size and signature
        var rawData = reader.ReadBytes(dataSize);

        return new ConsoleDataBlock(blockSize, blockSignature)
        {
            RawData = rawData
        };
    }
}
