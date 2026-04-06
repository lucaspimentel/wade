using Wade.LnkParser.ExtraData;

namespace Wade.LnkParser;

/// <summary>
/// Represents a parsed Windows .lnk (Shell Link) file.
/// These are binary files that store shortcuts to files, folders, and shell objects.
/// </summary>
internal class LnkFile
{
    /// <summary>
    /// The Shell Link header containing identification information, timestamps, and flags.
    /// </summary>
    public ShellLinkHeader Header { get; private init; } = null!;

    /// <summary>
    /// Optional structure containing the target's Item ID List (shell namespace path).
    /// Contains URIs for Windows Store apps and CLSIDs for shell folders.
    /// </summary>
    public LinkTargetIdList? LinkTargetIdList { get; private init; }

    /// <summary>
    /// Optional structure containing information about the link target location.
    /// Provides local or network path information.
    /// </summary>
    public LinkInfo? LinkInfo { get; init; }

    /// <summary>
    /// Optional structure containing string data like description, working directory, arguments, etc.
    /// </summary>
    public StringData? StringData { get; init; }

    /// <summary>
    /// Extra data blocks that appear after StringData.
    /// Includes icon locations, special folders, distributed link tracking, and property stores.
    /// </summary>
    public List<ExtraDataBlock> ExtraDataBlocks { get; init; } = [];

    /// <summary>
    /// Parses a Windows .lnk file.
    /// </summary>
    /// <param name="filePath">Path to the .lnk file</param>
    /// <returns>Parsed LnkFile object</returns>
    /// <exception cref="FileNotFoundException">If the file doesn't exist</exception>
    /// <exception cref="InvalidDataException">If the file is not a valid .lnk file</exception>
    public static LnkFile Parse(string filePath)
    {
        using var fileStream = File.OpenRead(filePath);
        using var reader = new BinaryReader(fileStream);

        var header = ShellLinkHeader.Parse(reader);

        LinkTargetIdList? linkTargetIdList = null;
        if (header.LinkFlags.HasFlag(LinkFlags.HasLinkTargetIdList))
        {
            linkTargetIdList = LinkTargetIdList.Parse(reader);
        }

        LinkInfo? linkInfo = null;
        if (header.LinkFlags.HasFlag(LinkFlags.HasLinkInfo))
        {
            linkInfo = LinkInfo.Parse(reader);
        }

        var stringData = StringData.Parse(reader, header.LinkFlags);
        var extraDataBlocks = ExtraDataBlock.ParseAll(reader);

        return new LnkFile
        {
            Header = header,
            LinkTargetIdList = linkTargetIdList,
            LinkInfo = linkInfo,
            StringData = stringData,
            ExtraDataBlocks = extraDataBlocks
        };
    }

    /// <summary>
    /// Gets the target file path from the shortcut.
    /// Returns null for Windows Store apps and shell folders.
    /// </summary>
    /// <returns>Target file path, or null if not a traditional file shortcut</returns>
    public string? GetTargetPath()
    {
        // Prefer full path from LinkInfo
        if (LinkInfo != null)
        {
            var fullPath = LinkInfo.GetFullPath();
            if (!string.IsNullOrEmpty(fullPath))
            {
                return fullPath;
            }
        }

        // Fall back to relative path
        return StringData?.RelativePath;
    }

    /// <summary>
    /// Gets the launch URI for Windows Store/Xbox apps.
    /// Returns null for traditional file shortcuts.
    /// </summary>
    /// <returns>Launch URI (e.g., "msgamelaunch://..."), or null if not found</returns>
    public string? GetLaunchUri()
    {
        return LinkTargetIdList?.GetLaunchUri();
    }

    /// <summary>
    /// Gets the CLSID for shell folder shortcuts (e.g., File Explorer, Control Panel).
    /// Returns null for traditional file shortcuts and Windows Store apps.
    /// </summary>
    /// <returns>Shell folder CLSID, or null if not a shell folder shortcut</returns>
    public Guid? GetShellFolderCLSID()
    {
        return LinkTargetIdList?.GetShellFolderClsid();
    }
}
