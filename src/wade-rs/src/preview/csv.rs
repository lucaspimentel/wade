//! Rust only: the CSV/TSV table preview and its file details. Records are
//! parsed per RFC 4180 (quoted fields, `""` escapes, separators and line
//! breaks inside quotes), laid out as aligned columns with a header row,
//! `│` separators, right-aligned numbers and record numbers.

use super::{
    MetadataEntry, MetadataProvider, MetadataResult, MetadataSection, PreviewContext, PreviewLimits, PreviewProvider,
    PreviewResult, Truncation, group_thousands, truncation_marker,
};
use crate::fs::file_preview;
use crate::highlight::StyledLine;
use crate::input::CancelToken;
use crate::rune_width::rune_width;
use crate::screen::{CellStyle, Color};

/// The widest a column gets; longer cells end with `…`.
pub const MAX_COLUMN_WIDTH: usize = 30;
/// Records read to guess the separator of a .csv file.
const SNIFF_RECORDS: usize = 20;
/// Stands in for a line break inside a quoted field.
const LINE_BREAK_MARK: char = '\u{21b5}';
const COLUMN_SEPARATOR: &str = " \u{2502} ";
const RULE_CROSSING: &str = "\u{2500}\u{253c}\u{2500}";

const fn style(r: u8, g: u8, b: u8, bold: bool) -> CellStyle {
    CellStyle {
        fg: Some(Color { r, g, b }),
        bg: None,
        bold,
        dim: false,
        inverse: false,
        underline: false,
        strikethrough: false,
    }
}

const CELL_STYLE: CellStyle = crate::highlight::theme::PLAIN;
const HEADER_STYLE: CellStyle = style(86, 156, 214, true);
/// The pane's line-number color, for the gutter, `│` and the rule.
const DIM_STYLE: CellStyle = style(100, 100, 100, false);

/// True for the extensions the table preview handles.
#[must_use]
pub fn is_table_file(path: &str) -> bool {
    [".csv", ".tsv", ".tab"].iter().any(|ext| ends_with_ignore_case(path, ext))
}

fn ends_with_ignore_case(path: &str, suffix: &str) -> bool {
    path.len() >= suffix.len()
        && path.is_char_boundary(path.len() - suffix.len())
        && path[path.len() - suffix.len()..].eq_ignore_ascii_case(suffix)
}

/// Tab for .tsv and .tab, comma for .csv.
#[must_use]
pub fn default_separator(path: &str) -> char {
    if ends_with_ignore_case(path, ".csv") { ',' } else { '\t' }
}

/// The separator for `path`: tab for .tsv and .tab; for .csv, `;` or `|`
/// when the first records are more consistently split by it than by a
/// comma, else a comma.
#[must_use]
pub fn separator_for(path: &str, text: &str) -> char {
    let default = default_separator(path);
    if default != ',' {
        return default;
    }

    let score = |separator: char| {
        let (records, _) = parse_records(text, separator, SNIFF_RECORDS);
        let mut counts: Vec<usize> = records.iter().map(Vec::len).filter(|&count| count > 1).collect();
        counts.sort_unstable();
        // How many records share the most common field count (> 1)
        counts.chunk_by(|a, b| a == b).map(<[usize]>::len).max().unwrap_or(0)
    };

    let comma = score(',');
    [';', '|']
        .into_iter()
        .map(|separator| (separator, score(separator)))
        .filter(|&(_, candidate)| candidate > comma)
        .max_by_key(|&(_, candidate)| candidate)
        .map_or(',', |(separator, _)| separator)
}

