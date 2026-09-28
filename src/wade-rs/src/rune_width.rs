//! Port of src/Wade/Terminal/RuneWidth.cs: display width of a Unicode rune
//! in a terminal (1 or 2 columns), based on East Asian Width and emoji ranges.

/// Returns the number of terminal columns a rune occupies (1 or 2).
#[must_use]
pub fn rune_width(c: char) -> usize {
    let cp = c as u32;

    // Control characters and zero-width
    if cp < 0x20 || (0x7F..0xA0).contains(&cp) {
        return 1;
    }

    if is_wide(cp) { 2 } else { 1 }
}

#[must_use]
fn is_wide(cp: u32) -> bool {
    // CJK Radicals Supplement .. Enclosed CJK Letters
    (0x2E80..=0x33FF).contains(&cp)
        // CJK Unified Ideographs Extension A
        || (0x3400..=0x4DBF).contains(&cp)
        // CJK Unified Ideographs
        || (0x4E00..=0x9FFF).contains(&cp)
        // Yi Syllables .. Yi Radicals
        || (0xA000..=0xA4CF).contains(&cp)
        // Hangul Jamo
        || (0x1100..=0x115F).contains(&cp)
        // Hangul Jamo Extended-A
        || (0xA960..=0xA97C).contains(&cp)
        // Hangul Syllables
        || (0xAC00..=0xD7AF).contains(&cp)
        // Hangul Jamo Extended-B
        || (0xD7B0..=0xD7FF).contains(&cp)
        // CJK Compatibility Ideographs
        || (0xF900..=0xFAFF).contains(&cp)
        // CJK Compatibility Forms .. Small Form Variants
        || (0xFE10..=0xFE6F).contains(&cp)
        // Fullwidth Forms (not halfwidth)
        || (0xFF01..=0xFF60).contains(&cp)
        || (0xFFE0..=0xFFE6).contains(&cp)
        // CJK Unified Ideographs Extension B+
        || (0x20000..=0x3FFFF).contains(&cp)
        // Miscellaneous Symbols and Pictographs, Emoticons, etc.
        || (0x1F300..=0x1F9FF).contains(&cp)
        // Supplemental Symbols and Pictographs
        || (0x1FA00..=0x1FAFF).contains(&cp)
        // Symbols and Pictographs Extended-A
        || (0x1FB00..=0x1FBFF).contains(&cp)
        // Dingbats (some are wide in practice)
        || (0x2600..=0x27BF).contains(&cp)
        // Enclosed Alphanumeric Supplement (circled numbers, etc.)
        || (0x1F100..=0x1F1FF).contains(&cp)
}
