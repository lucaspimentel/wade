namespace Wade.LnkParser.ExtraData;

/// <summary>
/// KnownFolderDataBlock (signature 0xA000000B)
/// Stores known folder GUID and offset into LinkTargetIDList.
/// </summary>
internal class KnownFolderDataBlock : ExtraDataBlock
{
    public Guid KnownFolderId { get; init; }
    public uint Offset { get; init; }

    private KnownFolderDataBlock(uint blockSize, uint blockSignature)
        : base(blockSize, blockSignature)
    {
    }

    public static KnownFolderDataBlock Parse(BinaryReader reader, uint blockSize, uint blockSignature)
    {
        var startPos = reader.BaseStream.Position;
        var knownFolderId = new Guid(reader.ReadBytes(16));
        var offset = reader.ReadUInt32();

        // Skip any remaining bytes in this block
        var bytesRead = reader.BaseStream.Position - startPos;
        var expectedBytes = blockSize - 8; // -8 for size and signature
        if (bytesRead < expectedBytes)
        {
            reader.ReadBytes((int)(expectedBytes - bytesRead));
        }

        return new KnownFolderDataBlock(blockSize, blockSignature)
        {
            KnownFolderId = knownFolderId,
            Offset = offset
        };
    }
}
