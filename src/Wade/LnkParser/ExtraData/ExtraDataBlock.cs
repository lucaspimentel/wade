namespace Wade.LnkParser.ExtraData;

/// <summary>
/// Base class for extra data blocks that appear after StringData.
/// Each block has a 32-bit size followed by a 32-bit signature.
/// </summary>
internal abstract class ExtraDataBlock
{
    public uint BlockSize { get; init; }
    public uint BlockSignature { get; init; }

    protected ExtraDataBlock(uint blockSize, uint blockSignature)
    {
        BlockSize = blockSize;
        BlockSignature = blockSignature;
    }

    /// <summary>
    /// Parses all extra data blocks from the current position in the stream.
    /// Stops when it encounters a terminal block (size &lt; 4).
    /// </summary>
    public static List<ExtraDataBlock> ParseAll(BinaryReader reader)
    {
        var blocks = new List<ExtraDataBlock>();

        try
        {
            while (true)
            {
                // Check if we have at least 4 bytes for the size field
                if (reader.BaseStream.Position + 4 > reader.BaseStream.Length)
                {
                    System.Diagnostics.Debug.WriteLine($"ExtraData: Reached EOF at {reader.BaseStream.Position}");
                    break;
                }

                var startPos = reader.BaseStream.Position;
                var blockSize = reader.ReadUInt32();

                System.Diagnostics.Debug.WriteLine($"ExtraData: pos={startPos}, blockSize=0x{blockSize:X8}");

                // Terminal block
                if (blockSize < 4)
                {
                    System.Diagnostics.Debug.WriteLine($"ExtraData: Terminal block at {startPos}");
                    break;
                }

                // Sanity check: block size should be reasonable
                var remainingBytes = reader.BaseStream.Length - startPos;
                if (blockSize > remainingBytes)
                {
                    // Invalid block size, likely end of data
                    System.Diagnostics.Debug.WriteLine($"ExtraData: Invalid blockSize {blockSize} > remaining {remainingBytes}");
                    break;
                }

                var blockSignature = reader.ReadUInt32();
                var startPosition = reader.BaseStream.Position;

                ExtraDataBlock? block = blockSignature switch
                {
                    0xA0000002 => EnvironmentVariableDataBlock.Parse(reader, blockSize, blockSignature),
                    0xA0000001 => ConsoleDataBlock.Parse(reader, blockSize, blockSignature),
                    0xA0000006 => DistributedLinkTrackerDataBlock.Parse(reader, blockSize, blockSignature),
                    0xA0000004 => ConsoleFEDataBlock.Parse(reader, blockSize, blockSignature),
                    0xA0000005 => SpecialFolderDataBlock.Parse(reader, blockSize, blockSignature),
                    0xA0000003 => DarwinDataBlock.Parse(reader, blockSize, blockSignature),
                    0xA0000007 => IconEnvironmentDataBlock.Parse(reader, blockSize, blockSignature),
                    0xA000000B => KnownFolderDataBlock.Parse(reader, blockSize, blockSignature),
                    0xA0000009 => PropertyStoreDataBlock.Parse(reader, blockSize, blockSignature),
                    0xA000000C => ShimDataBlock.Parse(reader, blockSize, blockSignature),
                    _ => UnknownExtraDataBlock.Parse(reader, blockSize, blockSignature)
                };

                if (block != null)
                {
                    blocks.Add(block);
                }

                // The parser should have read all its data
                // Verify we're at the expected position
                var expectedPosition = startPos + blockSize;
                var actualPosition = reader.BaseStream.Position;

                if (actualPosition != expectedPosition)
                {
                    System.Diagnostics.Debug.WriteLine($"ExtraData: Position mismatch! Expected {expectedPosition}, actual {actualPosition}");
                    // Force to correct position
                    if (expectedPosition <= reader.BaseStream.Length)
                    {
                        reader.BaseStream.Position = expectedPosition;
                    }
                    else
                    {
                        System.Diagnostics.Debug.WriteLine($"ExtraData: Invalid expectedPosition {expectedPosition} > length {reader.BaseStream.Length}");
                        break;
                    }
                }
            }
        }
        catch (Exception ex)
        {
            // If we encounter any parsing errors in extra data, just return what we have
            // Extra data is optional and shouldn't fail the entire parse
            System.Diagnostics.Debug.WriteLine($"ExtraData parsing stopped due to error: {ex.Message}");
        }

        return blocks;
    }
}
