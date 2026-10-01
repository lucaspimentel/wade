//! Port of `FuzzyScorer`: greedy subsequence scorer with boundary-aware
//! scoring, inspired by fzf v1. Matches query characters in order against a
//! target with gaps allowed; boundary bonuses (path separators, dots,
//! camelCase) reward matches at meaningful positions.
//!
//! Scores use the C# `int.MinValue` sentinel (`NO_MATCH`) for "not a match".
//! Character classification approximates .NET's `char` methods with std
//! (exact for ASCII; see KNOWN_DEVIATIONS.md).

use super::is_separator;
use crate::text::{is_digit, is_letter_or_digit, is_lower, is_upper, to_lower, to_upper};

// Scoring constants (fzf-inspired, proven to produce good rankings)
pub const SCORE_MATCH: i32 = 16;
pub const PENALTY_GAP_START: i32 = -3;
pub const PENALTY_GAP_EXTENSION: i32 = -1;
pub const BONUS_BOUNDARY: i32 = 8;
pub const BONUS_BOUNDARY_DELIMITER: i32 = 9;
pub const BONUS_CAMEL: i32 = 7;
pub const BONUS_NON_WORD: i32 = 8;
pub const BONUS_CONSECUTIVE: i32 = 4;
pub const BONUS_FIRST_CHAR_MULTIPLIER: i32 = 2;
pub const BONUS_CASE_MATCH: i32 = 1;
pub const PENALTY_TRAILING_GAP: i32 = -1;
pub const FILE_NAME_BONUS: i32 = 1000;
pub const PENALTY_DEPTH: i32 = -5;

/// `int.MinValue`: the query is not a match.
pub const NO_MATCH: i32 = i32::MIN;

/// Score a query against a target using greedy subsequence matching.
/// Returns `NO_MATCH` when the query is not a subsequence of the target.
#[must_use]
pub fn score(query: &[char], target: &[char]) -> i32 {
    let mut positions = vec![0usize; query.len()];
    score_core(query, target, &mut positions)
}

/// `score` that also returns the tightened match positions (empty when the
/// query is empty or does not match).
#[must_use]
pub fn score_with_positions(query: &[char], target: &[char]) -> (i32, Vec<usize>) {
    if query.is_empty() {
        return (0, Vec::new());
    }

    let mut positions = vec![0usize; query.len()];
    let score = score_core(query, target, &mut positions);

    if score == NO_MATCH {
        (NO_MATCH, Vec::new())
    } else {
        (score, positions)
    }
}

/// Core scoring logic: writes tightened match positions into `positions`
/// (at least `query.len()` long) and returns the score or `NO_MATCH`.
fn score_core(query: &[char], target: &[char], positions: &mut [usize]) -> i32 {
    let query_len = query.len();
    let target_len = target.len();

    if query_len == 0 {
        return 0;
    }

    if query_len > target_len {
        return NO_MATCH;
    }

    // Pre-lowercase the query once, normalizing path separators.
    let query_lower: Vec<char> = query
        .iter()
        .map(|&c| {
            if c == '/' || c == '\\' {
                to_lower(std::path::MAIN_SEPARATOR)
            } else {
                to_lower(c)
            }
        })
        .collect();

    // Forward scan: greedily find the first subsequence match.
    let mut forward_positions = vec![0usize; query_len];
    let mut qi = 0;

    for (ti, &tc) in target.iter().enumerate() {
        if qi >= query_len {
            break;
        }

        if to_lower(tc) == query_lower[qi] {
            forward_positions[qi] = ti;
            qi += 1;
        }
    }

    if qi < query_len {
        return NO_MATCH; // Not all query chars found
    }

    // Backward scan: from the last forward match, walk backward to tighten
    // the match span (a shorter span ending at the same position typically
    // scores better).
    let last_forward_pos = forward_positions[query_len - 1];
    let mut remaining = query_len;

    for ti in (0..=last_forward_pos).rev() {
        if remaining == 0 {
            break;
        }

        if to_lower(target[ti]) == query_lower[remaining - 1] {
            positions[remaining - 1] = ti;
            remaining -= 1;
        }
    }

    compute_score(query, target, &positions[..query_len])
}

