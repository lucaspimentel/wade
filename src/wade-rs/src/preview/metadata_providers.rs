//! Metadata providers ported so far: `FileMetadataProvider`,
//! `ShortcutMetadataProvider`, `ArchiveMetadataProvider` and
//! `PdfMetadataProvider`.

use super::{MetadataEntry, MetadataProvider, MetadataResult, MetadataSection, PreviewContext};
use crate::fs::{GitFileStatus, lnk, tar_preview, zip_preview};
use crate::input::CancelToken;
use crate::ui::format_helpers::{format_percent_p0, format_size_string};
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
            sections: vec![MetadataSection { header: Some(name.to_string()), entries }],
            file_type_label: None,
        })
    }
}

/// Port of `ShortcutMetadataProvider`: .lnk target, string data, hotkey,
/// window state and link info. Unparsable files give nothing.
pub struct ShortcutMetadataProvider;

impl MetadataProvider for ShortcutMetadataProvider {
    fn label(&self) -> &'static str {
        "Shortcut properties"
    }

    fn can_provide_metadata(&self, path: &str, _context: &PreviewContext) -> bool {
        crate::fs::file_preview::extension(path).eq_ignore_ascii_case(".lnk")
    }

    fn get_metadata(&self, path: &str, _context: &PreviewContext, cancel: &CancelToken) -> Option<MetadataResult> {
        if cancel.is_cancelled() {
            return None;
        }

        let lnk = lnk::LnkFile::parse(path).ok()?;
        let mut sections = Vec::new();
        let mut entries = Vec::new();
        let non_empty = |value: &Option<String>| value.as_deref().filter(|v| !v.is_empty()).map(str::to_string);

        if let Some(target) = lnk.target_path() {
            entries.push(MetadataEntry::new("Target", &target));
        }
        if let Some(uri) = lnk.launch_uri() {
            entries.push(MetadataEntry::new("Launch URI", &uri));
        }
        if let Some(dir) = non_empty(&lnk.string_data.working_dir) {
            entries.push(MetadataEntry::new("Working Dir", &dir));
        }
        if let Some(args) = non_empty(&lnk.string_data.command_line_arguments) {
            entries.push(MetadataEntry::new("Arguments", &args));
        }
        if let Some(description) = non_empty(&lnk.string_data.name) {
            entries.push(MetadataEntry::new("Description", &description));
        }
        if let Some(icon) = non_empty(&lnk.string_data.icon_location) {
            entries.push(MetadataEntry::new("Icon", &icon));
        }
        if lnk.header.hot_key != 0 {
            entries.push(MetadataEntry::new("Hotkey", &lnk::decode_hot_key(lnk.header.hot_key)));
        }
        if lnk.header.show_command != lnk::SHOW_NORMAL {
            entries.push(MetadataEntry::new("Window", &lnk::show_command_name(lnk.header.show_command)));
        }

        if !entries.is_empty() {
            sections.push(MetadataSection {
                header: Some("Shortcut".to_string()),
                entries,
            });
        }

        if let Some(info) = &lnk.link_info {
            let mut link_entries = Vec::new();

            if let Some(local) = non_empty(&info.local_base_path) {
                link_entries.push(MetadataEntry::new("Local Path", &local));
            }
            if let Some(label) = non_empty(&info.volume_label) {
                link_entries.push(MetadataEntry::new("Volume", &label));
            }

            if !link_entries.is_empty() {
                sections.push(MetadataSection {
                    header: Some("Link Info".to_string()),
                    entries: link_entries,
                });
            }
        }

        Some(MetadataResult {
            sections,
            file_type_label: Some("Windows Shortcut".to_string()),
        })
    }
}

/// Port of `ArchiveMetadataProvider`: file count, sizes and ratio for zip,
/// tar, tar.gz and plain gzip files.
pub struct ArchiveMetadataProvider;

impl MetadataProvider for ArchiveMetadataProvider {
    fn label(&self) -> &'static str {
        "Archive metadata"
    }

    fn can_provide_metadata(&self, path: &str, _context: &PreviewContext) -> bool {
        zip_preview::is_zip_file(path) || tar_preview::is_tar_archive(path) || tar_preview::is_plain_gzip(path)
    }

    fn get_metadata(&self, path: &str, _context: &PreviewContext, cancel: &CancelToken) -> Option<MetadataResult> {
        if cancel.is_cancelled() {
            return None;
        }

        if zip_preview::is_zip_file(path) {
            return zip_metadata(path, cancel);
        }

        if tar_preview::is_tar_archive(path) || tar_preview::is_plain_gzip(path) {
            return tar_metadata(path, cancel);
        }

        None
    }
}

fn archive_section(entries: Vec<MetadataEntry>) -> MetadataResult {
    MetadataResult {
        sections: vec![MetadataSection {
            header: Some("Archive".to_string()),
            entries,
        }],
        file_type_label: None,
    }
}

/// Port of `GetZipMetadata`; `None` for unreadable archives.
fn zip_metadata(path: &str, cancel: &CancelToken) -> Option<MetadataResult> {
    let entries = zip_preview::read_entries(path).ok()?;

    if cancel.is_cancelled() {
        return None;
    }

    let files: Vec<&zip_preview::ZipEntry> = entries.iter().filter(|entry| !entry.full_name.ends_with('/')).collect();
    let total_size: i64 = files.iter().map(|entry| entry.length as i64).sum();
    let total_compressed: i64 = files.iter().map(|entry| entry.compressed_length as i64).sum();

    let ratio = if total_size > 0 {
        format_percent_p0(total_compressed as f64 / total_size as f64)
    } else {
        "---".to_string()
    };

    Some(archive_section(vec![
        MetadataEntry::new("Files", &crate::app::group_thousands(files.len() as i64)),
        MetadataEntry::new("Total size", &format_size_string(total_size)),
        MetadataEntry::new("Compressed", &format_size_string(total_compressed)),
        MetadataEntry::new("Ratio", &ratio),
    ]))
}

