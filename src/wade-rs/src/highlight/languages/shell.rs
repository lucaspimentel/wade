//! Port of `ShellLanguage`.

use crate::highlight::c_like::CLikeLanguage;
use crate::highlight::c_like::base_try_match_string;
use crate::highlight::{StyledSpan, TokenKind};

const KEYWORDS: &[&str] = &[
    "if", "then", "else", "elif", "fi", "for", "while", "do", "done", "case", "esac", "in", "function", "return",
    "exit", "export", "local", "readonly", "declare", "typeset", "unset", "shift", "source", "break", "continue",
    "trap", "exec",
];

const CONSTANTS: &[&str] = &["true", "false"];

const BUILTINS: &[&str] = &[
    "echo", "printf", "read", "test", "cd", "pwd", "ls", "cp", "mv", "rm", "mkdir", "chmod", "chown", "grep", "sed",
    "awk", "find", "cat", "head", "tail", "sort", "uniq", "wc", "cut", "tr",
];

pub struct ShellLanguage;

impl CLikeLanguage for ShellLanguage {
    fn name(&self) -> &'static str {
        "ShellLanguage"
    }

    fn keywords(&self) -> &'static [&'static str] {
        KEYWORDS
    }

    fn constants(&self) -> &'static [&'static str] {
        CONSTANTS
    }

    fn builtins(&self) -> &'static [&'static str] {
        BUILTINS
    }

    fn line_comment_prefix(&self) -> Option<&'static str> {
        Some("#")
    }

    fn block_comment(&self) -> Option<(&'static str, &'static str)> {
        None
    }

    fn try_match_string(
        &self,
        line: &[char],
        pos: usize,
        spans: &mut Vec<StyledSpan>,
        _state: &mut u8,
    ) -> Option<usize> {
        // Single-quoted strings: no escape processing
        if line[pos] == '\'' {
            let mut p = pos + 1;

            while p < line.len() && line[p] != '\'' {
                p += 1;
            }

            if p < line.len() {
                p += 1;
            }

            spans.push(StyledSpan::new(pos, p - pos, TokenKind::String));
            return Some(p);
        }

        base_try_match_string(line, pos, spans)
    }
}
