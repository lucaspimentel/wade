//! Character classification shared by the search scorer and the syntax
//! highlighters: .NET `char` predicates, exact for ASCII and approximated
//! with std's Unicode properties elsewhere (KNOWN_DEVIATIONS.md).

/// `char.IsUpper`.
#[must_use]
pub fn is_upper(c: char) -> bool {
    if c.is_ascii() { c.is_ascii_uppercase() } else { c.is_uppercase() }
}

/// `char.IsLower`.
#[must_use]
pub fn is_lower(c: char) -> bool {
    if c.is_ascii() { c.is_ascii_lowercase() } else { c.is_lowercase() }
}

/// `char.IsDigit`.
#[must_use]
pub fn is_digit(c: char) -> bool {
    if c.is_ascii() { c.is_ascii_digit() } else { c.is_numeric() }
}

/// `char.IsLetter`.
#[must_use]
pub fn is_letter(c: char) -> bool {
    if c.is_ascii() { c.is_ascii_alphabetic() } else { c.is_alphabetic() }
}

/// `char.IsLetterOrDigit`.
#[must_use]
pub fn is_letter_or_digit(c: char) -> bool {
    if c.is_ascii() { c.is_ascii_alphanumeric() } else { c.is_alphanumeric() }
}

/// `char.IsWhiteSpace` (std's White_Space property matches .NET's set).
#[must_use]
pub fn is_whitespace(c: char) -> bool {
    c.is_whitespace()
}

/// `char.ToLowerInvariant`: the first char of the full lowercase mapping
/// equals the simple mapping .NET uses (e.g. `İ` -> `i`).
#[must_use]
pub fn to_lower(c: char) -> char {
    if c.is_ascii() {
        return c.to_ascii_lowercase();
    }

    c.to_lowercase().next().unwrap_or(c)
}

/// Per-char upper-casing for `OrdinalIgnoreCase`: simple mappings only, so
/// characters whose uppercase expands (`ß` -> `SS`) stay as themselves.
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

/// `string.Compare(a, b, StringComparison.OrdinalIgnoreCase)`: per-char
/// simple uppercase, compared as UTF-16 code units.
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

        // .NET treats these ASCII controls as whitespace too
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
