//! Ports of `MsiInterop`, `MsiPreviewProvider` and `MsiMetadataProvider`.
//! C# reads installers through msi.dll on Windows only; Rust reads the
//! database with the pure-Rust `msi` crate on every OS (accepted
//! deviation, KNOWN_DEVIATIONS.md).

use std::fs::File;

use super::{
    MetadataEntry, MetadataProvider, MetadataResult, MetadataSection, PreviewContext, PreviewProvider, PreviewResult,
};
use crate::highlight::StyledLine;
use crate::input::CancelToken;
use crate::ui::format_helpers::format_size_string;

const MAX_ENTRIES: usize = 100;

/// A File table row: `MsiFileEntry`.
#[derive(Clone, Debug, PartialEq, Eq)]
pub struct MsiFileEntry {
    pub file_name: String,
    pub file_size: i32,
}

/// The summary stream fields wade shows: `MsiSummaryInfo`.
#[derive(Clone, Debug, Default, PartialEq, Eq)]
pub struct MsiSummaryInfo {
    pub subject: Option<String>,
    pub author: Option<String>,
    pub template: Option<String>,
    pub comments: Option<String>,
}

/// Port of `MsiFileName.ParseLongName`: "short|long" -> "long" (first pipe).
#[must_use]
pub fn parse_long_name(raw_name: &str) -> &str {
    raw_name.split_once('|').map_or(raw_name, |(_, long)| long)
}

type Package = msi::Package<File>;

fn open_database(path: &str) -> Option<Package> {
    msi::Package::open(File::open(path).ok()?).ok()
}

/// `MsiRecordGetString` of a column: null cells read as "".
fn cell_string(value: &msi::Value) -> String {
    match value {
        msi::Value::Str(text) => text.clone(),
        msi::Value::Int(number) => number.to_string(),
        msi::Value::Null => String::new(),
    }
}

/// Port of `QueryPropertyTable`: (name, value) pairs in table order;
/// lookups are case-insensitive and the last duplicate wins.
fn query_property_table(db: &mut Package) -> Vec<(String, String)> {
    let Ok(rows) = db.select_rows(msi::Select::table("Property").columns(&["Property", "Value"])) else {
        return Vec::new();
    };

    rows.map(|row| (cell_string(&row[0]), cell_string(&row[1]))).collect()
}

fn property<'a>(properties: &'a [(String, String)], name: &str) -> Option<&'a str> {
    properties
        .iter()
        .rev()
        .find(|(key, _)| key.eq_ignore_ascii_case(name))
        .map(|(_, value)| value.as_str())
}

/// Port of `QueryFileTable`.
fn query_file_table(db: &mut Package) -> Vec<MsiFileEntry> {
    let Ok(rows) = db.select_rows(msi::Select::table("File").columns(&["FileName", "FileSize"])) else {
        return Vec::new();
    };

    rows.map(|row| MsiFileEntry {
        file_name: parse_long_name(&cell_string(&row[0])).to_string(),
        // MsiRecordGetInteger returns MSI_NULL_INTEGER for null cells
        file_size: row[1].as_int().unwrap_or(i32::MIN),
    })
    .collect()
}

/// Port of `GetSummaryInfo`. The crate splits the Template property into
/// architecture and languages; it is rejoined as `arch;lang,lang`.
fn summary_info(db: &Package) -> MsiSummaryInfo {
    let summary = db.summary_info();
    let present = |value: Option<&str>| value.filter(|v| !v.trim().is_empty()).map(str::to_string);
    let languages: Vec<String> = summary.languages().iter().map(|lang| lang.code().to_string()).collect();
    let template = match (summary.arch(), languages.is_empty()) {
        (arch, false) => Some(format!("{};{}", arch.unwrap_or(""), languages.join(","))),
        (Some(arch), true) => Some(arch.to_string()),
        (None, true) => None,
    };

    MsiSummaryInfo {
        subject: present(summary.subject()),
        author: present(summary.author()),
        template: present(template.as_deref()),
        comments: present(summary.comments()),
    }
}

