//! Port of `src/Wade.Search`: fuzzy/exact path scoring, query parsing, and
//! a thread-safe index that streams scored results over a channel.
//!
//! Strings are scored as `&[char]` (code points), so match positions index
//! the `Vec<char>` of the relative path. For BMP-only paths these equal the
//! C# UTF-16 indices.

pub mod index;
pub mod query;
pub mod scorer;
pub mod segmenter;

pub use index::{SearchIndex, SearchOptions};
pub use query::{QueryMode, SearchQuery};

/// Port of `SearchResult`: an indexed absolute path, its score (higher is
/// better), and the matched character positions within its relative path.
#[derive(Clone, Debug, PartialEq, Eq)]
pub struct SearchResult {
    pub path: String,
    pub score: i32,
    pub match_positions: Vec<usize>,
}

/// `Path.DirectorySeparatorChar` / `AltDirectorySeparatorChar` set used by
/// `FuzzyScorer`, `PathSegmenter` and `SearchIndex`.
#[must_use]
pub fn is_separator(c: char) -> bool {
    c == std::path::MAIN_SEPARATOR || (cfg!(windows) && c == '/')
}

#[cfg(test)]
mod tests {
    use super::SearchResult;

    /// Port of `SearchResultTests.Score_ReflectsConstructorValue`.
    #[test]
    fn score_reflects_constructor_value() {
        for (path, score) in [("path/file.txt", 100), ("other/file.cs", 0), ("deep/nested/path.rs", -5)] {
            let result = SearchResult {
                path: path.to_string(),
                score,
                match_positions: Vec::new(),
            };
            assert_eq!(result.path, path);
            assert_eq!(result.score, score);
            assert!(result.match_positions.is_empty());
        }
    }
}