/// Score with filename priority: scores the query against just the filename
/// (with a large bonus) and against the full relative path, keeps the
/// higher, then applies the depth penalty. `NO_MATCH` if neither matches.
#[must_use]
pub fn score_with_file_name_priority(query: &[char], relative_path: &[char], file_name_start: usize) -> i32 {
    let best = best_with_file_name_priority(relative_path, file_name_start, |target| score(query, target)).0;

    if best == NO_MATCH {
        return NO_MATCH;
    }

    best + PENALTY_DEPTH * count_separators(relative_path)
}

/// `score_with_file_name_priority` plus match positions into the full
/// relative path (allocated once, for the winning candidate).
#[must_use]
pub fn score_with_file_name_priority_positions(
    query: &[char],
    relative_path: &[char],
    file_name_start: usize,
) -> (i32, Vec<usize>) {
    let (best, use_file_name) =
        best_with_file_name_priority(relative_path, file_name_start, |target| score(query, target));

    if best == NO_MATCH {
        return (NO_MATCH, Vec::new());
    }

    let positions = if use_file_name {
        let (_, mut positions) = score_with_positions(query, &relative_path[file_name_start..]);
        positions.iter_mut().for_each(|p| *p += file_name_start);
        positions
    } else {
        score_with_positions(query, relative_path).1
    };

    (best + PENALTY_DEPTH * count_separators(relative_path), positions)
}

/// Score a query as an exact contiguous substring (fzf `'` prefix).
/// Separators in the query are normalized to the platform separator.
/// `NO_MATCH` when the substring is not found.
#[must_use]
pub fn exact_score(query: &[char], target: &[char], case_sensitive: bool) -> i32 {
    exact_score_with_positions(query, target, case_sensitive).0
}

/// `exact_score` plus contiguous match positions (empty on no match or an
/// empty query).
#[must_use]
pub fn exact_score_with_positions(query: &[char], target: &[char], case_sensitive: bool) -> (i32, Vec<usize>) {
    let query_len = query.len();

    if query_len == 0 {
        return (0, Vec::new());
    }

    if query_len > target.len() {
        return (NO_MATCH, Vec::new());
    }

    // Normalize path separators so `/` and `\` are interchangeable
    // (consistent with the fuzzy path).
    let normalized: Vec<char> = query
        .iter()
        .map(|&c| if c == '/' || c == '\\' { std::path::MAIN_SEPARATOR } else { c })
        .collect();

    let Some(index) = index_of(target, &normalized, case_sensitive) else {
        return (NO_MATCH, Vec::new());
    };

    let positions: Vec<usize> = (index..index + query_len).collect();
    (compute_score(&normalized, target, &positions), positions)
}

/// Exact-substring counterpart to `score_with_file_name_priority_positions`.
#[must_use]
pub fn exact_score_with_file_name_priority_positions(
    query: &[char],
    relative_path: &[char],
    file_name_start: usize,
    case_sensitive: bool,
) -> (i32, Vec<usize>) {
    let (best, use_file_name) = best_with_file_name_priority(relative_path, file_name_start, |target| {
        exact_score(query, target, case_sensitive)
    });

    if best == NO_MATCH {
        return (NO_MATCH, Vec::new());
    }

    let positions = if use_file_name {
        let (_, mut positions) =
            exact_score_with_positions(query, &relative_path[file_name_start..], case_sensitive);
        positions.iter_mut().for_each(|p| *p += file_name_start);
        positions
    } else {
        exact_score_with_positions(query, relative_path, case_sensitive).1
    };

    (best + PENALTY_DEPTH * count_separators(relative_path), positions)
}