fn is_msi(path: &str) -> bool {
    std::path::Path::new(path).extension().is_some_and(|e| e.eq_ignore_ascii_case("msi"))
}

// ── Preview ──────────────────────────────────────────────────────────────

pub struct MsiPreviewProvider;

impl PreviewProvider for MsiPreviewProvider {
    fn label(&self) -> &'static str {
        "Installer files"
    }

    fn can_preview(&self, path: &str, _context: &PreviewContext) -> bool {
        is_msi(path)
    }

    fn get_preview(&self, path: &str, _context: &PreviewContext, cancel: &CancelToken) -> Option<PreviewResult> {
        if cancel.is_cancelled() {
            return None;
        }

        let mut files = query_file_table(&mut open_database(path)?);

        if cancel.is_cancelled() {
            return None;
        }

        if files.is_empty() {
            return Some(PreviewResult {
                text_lines: Some(vec![StyledLine::plain("[empty installer]")]),
                file_type_label: Some("MSI".to_string()),
                is_rendered: true,
                is_placeholder: true,
                ..PreviewResult::default()
            });
        }

        Some(PreviewResult {
            text_lines: Some(file_listing(&mut files)),
            file_type_label: Some("MSI".to_string()),
            is_rendered: true,
            ..PreviewResult::default()
        })
    }
}

/// The listing body of `GetPreview`: sorted by name (OrdinalIgnoreCase),
/// capped at 100 rows.
#[must_use]
pub fn file_listing(files: &mut [MsiFileEntry]) -> Vec<StyledLine> {
    files.sort_by(|a, b| crate::text::compare_ordinal_ignore_case(&a.file_name, &b.file_name));
    let take = files.len().min(MAX_ENTRIES);
    let mut lines = Vec::with_capacity(take + 4);
    lines.push(StyledLine::plain("  Installer Files"));
    lines.push(StyledLine::plain(&format!("  {}", "\u{2500}".repeat(16))));
    lines.push(StyledLine::plain("        Size  Name"));

    for entry in &files[..take] {
        let size = format_size_string(i64::from(entry.file_size));
        lines.push(StyledLine::plain(&format!("  {size:>10}  {}", entry.file_name)));
    }

    if files.len() > MAX_ENTRIES {
        lines.push(StyledLine::plain(&format!("... and {} more files", files.len() - MAX_ENTRIES)));
    }

    lines
}

// ── Metadata ─────────────────────────────────────────────────────────────

pub struct MsiMetadataProvider;

impl MetadataProvider for MsiMetadataProvider {
    fn label(&self) -> &'static str {
        "MSI metadata"
    }

    fn can_provide_metadata(&self, path: &str, _context: &PreviewContext) -> bool {
        is_msi(path)
    }

    fn get_metadata(&self, path: &str, _context: &PreviewContext, cancel: &CancelToken) -> Option<MetadataResult> {
        if cancel.is_cancelled() {
            return None;
        }

        let mut db = open_database(path)?;
        let properties = query_property_table(&mut db);

        if cancel.is_cancelled() {
            return None;
        }

        let sections = metadata_sections(&properties, &summary_info(&db));
        (!sections.is_empty()).then(|| MetadataResult {
            sections,
            file_type_label: Some("MSI".to_string()),
        })
    }
}

