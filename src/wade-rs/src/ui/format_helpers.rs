//! Port of src/Wade/UI/FormatHelpers.cs.

/// Mirrors `PercentBarResult(int Length, int FilledCount, int LabelStart, int LabelLength)`.
#[derive(Clone, Copy, PartialEq, Eq, Debug, Default)]
pub struct PercentBarResult {
    pub length: usize,
    pub filled_count: usize,
    pub label_start: usize,
    pub label_length: usize,
}

/// Wall-clock fields mirroring a C# `DateTime` value for `format_date`.
#[derive(Clone, Copy, PartialEq, Eq, Debug, Default)]
pub struct DateParts {
    pub year: i32,
    pub month: u32,
    pub day: u32,
    pub hour: u32,
    pub minute: u32,
    /// Sub-minute precision, kept so "sort by modified" breaks ties like
    /// C# (which compares the full `DateTime`).
    pub second: u32,
    pub nanosecond: u32,
}

/// Port of `FormatHelpers.FormatSize`: formats a byte count as "N B" or
/// "N.N KB/MB/GB/TB" with .NET "F1" semantics (one decimal, midpoint away
/// from zero, invariant culture).
#[must_use]
pub fn format_size(buf: &mut [char], bytes: i64) -> usize {
    let text = if bytes < 1024 {
        format!("{bytes} B")
    } else {
        let (divisor, suffix) = if bytes < 1024_i64 * 1024 {
            (1024.0_f64, "KB")
        } else if bytes < 1024_i64 * 1024 * 1024 {
            (1024.0_f64 * 1024.0, "MB")
        } else if bytes < 1024_i64 * 1024 * 1024 * 1024 {
            (1024.0_f64 * 1024.0 * 1024.0, "GB")
        } else {
            (1024.0_f64 * 1024.0 * 1024.0 * 1024.0, "TB")
        };

        // .NET "F1": round to one decimal, midpoint away from zero;
        // the decimal is always shown ("1.0 KB", never "1 KB")
        let tenths = ((bytes as f64 / divisor) * 10.0).round() as i64;
        let whole = tenths / 10;
        let frac = tenths % 10;
        let sign = if tenths < 0 { "-" } else { "" };
        format!("{sign}{whole}.{frac} {suffix}")
    };

    copy_fit(buf, &text)
}

/// Port of `FormatHelpers.FormatDate`: 19 -> "yyyy-MM-dd hh:mm tt",
/// 10 -> "yyyy-MM-dd", 6 -> "MMM dd" (invariant culture; 12-hour clock).
#[must_use]
pub fn format_date(buf: &mut [char], parts: DateParts, max_width: usize) -> usize {
    let text = if max_width >= 19 {
        let hh = if parts.hour.is_multiple_of(12) { 12 } else { parts.hour % 12 };
        let tt = if parts.hour < 12 { "AM" } else { "PM" };
        format!("{:04}-{:02}-{:02} {hh:02}:{:02} {tt}", parts.year, parts.month, parts.day, parts.minute)
    } else if max_width >= 10 {
        format!("{:04}-{:02}-{:02}", parts.year, parts.month, parts.day)
    } else if max_width >= 6 {
        format!("{} {:02}", month_abbrev(parts.month), parts.day)
    } else {
        return 0;
    };

    copy_fit(buf, &text)
}

/// Port of `FormatPercentBar`: fills `buf` with the bar and returns the
/// label bounds (`PercentBarResult`).
#[must_use]
pub fn format_percent_bar(buf: &mut [char], fraction: f64, bar_width: usize) -> PercentBarResult {
    if buf.len() < bar_width {
        return PercentBarResult::default();
    }

    let filled = ((fraction * f64::from(bar_width as u32)) + 0.5) as usize;
    let filled = filled.min(bar_width);

    for (_i, cell) in buf.iter_mut().enumerate().take(filled) {
        *cell = '\u{2588}'; // full block
    }
    for cell in buf.iter_mut().take(bar_width).skip(filled) {
        *cell = '\u{2591}'; // light shade
    }

    // Overlay percent text centered in the bar
    let percent = ((fraction * 100.0) + 0.5) as i64;
    let percent = percent.clamp(0, 100);
    let label: Vec<char> = format!("{percent}%").chars().collect();

    let (label_start, label_length) = if !label.is_empty() && label.len() <= bar_width {
        let start = (bar_width - label.len()) / 2;
        buf[start..start + label.len()].copy_from_slice(&label);
        (start, label.len())
    } else {
        (0, 0)
    };

    PercentBarResult {
        length: bar_width,
        filled_count: filled,
        label_start,
        label_length,
    }
}

fn copy_fit(buf: &mut [char], s: &str) -> usize {
    let chars: Vec<char> = s.chars().collect();
    if chars.len() > buf.len() {
        return 0;
    }
    buf[..chars.len()].copy_from_slice(&chars);
    chars.len()
}

#[must_use]
pub fn month_abbrev(month: u32) -> &'static str {
    match month {
        1 => "Jan",
        2 => "Feb",
        3 => "Mar",
        4 => "Apr",
        5 => "May",
        6 => "Jun",
        7 => "Jul",
        8 => "Aug",
        9 => "Sep",
        10 => "Oct",
        11 => "Nov",
        _ => "Dec",
    }
}

/// `FormatSize` into an owned string.
#[must_use]
pub fn format_size_string(bytes: i64) -> String {
    let mut buf = ['\0'; 32];
    let n = format_size(&mut buf, bytes);
    buf[..n].iter().collect()
}