/// RFC 4180 records: fields split on `separator`, quoted fields may hold
/// separators, `""` and line breaks (shown as `↵`). Blank lines are
/// skipped. Stops after `max_records`; the flag says whether more remain.
#[must_use]
pub fn parse_records(text: &str, separator: char, max_records: usize) -> (Vec<Vec<String>>, bool) {
    let text = text.strip_prefix('\u{feff}').unwrap_or(text);
    let mut records = Vec::new();
    let mut record: Vec<String> = Vec::new();
    let mut field = String::new();
    let mut in_quotes = false;
    let mut chars = text.chars().peekable();

    while let Some(ch) = chars.next() {
        if in_quotes {
            match ch {
                '"' if chars.peek() == Some(&'"') => {
                    chars.next();
                    field.push('"');
                }
                '"' => in_quotes = false,
                '\r' | '\n' => {
                    if ch == '\r' && chars.peek() == Some(&'\n') {
                        chars.next();
                    }

                    field.push(LINE_BREAK_MARK);
                }
                _ => field.push(ch),
            }

            continue;
        }

        match ch {
            '"' if field.is_empty() => in_quotes = true,
            '\r' | '\n' => {
                if ch == '\r' && chars.peek() == Some(&'\n') {
                    chars.next();
                }

                record.push(std::mem::take(&mut field));
                let finished = std::mem::take(&mut record);

                if !(finished.len() == 1 && finished[0].is_empty()) {
                    records.push(finished);

                    if records.len() == max_records {
                        let more = chars.any(|rest| rest != '\r' && rest != '\n');
                        return (records, more);
                    }
                }
            }
            _ if ch == separator => record.push(std::mem::take(&mut field)),
            _ => field.push(ch),
        }
    }

    if !field.is_empty() || !record.is_empty() {
        record.push(field);
        records.push(record);
    }

    (records, false)
}

/// A number as spreadsheets write them: an optional sign, digits with
/// optional `,` thousands groups, a fraction, an exponent and a `%`.
#[must_use]
pub fn is_number(cell: &str) -> bool {
    let all_digits = |part: &str| !part.is_empty() && part.bytes().all(|b| b.is_ascii_digit());

    let text = cell.trim();
    let text = text.strip_suffix('%').unwrap_or(text);
    let text = text.strip_prefix(['+', '-']).unwrap_or(text);

    let (mantissa, exponent) = match text.find(['e', 'E']) {
        Some(index) => (&text[..index], Some(&text[index + 1..])),
        None => (text, None),
    };

    if let Some(exponent) = exponent
        && !all_digits(exponent.strip_prefix(['+', '-']).unwrap_or(exponent))
    {
        return false;
    }

    let (integer, fraction) = match mantissa.split_once('.') {
        Some((integer, fraction)) => (integer, Some(fraction)),
        None => (mantissa, None),
    };

    if fraction.is_some_and(|fraction| !all_digits(fraction)) {
        return false;
    }

    if integer.is_empty() {
        return fraction.is_some();
    }

    let mut groups = integer.split(',');
    let first = groups.next().unwrap_or_default();
    let rest: Vec<&str> = groups.collect();

    if rest.is_empty() {
        all_digits(first)
    } else {
        all_digits(first) && first.len() <= 3 && rest.iter().all(|group| group.len() == 3 && all_digits(group))
    }
}

fn display_width(text: &str) -> usize {
    text.chars().map(rune_width).sum()
}

/// `text` cut to `width` cells (ending with `…` when cut), padded with
/// spaces on the right, or on the left when `right_align`.
fn fit(text: &str, width: usize, right_align: bool) -> String {
    let mut fitted = String::new();
    let mut used = 0;

    if display_width(text) > width {
        for ch in text.chars() {
            let w = rune_width(ch);
            if used + w > width.saturating_sub(1) {
                break;
            }

            fitted.push(ch);
            used += w;
        }

        fitted.push('\u{2026}');
        used += 1;
    } else {
        fitted.push_str(text);
        used = display_width(text);
    }

    let padding = " ".repeat(width.saturating_sub(used));
    if right_align { padding + &fitted } else { fitted + &padding }
}

/// Builds a line from styled parts, with one style per character.
fn styled_line(parts: &[(String, CellStyle)]) -> StyledLine {
    let mut text = String::new();
    let mut styles = Vec::new();

    for (part, style) in parts {
        text.push_str(part);
        styles.extend(std::iter::repeat_n(*style, part.chars().count()));
    }

    StyledLine {
        text,
        spans: None,
        char_styles: Some(styles),
    }
}

