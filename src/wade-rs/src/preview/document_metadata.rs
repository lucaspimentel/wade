//! Ports of `OfficeMetadataProvider` (OPC `docProps/core.xml` and
//! `docProps/app.xml`) and `NuGetMetadataProvider` (the root `.nuspec`),
//! reading the package with `fs::zip_preview` and the XML with roxmltree.
//!
//! Malformed XML: C# lets the `XmlException` escape, which faults the
//! preview load; Rust treats it as no result (KNOWN_DEVIATIONS.md).

use roxmltree::{Document, Node};

use super::text_helper::{format_n0, parse_integer, wrap_text};
use super::{MetadataEntry, MetadataProvider, MetadataResult, MetadataSection, PreviewContext};
use crate::fs::zip_preview::{self, ZipEntry};
use crate::input::CancelToken;

const DC_NS: &str = "http://purl.org/dc/elements/1.1/";
const DC_TERMS_NS: &str = "http://purl.org/dc/terms/";
const CP_NS: &str = "http://schemas.openxmlformats.org/package/2006/metadata/core-properties";
const APP_NS: &str = "http://schemas.openxmlformats.org/officeDocument/2006/extended-properties";
const VT_NS: &str = "http://schemas.openxmlformats.org/officeDocument/2006/docPropsVTypes";

fn extension(path: &str) -> String {
    std::path::Path::new(path)
        .extension()
        .map(|e| e.to_string_lossy().to_ascii_lowercase())
        .unwrap_or_default()
}

/// Decodes an XML part's bytes (BOM-aware UTF-8/UTF-16) and parses it.
fn parse_xml(bytes: &[u8]) -> Option<String> {
    if let Some(rest) = bytes.strip_prefix(&[0xEF, 0xBB, 0xBF]) {
        return String::from_utf8(rest.to_vec()).ok();
    }

    let utf16 = |rest: &[u8], be: bool| -> Option<String> {
        let units: Vec<u16> = rest
            .chunks_exact(2)
            .map(|pair| {
                if be {
                    u16::from_be_bytes([pair[0], pair[1]])
                } else {
                    u16::from_le_bytes([pair[0], pair[1]])
                }
            })
            .collect();
        String::from_utf16(&units).ok()
    };

    if let Some(rest) = bytes.strip_prefix(&[0xFF, 0xFE]) {
        return utf16(rest, false);
    }

    if let Some(rest) = bytes.strip_prefix(&[0xFE, 0xFF]) {
        return utf16(rest, true);
    }

    String::from_utf8(bytes.to_vec()).ok()
}

fn parse_document(text: &str) -> Option<Document<'_>> {
    let options = roxmltree::ParsingOptions {
        allow_dtd: true,
        ..roxmltree::ParsingOptions::default()
    };
    Document::parse_with_options(text, options).ok()
}

const XML_NS: &str = "http://www.w3.org/XML/1998/namespace";

/// `XElement.Value` of an `XDocument.Load`ed tree: the concatenated text of
/// all descendants. Without `LoadOptions.PreserveWhitespace`, .NET drops
/// whitespace-only text segments (each run between CDATA sections,
/// elements, comments and PIs) unless `xml:space="preserve"` applies;
/// CDATA content is always kept. roxmltree merges text with adjacent
/// CDATA, so the segments are recovered from the source span.
fn value(node: Node) -> String {
    let mut out = String::new();

    for text in node.descendants().filter(Node::is_text) {
        let preserve = text
            .ancestors()
            .filter(Node::is_element)
            .find_map(|e| e.attribute((XML_NS, "space")))
            .is_some_and(|v| v == "preserve");
        let raw = raw_text_span(text);

        if !raw.contains("<![CDATA[") {
            let decoded = text.text().unwrap_or("");
            if preserve || !is_xml_whitespace(decoded) {
                out.push_str(decoded);
            }
            continue;
        }

        let mut rest = raw;

        while !rest.is_empty() {
            let (segment, after) = match rest.find("<![CDATA[") {
                Some(at) => (&rest[..at], Some(&rest[at + 9..])),
                None => (rest, None),
            };

            let decoded = normalize_newlines(&decode_entities(segment));

            if !decoded.is_empty() && (preserve || !is_xml_whitespace(&decoded)) {
                out.push_str(&decoded);
            }

            let Some(after) = after else {
                break;
            };

            let end = after.find("]]>").unwrap_or(after.len());
            out.push_str(&normalize_newlines(&after[..end]));
            rest = after.get(end + 3..).unwrap_or("");
        }
    }

    out
}

