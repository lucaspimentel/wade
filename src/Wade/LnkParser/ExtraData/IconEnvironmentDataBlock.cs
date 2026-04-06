namespace Wade.LnkParser.ExtraData;

/// <summary>
/// IconEnvironmentDataBlock (signature 0xA0000007)
/// Stores icon location with environment variables (e.g., %ProgramFiles%).
/// </summary>
internal class IconEnvironmentDataBlock : ExtraDataBlock
{
    public string TargetAnsi { get; init; } = string.Empty;
    public string TargetUnicode { get; init; } = string.Empty;

    private IconEnvironmentDataBlock(uint blockSize, uint blockSignature)
        : base(blockSize, blockSignature)
    {
    }

    public static IconEnvironmentDataBlock Parse(BinaryReader reader, uint blockSize, uint blockSignature)
    {
        var targetAnsiBytes = reader.ReadBytes(260);
        var targetUnicodeBytes = reader.ReadBytes(520);

        var targetAnsi = ReadNullTerminatedString(targetAnsiBytes);
        var targetUnicode = ReadNullTerminatedUnicodeString(targetUnicodeBytes);

        return new IconEnvironmentDataBlock(blockSize, blockSignature)
        {
            TargetAnsi = targetAnsi,
            TargetUnicode = targetUnicode
        };
    }

    private static string ReadNullTerminatedString(byte[] bytes)
    {
        var length = Array.IndexOf(bytes, (byte)0);
        if (length < 0) length = bytes.Length;
        return System.Text.Encoding.Default.GetString(bytes, 0, length);
    }

    private static string ReadNullTerminatedUnicodeString(byte[] bytes)
    {
        for (int i = 0; i < bytes.Length - 1; i += 2)
        {
            if (bytes[i] == 0 && bytes[i + 1] == 0)
            {
                return System.Text.Encoding.Unicode.GetString(bytes, 0, i);
            }
        }
        return System.Text.Encoding.Unicode.GetString(bytes);
    }
}
