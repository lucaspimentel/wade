//! Port of `MetadataRenderer`: metadata sections as styled lines (header,
//! divider, aligned label/value rows, list items).

use crate::highlight::StyledLine;
use crate::preview::MetadataSection;
use crate::screen::{CellStyle, Color};

const LABEL_COLOR: Color = Color { r: 120, g: 120, b: 140 };
const VALUE_COLOR: Color = Color { r: 200, g: 200, b: 200 };
const HEADER_COLOR: Color = Color { r: 180, g: 180, b: 200 };
const DIVIDER_COLOR: Color = Color { r: 80, g: 80, b: 100 };

fn fg(color: Color) -> CellStyle {
    CellStyle {
        fg: Some(color),
        ..CellStyle::default()
    }
}

fn styled(text: String, style: CellStyle) -> StyledLine {
    let len = text.chars().count();

    StyledLine {
        text,
        spans: None,
        char_styles: Some(vec![style; len]),
    }
}

/// Port of `MetadataRenderer.Render`. `max_width` only caps the header
/// divider (0 = default 20).
#[must_use]
pub fn render(sections: &[MetadataSection], max_width: i32) -> Vec<StyledLine> {
    let mut lines = Vec::new();

    // Label column width across all sections, plus padding
    let label_width = sections
        .iter()
        .flat_map(|section| &section.entries)
        .map(|entry| entry.label.chars().count())
        .max()
        .unwrap_or(0)
        + 2;

    for (index, section) in sections.iter().enumerate() {
        // Blank line between sections
        if index > 0 {
            lines.push(StyledLine::plain(""));
        }

        if let Some(header) = &section.header {
            lines.push(styled(
                format!("  {header}"),
                CellStyle {
                    bold: true,
                    ..fg(HEADER_COLOR)
                },
            ));

            // Divider only when entries follow
            if !section.entries.is_empty() {
                let cap = if max_width > 4 { (max_width - 4) as usize } else { 20 };
                let width = (header.chars().count() + 4).min(cap);
                lines.push(styled(format!("  {}", "\u{2500}".repeat(width)), fg(DIVIDER_COLOR)));
            }
        }

        for entry in &section.entries {
            if entry.label.is_empty() {
                // List item: indented value
                lines.push(styled(format!("    {}", entry.value), fg(VALUE_COLOR)));
                continue;
            }

            let label_part = format!("  {:<label_width$}", entry.label);
            let label_len = label_part.chars().count();
            let text = format!("{label_part}{}", entry.value);
            let label_style = CellStyle {
                dim: true,
                ..fg(LABEL_COLOR)
            };

            let styles = (0..text.chars().count())
                .map(|i| if i < label_len { label_style } else { fg(VALUE_COLOR) })
                .collect();

            lines.push(StyledLine {
                text,
                spans: None,
                char_styles: Some(styles),
            });
        }
    }

    lines
}