/// The table: the first record as a bold header and a `─` rule, then the
/// rest, each column as wide as its widest cell up to
/// `MAX_COLUMN_WIDTH`, `│` between columns, numbers right-aligned and a
/// record-number gutter (the header gets a blank one).
#[must_use]
pub fn layout(records: &[Vec<String>]) -> Vec<StyledLine> {
    let columns = records.iter().map(Vec::len).max().unwrap_or(0);
    if columns == 0 {
        return Vec::new();
    }

    let widths: Vec<usize> = (0..columns)
        .map(|column| {
            records
                .iter()
                .filter_map(|record| record.get(column))
                .map(|cell| display_width(cell))
                .max()
                .unwrap_or(0)
                .clamp(1, MAX_COLUMN_WIDTH)
        })
        .collect();

    let data_rows = records.len().saturating_sub(1);
    let gutter_width = data_rows.to_string().len().max(4);
    let blank_gutter = " ".repeat(gutter_width + 1);

    let row_line = |gutter: String, record: &[String], is_header: bool| {
        let mut parts = vec![(gutter, DIM_STYLE)];

        for (column, &width) in widths.iter().enumerate() {
            if column > 0 {
                parts.push((COLUMN_SEPARATOR.to_string(), DIM_STYLE));
            }

            let cell = record.get(column).map_or("", String::as_str);
            let right_align = !is_header && is_number(cell);
            parts.push((fit(cell, width, right_align), if is_header { HEADER_STYLE } else { CELL_STYLE }));
        }

        styled_line(&parts)
    };

    let mut lines = Vec::with_capacity(records.len() + 1);
    lines.push(row_line(blank_gutter.clone(), &records[0], true));

    let rule = widths.iter().map(|&width| "\u{2500}".repeat(width)).collect::<Vec<_>>().join(RULE_CROSSING);
    lines.push(styled_line(&[(blank_gutter, DIM_STYLE), (rule, DIM_STYLE)]));

    for (index, record) in records[1..].iter().enumerate() {
        let gutter = format!("{:>gutter_width$} ", index + 1);
        lines.push(row_line(gutter, record, false));
    }

    lines
}

fn file_type_label(path: &str) -> Option<String> {
    crate::fs::file_type_labels::get_file_type_label(path).map(str::to_string)
}

/// The "Table" preview for .csv, .tsv and .tab files.
pub struct CsvTablePreviewProvider;

impl PreviewProvider for CsvTablePreviewProvider {
    fn label(&self) -> &'static str {
        "Table"
    }

    fn can_preview(&self, path: &str, context: &PreviewContext) -> bool {
        context.csv_preview_enabled && is_table_file(path) && !file_preview::is_binary(path)
    }

    fn get_preview(&self, path: &str, context: &PreviewContext, cancel: &CancelToken) -> Option<PreviewResult> {
        let limits: PreviewLimits = context.limits;
        let (text, more_bytes) = file_preview::read_text(path, limits.bytes)?;

        if cancel.is_cancelled() {
            return None;
        }

        let separator = separator_for(path, &text);
        // The header plus `limits.lines` data rows
        let (records, more_records) = parse_records(&text, separator, limits.lines.saturating_add(1));

        if records.is_empty() {
            return Some(PreviewResult {
                text_lines: Some(vec![StyledLine::plain("[empty file]")]),
                file_type_label: file_type_label(path),
                is_rendered: true,
                is_placeholder: true,
                ..PreviewResult::default()
            });
        }

        let mut lines = layout(&records);
        let truncation = if more_records {
            Some(Truncation::Lines)
        } else if more_bytes {
            Some(Truncation::Bytes)
        } else {
            None
        };

        if let Some(truncation) = truncation.filter(|_| limits.mark_truncation) {
            lines.push(truncation_marker(limits, truncation));
        }

        Some(PreviewResult {
            text_lines: Some(lines),
            file_type_label: file_type_label(path),
            is_rendered: true,
            ..PreviewResult::default()
        })
    }
}

/// "Table" file details: data rows (header excluded; `N+` when the byte
/// limit stopped the count), columns, and the separator when sniffing
/// picked one other than the extension's.
pub struct CsvMetadataProvider;

