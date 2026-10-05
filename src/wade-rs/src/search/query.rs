//! Port of `SearchQuery` / `QueryMode`, extended (Rust only) with fzf's
//! multi-term syntax: space-separated terms are ANDed; `'foo` is an exact
//! substring, `^foo` an exact match at the start of a path segment, `foo$`
//! an exact match at the end of the path, and `!` negates a term (always
//! exact). `\ ` is a literal space. Every term uses smart case.

/// Port of `QueryMode`, plus the anchored modes.
#[derive(Clone, Copy, Debug, PartialEq, Eq)]
pub enum QueryMode {
    Fuzzy,
    ExactSubstring,
    /// `^foo`: exact, starting at the path start or after a separator.
    Prefix,
    /// `foo$`: exact, ending at the end of the relative path.
    Suffix,
    /// `^foo$`: exact, a whole trailing run of segments.
    PrefixSuffix,
}

/// One term of a query.
#[derive(Clone, Debug, PartialEq, Eq)]
pub struct SearchTerm {
    pub mode: QueryMode,
    pub text: String,
    /// Smart case: the text has an uppercase letter.
    pub case_sensitive: bool,
    /// `!` prefix: the entry must not match.
    pub negated: bool,
}

/// A parsed query: every positive term must match and no negated term may.
#[derive(Clone, Debug, PartialEq, Eq)]
pub struct SearchQuery {
    pub terms: Vec<SearchTerm>,
}

impl SearchQuery {
    /// True when no term survived parsing (empty input or lone operators).
    #[must_use]
    pub fn is_empty(&self) -> bool {
        self.terms.is_empty()
    }

    #[must_use]
    pub fn parse(raw: &str) -> Self {
        Self {
            terms: split_terms(raw).iter().filter_map(|term| parse_term(term)).collect(),
        }
    }
}

/// Splits on runs of spaces; `\ ` is a literal space.
fn split_terms(raw: &str) -> Vec<String> {
    let mut terms = Vec::new();
    let mut current = String::new();
    let mut chars = raw.chars().peekable();

    while let Some(c) = chars.next() {
        match c {
            '\\' if chars.peek() == Some(&' ') => {
                current.push(' ');
                chars.next();
            }
            ' ' => {
                if !current.is_empty() {
                    terms.push(std::mem::take(&mut current));
                }
            }
            _ => current.push(c),
        }
    }

    if !current.is_empty() {
        terms.push(current);
    }

    terms
}

/// Strips the operators of one term; `None` when no text remains.
fn parse_term(raw: &str) -> Option<SearchTerm> {
    let (negated, rest) = match raw.strip_prefix('!') {
        Some(rest) => (true, rest),
        None => (false, raw),
    };

    let (mode, text) = if let Some(text) = rest.strip_prefix('\'') {
        (QueryMode::ExactSubstring, text)
    } else {
        let (prefix, rest) = match rest.strip_prefix('^') {
            Some(rest) => (true, rest),
            None => (false, rest),
        };
        let (suffix, text) = match rest.strip_suffix('$') {
            Some(text) => (true, text),
            None => (false, rest),
        };

        let mode = match (prefix, suffix) {
            (true, true) => QueryMode::PrefixSuffix,
            (true, false) => QueryMode::Prefix,
            (false, true) => QueryMode::Suffix,
            // A negated term is never fuzzy (fzf)
            (false, false) if negated => QueryMode::ExactSubstring,
            (false, false) => QueryMode::Fuzzy,
        };

        (mode, text)
    };

    if text.is_empty() {
        return None;
    }

    Some(SearchTerm {
        mode,
        text: text.to_string(),
        case_sensitive: has_upper(text),
        negated,
    })
}

fn has_upper(s: &str) -> bool {
    s.chars().any(crate::text::is_upper)
}

#[cfg(test)]
mod tests {
    use super::{QueryMode, SearchQuery, SearchTerm};

    fn term(mode: QueryMode, text: &str, case_sensitive: bool, negated: bool) -> SearchTerm {
        SearchTerm {
            mode,
            text: text.to_string(),
            case_sensitive,
            negated,
        }
    }

    fn single(raw: &str) -> SearchTerm {
        let query = SearchQuery::parse(raw);
        assert_eq!(query.terms.len(), 1, "{raw:?}");
        query.terms[0].clone()
    }

    #[test]
    fn parse_empty_input_and_lone_operators_have_no_terms() {
        for raw in ["", " ", "'", "!", "^", "$", "^$", "!'", "!^", "!$", "' ! ^"] {
            assert!(SearchQuery::parse(raw).is_empty(), "{raw:?}");
        }
    }

    #[test]
    fn parse_no_prefix_returns_fuzzy_with_smart_case() {
        for (raw, case_sensitive) in [("foo", false), ("Foo", true), ("src/Wade", true), ("a1-b", false)] {
            assert_eq!(single(raw), term(QueryMode::Fuzzy, raw, case_sensitive, false));
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
            assert_eq!(
                single(raw),
                term(QueryMode::ExactSubstring, expected_text, expected_case_sensitive, false)
            );
        }
    }

    #[test]
    fn parse_anchors() {
        assert_eq!(single("^src"), term(QueryMode::Prefix, "src", false, false));
        assert_eq!(single(".rs$"), term(QueryMode::Suffix, ".rs", false, false));
        assert_eq!(single("^App.cs$"), term(QueryMode::PrefixSuffix, "App.cs", true, false));
        // A quoted term keeps ^ and $ as text
        assert_eq!(single("'^a$"), term(QueryMode::ExactSubstring, "^a$", false, false));
    }

    #[test]
    fn parse_negation_is_never_fuzzy() {
        assert_eq!(single("!test"), term(QueryMode::ExactSubstring, "test", false, true));
        assert_eq!(single("!'Test"), term(QueryMode::ExactSubstring, "Test", true, true));
        assert_eq!(single("!^obj"), term(QueryMode::Prefix, "obj", false, true));
        assert_eq!(single("!.md$"), term(QueryMode::Suffix, ".md", false, true));
    }

    #[test]
    fn parse_splits_on_spaces_with_per_term_case() {
        let query = SearchQuery::parse("  src  'App   !test .cs$ ");
        assert_eq!(
            query.terms,
            [
                term(QueryMode::Fuzzy, "src", false, false),
                term(QueryMode::ExactSubstring, "App", true, false),
                term(QueryMode::ExactSubstring, "test", false, true),
                term(QueryMode::Suffix, ".cs", false, false),
            ]
        );
    }

    #[test]
    fn parse_backslash_space_is_a_literal_space() {
        assert_eq!(single("Program\\ Files"), term(QueryMode::Fuzzy, "Program Files", true, false));
        assert_eq!(single("'a\\ b"), term(QueryMode::ExactSubstring, "a b", false, false));
        // A backslash not followed by a space is kept (a Windows separator)
        assert_eq!(single("src\\a"), term(QueryMode::Fuzzy, "src\\a", false, false));
    }
}