/// The source of a text node through any merged CDATA: from its start to
/// the next sibling, or to the parent's end tag.
fn raw_text_span<'a>(text: Node<'a, '_>) -> &'a str {
    let input = text.document().input_text();
    let start = text.range().start;
    let end = match text.next_sibling() {
        Some(next) => next.range().start,
        None => text.parent().map_or(input.len(), |parent| {
            let range = parent.range();
            input[range.clone()].rfind("</").map_or(range.end, |at| range.start + at)
        }),
    };

    input.get(start..end).unwrap_or("")
}

fn is_xml_whitespace(text: &str) -> bool {
    text.chars().all(|c| matches!(c, ' ' | '\t' | '\r' | '\n'))
}

fn normalize_newlines(text: &str) -> String {
    text.replace("\r\n", "\n").replace('\r', "\n")
}

/// The predefined and numeric character references.
fn decode_entities(text: &str) -> String {
    let mut out = String::with_capacity(text.len());
    let mut rest = text;

    while let Some(at) = rest.find('&') {
        out.push_str(&rest[..at]);
        let after = &rest[at + 1..];
        let Some(end) = after.find(';') else {
            out.push_str(&rest[at..]);
            return out;
        };

        let name = &after[..end];
        let decoded = match name {
            "lt" => Some('<'),
            "gt" => Some('>'),
            "amp" => Some('&'),
            "quot" => Some('"'),
            "apos" => Some('\''),
            _ => name
                .strip_prefix("#x")
                .map(|hex| u32::from_str_radix(hex, 16))
                .or_else(|| name.strip_prefix('#').map(str::parse::<u32>))
                .and_then(Result::ok)
                .and_then(char::from_u32),
        };

        match decoded {
            Some(ch) => out.push(ch),
            None => out.push_str(&rest[at..at + end + 2]),
        }

        rest = &after[end + 1..];
    }

    out.push_str(rest);
    out
}

/// `XElement.Element(ns + name)`: the first child element with that
/// expanded name.
fn element<'a, 'input>(node: Node<'a, 'input>, ns: &str, name: &str) -> Option<Node<'a, 'input>> {
    node.children()
        .find(|child| child.is_element() && child.tag_name().namespace() == Some(ns) && child.tag_name().name() == name)
}

fn element_value(node: Node, ns: &str, name: &str) -> Option<String> {
    element(node, ns, name).map(value)
}

fn add_entry_if_present(entries: &mut Vec<MetadataEntry>, label: &str, value: Option<String>) {
    if let Some(value) = value.filter(|v| !v.trim().is_empty()) {
        entries.push(MetadataEntry::new(label, &value));
    }
}

/// Reads the first entry named exactly `name` (`ZipArchive.GetEntry`).
fn read_entry(path: &str, entries: &[ZipEntry], name: &str) -> Result<Option<Vec<u8>>, ()> {
    match entries.iter().find(|entry| entry.full_name == name) {
        None => Ok(None),
        Some(entry) => zip_preview::read_entry_data(path, entry).map(Some).map_err(|_| ()),
    }
}

fn description_entries(description: &str, context: &PreviewContext) -> Vec<MetadataEntry> {
    let width = usize::try_from((context.pane_width_cells - 6).max(20)).unwrap_or(20);
    wrap_text(description.trim(), width).iter().map(|line| MetadataEntry::new("", line)).collect()
}

fn single_message(message: &str, label: &str) -> MetadataResult {
    MetadataResult {
        sections: vec![MetadataSection {
            header: None,
            entries: vec![MetadataEntry::new("", message)],
        }],
        file_type_label: Some(label.to_string()),
    }
}

// ── Office ───────────────────────────────────────────────────────────────

pub struct OfficeMetadataProvider;

