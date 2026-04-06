namespace Wade.LnkParser.ExtraData;

/// <summary>
/// SpecialFolderDataBlock (signature 0xA0000005)
/// Stores special folder ID and offset into LinkTargetIDList.
/// </summary>
internal class SpecialFolderDataBlock : ExtraDataBlock
{
    public uint SpecialFolderId { get; init; }
    public uint Offset { get; init; }

    private SpecialFolderDataBlock(uint blockSize, uint blockSignature)
        : base(blockSize, blockSignature)
    {
    }

    public static SpecialFolderDataBlock Parse(BinaryReader reader, uint blockSize, uint blockSignature)
    {
        var specialFolderId = reader.ReadUInt32();
        var offset = reader.ReadUInt32();

        return new SpecialFolderDataBlock(blockSize, blockSignature)
        {
            SpecialFolderId = specialFolderId,
            Offset = offset
        };
    }
}
