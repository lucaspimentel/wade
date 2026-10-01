//! Ports of `PreviewProviderRegistry` and `MetadataProviderRegistry`: the
//! providers that apply to a file, in priority order (the first preview
//! provider is the default; every metadata provider contributes).

use super::metadata_providers::FileMetadataProvider;
use super::providers::{DiffPreviewProvider, HexPreviewProvider, NonePreviewProvider, TextPreviewProvider};
use super::{MetadataProvider, PreviewContext, PreviewProvider};

/// C# order: Image, PDF, Markdown, Zip, MSI, Tar, Text, Diff, None, Hex.
/// Slots for providers of later phases are absent.
static PREVIEW_PROVIDERS: [&dyn PreviewProvider; 4] =
    [&TextPreviewProvider, &DiffPreviewProvider, &NonePreviewProvider, &HexPreviewProvider];

/// C# order: File, Image, Executable, Office, Media, NuGet, MSI, Shortcut,
/// Archive, PDF. Slots for providers of later phases are absent.
static METADATA_PROVIDERS: [&dyn MetadataProvider; 1] = [&FileMetadataProvider];

/// Port of `PreviewProviderRegistry.GetApplicableProviders`: nothing for
/// broken symlinks and cloud placeholders.
#[must_use]
pub fn applicable_preview_providers(path: &str, context: &PreviewContext) -> Vec<&'static dyn PreviewProvider> {
    if context.is_broken_symlink || context.is_cloud_placeholder {
        return Vec::new();
    }

    PREVIEW_PROVIDERS
        .iter()
        .copied()
        .filter(|provider| provider.can_preview(path, context))
        .collect()
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
        .filter(|provider| provider.can_provide_metadata(path, context))
        .collect()
}

#[cfg(test)]
mod tests {
    //! Port of the text-family cases in PreviewProviderRegistryTests.cs
    //! and MetadataProviderRegistryTests.cs (archive, image and executable
    //! cases land with their providers).

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
