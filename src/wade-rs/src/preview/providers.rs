//! Port of the providers in `PreviewProviders.cs` ported so far: image,
//! PDF, rendered Markdown, archive contents (zip, tar/gzip), Text, Hex dump, None and
//! Git diff.

use super::{PreviewContext, PreviewProvider, PreviewResult};
use crate::fs::{file_preview, git_utils, hex_preview, tar_preview, zip_preview, GitFileStatus};
use crate::highlight::languages::diff::DiffLanguage;
use crate::highlight::{self, Language, StyledLine};
use crate::input::CancelToken;

fn label_or(path: &str, fallback: &str) -> Option<String> {
    Some(file_preview::get_file_type_label(path).unwrap_or(fallback).to_string())
}

/// Port of `ImagePreviewProvider`: Sixel previews of image files.
pub struct ImagePreviewProvider;

impl PreviewProvider for ImagePreviewProvider {
    fn label(&self) -> &'static str {
        "Image"
    }

    fn can_preview(&self, path: &str, context: &PreviewContext) -> bool {
        context.image_previews_enabled && crate::imaging::image_preview::is_image_file(path)
    }

    fn get_preview(&self, path: &str, context: &PreviewContext, cancel: &CancelToken) -> Option<PreviewResult> {
        let result = crate::imaging::image_preview::load(
            path,
            context.pane_width_cells,
            context.pane_height_cells,
            context.cell_pixel_width,
            context.cell_pixel_height,
            cancel,
        )?;

        Some(PreviewResult {
            sixel_data: Some(result.sixel_data),
            sixel_pixel_width: result.pixel_width,
            sixel_pixel_height: result.pixel_height,
            file_type_label: Some(result.label),
            ..PreviewResult::default()
        })
    }
}

/// Port of `PdfPreviewProvider`: page 1 rendered by `pdftopng`, shown as a
/// Sixel image.
pub struct PdfPreviewProvider;

impl PreviewProvider for PdfPreviewProvider {
    fn label(&self) -> &'static str {
        "PDF"
    }

    fn can_preview(&self, path: &str, context: &PreviewContext) -> bool {
        context.pdf_preview_enabled && context.sixel_supported && crate::imaging::pdf::can_convert(path)
    }

    fn get_preview(&self, path: &str, context: &PreviewContext, cancel: &CancelToken) -> Option<PreviewResult> {
        let page = crate::imaging::pdf::convert_to_image(path, cancel)?;
        let result = crate::imaging::image_preview::load(
            &page.path.to_string_lossy(),
            context.pane_width_cells,
            context.pane_height_cells,
            context.cell_pixel_width,
            context.cell_pixel_height,
            cancel,
        )?;

        let doc_ext = file_preview::extension(path).trim_start_matches('.').to_uppercase();

        Some(PreviewResult {
            sixel_data: Some(result.sixel_data),
            sixel_pixel_width: result.pixel_width,
            sixel_pixel_height: result.pixel_height,
            file_type_label: Some(format!("{doc_ext} Document (page 1)")),
            ..PreviewResult::default()
        })
    }
}

/// Port of `MarkdigMarkdownPreviewProvider`: rendered .md/.markdown files.
pub struct MarkdigMarkdownPreviewProvider;

impl PreviewProvider for MarkdigMarkdownPreviewProvider {
    fn label(&self) -> &'static str {
        "Rendered markdown (built-in)"
    }

    fn can_preview(&self, path: &str, context: &PreviewContext) -> bool {
        let ext = file_preview::extension(path);
        context.markdown_preview_enabled && (ext.eq_ignore_ascii_case(".md") || ext.eq_ignore_ascii_case(".markdown"))
    }

    fn get_preview(&self, path: &str, context: &PreviewContext, cancel: &CancelToken) -> Option<PreviewResult> {
        let lines = super::markdown::render(path, context.pane_width_cells - 2, cancel)?;

        Some(PreviewResult {
            text_lines: Some(lines),
            file_type_label: label_or(path, "Markdown"),
            is_rendered: true,
            is_placeholder: false,
            ..PreviewResult::default()
        })
    }
}

