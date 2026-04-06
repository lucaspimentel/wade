using System.Text;

namespace Wade.LnkParser;

internal class StringData
{
    public string? Name { get; init; }
    public string? RelativePath { get; init; }
    public string? WorkingDir { get; init; }
    public string? CommandLineArguments { get; init; }
    public string? IconLocation { get; init; }

    public static StringData Parse(BinaryReader reader, LinkFlags linkFlags)
    {
        var isUnicode = linkFlags.HasFlag(LinkFlags.IsUnicode);

        return new StringData
        {
            Name = linkFlags.HasFlag(LinkFlags.HasName)
                ? ReadStringDataField(reader, isUnicode)
                : null,
            RelativePath = linkFlags.HasFlag(LinkFlags.HasRelativePath)
                ? ReadStringDataField(reader, isUnicode)
                : null,
            WorkingDir = linkFlags.HasFlag(LinkFlags.HasWorkingDir)
                ? ReadStringDataField(reader, isUnicode)
                : null,
            CommandLineArguments = linkFlags.HasFlag(LinkFlags.HasArguments)
                ? ReadStringDataField(reader, isUnicode)
                : null,
            IconLocation = linkFlags.HasFlag(LinkFlags.HasIconLocation)
                ? ReadStringDataField(reader, isUnicode)
                : null
        };
    }

    private static string ReadStringDataField(BinaryReader reader, bool isUnicode)
    {
        var countCharacters = reader.ReadUInt16();

        if (isUnicode)
        {
            var bytes = reader.ReadBytes(countCharacters * 2);
            return Encoding.Unicode.GetString(bytes);
        }
        else
        {
            var bytes = reader.ReadBytes(countCharacters);
            return Encoding.Default.GetString(bytes);
        }
    }
}
