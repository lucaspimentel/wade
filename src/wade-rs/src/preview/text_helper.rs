//! Port of src/Wade/Preview/TextHelper.cs plus the invariant-culture
//! number formatting the metadata providers share.

/// Port of `TextHelper.WrapText`: collapses whitespace runs to single
/// spaces, then breaks at the last space within `max_width` (or hard-breaks
/// a longer word). Widths count code points (KNOWN_DEVIATIONS.md).
#[must_use]
pub fn wrap_text(text: &str, max_width: usize) -> Vec<String> {
    let normalized: Vec<char> = text.split_whitespace().collect::<Vec<_>>().join(" ").chars().collect();

    if normalized.len() <= max_width {
        return vec![normalized.iter().collect()];
    }

    let mut result = Vec::new();
    let mut pos = 0;

    while pos < normalized.len() {
        if pos + max_width >= normalized.len() {
            result.push(normalized[pos..].iter().collect());
            break;
        }

        // LastIndexOf(' ', pos + maxWidth, maxWidth): searches (pos, pos + maxWidth]
        let mut break_at = (pos + 1..=pos + max_width).rev().find(|&i| normalized[i] == ' ').unwrap_or(pos);

        if break_at <= pos {
            break_at = pos + max_width;
        }

        result.push(normalized[pos..break_at].iter().collect());
        pos = break_at + 1;
    }

    result
}

/// .NET `{value:N0}` (invariant/en-US): thousands separators, no decimals.
#[must_use]
pub fn format_n0(value: i64) -> String {
    let digits = value.unsigned_abs().to_string();
    let mut out = String::new();

    for (i, ch) in digits.chars().enumerate() {
        if i > 0 && (digits.len() - i).is_multiple_of(3) {
            out.push(',');
        }

        out.push(ch);
    }

    if value < 0 { format!("-{out}") } else { out }
}

/// .NET `int.TryParse`/`long.TryParse` with `NumberStyles.Integer`:
/// surrounding whitespace and one leading sign are allowed.
#[must_use]
pub fn parse_integer<T: std::str::FromStr>(text: &str) -> Option<T> {
    let trimmed = text.trim();
    let unsigned = trimmed.strip_prefix(['+', '-']).unwrap_or(trimmed);

    if unsigned.is_empty() || !unsigned.bytes().all(|b| b.is_ascii_digit()) {
        return None;
    }

    trimmed.strip_prefix('+').unwrap_or(trimmed).parse().ok()
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn short_text_is_one_line_with_whitespace_collapsed() {
        assert_eq!(wrap_text("  a \t b\n c ", 20), ["a b c"]);
    }

    #[test]
    fn wraps_at_last_space_and_hard_breaks_long_words() {
        assert_eq!(wrap_text("the quick brown fox jumps", 10), ["the quick", "brown fox", "jumps"]);
        // Like C#, a hard break skips the character after it (pos = breakAt + 1)
        assert_eq!(wrap_text("abcdefghijklmno", 5), ["abcde", "ghijk", "mno"]);
        assert_eq!(wrap_text("ab abcdefghij", 5), ["ab", "abcde", "ghij"]);
    }

    #[test]
    fn n0_groups_thousands() {
        assert_eq!(format_n0(0), "0");
        assert_eq!(format_n0(999), "999");
        assert_eq!(format_n0(1000), "1,000");
        assert_eq!(format_n0(-1_234_567), "-1,234,567");
    }

    #[test]
    fn integers_parse_like_dotnet() {
        assert_eq!(parse_integer::<i32>(" 42 "), Some(42));
        assert_eq!(parse_integer::<i32>("+7"), Some(7));
        assert_eq!(parse_integer::<i32>("-7"), Some(-7));
        assert_eq!(parse_integer::<i32>("4.2"), None);
        assert_eq!(parse_integer::<i32>("99999999999"), None);
        assert_eq!(parse_integer::<i64>(""), None);
    }
}
