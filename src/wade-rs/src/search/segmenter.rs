//! Port of `PathSegmenter`.

use super::is_separator;

/// Split a path into non-empty segments on directory separator characters.
#[must_use]
pub fn split(path: &str) -> Vec<&str> {
    path.split(is_separator).filter(|segment| !segment.is_empty()).collect()
}

#[cfg(test)]
mod tests {
    use super::split;

    const SEP: char = std::path::MAIN_SEPARATOR;

    #[test]
    fn splits_on_directory_separator() {
        assert_eq!(split(&format!("src{SEP}Wade{SEP}App.cs")), ["src", "Wade", "App.cs"]);
    }

    #[test]
    fn trailing_separator_ignored() {
        assert_eq!(split(&format!("src{SEP}Wade{SEP}")), ["src", "Wade"]);
    }

    #[test]
    fn leading_separator_ignored() {
        assert_eq!(split(&format!("{SEP}src{SEP}file.txt")), ["src", "file.txt"]);
    }

    #[test]
    fn single_segment() {
        assert_eq!(split("file.txt"), ["file.txt"]);
    }

    #[test]
    fn empty_string_returns_empty() {
        assert!(split("").is_empty());
    }

    #[test]
    fn consecutive_separators_ignored() {
        assert_eq!(split(&format!("src{SEP}{SEP}Wade")), ["src", "Wade"]);
    }
}