impl MetadataProvider for CsvMetadataProvider {
    fn label(&self) -> &'static str {
        "Table info"
    }

    fn can_provide_metadata(&self, path: &str, context: &PreviewContext) -> bool {
        context.csv_preview_enabled && is_table_file(path) && !file_preview::is_binary(path)
    }

    fn get_metadata(&self, path: &str, context: &PreviewContext, cancel: &CancelToken) -> Option<MetadataResult> {
        let (text, more_bytes) = file_preview::read_text(path, context.limits.bytes)?;

        if cancel.is_cancelled() {
            return None;
        }

        let separator = separator_for(path, &text);
        let (records, _) = parse_records(&text, separator, usize::MAX);
        let rows = records.len().saturating_sub(1) as u64;
        let columns = records.iter().map(Vec::len).max().unwrap_or(0) as u64;

        let mut entries = vec![
            MetadataEntry::new("Rows", &format!("{}{}", group_thousands(rows), if more_bytes { "+" } else { "" })),
            MetadataEntry::new("Columns", &group_thousands(columns)),
        ];

        if separator != default_separator(path) {
            entries.push(MetadataEntry::new("Separator", &separator.to_string()));
        }

        Some(MetadataResult {
            sections: vec![MetadataSection {
                header: Some("Table".to_string()),
                entries,
            }],
            file_type_label: None,
        })
    }
}

#[cfg(test)]
mod tests {
    use super::{
        CsvMetadataProvider, CsvTablePreviewProvider, DIM_STYLE, HEADER_STYLE, MAX_COLUMN_WIDTH, is_number, layout,
        parse_records, separator_for,
    };
    use crate::input::CancelToken;
    use crate::preview::{MetadataProvider, PreviewContext, PreviewLimits, PreviewProvider, test_context, test_path};

    fn records(text: &str, separator: char) -> Vec<Vec<String>> {
        parse_records(text, separator, usize::MAX).0
    }

    fn row(cells: &[&str]) -> Vec<String> {
        cells.iter().map(|cell| (*cell).to_string()).collect()
    }

    #[test]
    fn parses_quotes_escapes_and_line_breaks_inside_quotes() {
        let text = "name,note\r\n\"Smith, J\",\"said \"\"hi\"\"\"\r\n\"two\nlines\",x\r\n";
        assert_eq!(
            records(text, ','),
            [row(&["name", "note"]), row(&["Smith, J", "said \"hi\""]), row(&["two\u{21b5}lines", "x"]),]
        );
    }

    #[test]
    fn parses_edge_cases() {
        assert!(records("", ',').is_empty());
        assert_eq!(records("\u{feff}a,b\n", ','), [row(&["a", "b"])], "BOM skipped");
        assert_eq!(records("a,b\n\n\nc,d", ','), [row(&["a", "b"]), row(&["c", "d"])], "blank lines skipped");
        assert_eq!(records("a,b,c\nd\n", ','), [row(&["a", "b", "c"]), row(&["d"])], "uneven rows kept as is");
        assert_eq!(records("a,\"open\n", ','), [row(&["a", "open\u{21b5}"])], "unterminated quote keeps its text");
        assert_eq!(records("a,,c\r", ','), [row(&["a", "", "c"])], "CR alone ends a record");
        assert_eq!(records("x\"y,z\n", ','), [row(&["x\"y", "z"])], "a quote mid-field is literal");
    }

    #[test]
    fn stops_after_max_records_and_says_whether_more_remain() {
        assert_eq!(parse_records("a\nb\nc\n", ',', 2), (vec![row(&["a"]), row(&["b"])], true));
        assert_eq!(parse_records("a\nb\n\n", ',', 2), (vec![row(&["a"]), row(&["b"])], false));
    }

    #[test]
    fn sniffs_semicolon_and_pipe_but_defaults_to_comma() {
        assert_eq!(separator_for("x.csv", "a;b;c\n1;2;3\n4;5;6\n"), ';');
        assert_eq!(separator_for("x.csv", "a|b\n1|2\n"), '|');
        assert_eq!(separator_for("x.csv", "a,b\n1,2\n"), ',');
        assert_eq!(separator_for("x.csv", "a,b\n\"1;2\",3\n\"4;5\",6\n"), ',', "semicolons inside quotes");
        assert_eq!(separator_for("x.csv", "single\ncolumn\n"), ',');
        assert_eq!(separator_for("x.CSV", "a;b\n"), ';');
        assert_eq!(separator_for("x.tsv", "a;b;c\n1;2;3\n"), '\t', "TSV is always tab");
        assert_eq!(separator_for("x.tab", "a,b\n"), '\t');
    }

