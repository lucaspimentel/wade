//! Char-slice helpers mirroring the C# `string` calls the tokenizers use.

/// `line.AsSpan(pos).StartsWith(prefix)`.
#[must_use]
pub fn starts_with_at(line: &[char], pos: usize, prefix: &str) -> bool {
    for (index, expected) in (pos..).zip(prefix.chars()) {
        if index >= line.len() || line[index] != expected {
            return false;
        }
    }

    true
}

/// `line.IndexOf(needle, from, StringComparison.Ordinal)`.
#[must_use]
pub fn index_of(line: &[char], needle: &str, from: usize) -> Option<usize> {
    let needle: Vec<char> = needle.chars().collect();

    if needle.is_empty() {
        return Some(from.min(line.len()));
    }

    if needle.len() > line.len() {
        return None;
    }

    (from..=line.len() - needle.len()).find(|&start| line[start..start + needle.len()] == needle[..])
}

/// `line.IndexOf(ch, from)`.
#[must_use]
pub fn index_of_char(line: &[char], ch: char, from: usize) -> Option<usize> {
    line.iter().skip(from).position(|&c| c == ch).map(|i| i + from)
}

/// The chars of `line[start..end]` as a `String`.
#[must_use]
pub fn substring(line: &[char], start: usize, end: usize) -> String {
    line[start..end].iter().collect()
}

#[cfg(test)]
mod tests {
    use super::{index_of, starts_with_at};

    #[test]
    fn helpers_match_dotnet_semantics() {
        let line: Vec<char> = "a /* b */ c".chars().collect();
        assert!(starts_with_at(&line, 2, "/*"));
        assert!(!starts_with_at(&line, 10, "c "));
        assert_eq!(index_of(&line, "*/", 4), Some(7));
        assert_eq!(index_of(&line, "*/", 8), None);
        assert_eq!(index_of(&line, "", 3), Some(3));
    }
}