/// Shared candidate selection of the `*WithFileNamePriority` methods: a
/// root file (`file_name_start == 0`) is all filename; otherwise the
/// filename score (plus bonus) must strictly beat the full-path score.
/// Returns `(best score before depth penalty, filename won)`.
fn best_with_file_name_priority(
    relative_path: &[char],
    file_name_start: usize,
    score_fn: impl Fn(&[char]) -> i32,
) -> (i32, bool) {
    let with_bonus = |s: i32| if s == NO_MATCH { NO_MATCH } else { s + FILE_NAME_BONUS };

    if file_name_start == 0 {
        // Root file: the full path IS the filename
        (with_bonus(score_fn(relative_path)), false)
    } else if file_name_start < relative_path.len() {
        let full_score = score_fn(relative_path);
        let file_name_score = with_bonus(score_fn(&relative_path[file_name_start..]));

        if file_name_score > full_score {
            (file_name_score, true)
        } else {
            (full_score, false)
        }
    } else {
        (score_fn(relative_path), false)
    }
}

/// `ReadOnlySpan<char>.IndexOf` with `Ordinal` / `OrdinalIgnoreCase`.
fn index_of(target: &[char], needle: &[char], case_sensitive: bool) -> Option<usize> {
    if needle.len() > target.len() {
        return None;
    }

    (0..=target.len() - needle.len()).find(|&start| {
        target[start..start + needle.len()]
            .iter()
            .zip(needle)
            .all(|(&a, &b)| if case_sensitive { a == b } else { a == b || to_upper(a) == to_upper(b) })
    })
}

fn count_separators(path: &[char]) -> i32 {
    path.iter().filter(|&&c| is_separator(c)).count() as i32
}

fn compute_score(query: &[char], target: &[char], match_positions: &[usize]) -> i32 {
    let mut score = 0;
    let mut prev_position: Option<usize> = None;
    let mut prev_bonus = 0;

    for (qi, &pos) in match_positions.iter().enumerate() {
        // Base match score
        score += SCORE_MATCH;

        // Case-sensitive bonus (treat '/' and '\' as equivalent)
        let qc = query[qi];
        let tc = target[pos];

        if qc == tc || (matches!(qc, '/' | '\\') && matches!(tc, '/' | '\\')) {
            score += BONUS_CASE_MATCH;
        }

        // Boundary bonus for this position
        let mut bonus = boundary_bonus(target, pos);

        // Consecutive match bonus: use the higher of the current bonus, the
        // previous bonus, or the minimum consecutive bonus
        if prev_position.is_some_and(|prev| pos == prev + 1) {
            bonus = bonus.max(prev_bonus.max(BONUS_CONSECUTIVE));
        }

        // First character multiplier
        if qi == 0 {
            bonus *= BONUS_FIRST_CHAR_MULTIPLIER;
        }

        score += bonus;

        // Gap penalty
        match prev_position {
            Some(prev) => {
                let gap = (pos - prev - 1) as i32;

                if gap > 0 {
                    score += PENALTY_GAP_START + PENALTY_GAP_EXTENSION * (gap - 1);
                }
            }
            None if pos > 0 => {
                // Gap before the first match
                score += PENALTY_GAP_START + PENALTY_GAP_EXTENSION * (pos as i32 - 1);
            }
            None => {}
        }

        prev_position = Some(pos);
        prev_bonus = bonus;
    }

    // Trailing gap penalty: favors tighter matches (e.g. "src\Foo" over
    // "src\Foo.Bar.Baz")
    if let Some(prev) = prev_position {
        let trailing_gap = (target.len() - prev - 1) as i32;
        score += PENALTY_TRAILING_GAP * trailing_gap;
    }

    score
}

fn boundary_bonus(target: &[char], position: usize) -> i32 {
    if position == 0 {
        // Start of string treated as delimiter boundary
        return BONUS_BOUNDARY_DELIMITER;
    }

    let prev = target[position - 1];
    let curr = target[position];

    if is_separator(prev) {
        return BONUS_BOUNDARY_DELIMITER;
    }

    if !is_letter_or_digit(prev) && is_letter_or_digit(curr) {
        return BONUS_BOUNDARY;
    }

    if is_lower(prev) && is_upper(curr) {
        return BONUS_CAMEL;
    }

    if !is_digit(prev) && is_digit(curr) {
        return BONUS_CAMEL;
    }

    if !is_letter_or_digit(curr) {
        return BONUS_NON_WORD;
    }

    0
}

