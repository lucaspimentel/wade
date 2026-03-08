using System.Text;

namespace Wade.Terminal;

/// <summary>
/// Determines the display width of a Unicode rune in a terminal (1 or 2 columns).
/// Based on Unicode East Asian Width property and emoji presentation ranges.
/// </summary>
internal static class RuneWidth
{
    /// <summary>
    /// Returns the number of terminal columns a rune occupies (1 or 2).
    /// </summary>
    public static int GetWidth(Rune rune)
    {
        int cp = rune.Value;

        // Control characters and zero-width
        if (cp < 0x20 || (cp >= 0x7F && cp < 0xA0))
        {
            return 1;
        }

        // East Asian Wide and Fullwidth ranges
        if (IsWide(cp))
        {
            return 2;
        }

        return 1;
    }

    private static bool IsWide(int cp) =>
        // CJK Radicals Supplement .. Enclosed CJK Letters
        (cp >= 0x2E80 && cp <= 0x33FF) ||
        // CJK Unified Ideographs Extension A
        (cp >= 0x3400 && cp <= 0x4DBF) ||
        // CJK Unified Ideographs
        (cp >= 0x4E00 && cp <= 0x9FFF) ||
        // Yi Syllables .. Yi Radicals
        (cp >= 0xA000 && cp <= 0xA4CF) ||
        // Hangul Jamo
        (cp >= 0x1100 && cp <= 0x115F) ||
        // Hangul Jamo Extended-A
        (cp >= 0xA960 && cp <= 0xA97C) ||
        // Hangul Syllables
        (cp >= 0xAC00 && cp <= 0xD7AF) ||
        // Hangul Jamo Extended-B
        (cp >= 0xD7B0 && cp <= 0xD7FF) ||
        // CJK Compatibility Ideographs
        (cp >= 0xF900 && cp <= 0xFAFF) ||
        // CJK Compatibility Forms .. Small Form Variants
        (cp >= 0xFE10 && cp <= 0xFE6F) ||
        // Fullwidth Forms (not halfwidth)
        (cp >= 0xFF01 && cp <= 0xFF60) ||
        (cp >= 0xFFE0 && cp <= 0xFFE6) ||
        // CJK Unified Ideographs Extension B+
        (cp >= 0x20000 && cp <= 0x3FFFF) ||
        // Miscellaneous Symbols and Pictographs, Emoticons, etc.
        (cp >= 0x1F300 && cp <= 0x1F9FF) ||
        // Supplemental Symbols and Pictographs
        (cp >= 0x1FA00 && cp <= 0x1FAFF) ||
        // Symbols and Pictographs Extended-A
        (cp >= 0x1FB00 && cp <= 0x1FBFF) ||
        // Dingbats (some are wide in practice)
        (cp >= 0x2600 && cp <= 0x27BF) ||
        // Enclosed Alphanumeric Supplement (circled numbers, etc.)
        (cp >= 0x1F100 && cp <= 0x1F1FF);
}