/// .NET `{ratio:P0}` under the invariant culture: the exact binary value
/// times 100 rounded half-to-even (as .NET formats doubles), digits grouped
/// with commas, then " %".
#[must_use]
pub fn format_percent_p0(ratio: f64) -> String {
    // Two decimals of the ratio are the integer percent digits
    let fixed = format!("{:.2}", ratio.abs());
    let digits: String = fixed.chars().filter(char::is_ascii_digit).collect();
    let digits = digits.trim_start_matches('0');
    let digits = if digits.is_empty() { "0" } else { digits };

    let mut grouped = String::new();
    for (i, ch) in digits.chars().enumerate() {
        if i > 0 && (digits.len() - i).is_multiple_of(3) {
            grouped.push(',');
        }
        grouped.push(ch);
    }

    let sign = if ratio < 0.0 && digits != "0" { "-" } else { "" };
    format!("{sign}{grouped} %")
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn format_size_matches_csharp_output() {
        let mut buf = ['\0'; 16];
        assert_eq!(format_size(&mut buf, 0), 3);
        assert_eq!(&buf[..3], ['0', ' ', 'B']);
        assert_eq!(format_size(&mut buf, 512), 5);
        assert_eq!(&buf[..5], &['5', '1', '2', ' ', 'B']);
        assert_eq!(format_size(&mut buf, 1024), 6);
        assert_eq!(&buf[..6], &['1', '.', '0', ' ', 'K', 'B']);
        assert_eq!(format_size(&mut buf, 1536), 6);
        assert_eq!(&buf[..6], &['1', '.', '5', ' ', 'K', 'B']);
        assert_eq!(format_size(&mut buf, 1_048_576), 6);
        assert_eq!(&buf[..6], &['1', '.', '0', ' ', 'M', 'B']);
        assert_eq!(format_size(&mut buf, 1_572_864), 6);
        assert_eq!(&buf[..6], &['1', '.', '5', ' ', 'M', 'B']);
    }

    // Port of the rest of FormatHelpersTests.cs

    #[test]
    fn format_size_string_covers_bytes_to_gigabytes() {
        for (bytes, expected) in [
            (0, "0 B"),
            (512, "512 B"),
            (1023, "1023 B"),
            (1024, "1.0 KB"),
            (1536, "1.5 KB"),
            (1_048_576, "1.0 MB"),
            (1_572_864, "1.5 MB"),
            (1_073_741_824, "1.0 GB"),
            (1_610_612_736, "1.5 GB"),
        ] {
            assert_eq!(format_size_string(bytes), expected, "{bytes}");
        }
    }

    fn date_text(max_width: usize) -> String {
        let parts = DateParts {
            year: 2025,
            month: 3,
            day: 6,
            hour: 14,
            minute: 30,
            ..DateParts::default()
        };
        let mut buf = ['\0'; 32];
        let len = format_date(&mut buf, parts, max_width);
        buf[..len].iter().collect()
    }

    #[test]
    fn format_date_picks_the_form_that_fits() {
        assert_eq!(date_text(19), "2025-03-06 02:30 PM");
        assert_eq!(date_text(10), "2025-03-06");
        assert_eq!(date_text(6), "Mar 06");
        assert_eq!(date_text(5), "", "too narrow");
    }

    fn bar(fraction: f64, width: usize) -> (PercentBarResult, String) {
        let mut buf = ['\0'; 32];
        let result = format_percent_bar(&mut buf, fraction, width);
        let text = buf[..result.length].iter().collect();
        (result, text)
    }

    #[test]
    fn format_percent_bar_fills_and_centers_the_label() {
        let full = '\u{2588}';
        let empty = '\u{2591}';
        let cells = |n: usize, c: char| -> String { std::iter::repeat_n(c, n).collect() };
        for (fraction, width, filled, expected) in [
            (0.0, 10, 0, format!("{}0%{}", cells(4, empty), cells(4, empty))),
            (1.0, 10, 10, format!("{}100%{}", cells(3, full), cells(3, full))),
            (0.75, 20, 15, format!("{}75%{}{}", cells(8, full), cells(4, full), cells(5, empty))),
            (0.5, 10, 5, format!("{}50%{}", cells(3, full), cells(4, empty))),
            (0.3, 10, 3, format!("{}30%{}", cells(3, full), cells(4, empty))),
        ] {
            let (result, text) = bar(fraction, width);
            assert_eq!(
                (result.length, result.filled_count, text.as_str()),
                (width, filled, expected.as_str()),
                "{fraction}"
            );
        }

        let (result, _) = bar(0.5, 10);
        assert_eq!((result.label_start, result.label_length), (3, 3), "\"50%\" centered");
    }

    #[test]
    fn format_percent_bar_clamps_and_rejects_a_small_buffer() {
        let (over, over_text) = bar(1.5, 5);
        assert_eq!((over.length, over.filled_count), (5, 5));
        assert!(over_text.contains("100%"), "{over_text}");
        let (under, under_text) = bar(-0.5, 5);
        assert_eq!((under.length, under.filled_count), (5, 0));
        assert!(under_text.contains("0%") && !under_text.contains('-'), "{under_text}");

        let mut small = ['\0'; 3];
        assert_eq!(format_percent_bar(&mut small, 0.5, 5).length, 0);
    }
}
