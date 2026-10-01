//! Port of `DockerfileLanguage`: the leading instruction keyword (case
//! insensitive), strings, `$VAR`/`${VAR}` references, `--flags`, the `AS`
//! keyword and comments.

use crate::highlight::scan::{index_of_char, substring};
use crate::highlight::{Language, StyledLine, StyledSpan, TokenKind};

const INSTRUCTIONS: &[&str] = &[
    "FROM", "RUN", "CMD", "LABEL", "MAINTAINER", "EXPOSE", "ENV", "ADD", "COPY", "ENTRYPOINT", "VOLUME", "USER",
    "WORKDIR", "ARG", "ONBUILD", "STOPSIGNAL", "HEALTHCHECK", "SHELL", "CROSS_BUILD",
];

pub struct DockerfileLanguage;

impl Language for DockerfileLanguage {
    fn name(&self) -> &'static str {
        "DockerfileLanguage"
    }

    fn tokenize_line(&self, text: &str, _state: &mut u8) -> StyledLine {
        if text.is_empty() {
            return StyledLine::plain(text);
        }

        let line: Vec<char> = text.chars().collect();
        let len = line.len();
        let mut pos = line.iter().take_while(|&&c| c == ' ').count();

        if pos >= len {
            return StyledLine::plain(text);
        }

        // Comment line
        if line[pos] == '#' {
            return StyledLine::with_spans(text, vec![StyledSpan::new(pos, len - pos, TokenKind::Comment)]);
        }

        let mut spans = Vec::new();

        // Instruction keyword at the start of the line
        let mut word_end = pos;
        while word_end < len && is_instruction_char(line[word_end]) {
            word_end += 1;
        }

        if word_end > pos {
            let word = substring(&line, pos, word_end);

            if INSTRUCTIONS.iter().any(|instruction| instruction.eq_ignore_ascii_case(&word)) {
                spans.push(StyledSpan::new(pos, word_end - pos, TokenKind::Keyword));
            }
        }

        // The rest: strings, variables, flags, AS
        pos = word_end;

        while pos < len {
            let ch = line[pos];

            // Quoted strings
            if ch == '"' || ch == '\'' {
                let start = pos;
                pos += 1;

                while pos < len {
                    if line[pos] == '\\' && pos + 1 < len {
                        pos += 2;
                    } else if line[pos] == ch {
                        pos += 1;
                        break;
                    } else {
                        pos += 1;
                    }
                }

                spans.push(StyledSpan::new(start, pos - start, TokenKind::String));
                continue;
            }

            // Variable references: $VAR or ${VAR}
            if ch == '$' && pos + 1 < len {
                let start = pos;
                pos += 1;

                if line[pos] == '{' {
                    // Unclosed brace: to the end of the line
                    pos = index_of_char(&line, '}', pos + 1).map_or(len, |close| close + 1);
                } else {
                    while pos < len && is_variable_char(line[pos]) {
                        pos += 1;
                    }
                }

                if pos > start + 1 {
                    spans.push(StyledSpan::new(start, pos - start, TokenKind::Constant));
                }

                continue;
            }

            // Comment after an instruction
            if ch == '#' {
                spans.push(StyledSpan::new(pos, len - pos, TokenKind::Comment));
                break;
            }

            // Flags like --from=builder (before the word check: '-' is a word char)
            if ch == '-' && pos + 1 < len && line[pos + 1] == '-' {
                let start = pos;
                pos += 2;

                while pos < len && (is_word_char(line[pos]) || line[pos] == '-') {
                    pos += 1;
                }

                spans.push(StyledSpan::new(start, pos - start, TokenKind::Attribute));
                continue;
            }

            // Words: only AS is highlighted
            if is_word_char(ch) {
                let start = pos;

                while pos < len && is_word_char(line[pos]) {
                    pos += 1;
                }

                if substring(&line, start, pos).eq_ignore_ascii_case("AS") {
                    spans.push(StyledSpan::new(start, pos - start, TokenKind::Keyword));
                }

                continue;
            }

            pos += 1;
        }

        StyledLine::with_spans(text, spans)
    }
}

const fn is_instruction_char(ch: char) -> bool {
    ch.is_ascii_alphabetic() || ch == '_'
}

const fn is_variable_char(ch: char) -> bool {
    ch.is_ascii_alphanumeric() || ch == '_'
}

const fn is_word_char(ch: char) -> bool {
    ch.is_ascii_alphanumeric() || matches!(ch, '_' | '.' | '-')
}