impl MetadataProvider for OfficeMetadataProvider {
    fn label(&self) -> &'static str {
        "Document metadata"
    }

    fn can_provide_metadata(&self, path: &str, _context: &PreviewContext) -> bool {
        matches!(extension(path).as_str(), "docx" | "xlsx" | "pptx" | "dotx" | "xltx" | "potx")
    }

    fn get_metadata(&self, path: &str, context: &PreviewContext, cancel: &CancelToken) -> Option<MetadataResult> {
        if cancel.is_cancelled() {
            return None;
        }

        let entries = zip_preview::read_entries(path).ok()?;
        let file_type_label = office_file_type_label(path);
        let mut sections = Vec::new();

        let has_core = add_core_properties(&mut sections, path, &entries, context)?;

        if cancel.is_cancelled() {
            return None;
        }

        let has_app = add_app_properties(&mut sections, path, &entries)?;

        if !has_core && !has_app {
            return Some(single_message("[no document metadata found]", file_type_label));
        }

        Some(MetadataResult {
            sections,
            file_type_label: Some(file_type_label.to_string()),
        })
    }
}

fn office_file_type_label(path: &str) -> &'static str {
    match extension(path).as_str() {
        "docx" | "dotx" => "Word Document",
        "xlsx" | "xltx" => "Excel Workbook",
        "pptx" | "potx" => "PowerPoint Presentation",
        _ => "Office Document",
    }
}

/// Port of `AddCoreProperties`; `None` when the part can't be read or parsed.
fn add_core_properties(
    sections: &mut Vec<MetadataSection>,
    path: &str,
    zip: &[ZipEntry],
    context: &PreviewContext,
) -> Option<bool> {
    let Some(bytes) = read_entry(path, zip, "docProps/core.xml").ok()? else {
        return Some(false);
    };

    let text = parse_xml(&bytes)?;
    let doc = parse_document(&text)?;
    let root = doc.root_element();
    let mut entries = Vec::new();

    add_entry_if_present(&mut entries, "Title", element_value(root, DC_NS, "title"));
    add_entry_if_present(&mut entries, "Author", element_value(root, DC_NS, "creator"));
    add_entry_if_present(&mut entries, "Subject", element_value(root, DC_NS, "subject"));
    add_entry_if_present(&mut entries, "Keywords", element_value(root, CP_NS, "keywords"));
    add_entry_if_present(&mut entries, "Category", element_value(root, CP_NS, "category"));
    add_date_entry(&mut entries, "Created", element_value(root, DC_TERMS_NS, "created"));
    add_date_entry(&mut entries, "Modified", element_value(root, DC_TERMS_NS, "modified"));
    add_entry_if_present(&mut entries, "Last author", element_value(root, CP_NS, "lastModifiedBy"));
    add_entry_if_present(&mut entries, "Revision", element_value(root, CP_NS, "revision"));

    if let Some(description) = element_value(root, DC_NS, "description").filter(|d| !d.trim().is_empty()) {
        if !entries.is_empty() {
            sections.push(MetadataSection {
                header: Some("Document Properties".to_string()),
                entries,
            });
        }

        sections.push(MetadataSection {
            header: Some("Description".to_string()),
            entries: description_entries(&description, context),
        });
        return Some(true);
    }

    if entries.is_empty() {
        return Some(false);
    }

    sections.push(MetadataSection {
        header: Some("Document Properties".to_string()),
        entries,
    });
    Some(true)
}

