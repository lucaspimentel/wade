//! Port of `ZipPreview` plus the part of .NET's `ZipArchive` (read mode)
//! it relies on: the end-of-central-directory search, the Zip64 EOCD
//! locator and record, and the central-directory file headers (names,
//! sizes, Zip64 extra fields). Entry data is never decompressed.

use std::io::{self, Read, Seek, SeekFrom};

use crate::input::CancelToken;
use crate::ui::format_helpers::{format_percent_p0, format_size_string};

const MAX_ENTRIES: usize = 100;

const EOCD_SIGNATURE: [u8; 4] = [0x50, 0x4b, 0x05, 0x06];
const EOCD_SIZE: u64 = 22;
const EOCD_SIZE_WITHOUT_SIGNATURE: u64 = 18;
const MAX_COMMENT_LENGTH: u64 = 65535;
const ZIP64_LOCATOR_SIGNATURE: u32 = 0x0706_4b50;
const ZIP64_LOCATOR_SIZE: u64 = 20;
const ZIP64_EOCD_SIGNATURE: u32 = 0x0606_4b50;
const CENTRAL_HEADER_SIGNATURE: u32 = 0x0201_4b50;
const CENTRAL_HEADER_SIZE: usize = 46;
const ZIP64_EXTRA_TAG: u16 = 0x0001;
const MASK16: u16 = 0xffff;
const MASK32: u32 = 0xffff_ffff;

/// Primary archive types: archive contents is the default preview.
const PRIMARY_ARCHIVE_EXTENSIONS: &[&str] = &[".zip", ".jar", ".war", ".ear", ".apk", ".vsix", ".whl"];

/// All zip-based extensions (primary + secondary).
const ZIP_EXTENSIONS: &[&str] = &[
    ".zip", ".nupkg", ".snupkg", ".jar", ".war", ".ear", ".docx", ".xlsx", ".pptx", ".dotx", ".xltx", ".potx", ".odt",
    ".ods", ".odp", ".apk", ".vsix", ".whl", ".epub",
];

/// A central-directory entry: `ZipArchiveEntry.FullName`, `Length` and
/// `CompressedLength`.
#[derive(Clone, Debug, PartialEq, Eq)]
pub struct ZipEntry {
    pub full_name: String,
    pub length: u64,
    pub compressed_length: u64,
}

/// Why an archive could not be read: .NET's `InvalidDataException`
/// (corrupt structure) versus any other `IOException`.
#[derive(Debug)]
pub enum ZipError {
    InvalidData,
    Io(io::Error),
}

impl From<io::Error> for ZipError {
    fn from(err: io::Error) -> Self {
        Self::Io(err)
    }
}

fn has_extension(path: &str, extensions: &[&str]) -> bool {
    let ext = crate::fs::file_preview::extension(path);
    !ext.is_empty() && extensions.iter().any(|candidate| candidate.eq_ignore_ascii_case(ext))
}

/// Port of `IsZipFile`.
#[must_use]
pub fn is_zip_file(path: &str) -> bool {
    has_extension(path, ZIP_EXTENSIONS)
}

/// Port of `IsPrimaryArchive`: archive contents is the default preview
/// (false for secondary types like .docx or .nupkg).
#[must_use]
pub fn is_primary_archive(path: &str) -> bool {
    has_extension(path, PRIMARY_ARCHIVE_EXTENSIONS)
}

fn u16_at(buf: &[u8], at: usize) -> u16 {
    u16::from_le_bytes([buf[at], buf[at + 1]])
}

fn u32_at(buf: &[u8], at: usize) -> u32 {
    u32::from_le_bytes(buf[at..at + 4].try_into().expect("4 bytes"))
}

fn u64_at(buf: &[u8], at: usize) -> u64 {
    u64::from_le_bytes(buf[at..at + 8].try_into().expect("8 bytes"))
}

/// Reads exactly `buf.len()` bytes, or reports how many were available.
fn read_up_to(reader: &mut impl Read, buf: &mut [u8]) -> io::Result<usize> {
    crate::fs::gzip::read_fully(reader, buf)
}