#[cfg(test)]
mod tests {
    use super::*;

    const SEP: char = std::path::MAIN_SEPARATOR;

    fn c(s: &str) -> Vec<char> {
        s.chars().collect()
    }

    fn path(parts: &[&str]) -> Vec<char> {
        c(&parts.join(&SEP.to_string()))
    }

    fn file_name_start(path: &[char]) -> usize {
        path.iter().rposition(|&ch| ch == SEP).map_or(0, |i| i + 1)
    }

    fn s(query: &str, target: &str) -> i32 {
        score(&c(query), &c(target))
    }

    #[test]
    fn score_matches_subsequence() {
        for (query, target) in [
            ("abc", "abc"),                  // exact match
            ("abc", "aXbXc"),                // subsequence with gaps
            ("app", "App.cs"),               // case insensitive
            ("pdf", "report.pdf"),           // match after dot boundary
            ("cs", "App.cs"),                // extension match
            ("wade", "Wade"),                // case insensitive full match
            ("NP", "NullPointerException"),  // camelCase initials
        ] {
            assert!(s(query, target) > NO_MATCH, "{query:?} in {target:?}");
        }
    }

    #[test]
    fn score_no_match_returns_min_value() {
        for (query, target) in [("abc", "acb"), ("xyz", "abc"), ("abcd", "abc"), ("test", "")] {
            assert_eq!(s(query, target), NO_MATCH, "{query:?} in {target:?}");
        }
    }

    #[test]
    fn score_empty_query_returns_zero() {
        assert_eq!(s("", "anything"), 0);
    }

    #[test]
    fn score_boundary_match_scores_higher_than_mid_word() {
        assert!(s("p", ".pdf") > s("p", "upping"));
    }

    #[test]
    fn score_consecutive_match_scores_higher_than_spread() {
        assert!(s("wade", "Wade") > s("wade", "W_a_d_e"));
    }

    #[test]
    fn score_camel_case_match_scores_well() {
        assert!(s("NP", "NullPointer") > s("NP", "xnxpx"));
    }

    #[test]
    fn score_exact_case_match_scores_higher_than_case_insensitive() {
        assert!(s("App", "App.cs") > s("app", "App.cs"));
    }

    #[test]
    fn score_tight_match_scores_higher_than_loose() {
        assert!(s("abc", "abcxxx") > s("abc", "axxbxxcxxx"));
    }

    #[test]
    fn score_with_file_name_priority_file_name_match_gets_bonus_over_path_match() {
        let p = path(&["src", "Wade", "App.cs"]);
        let priority = score_with_file_name_priority(&c("App"), &p, file_name_start(&p));
        assert!(priority > score(&c("App"), &p));
    }

    #[test]
    fn score_with_file_name_priority_app_ranks_higher_for_app_cs() {
        let app_cs = path(&["src", "Wade", "App.cs"]);
        let config_cs = path(&["src", "Applications", "Config.cs"]);
        let app_score = score_with_file_name_priority(&c("App"), &app_cs, file_name_start(&app_cs));
        let config_score = score_with_file_name_priority(&c("App"), &config_cs, file_name_start(&config_cs));
        assert!(app_score > config_score, "{app_score} vs {config_score}");
    }

    #[test]
    fn score_with_file_name_priority_no_file_name_match_falls_back_to_full_path() {
        let p = path(&["src", "Wade", "App.cs"]);
        let result = score_with_file_name_priority(&c("Wade"), &p, file_name_start(&p));
        let depth = p.iter().filter(|&&ch| ch == SEP).count() as i32;
        assert_eq!(result, score(&c("Wade"), &p) + PENALTY_DEPTH * depth);
    }

