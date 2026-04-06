namespace Wade.LnkParser;

/// <summary>
/// HotKey helper class to decode hotkey combinations
/// </summary>
internal static class HotKeyHelper
{
    public static string Decode(ushort hotKey)
    {
        if (hotKey == 0)
        {
            return "UNSET - UNSET {0x0000}";
        }

        byte lowByte = (byte)(hotKey & 0xFF);
        byte highByte = (byte)((hotKey >> 8) & 0xFF);

        var keyName = GetKeyName(lowByte);
        var modifierName = GetModifierName(highByte);

        return $"{modifierName} - {keyName} {{0x{hotKey:X4}}}";
    }

    private static string GetKeyName(byte key)
    {
        return key switch
        {
            >= 0x30 and <= 0x39 => ((char)key).ToString(), // 0-9
            >= 0x41 and <= 0x5A => ((char)key).ToString(), // A-Z
            0x70 => "F1",
            0x71 => "F2",
            0x72 => "F3",
            0x73 => "F4",
            0x74 => "F5",
            0x75 => "F6",
            0x76 => "F7",
            0x77 => "F8",
            0x78 => "F9",
            0x79 => "F10",
            0x7A => "F11",
            0x7B => "F12",
            0x7C => "F13",
            0x7D => "F14",
            0x7E => "F15",
            0x7F => "F16",
            0x80 => "F17",
            0x81 => "F18",
            0x82 => "F19",
            0x83 => "F20",
            0x84 => "F21",
            0x85 => "F22",
            0x86 => "F23",
            0x87 => "F24",
            0x90 => "NUM LOCK",
            0x91 => "SCROLL LOCK",
            _ => $"Unknown (0x{key:X2})"
        };
    }

    private static string GetModifierName(byte modifier)
    {
        if (modifier == 0) return "UNSET";

        var modifiers = new List<string>();

        if ((modifier & 0x01) != 0) modifiers.Add("SHIFT");
        if ((modifier & 0x02) != 0) modifiers.Add("CTRL");
        if ((modifier & 0x04) != 0) modifiers.Add("ALT");

        return modifiers.Count > 0 ? string.Join(" + ", modifiers) : "UNSET";
    }
}