/// Port of `ZipHelper.SeekBackwardsToSignature` for the EOCD: the last
/// signature that starts within the comment window before the minimal
/// EOCD position.
fn find_eocd(file: &mut (impl Read + Seek), length: u64) -> Result<u64, ZipError> {
    if length < EOCD_SIZE_WITHOUT_SIGNATURE {
        // .NET seeks to -18 from the end: before the start is an IOException
        return Err(ZipError::Io(io::Error::new(
            io::ErrorKind::InvalidInput,
            "An attempt was made to move the position before the beginning of the stream.",
        )));
    }

    if length < EOCD_SIZE {
        return Err(ZipError::InvalidData);
    }

    let last_start = length - EOCD_SIZE;
    let first_start = last_start.saturating_sub(MAX_COMMENT_LENGTH);
    let window = usize::try_from(last_start - first_start + 4).expect("window fits");
    let mut buf = vec![0u8; window];
    file.seek(SeekFrom::Start(first_start))?;
    let read = read_up_to(file, &mut buf)?;
    buf.truncate(read);

    (0..=buf.len().saturating_sub(4))
        .rev()
        .find(|&i| buf[i..i + 4] == EOCD_SIGNATURE)
        .map(|i| first_start + i as u64)
        .ok_or(ZipError::InvalidData)
}

/// The central-directory location and entry count (`ReadEndOfCentralDirectory`
/// and `TryReadZip64EndOfCentralDirectory`).
fn read_end_of_central_directory(file: &mut (impl Read + Seek), length: u64) -> Result<(u64, u64), ZipError> {
    let eocd_start = find_eocd(file, length)?;
    let mut eocd = [0u8; EOCD_SIZE as usize];
    file.seek(SeekFrom::Start(eocd_start))?;
    read_up_to(file, &mut eocd)?;

    let this_disk = u16_at(&eocd, 4);
    let cd_disk = u16_at(&eocd, 6);
    let entries_on_disk = u16_at(&eocd, 8);
    let entries_total = u16_at(&eocd, 10);
    let cd_offset = u32_at(&eocd, 16);

    if this_disk != cd_disk || entries_total != entries_on_disk {
        return Err(ZipError::InvalidData); // split or spanned
    }

    let mut expected_entries = u64::from(entries_total);
    let mut cd_start = u64::from(cd_offset);

    if (this_disk == MASK16 || cd_offset == MASK32 || entries_total == MASK16) && eocd_start >= ZIP64_LOCATOR_SIZE {
        let mut locator = [0u8; ZIP64_LOCATOR_SIZE as usize];
        file.seek(SeekFrom::Start(eocd_start - ZIP64_LOCATOR_SIZE))?;
        let read = read_up_to(file, &mut locator)?;

        if read == locator.len() && u32_at(&locator, 0) == ZIP64_LOCATOR_SIGNATURE {
            let record_offset = u64_at(&locator, 8);
            if record_offset > i64::MAX as u64 {
                return Err(ZipError::InvalidData);
            }

            let mut record = [0u8; 56];
            file.seek(SeekFrom::Start(record_offset))?;
            let read = read_up_to(file, &mut record)?;
            if read < record.len() || u32_at(&record, 0) != ZIP64_EOCD_SIGNATURE {
                return Err(ZipError::InvalidData); // Zip64 EOCD not where expected
            }

            let entries_on_this_disk = u64_at(&record, 24);
            let entries = u64_at(&record, 32);
            let offset = u64_at(&record, 48);
            if entries > i64::MAX as u64 || offset > i64::MAX as u64 || entries != entries_on_this_disk {
                return Err(ZipError::InvalidData);
            }

            expected_entries = entries;
            cd_start = offset;
        }
    }

    if cd_start > length {
        return Err(ZipError::InvalidData);
    }

    Ok((cd_start, expected_entries))
}