/// Port of `AddAppProperties`.
fn add_app_properties(sections: &mut Vec<MetadataSection>, path: &str, zip: &[ZipEntry]) -> Option<bool> {
    let Some(bytes) = read_entry(path, zip, "docProps/app.xml").ok()? else {
        return Some(false);
    };

    let text = parse_xml(&bytes)?;
    let doc = parse_document(&text)?;
    let root = doc.root_element();
    let mut entries = Vec::new();
    let ext = extension(path);

    if ext == "docx" || ext == "dotx" {
        add_formatted_int_entry(&mut entries, "Pages", element_value(root, APP_NS, "Pages"));
        add_formatted_int_entry(&mut entries, "Words", element_value(root, APP_NS, "Words"));
        add_formatted_int_entry(&mut entries, "Paragraphs", element_value(root, APP_NS, "Paragraphs"));
    } else if ext == "pptx" || ext == "potx" {
        add_formatted_int_entry(&mut entries, "Slides", element_value(root, APP_NS, "Slides"));
        add_formatted_int_entry(&mut entries, "Hidden slides", element_value(root, APP_NS, "HiddenSlides"));
    }

    add_entry_if_present(&mut entries, "Application", element_value(root, APP_NS, "Application"));

    let parts: Vec<String> = element(root, APP_NS, "TitlesOfParts")
        .and_then(|titles| element(titles, VT_NS, "vector"))
        .map(|vector| {
            vector
                .children()
                .filter(|c| c.is_element() && c.tag_name().namespace() == Some(VT_NS) && c.tag_name().name() == "lpstr")
                .map(value)
                .filter(|v| !v.trim().is_empty())
                .collect()
        })
        .unwrap_or_default();

    if entries.is_empty() && parts.is_empty() {
        return Some(false);
    }

    if !entries.is_empty() {
        sections.push(MetadataSection {
            header: Some("Statistics".to_string()),
            entries,
        });
    }

    if !parts.is_empty() {
        let label = if ext == "xlsx" || ext == "xltx" { "Sheets" } else { "Parts" };
        sections.push(MetadataSection {
            header: Some(label.to_string()),
            entries: parts.iter().map(|part| MetadataEntry::new("", part)).collect(),
        });
    }

    Some(true)
}

/// Port of `AddDateEntry`: ISO 8601 dates print as `yyyy-MM-dd HH:mm:ss`
/// in their own offset; anything else prints as written.
fn add_date_entry(entries: &mut Vec<MetadataEntry>, label: &str, value: Option<String>) {
    let Some(value) = value.filter(|v| !v.trim().is_empty()) else {
        return;
    };

    let formatted = parse_iso_date_time(&value).unwrap_or(value);
    entries.push(MetadataEntry::new(label, &formatted));
}

/// The ISO 8601 subset of `DateTimeOffset.TryParse`: `yyyy-MM-dd`, then
/// optionally `T`/space `HH:mm[:ss[.fff]]` and `Z` or `±HH:mm`.
#[must_use]
pub fn parse_iso_date_time(value: &str) -> Option<String> {
    let text = value.trim();
    let (date, time) = match text.find(['T', ' ']) {
        Some(at) => (&text[..at], Some(&text[at + 1..])),
        None => (text, None),
    };

    let mut date_parts = date.split('-');
    let year: i32 = date_parts.next().filter(|p| p.len() == 4)?.parse().ok()?;
    let month: u32 = date_parts.next().filter(|p| p.len() == 2)?.parse().ok()?;
    let day: u32 = date_parts.next().filter(|p| p.len() == 2)?.parse().ok()?;

    if date_parts.next().is_some() || chrono::NaiveDate::from_ymd_opt(year, month, day).is_none() {
        return None;
    }

    let (mut hour, mut minute, mut second) = (0, 0, 0);

    if let Some(time) = time {
        let clock = time.trim_end_matches('Z');
        let clock = match clock.rfind(['+', '-']) {
            Some(at) => {
                let offset = &clock[at + 1..];
                let valid = matches!(offset.len(), 4 | 5)
                    && offset
                        .chars()
                        .enumerate()
                        .all(|(i, c)| c.is_ascii_digit() || (i == 2 && c == ':' && offset.len() == 5));
                if !valid || time.ends_with('Z') {
                    return None;
                }
                &clock[..at]
            }
            None => clock,
        };

        let clock = clock.split('.').next()?;
        let mut parts = clock.split(':');
        hour = parts.next().filter(|p| p.len() == 2)?.parse().ok()?;
        minute = parts.next().filter(|p| p.len() == 2)?.parse().ok()?;
        second = match parts.next() {
            Some(p) if p.len() == 2 => p.parse().ok()?,
            Some(_) => return None,
            None => 0,
        };

        if parts.next().is_some() || hour > 23 || minute > 59 || second > 59 {
            return None;
        }
    }

    Some(format!("{year:04}-{month:02}-{day:02} {hour:02}:{minute:02}:{second:02}"))
}

