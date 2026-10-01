//! Ports of `PreviewProviderRegistry` and `MetadataProviderRegistry`: the
//! providers that apply to a file, in priority order (the first preview
//! provider is the default; every metadata provider contributes).

use super::metadata_providers::{ArchiveMetadataProvider, FileMetadataProvider, ShortcutMetadataProvider};
use super::providers::{
    DiffPreviewProvider, HexPreviewProvider, ImagePreviewProvider, MarkdigMarkdownPreviewProvider, NonePreviewProvider,
    TarContentsPreviewProvider, TextPreviewProvider, ZipContentsPreviewProvider,
};
use super::{MetadataProvider, PreviewContext, PreviewProvider};

/// C# order: Image, PDF, Markdown, Zip, MSI, Tar, Text, Diff, None, Hex.
/// Slots for providers of later phases are absent.
static PREVIEW_PROVIDERS: [&dyn PreviewProvider; 8] = [
    &ImagePreviewProvider,
    &MarkdigMarkdownPreviewProvider,
    &ZipContentsPreviewProvider,
    &TarContentsPreviewProvider,
    &TextPreviewProvider,
    &DiffPreviewProvider,
    &NonePreviewProvider,
    &HexPreviewProvider,
];

/// C# order: File, Image, Executable, Office, Media, NuGet, MSI, Shortcut,
/// Archive, PDF. Slots for providers of later phases are absent.
static METADATA_PROVIDERS: [&dyn MetadataProvider; 3] =
    [&FileMetadataProvider, &ShortcutMetadataProvider, &ArchiveMetadataProvider];

/// Port of `PreviewProviderRegistry.GetApplicableProviders`: nothing for
/// broken symlinks and cloud placeholders; for secondary archive types
/// (.docx, .nupkg, ...) archive contents moves after None, so it stays
/// available without being the default.
#[must_use]
pub fn applicable_preview_providers(path: &str, context: &PreviewContext) -> Vec<&'static dyn PreviewProvider> {
    if context.is_broken_symlink || context.is_cloud_placeholder {
        return Vec::new();
    }

    let mut result: Vec<&'static dyn PreviewProvider> = PREVIEW_PROVIDERS
        .iter()
        .copied()
        .filter(|provider| provider.can_preview(path, context))
        .collect();

    if crate::fs::zip_preview::is_zip_file(path) && !crate::fs::zip_preview::is_primary_archive(path) {
        let index_of = |label: &str| result.iter().position(|provider| provider.label() == label);
        let zip_index = index_of(ZipContentsPreviewProvider.label());
        let none_index = index_of(NonePreviewProvider.label());

        if let (Some(zip_index), Some(none_index)) = (zip_index, none_index)
            && zip_index < none_index
        {
            let zip = result.remove(zip_index);
            result.insert(none_index, zip);
        }
    }

    result
}

/// Port of `MetadataProviderRegistry.GetApplicableProviders`: nothing for
/// broken symlinks; only file info for cloud placeholders.
#[must_use]
pub fn applicable_metadata_providers(path: &str, context: &PreviewContext) -> Vec<&'static dyn MetadataProvider> {
    if context.is_broken_symlink {
        return Vec::new();
    }

    METADATA_PROVIDERS
        .iter()
        .copied()
        .filter(|provider| !context.is_cloud_placeholder || provider.label() == FileMetadataProvider.label())
        .filter(|provider| context.archive_metadata_enabled || provider.label() != ArchiveMetadataProvider.label())
        .filter(|provider| provider.can_provide_metadata(path, context))
        .collect()
}

#[cfg(test)]
mod tests {
    //! Port of the text and archive cases in PreviewProviderRegistryTests.cs
    //! and MetadataProviderRegistryTests.cs (image and executable cases
    //! land with their providers).

    use super::{applicable_metadata_providers, applicable_preview_providers};
    use crate::fs::GitFileStatus;
    use crate::preview::{test_context, test_path, PreviewContext};

    fn cs_file() -> String {
        let path = test_path("file.cs");
        std::fs::write(&path, "Hello World").unwrap();
        path.to_string_lossy().into_owned()
    }