/// The "Archive Contents" title and rule above an archive listing.
fn archive_result(path: &str, body: Vec<StyledLine>) -> PreviewResult {
    let mut lines = Vec::with_capacity(body.len() + 2);
    lines.push(StyledLine::plain("  Archive Contents"));
    lines.push(StyledLine::plain(&format!("  {}", "\u{2500}".repeat(16))));
    lines.extend(body);

    PreviewResult {
        text_lines: Some(lines),
        file_type_label: label_or(path, "Archive"),
        is_rendered: true,
        is_placeholder: false,
        ..PreviewResult::default()
    }
}

/// Port of `ZipContentsPreviewProvider`: the central-directory listing of
/// zip-based archives.
pub struct ZipContentsPreviewProvider;

impl PreviewProvider for ZipContentsPreviewProvider {
    fn label(&self) -> &'static str {
        "Archive contents"
    }

    fn can_preview(&self, path: &str, context: &PreviewContext) -> bool {
        context.zip_preview_enabled && zip_preview::is_zip_file(path)
    }

    fn get_preview(&self, path: &str, _context: &PreviewContext, cancel: &CancelToken) -> Option<PreviewResult> {
        let lines = zip_preview::get_preview_lines(path, cancel)?;
        Some(archive_result(path, lines.iter().map(|line| StyledLine::plain(line)).collect()))
    }
}

/// Port of `TarContentsPreviewProvider`: tar and tar.gz listings, and the
/// head of plain .gz files.
pub struct TarContentsPreviewProvider;

impl PreviewProvider for TarContentsPreviewProvider {
    fn label(&self) -> &'static str {
        "Archive contents"
    }

    fn can_preview(&self, path: &str, context: &PreviewContext) -> bool {
        context.zip_preview_enabled && (tar_preview::is_tar_archive(path) || tar_preview::is_plain_gzip(path))
    }

    fn get_preview(&self, path: &str, _context: &PreviewContext, cancel: &CancelToken) -> Option<PreviewResult> {
        let body = if tar_preview::is_plain_gzip(path) {
            tar_preview::get_gzip_styled_preview(path, cancel)?
        } else {
            let lines = tar_preview::get_preview_lines(path, cancel)?;
            lines.iter().map(|line| StyledLine::plain(line)).collect()
        };

        Some(archive_result(path, body))
    }
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
                ..PreviewResult::default()
            });
        }

        let lines: Vec<&str> = raw_lines.iter().map(String::as_str).collect();

        Some(PreviewResult {
            text_lines: Some(highlight::highlight(&lines, path)),
            file_type_label: label_or(path, "Text"),
            is_rendered: false,
            is_placeholder: metadata.placeholder_message.is_some(),
            ..PreviewResult::default()
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
            ..PreviewResult::default()
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
            ..PreviewResult::default()
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
            ..PreviewResult::default()
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
    fn markdown_applies_to_md_and_markdown_when_enabled() {
        use super::MarkdigMarkdownPreviewProvider;

        for path in ["readme.md", "README.MD", "notes.markdown"] {
            assert!(MarkdigMarkdownPreviewProvider.can_preview(path, &test_context()), "{path}");
        }
        for path in ["notes.txt", "md", "file.mdx"] {
            assert!(!MarkdigMarkdownPreviewProvider.can_preview(path, &test_context()), "{path}");
        }

        let disabled = PreviewContext {
            markdown_preview_enabled: false,
            ..test_context()
        };
        assert!(!MarkdigMarkdownPreviewProvider.can_preview("readme.md", &disabled));
        assert_eq!(MarkdigMarkdownPreviewProvider.label(), "Rendered markdown (built-in)");
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
