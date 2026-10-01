//! Port of `XmlHtmlLanguage`: tags, attributes, entity references and
//! `<!-- -->` comments (multi-line via state 1).

use crate::highlight::scan::{index_of, starts_with_at};
use crate::highlight::{Language, StyledLine, StyledSpan, TokenKind};
use crate::text::{is_letter_or_digit, is_whitespace};

const STATE_NORMAL: u8 = 0;
const STATE_COMMENT: u8 = 1;

pub struct XmlHtmlLanguage;

impl Language for XmlHtmlLanguage {
    fn name(&self) -> &'static str {
        "XmlHtmlLanguage"
    }

    fn tokenize_line(&self, text: &str, state: &mut u8) -> StyledLine {
        if text.is_empty() {
            return StyledLine::plain(text);
        }

        let line: Vec<char> = text.chars().collect();
        let len = line.len();
        let mut spans = Vec::new();
        let mut pos = 0;

        if *state == STATE_COMMENT {
            let Some(close_index) = index_of(&line, "-->", 0) else {
                spans.push(StyledSpan::new(0, len, TokenKind::Comment));
                return StyledLine::with_spans(text, spans);
            };

            let close_end = close_index + 3;
            spans.push(StyledSpan::new(0, close_end, TokenKind::Comment));
            *state = STATE_NORMAL;
            pos = close_end;
        }

        while pos < len {
            // Comment: <!-- ... -->
            if starts_with_at(&line, pos, "<!--") {
                if let Some(close_index) = index_of(&line, "-->", pos + 4) {
                    spans.push(StyledSpan::new(pos, close_index + 3 - pos, TokenKind::Comment));
                    pos = close_index + 3;
                    continue;
                }

                spans.push(StyledSpan::new(pos, len - pos, TokenKind::Comment));
                *state = STATE_COMMENT;
                return StyledLine::with_spans(text, spans);
            }

            // Tag: <name ...>, </name> or <name />
            if line[pos] == '<' {
                pos += 1;

                if pos < len && line[pos] == '/' {
                    pos += 1;
                }

                let name_start = pos;
                while pos < len && (is_letter_or_digit(line[pos]) || matches!(line[pos], '-' | ':' | '_')) {
                    pos += 1;
                }

                if pos > name_start {
                    spans.push(StyledSpan::new(name_start, pos - name_start, TokenKind::TagName));
                }

                // Attributes up to '>'
                while pos < len && line[pos] != '>' {
                    if is_whitespace(line[pos]) {
                        pos += 1;
                        continue;
                    }

                    if line[pos] == '/' {
                        spans.push(StyledSpan::new(pos, 1, TokenKind::Punctuation));
                        pos += 1;
                        continue;
                    }

                    let attr_start = pos;
                    while pos < len && line[pos] != '=' && line[pos] != '>' && !is_whitespace(line[pos]) {
                        pos += 1;
                    }

                    if pos > attr_start {
                        spans.push(StyledSpan::new(attr_start, pos - attr_start, TokenKind::AttrName));
                    }

                    if pos < len && line[pos] == '=' {
                        spans.push(StyledSpan::new(pos, 1, TokenKind::Operator));
                        pos += 1;

                        // Quoted attribute value (unquoted values are left plain)
                        if pos < len && (line[pos] == '"' || line[pos] == '\'') {
                            let quote = line[pos];
                            let value_start = pos;
                            pos += 1;

                            while pos < len && line[pos] != quote {
                                pos += 1;
                            }

                            if pos < len {
                                pos += 1;
                            }

                            spans.push(StyledSpan::new(value_start, pos - value_start, TokenKind::AttrValue));
                        }
                    }
                }

                if pos < len && line[pos] == '>' {
                    spans.push(StyledSpan::new(pos, 1, TokenKind::Punctuation));
                    pos += 1;
                }

                continue;
            }

            // Entity reference: &amp; &lt; ...
            if line[pos] == '&' {
                let entity_start = pos;
                pos += 1;

                while pos < len && line[pos] != ';' && !is_whitespace(line[pos]) {
                    pos += 1;
                }

                if pos < len && line[pos] == ';' {
                    pos += 1;
                }

                spans.push(StyledSpan::new(entity_start, pos - entity_start, TokenKind::Constant));
                continue;
            }

            pos += 1;
        }

        StyledLine::with_spans(text, spans)
    }
}