    #[test]
    fn recognizes_spreadsheet_numbers() {
        for text in ["0", "42", "-3", "+7", "1,234", "1,234,567.5", "3.14", ".5", "-3e4", "1E-2", "12%", " 8 "] {
            assert!(is_number(text), "{text}");
        }

        for text in ["", "-", "abc", "1,23", "12,3456", "1.2.3", "e5", "1e", "1.", "1,234a", "$5", "1 000"] {
            assert!(!is_number(text), "{text}");
        }
    }

    fn texts(records: &[Vec<String>]) -> Vec<String> {
        layout(records).into_iter().map(|line| line.text).collect()
    }

    #[test]
    fn lays_out_aligned_columns_with_header_rule_and_record_numbers() {
        let table = [row(&["name", "qty"]), row(&["apple", "3"]), row(&["kiwi", "12"])];
        assert_eq!(
            texts(&table),
            [
                "     name  \u{2502} qty",
                "     \u{2500}\u{2500}\u{2500}\u{2500}\u{2500}\u{2500}\u{253c}\u{2500}\u{2500}\u{2500}\u{2500}",
                "   1 apple \u{2502}   3",
                "   2 kiwi  \u{2502}  12",
            ]
        );
    }

    #[test]
    fn header_and_text_stay_left_aligned_and_short_rows_are_padded() {
        let table = [row(&["1", "b"]), row(&["x", "2"]), row(&["10"])];
        assert_eq!(
            texts(&table),
            [
                "     1  \u{2502} b",
                "     \u{2500}\u{2500}\u{2500}\u{253c}\u{2500}\u{2500}",
                "   1 x  \u{2502} 2",
                "   2 10 \u{2502}  ",
            ]
        );
    }

    #[test]
    fn long_cells_are_cut_with_an_ellipsis_and_wide_characters_count_twice() {
        let long = "x".repeat(MAX_COLUMN_WIDTH + 10);
        let table = [row(&["h"]), row(&[&long]), row(&["\u{65e5}\u{672c}"])];
        let lines = texts(&table);
        assert_eq!(lines[2], format!("   1 {}\u{2026}", "x".repeat(MAX_COLUMN_WIDTH - 1)));
        assert_eq!(lines[3], format!("   2 \u{65e5}\u{672c}{}", " ".repeat(MAX_COLUMN_WIDTH - 4)));

        // A wide character never straddles the cut
        let wide = "\u{65e5}".repeat(MAX_COLUMN_WIDTH);
        let lines = texts(&[row(&["h"]), row(&[&wide])]);
        assert_eq!(lines[2], format!("   1 {}\u{2026} ", "\u{65e5}".repeat(MAX_COLUMN_WIDTH / 2 - 1)));
    }

    #[test]
    fn styles_the_header_bold_and_the_gutter_and_separators_dim() {
        let lines = layout(&[row(&["ab", "cd"]), row(&["1", "2"])]);
        let header = lines[0].char_styles.as_ref().unwrap();
        let text: Vec<char> = lines[0].text.chars().collect();
        assert_eq!(header.len(), text.len());

        for (ch, style) in text.iter().zip(header) {
            match ch {
                'a' | 'b' | 'c' | 'd' => assert_eq!(*style, HEADER_STYLE, "{ch}"),
                '\u{2502}' => assert_eq!(*style, DIM_STYLE),
                _ => {}
            }
        }

        let data = lines[2].char_styles.as_ref().unwrap();
        assert_eq!(data[3], DIM_STYLE, "the record number is dim");
        assert!(lines[1].char_styles.as_ref().unwrap().iter().all(|style| *style == DIM_STYLE));
    }

