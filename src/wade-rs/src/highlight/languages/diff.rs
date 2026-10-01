//! Placeholder until step 2.
use crate::highlight::{Language, StyledLine};
pub struct DiffLanguage;
impl Language for DiffLanguage {
    fn tokenize_line(&self, line: &str, _state: &mut u8) -> StyledLine { StyledLine::plain(line) }
    fn name(&self) -> &'static str { "DiffLanguage" }
}
