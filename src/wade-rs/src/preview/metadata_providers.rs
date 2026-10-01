//! Metadata providers ported so far: `FileMetadataProvider`.

use super::{MetadataEntry, MetadataProvider, MetadataResult, MetadataSection, PreviewContext};
use crate::fs::GitFileStatus;
use crate::input::CancelToken;
use crate::ui::properties_overlay::format_git_status;

/// Port of `FileMetadataProvider`: the file name as the section header,
/// plus cloud and git status when they apply. No file content is read.
pub struct FileMetadataProvider;

impl MetadataProvider for FileMetadataProvider {
    fn label(&self) -> &'static str {
        "File info"
    }

    fn can_provide_metadata(&self, _path: &str, _context: &PreviewContext) -> bool {
        true
    }

    fn get_metadata(&self, path: &str, context: &PreviewContext, cancel: &CancelToken) -> Option<MetadataResult> {
        if cancel.is_cancelled() || std::fs::metadata(path).is_err() {
            return None;
        }

        let mut entries = Vec::new();

        if context.is_cloud_placeholder {
            entries.push(MetadataEntry::new("Cloud", "not downloaded"));
        }

        if let Some(status) = context.git_status.filter(|&status| status != GitFileStatus::NONE) {
            entries.push(MetadataEntry::new("Git", &format_git_status(Some(status))));
        }

        let trimmed = path.trim_end_matches(crate::search::is_separator);
        let name = trimmed.rsplit(crate::search::is_separator).next().unwrap_or(trimmed);

        Some(MetadataResult {
            sections: vec![MetadataSection {
                header: Some(name.to_string()),
                entries,
            }],
            file_type_label: None,
        })
    }
}

#[cfg(test)]
mod tests {
    //! Port of FileMetadataProviderTests.cs.

    use super::FileMetadataProvider;
    use crate::fs::GitFileStatus;
    use crate::input::CancelToken;
    use crate::preview::{registry, test_context, test_path, MetadataProvider, MetadataResult, PreviewContext};

    fn flatten(result: &MetadataResult) -> String {
        result
            .sections
            .iter()
            .flat_map(|section| section.entries.iter().map(|entry| format!("{}: {}", entry.label, entry.value)))
            .collect::<Vec<_>>()
            .join("\n")
    }

    fn text_file() -> String {
        let path = test_path("file.txt");
        std::fs::write(&path, "hello").unwrap();
        path.to_string_lossy().into_owned()
    }

    #[test]
    fn can_provide_metadata_any_path() {
        for path in ["some/file.txt", "C:\\some\\path.exe", "C:\\some\\directory"] {
            assert!(FileMetadataProvider.can_provide_metadata(path, &test_context()), "{path}");
        }
    }

    #[test]
    fn directory_returns_name_without_size_or_modified() {
        let dir = test_path("some-dir");
        std::fs::create_dir_all(&dir).unwrap();

        let result = FileMetadataProvider
            .get_metadata(&dir.to_string_lossy(), &test_context(), &CancelToken::new())
            .expect("metadata");

        assert_eq!(result.sections.len(), 1);
        assert_eq!(result.sections[0].header.as_deref(), Some("some-dir"));
        assert!(result.sections[0].entries.iter().all(|e| e.label != "Size" && e.label != "Modified"));
    }

    #[test]
    fn returns_file_name_as_section_header_without_size_or_modified() {
        let path = text_file();
        let result = FileMetadataProvider.get_metadata(&path, &test_context(), &CancelToken::new()).expect("metadata");

        assert_eq!(result.sections[0].header.as_deref(), Some("file.txt"));
        assert!(result.sections[0].entries.iter().all(|e| e.label != "Size" && e.label != "Modified"));
    }

    #[test]
    fn git_status_adds_git_entry() {
        let path = text_file();
        let context = PreviewContext {
            git_status: Some(GitFileStatus::MODIFIED),
            ..test_context()
        };
        let result = FileMetadataProvider.get_metadata(&path, &context, &CancelToken::new()).expect("metadata");

        assert!(flatten(&result).contains("Git: Modified"));
    }

    #[test]
    fn cloud_placeholder_adds_cloud_entry() {
        let path = text_file();
        let context = PreviewContext {
            is_cloud_placeholder: true,
            ..test_context()
        };
        let result = FileMetadataProvider.get_metadata(&path, &context, &CancelToken::new()).expect("metadata");

        assert!(flatten(&result).contains("Cloud"));
    }

    #[test]
    fn no_git_status_omits_git_entry_and_label_is_none() {
        let path = text_file();
        let result = FileMetadataProvider.get_metadata(&path, &test_context(), &CancelToken::new()).expect("metadata");

        assert!(!flatten(&result).contains("Git"));
        assert_eq!(result.file_type_label, None);
    }

    #[test]
    fn cancelled_token_returns_none() {
        let path = text_file();
        let cancel = CancelToken::new();
        cancel.cancel();

        assert!(FileMetadataProvider.get_metadata(&path, &test_context(), &cancel).is_none());
    }

    #[test]
    fn registry_puts_file_metadata_provider_first() {
        let path = text_file();
        let providers = registry::applicable_metadata_providers(&path, &test_context());

        assert_eq!(providers.first().map(|p| p.label()), Some("File info"));
    }
}
