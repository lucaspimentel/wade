namespace Wade.LnkParser.ExtraData;

/// <summary>
/// DistributedLinkTrackerDataBlock (signature 0xA0000006)
/// Stores distributed link tracking information for finding moved files.
/// </summary>
internal class DistributedLinkTrackerDataBlock : ExtraDataBlock
{
    public uint Length { get; init; }
    public uint Version { get; init; }
    public string MachineIdentifier { get; init; } = string.Empty;
    public Guid DroidVolumeIdentifier { get; init; }
    public Guid DroidFileIdentifier { get; init; }
    public Guid BirthDroidVolumeIdentifier { get; init; }
    public Guid BirthDroidFileIdentifier { get; init; }

    private DistributedLinkTrackerDataBlock(uint blockSize, uint blockSignature)
        : base(blockSize, blockSignature)
    {
    }

    public static DistributedLinkTrackerDataBlock Parse(BinaryReader reader, uint blockSize, uint blockSignature)
    {
        var length = reader.ReadUInt32();
        var version = reader.ReadUInt32();

        var machineIdentifierBytes = reader.ReadBytes(16);
        var machineIdentifier = System.Text.Encoding.ASCII.GetString(machineIdentifierBytes).TrimEnd('\0');

        var droidVolumeIdentifier = new Guid(reader.ReadBytes(16));
        var droidFileIdentifier = new Guid(reader.ReadBytes(16));
        var birthDroidVolumeIdentifier = new Guid(reader.ReadBytes(16));
        var birthDroidFileIdentifier = new Guid(reader.ReadBytes(16));

        return new DistributedLinkTrackerDataBlock(blockSize, blockSignature)
        {
            Length = length,
            Version = version,
            MachineIdentifier = machineIdentifier,
            DroidVolumeIdentifier = droidVolumeIdentifier,
            DroidFileIdentifier = droidFileIdentifier,
            BirthDroidVolumeIdentifier = birthDroidVolumeIdentifier,
            BirthDroidFileIdentifier = birthDroidFileIdentifier
        };
    }
}