/// The section building of `MsiMetadataProvider.GetMetadata`.
#[must_use]
pub fn metadata_sections(properties: &[(String, String)], summary: &MsiSummaryInfo) -> Vec<MetadataSection> {
    let mut sections = Vec::new();
    let mut installer = Vec::new();

    for (label, name) in [
        ("Product", "ProductName"),
        ("Version", "ProductVersion"),
        ("Manufacturer", "Manufacturer"),
        ("ProductCode", "ProductCode"),
        ("UpgradeCode", "UpgradeCode"),
    ] {
        if let Some(value) = property(properties, name).filter(|v| !v.trim().is_empty()) {
            installer.push(MetadataEntry::new(label, value));
        }
    }

    if !installer.is_empty() {
        sections.push(MetadataSection {
            header: Some("Installer".to_string()),
            entries: installer,
        });
    }

    let mut entries = Vec::new();

    for (label, value) in [
        ("Subject", &summary.subject),
        ("Author", &summary.author),
        ("Comments", &summary.comments),
        ("Platform", &summary.template),
    ] {
        if let Some(value) = value {
            entries.push(MetadataEntry::new(label, value));
        }
    }

    if let Some(all_users) = property(properties, "ALLUSERS") {
        entries.push(MetadataEntry::new("Install scope", if all_users == "1" { "Per-machine" } else { "Per-user" }));
    }

    if !entries.is_empty() {
        sections.push(MetadataSection {
            header: Some("Summary".to_string()),
            entries,
        });
    }

    sections
}

#[cfg(test)]
mod tests {
    use super::*;
    use crate::preview::{test_context, test_path};

    #[test]
    fn parse_long_name_matches_csharp_theory() {
        for (input, expected) in [
            ("SHORTN~1|LongFileName.txt", "LongFileName.txt"),
            ("plain.txt", "plain.txt"),
            ("", ""),
            ("A|B|C", "B|C"),
            ("SHORT~1|My Long File Name.docx", "My Long File Name.docx"),
            ("|leading", "leading"),
        ] {
            assert_eq!(parse_long_name(input), expected);
        }
    }

    /// Builds an installer with the crate's writer: Property and File
    /// tables plus summary information.
    fn build_msi(name: &str, properties: &[(&str, Option<&str>)], files: &[(&str, i32)]) -> String {
        let path = test_path(name);
        let file = std::fs::OpenOptions::new().read(true).write(true).create(true).truncate(true).open(&path).unwrap();
        let mut package = msi::Package::create(msi::PackageType::Installer, file).unwrap();
        let summary = package.summary_info_mut();
        summary.set_subject("Contoso Tool");
        summary.set_author("Contoso Ltd.");
        summary.set_comments("   ");
        summary.set_arch("x64");
        summary.set_languages(&[msi::Language::from_code(1033)]);

        package
            .create_table(
                "Property",
                vec![
                    msi::Column::build("Property").primary_key().id_string(72),
                    msi::Column::build("Value").nullable().text_string(0),
                ],
            )
            .unwrap();
        package
            .insert_rows(
                msi::Insert::into("Property").rows(
                    properties
                        .iter()
                        .map(|(key, value)| {
                            vec![msi::Value::from(*key), value.map_or(msi::Value::Null, msi::Value::from)]
                        })
                        .collect(),
                ),
            )
            .unwrap();

        if !files.is_empty() {
            package
                .create_table(
                    "File",
                    vec![
                        msi::Column::build("File").primary_key().id_string(72),
                        msi::Column::build("FileName").string(255),
                        msi::Column::build("FileSize").int32(),
                    ],
                )
                .unwrap();
            package
                .insert_rows(
                    msi::Insert::into("File").rows(
                        files
                            .iter()
                            .enumerate()
                            .map(|(i, (name, size))| {
                                vec![
                                    msi::Value::from(format!("f{i}")),
                                    msi::Value::from(*name),
                                    msi::Value::from(*size),
                                ]
                            })
                            .collect(),
                    ),
                )
                .unwrap();
        }

        package.flush().unwrap();
        path.to_string_lossy().into_owned()
    }

    fn flatten(result: &MetadataResult) -> Vec<String> {
        result
            .sections
            .iter()
            .flat_map(|s| {
                std::iter::once(format!("[{}]", s.header.as_deref().unwrap_or("-")))
                    .chain(s.entries.iter().map(|e| format!("{}: {}", e.label, e.value)))
            })
            .collect()
    }

