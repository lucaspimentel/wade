//! Markdown parity: renders tests/golden/markdown/corpus with the
//! pulldown-cmark port and compares with markdown.golden.txt, written by
//! PreviewGoldenTests.MarkdownCorpus_MatchGolden (Markdig).
//!
//! Sections listed in KNOWN_DIFFERENCES are parser differences recorded in
//! KNOWN_DEVIATIONS.md; every other section must match exactly.

use std::fmt::Write as _;
use std::path::{Path, PathBuf};

use wade::highlight::StyledLine;
use wade::input::CancelToken;
use wade::preview::markdown;
use wade::screen::{CellStyle, Color};

/// Parser differences between Markdig and pulldown-cmark (KNOWN_DEVIATIONS.md),
/// as (section, Markdig text, pulldown-cmark text): the section must match
/// once the Markdig text is replaced. Markdig drops the "[" of an undefined
/// full reference at the end of a bracket chain (`H[m][n]`).
const KNOWN_DIFFERENCES: &[(&str, &str, &str)] = &[
    (
        "search-research.md @40",
        "text 3. Fill score matrix Hm][n] — every \nchars 0+3:180,180,180/-/- 3+33:",
        "text 3. Fill score matrix H[m][n] — every \nchars 0+3:180,180,180/-/- 3+34:",
    ),
    (
        "search-research.md @100",
        "Hm][n] — every pattern character must match somewhere\nchars 0+3:180,180,180/-/- 3+71:",
        "H[m][n] — every pattern character must match somewhere\nchars 0+3:180,180,180/-/- 3+72:",
    ),
];

const EDGE_WIDTHS: &[i32] = &[3, 30, 78];
const DOCUMENT_WIDTHS: &[i32] = &[40, 100];

fn golden_dir() -> PathBuf {
    Path::new(env!("CARGO_MANIFEST_DIR")).join("../../tests/golden/markdown")
}

fn escape(text: &str) -> String {
    let mut out = String::new();
    for ch in text.chars() {
        if ch == '\\' {
            out.push_str("\\\\");
        } else if ch < ' ' || ch == '\u{7f}' {
            let _ = write!(out, "\\u{:04x}", ch as u32);
        } else {
            out.push(ch);
        }
    }
    out
}

fn format_color(color: Option<Color>) -> String {
    color.map_or_else(|| "-".to_string(), |c| format!("{},{},{}", c.r, c.g, c.b))
}

fn format_style(style: &CellStyle) -> String {
    let mut flags: String = [
        (style.bold, 'b'),
        (style.dim, 'd'),
        (style.inverse, 'i'),
        (style.underline, 'u'),
        (style.strikethrough, 's'),
    ]
    .iter()
    .filter(|(on, _)| *on)
    .map(|(_, flag)| *flag)
    .collect();
    if flags.is_empty() {
        flags.push('-');
    }
    format!("{}/{}/{flags}", format_color(style.fg), format_color(style.bg))
}

/// The highlight-golden line format, positions in UTF-16 units.
fn append_styled_line(out: &mut String, line: &StyledLine) {
    let _ = writeln!(out, "text {}", escape(&line.text));

    if let Some(styles) = &line.char_styles {
        let styles: Vec<CellStyle> = line
            .text
            .chars()
            .zip(styles)
            .flat_map(|(ch, style)| std::iter::repeat_n(*style, ch.len_utf16()))
            .collect();
        out.push_str("chars");
        let mut i = 0;
        while i < styles.len() {
            let mut j = i + 1;
            while j < styles.len() && styles[j] == styles[i] {
                j += 1;
            }
            let _ = write!(out, " {i}+{}:{}", j - i, format_style(&styles[i]));
            i = j;
        }
        out.push('\n');
    }
}

/// Golden text split into "=== name @width" sections.
fn sections(text: &str) -> Vec<(String, String)> {
    let mut result: Vec<(String, String)> = Vec::new();
    for line in text.lines() {
        if let Some(name) = line.strip_prefix("=== ") {
            result.push((name.to_string(), String::new()));
        } else if let Some((_, body)) = result.last_mut() {
            body.push_str(line);
            body.push('\n');
        }
    }
    result
}

#[test]
fn markdown_corpus_matches_markdig_golden() {
    let dir = golden_dir();
    let mut files: Vec<PathBuf> =
        std::fs::read_dir(dir.join("corpus")).unwrap().map(|entry| entry.unwrap().path()).collect();
    files.sort_by(|a, b| a.file_name().cmp(&b.file_name()));

    let mut actual = String::new();
    for file in &files {
        let name = file.file_name().unwrap().to_string_lossy().into_owned();
        let widths = if name.starts_with("edge-") { EDGE_WIDTHS } else { DOCUMENT_WIDTHS };
        for &width in widths {
            let _ = writeln!(actual, "=== {name} @{width}");
            let lines = markdown::render(&file.to_string_lossy(), width, &CancelToken::new()).expect("rendered");
            for line in &lines {
                append_styled_line(&mut actual, line);
            }
        }
    }

    let expected = std::fs::read_to_string(dir.join("markdown.golden.txt")).unwrap().replace("\r\n", "\n");
    let expected_sections = sections(&expected);
    let actual_sections = sections(&actual);
    assert_eq!(
        expected_sections.iter().map(|(name, _)| name).collect::<Vec<_>>(),
        actual_sections.iter().map(|(name, _)| name).collect::<Vec<_>>()
    );

    let mut failures = Vec::new();
    for ((name, expected_body), (_, actual_body)) in expected_sections.iter().zip(&actual_sections) {
        let mut expected_body = expected_body.clone();

        for (_, markdig, pulldown) in KNOWN_DIFFERENCES.iter().filter(|(section, ..)| section == name) {
            if expected_body == *actual_body {
                failures.push(format!("{name}: listed as a known difference but now matches"));
            }
            assert_eq!(expected_body.matches(markdig).count(), 1, "{name}: known difference must match once");
            expected_body = expected_body.replace(markdig, pulldown);
        }

        if expected_body != *actual_body {
            let line = expected_body
                .lines()
                .zip(actual_body.lines())
                .position(|(e, a)| e != a)
                .unwrap_or_else(|| expected_body.lines().count().min(actual_body.lines().count()));
            failures.push(format!(
                "{name}: first difference at section line {}\n  expected: {}\n  actual:   {}",
                line + 1,
                expected_body.lines().nth(line).unwrap_or("<end>"),
                actual_body.lines().nth(line).unwrap_or("<end>")
            ));
        }
    }

    assert!(failures.is_empty(), "{} section(s) differ:\n{}", failures.len(), failures.join("\n"));
}
