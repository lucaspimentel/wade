//! Port of `PowerShellLanguage`. Its keyword and constant sets
//! include `-eq`/`$true`-style words the identifier scanner never produces
//! (kept for parity).

use crate::highlight::StyledSpan;
use crate::highlight::c_like::CLikeLanguage;
use crate::highlight::c_like::match_bracket_attribute;

const KEYWORDS: &[&str] = &[
    "begin", "break", "catch", "class", "continue", "data", "define", "do", "dynamicparam", "else", "elseif", "end",
    "enum", "exit", "filter", "finally", "for", "foreach", "from", "function", "if", "in", "inlinescript", "parallel",
    "param", "process", "return", "sequence", "switch", "throw", "trap", "try", "until", "using", "var", "while",
    "workflow", "-eq", "-ne", "-lt", "-le", "-gt", "-ge", "-and", "-or", "-not", "-xor", "-match", "-notmatch",
    "-like", "-notlike", "-contains", "-notcontains", "-in", "-notin",
];

const CONSTANTS: &[&str] = &["$true", "$false", "$null"];

const BUILTINS: &[&str] = &[];

pub struct PowerShellLanguage;

impl CLikeLanguage for PowerShellLanguage {
    fn name(&self) -> &'static str {
        "PowerShellLanguage"
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
        Some(("<#", "#>"))
    }

    fn try_match_extension(&self, line: &[char], pos: usize, spans: &mut Vec<StyledSpan>) -> usize {
        // Attributes: [Parameter()], [ValidateNotNull()], ...
        match_bracket_attribute(line, pos, spans)
    }
}