/// Port of `GetTarMetadata`.
fn tar_metadata(path: &str, cancel: &CancelToken) -> Option<MetadataResult> {
    let stats = tar_preview::get_stats(path, cancel)?;

    let format = match stats.format {
        tar_preview::TarFormat::Tar => "tar",
        tar_preview::TarFormat::TarGzip => "tar.gz",
        tar_preview::TarFormat::Gzip => "gzip",
    };

    let mut entries = vec![
        MetadataEntry::new("Files", &crate::app::group_thousands(stats.files as i64)),
        MetadataEntry::new("Total size", &format_size_string(stats.total_size)),
        MetadataEntry::new("Format", format),
    ];

    if let Some(compressed) = stats.compressed_size {
        entries.push(MetadataEntry::new("Compressed", &format_size_string(compressed)));

        if stats.total_size > 0 {
            entries.push(MetadataEntry::new("Ratio", &format_percent_p0(compressed as f64 / stats.total_size as f64)));
        }
    }

    Some(archive_section(entries))
}

/// Port of `PdfMetadataProvider`: document fields from `pdfinfo`.
pub struct PdfMetadataProvider;

impl PdfMetadataProvider {
    /// Port of `IsAvailable`.
    #[must_use]
    pub fn is_available() -> bool {
        crate::preview::cli_tool_hints::is_available("pdfinfo", Some("-v"), false)
    }
}

impl MetadataProvider for PdfMetadataProvider {
    fn label(&self) -> &'static str {
        "PDF metadata"
    }

    fn can_provide_metadata(&self, path: &str, context: &PreviewContext) -> bool {
        context.pdf_metadata_enabled
            && crate::fs::file_preview::extension(path).eq_ignore_ascii_case(".pdf")
            && Self::is_available()
    }

    fn get_metadata(&self, path: &str, _context: &PreviewContext, cancel: &CancelToken) -> Option<MetadataResult> {
        if cancel.is_cancelled() {
            return None;
        }

        let output = crate::preview::cli_tool_hints::run("pdfinfo", &[path], 5000, cancel)?;
        if cancel.is_cancelled() {
            return None;
        }

        Some(MetadataResult {
            sections: parse_pdf_info_output(&output)?,
            file_type_label: Some("PDF".to_string()),
        })
    }
}

/// Port of `ParsePdfInfoOutput`: the known `Key: value` lines, renamed.
/// Dates stay as pdfinfo prints them (C# reformats ones .NET can parse;
/// KNOWN_DEVIATIONS.md).
#[must_use]
pub fn parse_pdf_info_output(output: &str) -> Option<Vec<MetadataSection>> {
    if output.trim().is_empty() {
        return None;
    }

    let entries: Vec<MetadataEntry> = output
        .split('\n')
        .filter_map(|line| {
            let (key, value) = line.split_once(':')?;
            let (key, value) = (key.trim(), value.trim());
            let label = match key {
                "Title" | "Subject" | "Keywords" | "Author" | "Creator" | "Producer" | "Pages" | "Page size"
                | "PDF version" | "Encrypted" => key,
                "CreationDate" => "Created",
                "ModDate" => "Modified",
                _ => return None,
            };
            (!value.is_empty()).then(|| MetadataEntry::new(label, value))
        })
        .collect();

    (!entries.is_empty()).then(|| {
        vec![MetadataSection {
            header: Some("PDF Document".to_string()),
            entries,
        }]
    })
}

#[cfg(test)]
mod tests {
    //! Port of FileMetadataProviderTests.cs.

    use super::FileMetadataProvider;
    use crate::fs::GitFileStatus;
    use crate::input::CancelToken;
    use crate::preview::{MetadataProvider, MetadataResult, PreviewContext, registry, test_context, test_path};

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
    fn pdf_info_parsing() {
        use super::parse_pdf_info_output;

        let full = "Title:           Cover Page English\nAuthor:          John Doe\nCreator:         Windows NT 4.0\n\
                    Producer:        Acrobat Distiller 3.01 for Windows\nCreationDate:    Mon May 24 04:42:21 1999\n\
                    ModDate:         Mon May 24 04:44:24 1999\nPages:           57\nEncrypted:       no\n\
                    Page size:       595 x 842 pts (A4)\nPDF version:     1.2\nUnknown:   skipped\n";
        let sections = parse_pdf_info_output(full).expect("sections");
        assert_eq!(sections[0].header.as_deref(), Some("PDF Document"));
        let flat: Vec<String> = sections[0].entries.iter().map(|e| format!("{}={}", e.label, e.value)).collect();
        assert_eq!(
            flat,
            [
                "Title=Cover Page English",
                "Author=John Doe",
                "Creator=Windows NT 4.0",
                "Producer=Acrobat Distiller 3.01 for Windows",
                "Created=Mon May 24 04:42:21 1999",
                "Modified=Mon May 24 04:44:24 1999",
                "Pages=57",
                "Encrypted=no",
                "Page size=595 x 842 pts (A4)",
                "PDF version=1.2",
            ]
        );

        let minimal = parse_pdf_info_output("Pages:           3\nPDF version:     1.7\n").expect("sections");
        assert_eq!(minimal[0].entries.len(), 2);
        assert!(parse_pdf_info_output("").is_none());
        assert!(parse_pdf_info_output("Title:\nnothing here\n").is_none());
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