    fn csv_file(name: &str, content: &str) -> String {
        let path = test_path(name);
        std::fs::write(&path, content).unwrap();
        path.to_string_lossy().into_owned()
    }

    fn context(lines: usize, mark_truncation: bool) -> PreviewContext {
        PreviewContext {
            limits: PreviewLimits {
                lines,
                bytes: PreviewLimits::DEFAULT_MAX_BYTES,
                mark_truncation,
            },
            ..test_context()
        }
    }

    #[test]
    fn table_comes_before_text_and_follows_the_setting() {
        let path = csv_file("people.csv", "a,b\n1,2\n");
        let labels = |context: &PreviewContext| -> Vec<&str> {
            crate::preview::registry::applicable_preview_providers(&path, context)
                .iter()
                .map(|p| p.label())
                .collect()
        };

        assert_eq!(&labels(&test_context())[..2], ["Table", "Text"]);
        let off = PreviewContext {
            csv_preview_enabled: false,
            ..test_context()
        };
        assert_eq!(labels(&off)[0], "Text");

        let binary = csv_file("blob.csv", "a,b\0\n");
        assert!(!CsvTablePreviewProvider.can_preview(&binary, &test_context()));
        assert!(!CsvTablePreviewProvider.can_preview("notes.txt", &test_context()));
    }

    #[test]
    fn preview_reads_the_header_plus_the_line_limit_and_marks_a_cut_in_full_screen() {
        let content: String = std::iter::once("id\n".to_string()).chain((1..=10).map(|i| format!("{i}\n"))).collect();
        let path = csv_file("ten.csv", &content);
        let preview = |lines, mark| {
            CsvTablePreviewProvider.get_preview(&path, &context(lines, mark), &CancelToken::new()).unwrap()
        };

        let result = preview(4, true);
        assert!(result.is_rendered, "the gutter is in the text, not the pane's line numbers");
        let lines = result.text_lines.unwrap();
        assert_eq!(lines.len(), 2 + 4 + 1);
        assert_eq!(lines.last().unwrap().text, "\u{2026} preview limited to 4 lines");

        assert_eq!(preview(4, false).text_lines.unwrap().len(), 2 + 4, "no marker in the right pane");
        assert_eq!(preview(10, true).text_lines.unwrap().len(), 2 + 10, "everything fits, no marker");

        let empty = csv_file("empty.csv", "");
        let result = CsvTablePreviewProvider.get_preview(&empty, &test_context(), &CancelToken::new()).unwrap();
        assert!(result.is_placeholder);
    }

    #[test]
    fn details_count_rows_and_columns_and_name_a_sniffed_separator() {
        let entries = |path: &str| -> Vec<(String, String)> {
            let result = CsvMetadataProvider.get_metadata(path, &test_context(), &CancelToken::new()).unwrap();
            result.sections[0].entries.iter().map(|e| (e.label.clone(), e.value.clone())).collect()
        };
        let pair = |label: &str, value: &str| (label.to_string(), value.to_string());

        let comma = csv_file("comma.csv", "a,b,c\n1,2,3\n4,5\n");
        assert_eq!(entries(&comma), [pair("Rows", "2"), pair("Columns", "3")]);

        let semicolon = csv_file("semi.csv", "a;b\n1;2\n");
        assert_eq!(entries(&semicolon), [pair("Rows", "1"), pair("Columns", "2"), pair("Separator", ";")]);

        let tsv = csv_file("tabs.tsv", "a\tb\n1\t2\n");
        assert_eq!(entries(&tsv), [pair("Rows", "1"), pair("Columns", "2")], "tab is the .tsv default");

        let big: String = std::iter::once("n\n".to_string()).chain((0..20_000).map(|i| format!("{i}\n"))).collect();
        let capped = csv_file("big.csv", &big);
        let small_bytes = PreviewContext {
            limits: PreviewLimits { bytes: 1000, ..PreviewLimits::CSHARP },
            ..test_context()
        };
        let result = CsvMetadataProvider.get_metadata(&capped, &small_bytes, &CancelToken::new()).unwrap();
        assert!(result.sections[0].entries[0].value.ends_with('+'), "{:?}", result.sections[0].entries);
    }
}