    #[test]
    fn metadata_reads_properties_and_summary() {
        let path = build_msi(
            "tool.msi",
            &[
                ("ProductName", Some("Contoso Tool")),
                ("ProductVersion", Some("2.5.0")),
                ("Manufacturer", Some("  ")),
                ("ProductCode", Some("{11111111-2222-3333-4444-555555555555}")),
                ("ALLUSERS", Some("1")),
            ],
            &[],
        );
        let result = MsiMetadataProvider.get_metadata(&path, &test_context(), &CancelToken::new()).unwrap();
        assert_eq!(result.file_type_label.as_deref(), Some("MSI"));
        assert_eq!(
            flatten(&result),
            [
                "[Installer]",
                "Product: Contoso Tool",
                "Version: 2.5.0",
                "ProductCode: {11111111-2222-3333-4444-555555555555}",
                "[Summary]",
                "Subject: Contoso Tool",
                "Author: Contoso Ltd.",
                "Platform: x64;1033",
                "Install scope: Per-machine",
            ]
        );
    }

    #[test]
    fn null_allusers_is_per_user() {
        let sections = metadata_sections(&[("allusers".to_string(), String::new())], &MsiSummaryInfo::default());
        assert_eq!(sections[0].entries[0].value, "Per-user");
    }

    #[test]
    fn preview_lists_files_sorted_with_long_names() {
        let path = build_msi(
            "files.msi",
            &[("ProductName", Some("X"))],
            &[("B~1|beta.dll", 2048), ("alpha.exe", 10), ("Gamma.txt", 0)],
        );
        let result = MsiPreviewProvider.get_preview(&path, &test_context(), &CancelToken::new()).unwrap();
        let lines: Vec<String> = result.text_lines.unwrap().iter().map(|l| l.text.clone()).collect();
        assert_eq!(
            lines,
            [
                "  Installer Files",
                "  \u{2500}\u{2500}\u{2500}\u{2500}\u{2500}\u{2500}\u{2500}\u{2500}\u{2500}\u{2500}\u{2500}\u{2500}\u{2500}\u{2500}\u{2500}\u{2500}",
                "        Size  Name",
                &format!("  {:>10}  alpha.exe", format_size_string(10)),
                &format!("  {:>10}  beta.dll", format_size_string(2048)),
                &format!("  {:>10}  Gamma.txt", format_size_string(0)),
            ]
        );
        assert!(result.is_rendered && !result.is_placeholder);
    }

    #[test]
    fn missing_file_table_is_empty_installer() {
        let path = build_msi("empty.msi", &[("ProductName", Some("X"))], &[]);
        let result = MsiPreviewProvider.get_preview(&path, &test_context(), &CancelToken::new()).unwrap();
        assert!(result.is_placeholder);
        assert_eq!(result.text_lines.unwrap()[0].text, "[empty installer]");
    }

    #[test]
    fn listing_caps_at_one_hundred_rows() {
        let mut files: Vec<MsiFileEntry> = (0..105)
            .map(|i| MsiFileEntry {
                file_name: format!("f{i:03}"),
                file_size: 1,
            })
            .collect();
        let lines = file_listing(&mut files);
        assert_eq!(lines.len(), 3 + 100 + 1);
        assert_eq!(lines.last().unwrap().text, "... and 5 more files");
    }

    #[test]
    fn non_msi_files_return_none() {
        let path = test_path("fake.msi");
        std::fs::write(&path, "not an installer").unwrap();
        let path = path.to_string_lossy();
        assert!(MsiMetadataProvider.get_metadata(&path, &test_context(), &CancelToken::new()).is_none());
        assert!(MsiPreviewProvider.get_preview(&path, &test_context(), &CancelToken::new()).is_none());
        assert!(MsiPreviewProvider.can_preview("setup.MSI", &test_context()));
        assert!(!MsiMetadataProvider.can_provide_metadata("setup.msix", &test_context()));
    }
}