/// Port of `ZipCentralDirectoryFileHeader.TryReadBlock` for the fields the
/// preview uses; `None` ends the directory (wrong signature or short data).
fn read_central_header(file: &mut impl Read) -> Result<Option<ZipEntry>, ZipError> {
    let mut fixed = [0u8; CENTRAL_HEADER_SIZE];
    if read_up_to(file, &mut fixed)? < CENTRAL_HEADER_SIZE || u32_at(&fixed, 0) != CENTRAL_HEADER_SIGNATURE {
        return Ok(None);
    }

    let compressed32 = u32_at(&fixed, 20);
    let uncompressed32 = u32_at(&fixed, 24);
    let name_length = usize::from(u16_at(&fixed, 28));
    let extra_length = usize::from(u16_at(&fixed, 30));
    let comment_length = usize::from(u16_at(&fixed, 32));
    let disk32 = u16_at(&fixed, 34);
    let offset32 = u32_at(&fixed, 42);

    let mut variable = vec![0u8; name_length + extra_length + comment_length];
    if read_up_to(file, &mut variable)? < variable.len() {
        return Ok(None);
    }

    let name_bytes = &variable[..name_length];
    let extra = &variable[name_length..name_length + extra_length];

    // Bit 11 marks UTF-8 names; .NET's default for the rest is UTF-8 too,
    // so the flag doesn't matter
    let full_name = String::from_utf8_lossy(name_bytes).into_owned();

    let mut length = u64::from(uncompressed32);
    let mut compressed_length = u64::from(compressed32);

    // Zip64 extra field: present values replace the masked 32-bit ones, in
    // order uncompressed, compressed, offset, disk
    let need_uncompressed = uncompressed32 == MASK32;
    let need_compressed = compressed32 == MASK32;
    let need_offset = offset32 == MASK32;
    let need_disk = disk32 == MASK16;

    if need_uncompressed || need_compressed || need_offset || need_disk {
        let mut at = 0;
        while at + 4 <= extra.len() {
            let tag = u16_at(extra, at);
            let size = usize::from(u16_at(extra, at + 2));
            let data = &extra[at + 4..(at + 4 + size).min(extra.len())];

            if tag == ZIP64_EXTRA_TAG {
                let mut field = 0;
                let mut next = |data: &[u8]| -> Option<u64> {
                    let value = data.get(field..field + 8).map(|bytes| u64_at(bytes, 0));
                    field += 8;
                    value
                };

                if need_uncompressed && let Some(value) = next(data) {
                    length = value;
                }
                if need_compressed && let Some(value) = next(data) {
                    compressed_length = value;
                }
                break;
            }

            at += 4 + size;
        }
    }

    if length > i64::MAX as u64 || compressed_length > i64::MAX as u64 {
        return Err(ZipError::InvalidData);
    }

    Ok(Some(ZipEntry {
        full_name,
        length,
        compressed_length,
    }))
}

/// Port of `ZipFile.OpenRead(path).Entries`: every central-directory
/// entry, in directory order.
pub fn read_entries(path: &str) -> Result<Vec<ZipEntry>, ZipError> {
    let mut file = std::io::BufReader::new(std::fs::File::open(path)?);
    let length = file.get_ref().metadata()?.len();
    let (cd_start, expected_entries) = read_end_of_central_directory(&mut file, length)?;

    file.seek(SeekFrom::Start(cd_start))?;
    let mut entries = Vec::new();

    while let Some(entry) = read_central_header(&mut file)? {
        entries.push(entry);
    }

    if entries.len() as u64 != expected_entries {
        return Err(ZipError::InvalidData); // count disagrees with the EOCD
    }

    Ok(entries)
}

