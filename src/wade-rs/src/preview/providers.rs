//! Port of the text-family providers in `PreviewProviders.cs`: Text,
//! Hex dump, None and Git diff.

use super::{PreviewContext, PreviewProvider, PreviewResult};
use crate::fs::{file_preview, git_utils, hex_preview, GitFileStatus};
use crate::highlight::languages::diff::DiffLanguage;
use crate::highlight::{self, Language, StyledLine};
use crate::input::CancelToken;

fn label_or(path: &str, fallback: &str) -> Option<String> {
    Some(file_preview::get_file_type_label(path).unwrap_or(fallback).to_string())
}

/// Port of `TextPreviewProvider`: highlighted text for non-binary files.
pub struct TextPreviewProvider;

impl PreviewProvider for TextPreviewProvider {
    fn label(&self) -> &'static str {
        "Text"
    }

    fn can_preview(&self, path: &str, _context: &PreviewContext) -> bool {
        !file_preview::is_binary(path)
    }

    fn get_preview(&self, path: &str, _context: &PreviewContext, cancel: &CancelToken) -> Option<PreviewResult> {
        let (raw_lines, metadata) = file_preview::get_preview_lines(path);

        if cancel.is_cancelled() {
            return None;
        }

        if metadata.is_binary {
            return Some(PreviewResult {
                text_lines: Some(vec![StyledLine::plain("[binary file]")]),
                file_type_label: label_or(path, "Binary"),
                is_rendered: true,
                is_placeholder: true,
            });
        }

        let lines: Vec<&str> = raw_lines.iter().map(String::as_str).collect();

        Some(PreviewResult {
            text_lines: Some(highlight::highlight(&lines, path)),
            file_type_label: label_or(path, "Text"),
            is_rendered: false,
            is_placeholder: metadata.placeholder_message.is_some(),
        })
    }
}

/// Port of `HexPreviewProvider` (always applicable, opt-in via the menu).
pub struct HexPreviewProvider;

impl PreviewProvider for HexPreviewProvider {
    fn label(&self) -> &'static str {
        "Hex dump"
    }

    fn can_preview(&self, _path: &str, _context: &PreviewContext) -> bool {
        true
    }

    fn get_preview(&self, path: &str, _context: &PreviewContext, cancel: &CancelToken) -> Option<PreviewResult> {
        let lines = hex_preview::get_preview_lines(path, cancel)?;

        Some(PreviewResult {
            text_lines: Some(lines),
            file_type_label: label_or(path, "Binary"),
            is_rendered: true,
            is_placeholder: false,
        })
    }
}

/// Port of `NonePreviewProvider`: always applicable, shows nothing.
pub struct NonePreviewProvider;

impl PreviewProvider for NonePreviewProvider {
    fn label(&self) -> &'static str {
        "None"
    }

    fn can_preview(&self, _path: &str, _context: &PreviewContext) -> bool {
        true
    }

    fn get_preview(&self, _path: &str, _context: &PreviewContext, _cancel: &CancelToken) -> Option<PreviewResult> {
        Some(PreviewResult {
            text_lines: Some(Vec::new()),
            file_type_label: None,
            is_rendered: false,
            is_placeholder: true,
        })
    }
}

/// Port of `DiffPreviewProvider`: colored `git diff` for modified or staged
/// files (unstaged changes win when both apply).
pub struct DiffPreviewProvider;

impl PreviewProvider for DiffPreviewProvider {
    fn label(&self) -> &'static str {
        "Git diff"
    }

    fn can_preview(&self, _path: &str, context: &PreviewContext) -> bool {
        context.repo_root.is_some()
            && context
                .git_status
                .is_some_and(|status| status.intersects(GitFileStatus::MODIFIED | GitFileStatus::STAGED))
    }

    fn get_preview(&self, path: &str, context: &PreviewContext, cancel: &CancelToken) -> Option<PreviewResult> {
        let status = context.git_status?;
        let has_modified = status.intersects(GitFileStatus::MODIFIED);
        let staged = !has_modified && status.intersects(GitFileStatus::STAGED);

        let diff_lines = git_utils::get_diff(context.repo_root.as_deref()?, path, staged, cancel)?;
        if diff_lines.is_empty() {
            return None;
        }

        let language = DiffLanguage;
        let mut state = 0u8;
        let styled = diff_lines.iter().map(|line| language.tokenize_line(line, &mut state)).collect();

        Some(PreviewResult {
            text_lines: Some(styled),
            file_type_label: Some("Diff".to_string()),
            is_rendered: true,
            is_placeholder: false,
        })
    }
}

#[cfg(test)]
mod tests {
    //! Port of the DiffPreviewProvider and NonePreviewProvider cases in
    //! PreviewProviderTests.cs (text and hex results are covered by the
    //! preview golden).

    use super::{DiffPreviewProvider, NonePreviewProvider};
    use crate::fs::GitFileStatus;
    use crate::input::CancelToken;
    use crate::preview::{test_context, PreviewContext, PreviewProvider};

    fn context(git_status: Option<GitFileStatus>, repo_root: Option<&str>) -> PreviewContext {
        PreviewContext {
            git_status,
            repo_root: repo_root.map(str::to_string),
            ..test_context()
        }
    }

    #[test]
    fn diff_can_preview_modified_or_staged_with_repo_root() {
        let cases = [
            (Some(GitFileStatus::MODIFIED), Some("/repo"), true),
            (Some(GitFileStatus::STAGED), Some("/repo"), true),
            (Some(GitFileStatus::UNTRACKED), Some("/repo"), false),
            (Some(GitFileStatus::MODIFIED), None, false),
            (None, Some("/repo"), false),
            (Some(GitFileStatus::NONE), Some("/repo"), false),
        ];

        for (status, repo_root, expected) in cases {
            assert_eq!(
                DiffPreviewProvider.can_preview("file.cs", &context(status, repo_root)),
                expected,
                "{status:?} {repo_root:?}"
            );
        }
    }

    #[test]
    fn labels_match_csharp() {
        assert_eq!(DiffPreviewProvider.label(), "Git diff");
        assert_eq!(NonePreviewProvider.label(), "None");
    }

    #[test]
    fn none_returns_placeholder_for_any_file() {
        assert!(NonePreviewProvider.can_preview("anything.xyz", &test_context()));
        let result = NonePreviewProvider
            .get_preview("anything.xyz", &test_context(), &CancelToken::new())
            .expect("result");
        assert!(result.is_placeholder);
    }
}