    #[test]
    fn score_with_file_name_priority_pdf_finds_report_pdf() {
        let p = path(&["Documents", "report.pdf"]);
        let result = score_with_file_name_priority(&c("pdf"), &p, file_name_start(&p));
        assert!(result > NO_MATCH);
        assert!(result >= FILE_NAME_BONUS, "{result}");
    }

    #[test]
    fn score_path_separator_bonus() {
        let p = path(&["src", "Wade"]);
        assert!(score(&c("Wade"), &p) > s("Wade", "notwade"));
    }

    #[test]
    fn score_match_positions_exact_match() {
        let (result, positions) = score_with_positions(&c("abc"), &c("abc"));
        assert!(result > NO_MATCH);
        assert_eq!(positions, [0, 1, 2]);
    }

    #[test]
    fn score_match_positions_subsequence_with_gaps() {
        let (result, positions) = score_with_positions(&c("ac"), &c("abc"));
        assert!(result > NO_MATCH);
        assert_eq!(positions, [0, 2]);
    }

    #[test]
    fn score_match_positions_no_match_returns_empty() {
        let (result, positions) = score_with_positions(&c("xyz"), &c("abc"));
        assert_eq!(result, NO_MATCH);
        assert!(positions.is_empty());
    }

    #[test]
    fn score_match_positions_empty_query_returns_empty() {
        let (result, positions) = score_with_positions(&c(""), &c("abc"));
        assert_eq!(result, 0);
        assert!(positions.is_empty());
    }

    #[test]
    fn score_with_file_name_priority_match_positions_file_name_match_offsets_positions() {
        let p = path(&["src", "Wade", "App.cs"]);
        let start = file_name_start(&p);
        let (_, positions) = score_with_file_name_priority_positions(&c("App"), &p, start);
        assert_eq!(positions, [start, start + 1, start + 2]);
    }

    #[test]
    fn score_with_file_name_priority_match_positions_path_match_no_offset() {
        let p = path(&["src", "Wade", "App.cs"]);
        let (_, positions) = score_with_file_name_priority_positions(&c("Wade"), &p, file_name_start(&p));
        let wade_start = 4; // "src" + separator
        assert_eq!(positions, [wade_start, wade_start + 1, wade_start + 2, wade_start + 3]);
    }

    #[test]
    fn exact_score_match() {
        for (query, target, case_sensitive) in [
            ("foo", "foobar", false), // exact substring at start
            ("bar", "foobar", false), // exact substring at end
            ("oob", "foobar", false), // exact substring middle
            ("FOO", "foobar", false), // case-insensitive (smart-case off)
        ] {
            assert!(exact_score(&c(query), &c(target), case_sensitive) > NO_MATCH, "{query:?}");
        }
    }

    #[test]
    fn exact_score_no_match_returns_min_value() {
        for (query, target, case_sensitive) in [
            ("foB", "foobar", false),     // not contiguous
            ("xyz", "foobar", false),     // chars absent
            ("foobars", "foobar", false), // longer than target
            ("Foo", "foobar", true),      // case-sensitive miss
        ] {
            assert_eq!(exact_score(&c(query), &c(target), case_sensitive), NO_MATCH, "{query:?}");
        }
    }

    #[test]
    fn exact_score_empty_query_returns_zero() {
        assert_eq!(exact_score(&c(""), &c("anything"), false), 0);
    }

    #[test]
    fn exact_score_match_positions_are_contiguous() {
        let (_, positions) = exact_score_with_positions(&c("oob"), &c("foobar"), false);
        assert_eq!(positions, [1, 2, 3]);
    }

    #[test]
    fn exact_score_normalizes_path_separators() {
        let target = path(&["src", "Wade"]);
        assert!(exact_score(&c("src/Wade"), &target, false) > NO_MATCH);
    }