/// Port of `GetPreviewLines`. `None` on cancellation or an I/O error (C#
/// lets the `IOException` escape, so no preview arrives either).
#[must_use]
pub fn get_preview_lines(path: &str, cancel: &CancelToken) -> Option<Vec<String>> {
    let entries = match read_entries(path) {
        Ok(entries) => entries,
        Err(ZipError::InvalidData) => return Some(vec!["[invalid archive]".to_string()]),
        Err(ZipError::Io(_)) => return None,
    };

    if cancel.is_cancelled() {
        return None;
    }

    if entries.is_empty() {
        return Some(vec!["[empty archive]".to_string()]);
    }

    // File entries only, sorted case-insensitively (stable, like OrderBy)
    let mut files: Vec<&ZipEntry> = entries.iter().filter(|entry| !entry.full_name.ends_with('/')).collect();
    if files.is_empty() {
        return Some(vec!["[empty archive]".to_string()]);
    }

    files.sort_by(|a, b| crate::text::compare_ordinal_ignore_case(&a.full_name, &b.full_name));

    let take = files.len().min(MAX_ENTRIES);
    let mut lines = Vec::with_capacity(take + 2);
    lines.push("        Size  Compressed  Ratio  Name".to_string());

    for entry in &files[..take] {
        if cancel.is_cancelled() {
            return None;
        }

        let size = format_size_string(entry.length as i64);
        let compressed = format_size_string(entry.compressed_length as i64);
        let ratio = if entry.length > 0 {
            format_percent_p0(entry.compressed_length as f64 / entry.length as f64)
        } else {
            "---".to_string()
        };

        lines.push(format!("  {size:>10}  {compressed:>10}  {ratio:>5}  {}", entry.full_name));
    }

    if files.len() > MAX_ENTRIES {
        lines.push(format!("... and {} more entries", files.len() - MAX_ENTRIES));
    }

    Some(lines)
}

#[cfg(test)]
mod tests {
    //! Port of the ZipPreviewTests.cs cases the archive golden doesn't
    //! cover (extension sets, cancellation).

    use super::{get_preview_lines, is_primary_archive, is_zip_file};
    use crate::input::CancelToken;

    const PRIMARY: &[&str] = &[".zip", ".jar", ".war", ".ear", ".apk", ".vsix", ".whl"];
    const SECONDARY: &[&str] = &[
        ".nupkg", ".snupkg", ".docx", ".xlsx", ".pptx", ".dotx", ".xltx", ".potx", ".odt", ".ods", ".odp", ".epub",
    ];

    #[test]
    fn extension_sets_match_csharp() {
        for ext in PRIMARY {
            assert!(is_zip_file(&format!("file{ext}")), "{ext}");
            assert!(is_primary_archive(&format!("file{ext}")), "{ext}");
        }

        for ext in SECONDARY {
            assert!(is_zip_file(&format!("file{ext}")), "{ext}");
            assert!(!is_primary_archive(&format!("file{ext}")), "{ext}");
        }

        for ext in [".tar", ".gz", ".txt"] {
            assert!(!is_zip_file(&format!("file{ext}")), "{ext}");
        }

        assert!(is_zip_file("ARCHIVE.ZIP"));
        assert!(!is_zip_file("dir.zip/file"));
    }

    #[test]
    fn cancelled_returns_none() {
        let path = crate::preview::test_path("a.zip");
        // Empty archive: just the end-of-central-directory record
        let mut eocd = vec![0x50, 0x4b, 0x05, 0x06];
        eocd.extend([0u8; 18]);
        std::fs::write(&path, eocd).unwrap();

        let path = path.to_string_lossy().into_owned();
        assert_eq!(get_preview_lines(&path, &CancelToken::new()), Some(vec!["[empty archive]".to_string()]));

        let cancel = CancelToken::new();
        cancel.cancel();
        assert_eq!(get_preview_lines(&path, &cancel), None);
    }

    #[test]
    fn missing_or_tiny_files_are_io_errors() {
        assert_eq!(get_preview_lines("/no/such/file.zip", &CancelToken::new()), None);

        let tiny = crate::preview::test_path("tiny.zip");
        std::fs::write(&tiny, b"PK").unwrap();
        assert_eq!(get_preview_lines(&tiny.to_string_lossy(), &CancelToken::new()), None);
    }
}