/// Port of `AddFormattedIntEntry`.
fn add_formatted_int_entry(entries: &mut Vec<MetadataEntry>, label: &str, value: Option<String>) {
    let Some(value) = value.filter(|v| !v.trim().is_empty()) else {
        return;
    };

    match parse_integer::<i32>(&value) {
        Some(number) => entries.push(MetadataEntry::new(label, &format_n0(i64::from(number)))),
        None => entries.push(MetadataEntry::new(label, &value)),
    }
}

// ── NuGet ────────────────────────────────────────────────────────────────

pub struct NuGetMetadataProvider;

const NUGET_LABEL: &str = "NuGet Package";

impl MetadataProvider for NuGetMetadataProvider {
    fn label(&self) -> &'static str {
        "NuGet metadata"
    }

    fn can_provide_metadata(&self, path: &str, _context: &PreviewContext) -> bool {
        matches!(extension(path).as_str(), "nupkg" | "snupkg")
    }

    fn get_metadata(&self, path: &str, context: &PreviewContext, cancel: &CancelToken) -> Option<MetadataResult> {
        if cancel.is_cancelled() {
            return None;
        }

        let zip = zip_preview::read_entries(path).ok()?;
        let Some(nuspec) = zip
            .iter()
            .find(|entry| entry.full_name.to_ascii_lowercase().ends_with(".nuspec") && !entry.full_name.contains('/'))
        else {
            return Some(single_message("[no .nuspec found in package]", NUGET_LABEL));
        };

        if cancel.is_cancelled() {
            return None;
        }

        let bytes = zip_preview::read_entry_data(path, nuspec).ok()?;
        let text = parse_xml(&bytes)?;
        let doc = parse_document(&text)?;
        let root = doc.root_element();

        let Some(metadata) = child_by_local_name(root, "metadata") else {
            return Some(single_message("[invalid .nuspec: no metadata element]", NUGET_LABEL));
        };

        let mut sections = Vec::new();
        let mut main = Vec::new();
        add_field(&mut main, metadata, "Id", "Id");
        add_field(&mut main, metadata, "Version", "Version");
        add_field(&mut main, metadata, "Authors", "Authors");
        add_license(&mut main, metadata);
        add_field(&mut main, metadata, "ProjectUrl", "Project");
        add_repository_url(&mut main, metadata);
        add_field(&mut main, metadata, "Tags", "Tags");

        if !main.is_empty() {
            sections.push(MetadataSection {
                header: Some(NUGET_LABEL.to_string()),
                entries: main,
            });
        }

        if let Some(description) = element_value_ignore_case(metadata, "description").filter(|d| !d.trim().is_empty()) {
            sections.push(MetadataSection {
                header: Some("Description".to_string()),
                entries: description_entries(&description, context),
            });
        }

        add_dependencies(&mut sections, metadata);
        Some(MetadataResult {
            sections,
            file_type_label: Some(NUGET_LABEL.to_string()),
        })
    }
}

fn child_by_local_name<'a, 'input>(node: Node<'a, 'input>, name: &str) -> Option<Node<'a, 'input>> {
    node.children().find(|c| c.is_element() && c.tag_name().name() == name)
}

fn children_by_local_name<'a, 'input>(node: Node<'a, 'input>, name: &'a str) -> impl Iterator<Item = Node<'a, 'input>> {
    node.children().filter(move |c| c.is_element() && c.tag_name().name() == name)
}

/// Port of `GetElementValue`: local name matched case-insensitively.
fn element_value_ignore_case(node: Node, name: &str) -> Option<String> {
    node.children()
        .find(|c| c.is_element() && c.tag_name().name().eq_ignore_ascii_case(name))
        .map(value)
}

/// `XElement.Attribute(name)` for an attribute without a namespace.
fn attribute(node: Node, name: &str) -> Option<String> {
    node.attributes()
        .find(|a| a.namespace().is_none() && a.name() == name)
        .map(|a| a.value().to_string())
}

fn add_field(entries: &mut Vec<MetadataEntry>, metadata: Node, element_name: &str, label: &str) {
    add_entry_if_present(entries, label, element_value_ignore_case(metadata, element_name));
}