    fn labels(path: &str, context: &PreviewContext) -> Vec<&'static str> {
        applicable_preview_providers(path, context).iter().map(|p| p.label()).collect()
    }

    fn git(status: GitFileStatus) -> PreviewContext {
        PreviewContext {
            git_status: Some(status),
            repo_root: Some("/repo".to_string()),
            ..test_context()
        }
    }

    #[test]
    fn text_file_defaults_to_text_with_none_and_hex() {
        assert_eq!(labels(&cs_file(), &test_context()), ["Text", "None", "Hex dump"]);
    }

    #[test]
    fn cloud_placeholder_and_broken_symlink_return_empty() {
        let cloud = PreviewContext {
            is_cloud_placeholder: true,
            ..test_context()
        };
        let broken = PreviewContext {
            is_broken_symlink: true,
            ..test_context()
        };

        assert!(labels("file.cs", &cloud).is_empty());
        assert!(labels("file.cs", &broken).is_empty());
    }

    #[test]
    fn git_modified_text_file_adds_diff_after_text() {
        assert_eq!(labels(&cs_file(), &git(GitFileStatus::MODIFIED)), ["Text", "Git diff", "None", "Hex dump"]);
    }

    #[test]
    fn staged_includes_diff_untracked_excludes_it() {
        let path = cs_file();
        assert!(labels(&path, &git(GitFileStatus::STAGED)).contains(&"Git diff"));
        assert!(!labels(&path, &git(GitFileStatus::UNTRACKED)).contains(&"Git diff"));
    }

    #[test]
    fn exe_defaults_to_none_with_hex() {
        assert_eq!(labels("app.exe", &test_context()), ["None", "Hex dump"]);
    }

    #[test]
    fn markdown_file_defaults_to_rendered_markdown() {
        let path = test_path("readme.md");
        std::fs::write(&path, "# Title\n").unwrap();
        assert_eq!(
            labels(&path.to_string_lossy(), &test_context()),
            ["Rendered markdown (built-in)", "Text", "None", "Hex dump"]
        );
    }

    #[test]
    fn archives_order_by_primary_or_secondary_type() {
        let context = test_context();
        assert_eq!(labels("file.zip", &context), ["Archive contents", "None", "Hex dump"]);
        assert_eq!(labels("file.nupkg", &context), ["None", "Archive contents", "Hex dump"]);
        assert_eq!(labels("file.docx", &context), ["None", "Archive contents", "Hex dump"]);
        assert_eq!(labels("file.tar.gz", &context), ["Archive contents", "None", "Hex dump"]);

        let disabled = PreviewContext {
            zip_preview_enabled: false,
            ..test_context()
        };
        assert_eq!(labels("file.zip", &disabled), ["None", "Hex dump"]);
        assert_eq!(labels("file.tar", &disabled), ["None", "Hex dump"]);
    }

    #[test]
    fn archive_metadata_follows_its_config_flag() {
        let metadata = |context: &PreviewContext| -> Vec<&str> {
            applicable_metadata_providers("file.tar", context).iter().map(|p| p.label()).collect()
        };

        assert_eq!(metadata(&test_context()), ["File info", "Archive metadata"]);
        let off = PreviewContext {
            archive_metadata_enabled: false,
            ..test_context()
        };
        assert_eq!(metadata(&off), ["File info"]);
    }

    #[test]
    fn shortcut_metadata_applies_to_lnk_files() {
        let metadata: Vec<&str> =
            applicable_metadata_providers("C:\\x\\App.LNK", &test_context()).iter().map(|p| p.label()).collect();
        assert_eq!(metadata, ["File info", "Shortcut properties"]);
    }

    #[test]
    fn metadata_registry_short_circuits() {
        let cloud = PreviewContext {
            is_cloud_placeholder: true,
            ..test_context()
        };
        let broken = PreviewContext {
            is_broken_symlink: true,
            ..test_context()
        };

        let cloud_labels: Vec<&str> = applicable_metadata_providers("file.zip", &cloud).iter().map(|p| p.label()).collect();
        assert_eq!(cloud_labels, ["File info"]);
        assert!(applicable_metadata_providers("file.cs", &broken).is_empty());
        let plain: Vec<&str> = applicable_metadata_providers("readme.txt", &test_context()).iter().map(|p| p.label()).collect();
        assert_eq!(plain, ["File info"]);
    }
}
