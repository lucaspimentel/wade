namespace Wade.LnkParser;

[Flags]
internal enum LinkInfoFlags : uint
{
    None = 0,
    VolumeIDAndLocalBasePath = 0x00000001,
    CommonNetworkRelativeLinkAndPathSuffix = 0x00000002
}