fn add_license(entries: &mut Vec<MetadataEntry>, metadata: Node) {
    if let Some(license) = child_by_local_name(metadata, "license") {
        entries.push(MetadataEntry::new("License", &value(license)));
        return;
    }

    add_entry_if_present(entries, "License", element_value_ignore_case(metadata, "licenseUrl"));
}

fn add_repository_url(entries: &mut Vec<MetadataEntry>, metadata: Node) {
    if let Some(repository) = child_by_local_name(metadata, "repository") {
        add_entry_if_present(entries, "Repository", attribute(repository, "url"));
    }
}

fn dependency_entries(parent: Node) -> Vec<MetadataEntry> {
    children_by_local_name(parent, "dependency")
        .filter_map(|dep| {
            let id = attribute(dep, "id")?;
            Some(MetadataEntry::new(
                "",
                &match attribute(dep, "version") {
                    Some(version) => format!("{id} >= {version}"),
                    None => id,
                },
            ))
        })
        .collect()
}

/// Port of `AddDependencies`: grouped by target framework, or flat.
fn add_dependencies(sections: &mut Vec<MetadataSection>, metadata: Node) {
    let Some(deps) = child_by_local_name(metadata, "dependencies") else {
        return;
    };

    let groups: Vec<Node> = children_by_local_name(deps, "group").collect();

    if groups.is_empty() {
        let entries = dependency_entries(deps);

        if !entries.is_empty() {
            sections.push(MetadataSection {
                header: Some("Dependencies".to_string()),
                entries,
            });
        }

        return;
    }

    for group in groups {
        let framework = attribute(group, "targetFramework").unwrap_or_else(|| "any".to_string());
        let entries = dependency_entries(group);

        if !entries.is_empty() {
            sections.push(MetadataSection {
                header: Some(format!("Dependencies ({framework})")),
                entries,
            });
        }
    }
}

#[cfg(test)]
mod tests {
    use super::*;
    use crate::preview::test_context;

    #[test]
    fn office_extensions() {
        for path in ["a.docx", "a.XLSX", "a.pptx", "a.dotx", "a.xltx", "a.potx"] {
            assert!(OfficeMetadataProvider.can_provide_metadata(path, &test_context()), "{path}");
        }

        for path in ["a.doc", "a.zip", "a.nupkg"] {
            assert!(!OfficeMetadataProvider.can_provide_metadata(path, &test_context()), "{path}");
        }
    }

    #[test]
    fn nuget_extensions() {
        assert!(NuGetMetadataProvider.can_provide_metadata("a.nupkg", &test_context()));
        assert!(NuGetMetadataProvider.can_provide_metadata("a.SNUPKG", &test_context()));
        assert!(!NuGetMetadataProvider.can_provide_metadata("a.zip", &test_context()));
    }

    #[test]
    fn iso_dates_format_in_their_own_offset() {
        assert_eq!(parse_iso_date_time("2024-01-15T10:30:00Z").as_deref(), Some("2024-01-15 10:30:00"));
        assert_eq!(parse_iso_date_time("2024-01-15T10:30:00.123+05:30").as_deref(), Some("2024-01-15 10:30:00"));
        assert_eq!(parse_iso_date_time("2024-01-15 23:59").as_deref(), Some("2024-01-15 23:59:00"));
        assert_eq!(parse_iso_date_time("2024-01-15").as_deref(), Some("2024-01-15 00:00:00"));
        assert_eq!(parse_iso_date_time("2024-02-30T00:00:00Z"), None);
        assert_eq!(parse_iso_date_time("yesterday"), None);
    }

    #[test]
    fn whitespace_only_segments_drop_like_xdocument() {
        let xml = "<a>Prefixed <![CDATA[and CDATA]]> <b>nested</b> <c xml:space=\"preserve\"> </c> &amp;&#x41;<![CDATA[ ]]></a>";
        let doc = parse_document(xml).unwrap();
        assert_eq!(value(doc.root_element()), "Prefixed and CDATAnested  &A ");
    }

    #[test]
    fn malformed_xml_is_no_result() {
        assert!(parse_document("<a><b></a>").is_none());
    }
}
