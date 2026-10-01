//! Port of `SearchQuery` / `QueryMode`.

/// Port of `QueryMode`.
#[derive(Clone, Copy, Debug, PartialEq, Eq)]
pub enum QueryMode {
    Fuzzy,
    ExactSubstring,
}

/// Port of `SearchQuery`: a parsed query (`'foo` = exact substring with
/// smart case; anything else = case-insensitive fuzzy).
#[derive(Clone, Debug, PartialEq, Eq)]
pub struct SearchQuery {
    pub mode: QueryMode,
    pub text: String,
    pub case_sensitive: bool,
}

impl SearchQuery {
    #[must_use]
    pub fn is_empty(&self) -> bool {
        self.text.is_empty()
    }

    #[must_use]
    pub fn parse(raw: &str) -> Self {
        if raw.is_empty() {
            return Self {
                mode: QueryMode::Fuzzy,
                text: String::new(),
                case_sensitive: false,
            };
        }

        if let Some(text) = raw.strip_prefix('\'') {
            return Self {
                mode: QueryMode::ExactSubstring,
                text: text.to_string(),
                case_sensitive: has_upper(text),
            };
        }

        Self {
            mode: QueryMode::Fuzzy,
            text: raw.to_string(),
            case_sensitive: false,
        }
    }
}

fn has_upper(s: &str) -> bool {
    s.chars().any(super::scorer::is_upper)
}

#[cfg(test)]
mod tests {
    use super::{QueryMode, SearchQuery};

    #[test]
    fn parse_empty_body_is_empty() {
        for (raw, expected_exact_mode) in [("", false), ("'", true)] {
            let query = SearchQuery::parse(raw);
            let expected_mode = if expected_exact_mode { QueryMode::ExactSubstring } else { QueryMode::Fuzzy };
            assert_eq!(query.mode, expected_mode, "{raw:?}");
            assert_eq!(query.text, "");
            assert!(query.is_empty());
            assert!(!query.case_sensitive);
        }
    }

    #[test]
    fn parse_no_prefix_returns_fuzzy() {
        for raw in ["foo", "Foo", "src/Wade"] {
            let query = SearchQuery::parse(raw);
            assert_eq!(query.mode, QueryMode::Fuzzy);
            assert_eq!(query.text, raw);
            assert!(!query.is_empty());
            assert!(!query.case_sensitive); // Fuzzy mode is always case-insensitive today
        }
    }

    #[test]
    fn parse_quote_prefix_returns_exact_substring_with_smart_case() {
        for (raw, expected_text, expected_case_sensitive) in [
            ("'foo", "foo", false),
            ("'Foo", "Foo", true),
            ("'fooBar", "fooBar", true),
            ("'src/Wade", "src/Wade", true),
            ("'all-lower-with-digits-123", "all-lower-with-digits-123", false),
        ] {
            let query = SearchQuery::parse(raw);
            assert_eq!(query.mode, QueryMode::ExactSubstring, "{raw:?}");
            assert_eq!(query.text, expected_text);
            assert_eq!(query.case_sensitive, expected_case_sensitive, "{raw:?}");
            assert!(!query.is_empty());
        }
    }
}
