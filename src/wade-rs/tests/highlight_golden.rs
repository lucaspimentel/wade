//! Syntax-highlighting parity: runs the shared cases in
//! tests/golden/highlight/*.cases and compares with the goldens written by
//! tests/Wade.Tests/HighlightGoldenTests.cs.

use std::fmt::Write as _;
use std::path::{Path, PathBuf};

use wade::highlight::{self, Language, StyledLine};
use wade::screen::{CellStyle, Color};

/// Case files and C# language classes not ported yet (emptied as the
/// standalone languages land).
const PENDING: &[&str] = &[
    "css", "diff", "dockerfile", "json", "markdown", "toml", "xmlhtml", "yaml",
];
const PENDING_LANGUAGES: &[&str] = &[
    "CssLanguage",
    "DockerfileLanguage",
    "GitIgnoreLanguage",
    "JsonLanguage",
    "MarkdownLanguage",
    "TomlLanguage",
    "XmlHtmlLanguage",
    "YamlLanguage",
];

fn golden_dir() -> PathBuf {
    Path::new(env!("CARGO_MANIFEST_DIR")).join("../../tests/golden/highlight")
}

fn read(path: &Path) -> String {
    std::fs::read_to_string(path)
        .unwrap_or_else(|e| panic!("read {}: {e}", path.display()))
        .replace("\r\n", "\n")
}

/// Port of HighlightGoldenTests.Highlight: LanguageMap for real file names,
/// DiffLanguage directly for "diff".
fn highlight_case(lines: &[&str], target: &str) -> Vec<StyledLine> {
    if target != "diff" {
        return highlight::highlight(lines, target);
    }

    let diff = wade::highlight::languages::diff::DiffLanguage;
    let mut state = 0u8;
    lines.iter().map(|line| diff.tokenize_line(line, &mut state)).collect()
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
    let mut flags = String::new();

    for (on, flag) in [
        (style.bold, 'b'),
        (style.dim, 'd'),
        (style.inverse, 'i'),
        (style.underline, 'u'),
        (style.strikethrough, 's'),
    ] {
        if on {
            flags.push(flag);
        }
    }

    if flags.is_empty() {
        flags.push('-');
    }

    format!("{}/{}/{flags}", format_color(style.fg), format_color(style.bg))
}

fn append_line(out: &mut String, line: &StyledLine) {
    let _ = writeln!(out, "text {}", escape(&line.text));

    if let Some(spans) = &line.spans {
        out.push_str("spans");

        for span in spans {
            let _ = write!(out, " {}+{}:{:?}", span.start, span.len, span.kind);
        }

        out.push('\n');
    }

    if let Some(styles) = &line.char_styles {
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

fn run_cases(path: &Path) -> String {
    let text = read(path);
    let mut target = String::new();
    let mut cases: Vec<Vec<&str>> = Vec::new();

    for line in text.split('\n') {
        if cases.is_empty() && line.starts_with("file: ") {
            target = line["file: ".len()..].to_string();
        } else if line == "%%%%" {
            cases.push(Vec::new());
        } else if let Some(case) = cases.last_mut() {
            case.push(line);
        }
    }

    // File.ReadAllLines drops the empty string after the final newline
    if let Some(last) = cases.last_mut()
        && last.last() == Some(&"")
    {
        last.pop();
    }

    let mut out = String::new();

    for (index, case) in cases.iter().enumerate() {
        let _ = writeln!(out, "%%%% case {index}");

        for line in highlight_case(case, &target) {
            append_line(&mut out, &line);
        }
    }

    out
}

fn assert_matches_golden(golden_path: &Path, actual: &str) {
    let expected = read(golden_path);

    for (number, (expected_line, actual_line)) in expected.lines().zip(actual.lines()).enumerate() {
        assert_eq!(
            actual_line,
            expected_line,
            "{} line {}",
            golden_path.display(),
            number + 1
        );
    }

    assert_eq!(actual.lines().count(), expected.lines().count(), "{}", golden_path.display());
}

#[test]
fn highlight_cases_match_csharp_golden() {
    let mut checked = 0;

    for entry in std::fs::read_dir(golden_dir()).expect("golden dir") {
        let path = entry.unwrap().path();
        let stem = path.file_stem().unwrap().to_string_lossy().into_owned();

        if path.extension().is_none_or(|ext| ext != "cases") || stem == "language-map" {
            continue;
        }

        if PENDING.contains(&stem.as_str()) {
            continue;
        }

        let actual = run_cases(&path);
        assert_matches_golden(&path.with_extension("golden.txt"), &actual);
        checked += 1;
    }

    assert!(checked > 0, "no highlight cases found");
}

#[test]
fn language_map_matches_csharp_golden() {
    let dir = golden_dir();
    let golden = read(&dir.join("language-map.golden.txt"));

    for expected in golden.lines() {
        let (path, expected_name) = expected.rsplit_once(" => ").expect("path => name");

        if PENDING_LANGUAGES.contains(&expected_name) {
            continue;
        }

        let actual_name = highlight::get_language(path).map_or("none", |language| language.name());
        assert_eq!(actual_name, expected_name, "{path}");
    }
}