    #[test]
    fn exact_score_case_sensitive_distinguishes_case() {
        assert_eq!(exact_score(&c("App"), &c("app.cs"), true), NO_MATCH);
        assert!(exact_score(&c("App"), &c("App.cs"), true) > NO_MATCH);
    }

    #[test]
    fn exact_score_with_file_name_priority_file_name_match_gets_bonus() {
        let p = path(&["src", "Wade", "App.cs"]);
        let (priority, _) = exact_score_with_file_name_priority_positions(&c("App"), &p, file_name_start(&p), false);
        assert!(priority > exact_score(&c("App"), &p, false));
    }

    #[test]
    fn exact_score_with_file_name_priority_match_positions_offset_into_full_path() {
        let p = path(&["src", "Wade", "App.cs"]);
        let start = file_name_start(&p);
        let (_, positions) = exact_score_with_file_name_priority_positions(&c("App"), &p, start, false);
        assert_eq!(positions, [start, start + 1, start + 2]);
    }

    #[test]
    fn exact_score_non_contiguous_fails_where_fuzzy_succeeds() {
        assert!(s("abc", "aXbXc") > NO_MATCH);
        assert_eq!(exact_score(&c("abc"), &c("aXbXc"), false), NO_MATCH);
    }

    #[test]
    fn non_ascii_case_mapping() {
        // İ lowercases to i (the .NET simple mapping), so "i" matches it
        assert!(s("i", "\u{130}") > NO_MATCH);
        // ß has no single-char uppercase: OrdinalIgnoreCase keeps it distinct from "s"
        assert_eq!(exact_score(&c("s"), &c("\u{df}"), false), NO_MATCH);
        assert!(exact_score(&c("\u{e9}"), &c("\u{c9}"), false) > NO_MATCH);
    }

    /// Exact parity with C#: every case in tests/golden/search/scorer.cases.txt
    /// must reproduce the score and positions C# wrote to scorer.golden.txt.
    #[test]
    fn scorer_cases_match_csharp_golden() {
        let dir = std::path::Path::new(env!("CARGO_MANIFEST_DIR")).join("../../tests/golden/search");
        let cases = std::fs::read_to_string(dir.join("scorer.cases.txt")).unwrap();
        let golden = std::fs::read_to_string(dir.join("scorer.golden.txt")).unwrap().replace("\r\n", "\n");
        let expand = |s: &str| c(&s.replace("{/}", &SEP.to_string()));

        let mut actual = String::new();

        for line in cases.lines() {
            if line.is_empty() || line.starts_with('#') {
                continue;
            }

            let fields: Vec<&str> = line.split('\t').collect();
            let (query, target) = (expand(fields[1]), expand(fields[2]));
            let file_name_start = target.iter().rposition(|&ch| is_separator(ch)).map_or(0, |i| i + 1);

            let (result, positions) = match fields[0] {
                "score" => score_with_positions(&query, &target),
                "exact" => exact_score_with_positions(&query, &target, false),
                "exact-cs" => exact_score_with_positions(&query, &target, true),
                "fname" => score_with_file_name_priority_positions(&query, &target, file_name_start),
                "fname-exact" => exact_score_with_file_name_priority_positions(&query, &target, file_name_start, false),
                "fname-exact-cs" => exact_score_with_file_name_priority_positions(&query, &target, file_name_start, true),
                kind => panic!("unknown kind {kind}"),
            };

            // The score-only entry points must agree with the positions variants
            if fields[0] == "fname" {
                assert_eq!(score_with_file_name_priority(&query, &target, file_name_start), result, "{line}");
            }

            let score_text = if result == NO_MATCH { "none".to_string() } else { result.to_string() };
            let positions_text: Vec<String> = positions.iter().map(ToString::to_string).collect();
            actual.push_str(&format!("{line}\t=> {score_text} [{}]\n", positions_text.join(",")));
        }

        for (expected_line, actual_line) in golden.lines().zip(actual.lines()) {
            assert_eq!(actual_line, expected_line);
        }

        assert_eq!(actual.lines().count(), golden.lines().count());
    }
}
