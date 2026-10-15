//! Character classification shared by the search scorer and the syntax
//! highlighters: exact for ASCII, std's Unicode properties elsewhere
//! (KNOWN_DEVIATIONS.md).

/// An uppercase letter.
#[must_use]
pub fn is_upper(c: char) -> bool {
    if c.is_ascii() { c.is_ascii_uppercase() } else { c.is_uppercase() }
}

/// A lowercase letter.
#[must_use]
pub fn is_lower(c: char) -> bool {
    if c.is_ascii() { c.is_ascii_lowercase() } else { c.is_lowercase() }
}

/// A digit (any numeric character outside ASCII).
#[must_use]
pub fn is_digit(c: char) -> bool {
    if c.is_ascii() { c.is_ascii_digit() } else { c.is_numeric() }
}

/// A letter.
#[must_use]
pub fn is_letter(c: char) -> bool {
    if c.is_ascii() { c.is_ascii_alphabetic() } else { c.is_alphabetic() }
}

/// A letter or digit.
#[must_use]
pub fn is_letter_or_digit(c: char) -> bool {
    if c.is_ascii() { c.is_ascii_alphanumeric() } else { c.is_alphanumeric() }
}

/// Whitespace (the Unicode White_Space property).
#[must_use]
pub fn is_whitespace(c: char) -> bool {
    c.is_whitespace()
}

/// The simple lowercase mapping: the first char of the full mapping (e.g.
/// `İ` -> `i`).
#[must_use]
pub fn to_lower(c: char) -> char {
    if c.is_ascii() {
        return c.to_ascii_lowercase();
    }

    c.to_lowercase().next().unwrap_or(c)
}

/// Per-char upper-casing for case-insensitive comparison: simple mappings only,
/// so characters whose uppercase expands (`ß` -> `SS`) stay as themselves.
#[must_use]
pub fn to_upper(c: char) -> char {
    if c.is_ascii() {
        return c.to_ascii_uppercase();
    }

    let mut upper = c.to_uppercase();

    match (upper.next(), upper.next()) {
        (Some(single), None) => single,
        _ => c,
    }
}

/// Case-insensitive ordering: per-char simple uppercase, compared as UTF-16
/// code units.
#[must_use]
pub fn compare_ordinal_ignore_case(a: &str, b: &str) -> std::cmp::Ordering {
    let units = |s: &str| -> Vec<u16> {
        let mut buf = [0u16; 2];
        s.chars().flat_map(|c| to_upper(c).encode_utf16(&mut buf).to_vec()).collect()
    };

    units(a).cmp(&units(b))
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn ascii_matches_dotnet() {
        for c in (0u8..128).map(char::from) {
            assert_eq!(is_upper(c), c.is_ascii_uppercase(), "{c:?}");
            assert_eq!(is_digit(c), c.is_ascii_digit(), "{c:?}");
            assert_eq!(is_letter(c), c.is_ascii_alphabetic(), "{c:?}");
        }

        // These ASCII controls are whitespace too
        for c in ['\t', '\n', '\u{b}', '\u{c}', '\r', ' '] {
            assert!(is_whitespace(c));
        }
    }

    #[test]
    fn case_mapping_uses_simple_mappings() {
        assert_eq!(to_lower('\u{130}'), 'i');
        assert_eq!(to_upper('\u{df}'), '\u{df}');
        assert_eq!(to_upper('\u{e9}'), '\u{c9}');
    }
}
