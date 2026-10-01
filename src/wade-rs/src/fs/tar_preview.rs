//! Port of `TarPreview` plus the part of .NET's `System.Formats.Tar`
//! `TarReader` it relies on: V7, ustar, PAX (local and global extended
//! headers) and GNU (long name/link) headers, read without copying data.
//! Gzip goes through `fs::gzip::GzipReader`, which mirrors `GZipStream`.
//!
//! TarReader facts the port follows (probed against .NET, pinned by the
//! archive golden): checksums are not verified; an all-null or zero
//! checksum field ends the archive; names stop at the first NUL (trailing
//! spaces kept); on a seekable stream, being exactly at the end is the end
//! of the archive while anything else short is `EndOfStreamException`;
//! numeric fields that don't parse are `InvalidDataException`; GNU headers
//! parse the atime/ctime fields where ustar keeps its name prefix.

use std::fs::File;
use std::io::{self, BufReader, Read, Seek, SeekFrom};

use crate::fs::gzip::{read_fully, GzipReader};
use crate::highlight::StyledLine;
use crate::input::CancelToken;
use crate::ui::format_helpers::format_size_string;

const MAX_ENTRIES: usize = 100;
const TEXT_HEAD_LINES: usize = 30;
const BINARY_CHECK_SIZE: usize = 4096;
const RECORD_SIZE: usize = 512;
const USTAR_MAGIC_OFFSET: usize = 257;
const ISIZE_SAFETY_LIMIT: u64 = 3584 * 1024 * 1024; // ~3.5 GiB

/// Port of `TarFormat`.
#[derive(Clone, Copy, Debug, PartialEq, Eq)]
pub enum TarFormat {
    Tar,
    TarGzip,
    Gzip,
}

/// Port of `TarArchiveStats`.
#[derive(Clone, Copy, Debug, PartialEq, Eq)]
pub struct TarArchiveStats {
    pub files: usize,
    pub total_size: i64,
    pub format: TarFormat,
    pub compressed_size: Option<i64>,
    pub uncompressed_hint: Option<i64>,
}

/// The .NET exception families the C# code distinguishes.
#[derive(Debug)]
pub enum TarError {
    /// `InvalidDataException` (corrupt header, unparsable number).
    InvalidData,
    /// `EndOfStreamException` (archive cut short).
    EndOfStream,
    /// `NotSupportedException` (GNU sparse entries).
    NotSupported,
    /// Any other `IOException`.
    Io(io::Error),
}

impl From<io::Error> for TarError {
    fn from(err: io::Error) -> Self {
        if err.kind() == io::ErrorKind::InvalidData {
            Self::InvalidData
        } else {
            Self::Io(err)
        }
    }
}

fn ends_with_ignore_case(path: &str, suffix: &str) -> bool {
    path.len() >= suffix.len()
        && path.is_char_boundary(path.len() - suffix.len())
        && path[path.len() - suffix.len()..].eq_ignore_ascii_case(suffix)
}

/// Port of `IsTarArchive`: .tar, .tar.gz or .tgz.
#[must_use]
pub fn is_tar_archive(path: &str) -> bool {
    ends_with_ignore_case(path, ".tar") || ends_with_ignore_case(path, ".tar.gz") || ends_with_ignore_case(path, ".tgz")
}

/// Port of `IsPlainGzip`: .gz but not .tar.gz.
#[must_use]
pub fn is_plain_gzip(path: &str) -> bool {
    ends_with_ignore_case(path, ".gz") && !ends_with_ignore_case(path, ".tar.gz")
}

// ── TarReader ──────────────────────────────────────────────────────────

/// A tar source: a seekable file (position/length known) or a forward-only
/// stream such as gzip.
trait TarSource: Read {
    /// `Some((position, length))` when seekable.
    fn seek_state(&mut self) -> Option<(u64, u64)>;
    /// Skips `count` bytes (seekable: may move past the end).
    fn skip(&mut self, count: u64) -> Result<(), TarError>;
}

struct SeekableSource {
    file: BufReader<File>,
    position: u64,
    length: u64,
}

impl Read for SeekableSource {
    fn read(&mut self, buf: &mut [u8]) -> io::Result<usize> {
        let n = self.file.read(buf)?;
        self.position += n as u64;
        Ok(n)
    }
}

impl TarSource for SeekableSource {
    fn seek_state(&mut self) -> Option<(u64, u64)> {
        Some((self.position, self.length))
    }

    fn skip(&mut self, count: u64) -> Result<(), TarError> {
        self.position += count;
        self.file.seek(SeekFrom::Start(self.position))?;
        Ok(())
    }
}

struct StreamSource<R: Read> {
    inner: R,
}

impl<R: Read> Read for StreamSource<R> {
    fn read(&mut self, buf: &mut [u8]) -> io::Result<usize> {
        self.inner.read(buf)
    }
}

impl<R: Read> TarSource for StreamSource<R> {
    fn seek_state(&mut self) -> Option<(u64, u64)> {
        None
    }

    fn skip(&mut self, count: u64) -> Result<(), TarError> {
        let copied = io::copy(&mut (&mut self.inner).take(count), &mut io::sink())?;
        if copied < count {
            return Err(TarError::EndOfStream);
        }

        Ok(())
    }
}

/// The parts of a `TarEntry` the preview reads.
#[derive(Clone, Debug, PartialEq, Eq)]
pub struct TarEntry {
    pub name: String,
    pub length: i64,
    pub is_directory: bool,
}

#[derive(Clone, Copy, PartialEq, Eq)]
enum Format {
    Unknown,
    V7,
    Ustar,
    Pax,
    Gnu,
}

struct Header {
    name: String,
    size: i64,
    type_flag: u8,
}

/// Bytes up to the first NUL, as UTF-8 (`GetTrimmedUtf8String` behavior
/// observed in .NET: trailing spaces survive).
fn field_string(field: &[u8]) -> String {
    let end = field.iter().position(|&b| b == 0).unwrap_or(field.len());
    String::from_utf8_lossy(&field[..end]).into_owned()
}

fn is_null_or_space(b: u8) -> bool {
    b == 0 || b == b' '
}

/// Port of `TarHelpers.ParseOctal`: trims nulls/spaces at both ends.
fn parse_octal(field: &[u8]) -> Result<i64, TarError> {
    let start = field.iter().position(|&b| !is_null_or_space(b)).unwrap_or(field.len());
    let end = field.iter().rposition(|&b| !is_null_or_space(b)).map_or(start, |i| i + 1);
    let mut value: i64 = 0;

    for &b in &field[start..end.max(start)] {
        let digit = b.wrapping_sub(b'0');
        if digit >= 8 {
            return Err(TarError::InvalidData);
        }

        value = value.checked_mul(8).and_then(|v| v.checked_add(i64::from(digit))).ok_or(TarError::InvalidData)?;
    }

    Ok(value)
}

/// Port of `TarHelpers.ParseNumeric`: base-256 when the high bit is set,
/// octal otherwise.
fn parse_numeric(field: &[u8]) -> Result<i64, TarError> {
    if field.first().is_some_and(|&b| b & 0x80 != 0) {
        let negative = field[0] == 0xff;
        let mut value: i64 = if negative { -1 } else { 0 };
        for (i, &b) in field.iter().enumerate() {
            let byte = if i == 0 { b & 0x7f } else { b };
            if negative && i == 0 {
                continue;
            }
            value = value.checked_mul(256).and_then(|v| v.checked_add(i64::from(byte))).ok_or(TarError::InvalidData)?;
        }

        return Ok(value);
    }

    parse_octal(field)
}

fn read_exact_or_eos(source: &mut (impl Read + ?Sized), buf: &mut [u8]) -> Result<(), TarError> {
    if read_fully(source, buf)? < buf.len() {
        return Err(TarError::EndOfStream);
    }

    Ok(())
}

fn padding(size: i64) -> u64 {
    let size = size.max(0) as u64;
    (RECORD_SIZE as u64 - size % RECORD_SIZE as u64) % RECORD_SIZE as u64
}

/// Port of `TarHeader.TryGetNextHeader` (attributes only; the caller
/// handles the data block). `None` marks the end of the archive.
fn read_header(source: &mut dyn TarSource, initial: Format) -> Result<Option<Header>, TarError> {
    if let Some((position, length)) = source.seek_state()
        && position == length
    {
        return Ok(None);
    }

    let mut buf = [0u8; RECORD_SIZE];
    read_exact_or_eos(source, &mut buf)?;

    // An empty checksum means an all-blank (end) record
    let checksum_field = &buf[148..156];
    if checksum_field.iter().all(|&b| b == 0) || parse_octal(checksum_field)? == 0 {
        return Ok(None);
    }

    let size = parse_numeric(&buf[124..136])?;
    if size < 0 {
        return Err(TarError::InvalidData);
    }

    let mut name = field_string(&buf[0..100]);
    parse_numeric(&buf[100..108])?; // mode
    parse_numeric(&buf[136..148])?; // mtime
    parse_numeric(&buf[108..116])?; // uid
    parse_numeric(&buf[116..124])?; // gid
    let type_flag = buf[156];

    let mut format = initial;
    if format == Format::Unknown {
        format = match type_flag {
            b'x' | b'g' => Format::Pax,
            b'D' | b'K' | b'L' | b'M' | b'N' | b'V' => Format::Gnu,
            b'S' => return Err(TarError::NotSupported),
            b'0' => Format::Ustar,
            _ => Format::V7,
        };
    }

    // Magic: all nulls is V7; "ustar " is GNU; "ustar\0" upgrades V7 to ustar
    let magic = &buf[257..263];
    if magic.iter().all(|&b| b == 0) {
        format = Format::V7;
    } else if magic == b"ustar " {
        format = Format::Gnu;
    } else if magic == b"ustar\0" && format == Format::V7 {
        format = Format::Ustar;
    }

    if format != Format::V7 {
        let version = &buf[263..265];
        if version != b"00" && version != b" \0" {
            return Err(TarError::InvalidData);
        }

        if type_flag == b'3' || type_flag == b'4' {
            parse_numeric(&buf[329..337])?; // devmajor
            parse_numeric(&buf[337..345])?; // devminor
        }

        match format {
            Format::Ustar | Format::Pax => {
                let prefix = field_string(&buf[345..500]);
                if !prefix.is_empty() {
                    name = format!("{prefix}/{name}");
                }
            }
            Format::Gnu => {
                parse_numeric(&buf[345..357])?; // atime
                parse_numeric(&buf[357..369])?; // ctime
            }
            _ => {}
        }
    }

    Ok(Some(Header { name, size, type_flag }))
}

/// Reads a metadata entry's data block (PAX attributes, GNU long names).
fn read_data(source: &mut dyn TarSource, size: i64) -> Result<Vec<u8>, TarError> {
    let size = usize::try_from(size).map_err(|_| TarError::InvalidData)?;
    let mut data = vec![0u8; size];
    read_exact_or_eos(source, &mut data)?;
    source.skip(padding(size as i64))?;
    Ok(data)
}

/// PAX records ("LEN key=value\n"); parsing stops at the first malformed one.
fn parse_pax(data: &[u8]) -> Vec<(String, String)> {
    let mut records = Vec::new();
    let mut rest = data;

    while let Some(space) = rest.iter().position(|&b| b == b' ') {
        let Some(length) = std::str::from_utf8(&rest[..space]).ok().and_then(|s| s.parse::<usize>().ok()) else {
            break;
        };

        if length <= space + 1 || length > rest.len() || rest[length - 1] != b'\n' {
            break;
        }

        let record = &rest[space + 1..length - 1];
        let Some(eq) = record.iter().position(|&b| b == b'=') else {
            break;
        };

        records.push((
            String::from_utf8_lossy(&record[..eq]).into_owned(),
            String::from_utf8_lossy(&record[eq + 1..]).into_owned(),
        ));
        rest = &rest[length..];
    }

    records
}

/// Port of `TarReader.GetNextEntry(copyData: false)`.
struct TarReader<'a> {
    source: &'a mut dyn TarSource,
    reached_end: bool,
    pending_skip: u64,
}

impl<'a> TarReader<'a> {
    fn new(source: &'a mut dyn TarSource) -> Self {
        Self {
            source,
            reached_end: false,
            pending_skip: 0,
        }
    }

    fn next_entry(&mut self) -> Result<Option<TarEntry>, TarError> {
        if self.reached_end {
            return Ok(None);
        }

        // Advance past the previous entry's data and padding
        let skip = std::mem::take(&mut self.pending_skip);
        if skip > 0 {
            self.source.skip(skip)?;
        }

        let Some(mut header) = read_header(self.source, Format::Unknown)? else {
            self.reached_end = true;
            return Ok(None);
        };

        match header.type_flag {
            b'x' => {
                let attributes = parse_pax(&read_data(self.source, header.size)?);
                let Some(mut actual) = read_header(self.source, Format::Pax)? else {
                    return Err(TarError::InvalidData);
                };
                if matches!(actual.type_flag, b'x' | b'g') {
                    return Err(TarError::InvalidData);
                }

                for (key, value) in attributes {
                    match key.as_str() {
                        "path" => actual.name = value,
                        "size" => {
                            if let Ok(size) = value.parse::<i64>() {
                                actual.size = size;
                            }
                        }
                        _ => {}
                    }
                }

                header = actual;
            }
            b'g' => {
                // Returned as its own entry; its data is the attribute block
                read_data(self.source, header.size)?;
                return Ok(Some(TarEntry {
                    name: header.name,
                    length: header.size,
                    is_directory: false,
                }));
            }
            b'L' | b'K' => {
                let mut long_path = None;
                let mut current = header;

                // A long name and a long link may both precede the entry
                for _ in 0..2 {
                    let data = read_data(self.source, current.size)?;
                    if current.type_flag == b'L' {
                        long_path = Some(field_string(&data));
                    }

                    let Some(next) = read_header(self.source, Format::Gnu)? else {
                        return Err(TarError::InvalidData);
                    };
                    current = next;

                    if !matches!(current.type_flag, b'L' | b'K') {
                        break;
                    }
                }

                if matches!(current.type_flag, b'L' | b'K') {
                    return Err(TarError::InvalidData);
                }

                if let Some(path) = long_path {
                    current.name = path;
                }

                header = current;
            }
            _ => {}
        }

        self.pending_skip = header.size.max(0) as u64 + padding(header.size);

        Ok(Some(TarEntry {
            is_directory: header.type_flag == b'5',
            name: header.name,
            length: header.size,
        }))
    }
}

fn open_seekable(path: &str) -> Result<SeekableSource, TarError> {
    let file = File::open(path)?;
    let length = file.metadata()?.len();
    Ok(SeekableSource {
        file: BufReader::new(file),
        position: 0,
        length,
    })
}

fn open_gzip(path: &str) -> Result<StreamSource<GzipReader<File>>, TarError> {
    Ok(StreamSource {
        inner: GzipReader::new(File::open(path)?),
    })
}

/// Opens `path` as a tar stream: plain for .tar, through gzip otherwise.
fn with_tar_source<T>(path: &str, f: impl FnOnce(&mut dyn TarSource) -> Result<T, TarError>) -> Result<T, TarError> {
    if ends_with_ignore_case(path, ".tar") {
        f(&mut open_seekable(path)?)
    } else {
        f(&mut open_gzip(path)?)
    }
}

/// Port of `RenderTarEntries`: the first 100 non-directory entries in
/// archive order, then sorted case-insensitively.
fn render_tar_entries(source: &mut dyn TarSource, cancel: &CancelToken) -> Result<Option<Vec<String>>, TarError> {
    let mut reader = TarReader::new(source);
    let mut entries: Vec<(String, i64)> = Vec::new();
    let mut total_files = 0usize;

    loop {
        if cancel.is_cancelled() {
            return Ok(None);
        }

        let Some(entry) = reader.next_entry()? else {
            break;
        };

        if entry.is_directory {
            continue;
        }

        total_files += 1;
        if entries.len() < MAX_ENTRIES {
            entries.push((entry.name, entry.length));
        }
    }

    if total_files == 0 {
        return Ok(Some(vec!["[empty archive]".to_string()]));
    }

    entries.sort_by(|a, b| crate::text::compare_ordinal_ignore_case(&a.0, &b.0));

    let mut lines = Vec::with_capacity(entries.len() + 2);
    lines.push("        Size  Name".to_string());

    for (name, size) in entries {
        if cancel.is_cancelled() {
            return Ok(None);
        }

        lines.push(format!("  {:>10}  {name}", format_size_string(size)));
    }

    if total_files > MAX_ENTRIES {
        lines.push(format!("... and {} more entries", total_files - MAX_ENTRIES));
    }

    Ok(Some(lines))
}

/// Port of `CountTarEntries`.
fn count_tar_entries(source: &mut dyn TarSource, cancel: &CancelToken) -> Result<Option<(usize, i64)>, TarError> {
    let mut reader = TarReader::new(source);
    let mut files = 0;
    let mut total: i64 = 0;

    loop {
        if cancel.is_cancelled() {
            return Ok(None);
        }

        let Some(entry) = reader.next_entry()? else {
            break;
        };

        if !entry.is_directory {
            files += 1;
            total += entry.length;
        }
    }

    Ok(Some((files, total)))
}

/// Port of `LooksLikeTarGzip`: the first decompressed record carries the
/// ustar magic.
fn looks_like_tar_gzip(path: &str, cancel: &CancelToken) -> bool {
    let Ok(mut gz) = File::open(path).map(GzipReader::new) else {
        return false;
    };

    let mut buf = [0u8; RECORD_SIZE];
    let Ok(read) = read_fully(&mut gz, &mut buf) else {
        return false;
    };

    if cancel.is_cancelled() || read < USTAR_MAGIC_OFFSET + 5 {
        return false;
    }

    &buf[USTAR_MAGIC_OFFSET..USTAR_MAGIC_OFFSET + 5] == b"ustar"
}

/// Maps the C# catch blocks of `GetPreviewLines`/`GetGzipStyledPreview`:
/// corrupt or short archives become "[invalid archive]".
fn invalid_or_none<T>(result: Result<Option<T>, TarError>, invalid: impl FnOnce() -> T) -> Option<T> {
    match result {
        Ok(value) => value,
        Err(TarError::InvalidData | TarError::EndOfStream | TarError::Io(_)) => Some(invalid()),
        // C# lets NotSupportedException escape (the preview never arrives)
        Err(TarError::NotSupported) => None,
    }
}

/// Port of `GetPreviewLines` (tar and tar.gz listings; plain .gz text).
#[must_use]
pub fn get_preview_lines(path: &str, cancel: &CancelToken) -> Option<Vec<String>> {
    let invalid = || vec!["[invalid archive]".to_string()];

    if ends_with_ignore_case(path, ".tar")
        || ends_with_ignore_case(path, ".tar.gz")
        || ends_with_ignore_case(path, ".tgz")
    {
        return invalid_or_none(with_tar_source(path, |source| render_tar_entries(source, cancel)), invalid);
    }

    if ends_with_ignore_case(path, ".gz") {
        return invalid_or_none(render_gzip(path, cancel), invalid);
    }

    None
}

/// Port of `GetStats`; `None` on any failure.
#[must_use]
pub fn get_stats(path: &str, cancel: &CancelToken) -> Option<TarArchiveStats> {
    let compressed_size = i64::try_from(std::fs::metadata(path).ok()?.len()).ok()?;

    if ends_with_ignore_case(path, ".tar") {
        let (files, total) = with_tar_source(path, |source| count_tar_entries(source, cancel)).ok()??;
        return Some(TarArchiveStats {
            files,
            total_size: total,
            format: TarFormat::Tar,
            compressed_size: None,
            uncompressed_hint: None,
        });
    }

    let tar_gzip = ends_with_ignore_case(path, ".tar.gz")
        || ends_with_ignore_case(path, ".tgz")
        || (ends_with_ignore_case(path, ".gz") && looks_like_tar_gzip(path, cancel));

    if tar_gzip {
        let (files, total) = count_tar_entries(&mut open_gzip(path).ok()?, cancel).ok()??;
        return Some(TarArchiveStats {
            files,
            total_size: total,
            format: TarFormat::TarGzip,
            compressed_size: Some(compressed_size),
            uncompressed_hint: None,
        });
    }

    if ends_with_ignore_case(path, ".gz") {
        let uncompressed = uncompressed_hint(path, compressed_size);
        return Some(TarArchiveStats {
            files: 1,
            total_size: uncompressed.unwrap_or(0),
            format: TarFormat::Gzip,
            compressed_size: Some(compressed_size),
            uncompressed_hint: uncompressed,
        });
    }

    None
}

/// Port of `ReadGzipIsize`: the trailer's last four bytes.
fn read_gzip_isize(path: &str) -> Option<u32> {
    let mut file = File::open(path).ok()?;
    if file.metadata().ok()?.len() < 4 {
        return None;
    }

    file.seek(SeekFrom::End(-4)).ok()?;
    let mut buf = [0u8; 4];
    (read_fully(&mut file, &mut buf).ok()? == 4).then(|| u32::from_le_bytes(buf))
}

/// ISIZE as the uncompressed size, trusted only below the safety limit.
fn uncompressed_hint(path: &str, compressed: i64) -> Option<i64> {
    let isize = read_gzip_isize(path)?;
    ((compressed as u64) < ISIZE_SAFETY_LIMIT).then_some(i64::from(isize))
}

/// `Path.GetFileNameWithoutExtension`.
fn file_name_without_extension(path: &str) -> &str {
    let name = path.rsplit(crate::search::is_separator).next().unwrap_or(path);
    name.rfind('.').map_or(name, |dot| &name[..dot])
}

/// Port of `BuildGzipMetadataLine`.
fn build_gzip_metadata_line(path: &str) -> Result<String, TarError> {
    let inner_name = file_name_without_extension(path);
    let compressed = i64::try_from(std::fs::metadata(path)?.len()).unwrap_or(i64::MAX);
    let compressed_text = format_size_string(compressed);

    Ok(match uncompressed_hint(path, compressed) {
        Some(size) => format!(
            "[gzip] original: {inner_name}  compressed: {compressed_text}  uncompressed: ~{}",
            format_size_string(size)
        ),
        None => format!("[gzip] original: {inner_name}  compressed: {compressed_text}"),
    })
}

/// Port of `GzipContent`.
struct GzipContent {
    lines: Vec<String>,
    is_text: bool,
}

/// Port of `ReadGzipContent`: the first 4 KB decompressed, as up to 30
/// text lines, or a placeholder.
fn read_gzip_content(path: &str, cancel: &CancelToken) -> Result<Option<GzipContent>, TarError> {
    let placeholder = |text: &str| GzipContent {
        lines: vec![text.to_string()],
        is_text: false,
    };

    let mut gz = match File::open(path) {
        Ok(file) => GzipReader::new(file),
        Err(_) => return Ok(Some(placeholder("[read error]"))),
    };

    let mut buf = vec![0u8; BINARY_CHECK_SIZE];
    let read = match read_fully(&mut gz, &mut buf) {
        Ok(read) => read,
        Err(err) if err.kind() == io::ErrorKind::InvalidData => return Ok(Some(placeholder("[invalid gzip content]"))),
        Err(_) => return Ok(Some(placeholder("[read error]"))),
    };

    if cancel.is_cancelled() {
        return Ok(None);
    }

    if read == 0 {
        return Ok(Some(placeholder("[empty]")));
    }

    if buf[..read].contains(&0) {
        return Ok(Some(placeholder("[binary content]")));
    }

    let text = String::from_utf8_lossy(&buf[..read]);
    let lines = string_reader_lines(&text).into_iter().take(TEXT_HEAD_LINES).collect();

    Ok(Some(GzipContent { lines, is_text: true }))
}

/// `StringReader.ReadLine` until null: lines end at \r, \n or \r\n, and a
/// final unterminated segment counts only when non-empty.
fn string_reader_lines(text: &str) -> Vec<String> {
    let mut lines = Vec::new();
    let mut current = String::new();
    let mut chars = text.chars().peekable();

    while let Some(ch) = chars.next() {
        match ch {
            '\r' | '\n' => {
                if ch == '\r' && chars.peek() == Some(&'\n') {
                    chars.next();
                }
                lines.push(std::mem::take(&mut current));
            }
            _ => current.push(ch),
        }
    }

    if !current.is_empty() {
        lines.push(current);
    }

    lines
}

/// Port of `RenderGzip`.
fn render_gzip(path: &str, cancel: &CancelToken) -> Result<Option<Vec<String>>, TarError> {
    if looks_like_tar_gzip(path, cancel) {
        return render_tar_entries(&mut open_gzip(path)?, cancel);
    }

    let mut lines = vec![build_gzip_metadata_line(path)?, "\u{2500}".repeat(16)];
    let Some(content) = read_gzip_content(path, cancel)? else {
        return Ok(None);
    };

    lines.extend(content.lines);
    Ok(Some(lines))
}

/// Port of `GetGzipStyledPreview`: a tar listing for tar payloads, else
/// the metadata line, a rule, and the text head highlighted by the inner
/// file name (`script.py.gz` as Python).
#[must_use]
pub fn get_gzip_styled_preview(path: &str, cancel: &CancelToken) -> Option<Vec<StyledLine>> {
    let result = (|| -> Result<Option<Vec<StyledLine>>, TarError> {
        if looks_like_tar_gzip(path, cancel) {
            let lines = render_tar_entries(&mut open_gzip(path)?, cancel)?;
            return Ok(lines.map(|lines| lines.iter().map(|line| StyledLine::plain(line)).collect()));
        }

        let mut result = vec![
            StyledLine::plain(&build_gzip_metadata_line(path)?),
            StyledLine::plain(&"\u{2500}".repeat(16)),
        ];

        let Some(content) = read_gzip_content(path, cancel)? else {
            return Ok(None);
        };

        if content.is_text && !content.lines.is_empty() {
            let lines: Vec<&str> = content.lines.iter().map(String::as_str).collect();
            result.extend(crate::highlight::highlight(&lines, file_name_without_extension(path)));
        } else {
            result.extend(content.lines.iter().map(|line| StyledLine::plain(line)));
        }

        Ok(Some(result))
    })();

    invalid_or_none(result, || vec![StyledLine::plain("[invalid archive]")])
}

#[cfg(test)]
mod tests {
    //! Port of the TarPreviewTests.cs cases the archive golden doesn't
    //! cover (extension checks, cancellation, stats).

    use std::io::Write;

    use super::{get_gzip_styled_preview, get_preview_lines, get_stats, is_plain_gzip, is_tar_archive, TarFormat};
    use crate::input::CancelToken;

    fn cancelled() -> CancelToken {
        let cancel = CancelToken::new();
        cancel.cancel();
        cancel
    }

    fn gz(data: &[u8]) -> Vec<u8> {
        let mut encoder = flate2::write::GzEncoder::new(Vec::new(), flate2::Compression::default());
        encoder.write_all(data).unwrap();
        encoder.finish().unwrap()
    }

    fn fixture(name: &str) -> String {
        format!("{}/../../tests/golden/preview/archives/{name}", env!("CARGO_MANIFEST_DIR"))
    }

    #[test]
    fn extension_checks_match_csharp() {
        for path in ["archive.tar", "archive.tar.gz", "archive.TAR.GZ", "archive.tgz", "archive.TGZ"] {
            assert!(is_tar_archive(path), "{path}");
        }
        for path in ["file.gz", "file.zip", "file.txt", ""] {
            assert!(!is_tar_archive(path), "{path}");
        }
        for path in ["foo.log.gz", "foo.GZ"] {
            assert!(is_plain_gzip(path), "{path}");
        }
        for path in ["archive.tar.gz", "archive.TAR.GZ", "file.tar", "file.tgz", "file.txt"] {
            assert!(!is_plain_gzip(path), "{path}");
        }
    }

    #[test]
    fn cancelled_returns_none() {
        assert_eq!(get_preview_lines(&fixture("plain.tar"), &cancelled()), None);
        assert_eq!(get_preview_lines(&fixture("plain.tgz"), &cancelled()), None);

        let path = crate::preview::test_path("text.txt.gz");
        std::fs::write(&path, gz(b"hello\n")).unwrap();
        assert!(get_gzip_styled_preview(&path.to_string_lossy(), &cancelled()).is_none());
    }

    #[test]
    fn stats_per_format() {
        let cancel = CancelToken::new();

        let tar = get_stats(&fixture("plain.tar"), &cancel).expect("tar stats");
        assert_eq!((tar.files, tar.format, tar.compressed_size), (5, TarFormat::Tar, None));

        let tgz = get_stats(&fixture("plain.tgz"), &cancel).expect("tgz stats");
        assert_eq!((tgz.files, tgz.total_size, tgz.format), (5, tar.total_size, TarFormat::TarGzip));
        assert_eq!(tgz.compressed_size, Some(std::fs::metadata(fixture("plain.tgz")).unwrap().len() as i64));

        let gzip = get_stats(&fixture("script.py.gz"), &cancel).expect("gzip stats");
        assert_eq!((gzip.files, gzip.total_size, gzip.format), (1, 53, TarFormat::Gzip));
        assert_eq!(gzip.uncompressed_hint, Some(53));

        // A tar payload behind a bare .gz extension counts as tar.gz
        assert_eq!(get_stats(&fixture("tarball.gz"), &cancel).expect("tarball").format, TarFormat::TarGzip);
        assert_eq!(get_stats(&fixture("truncated.tar"), &cancel), None);
    }

    #[test]
    fn sparse_entries_produce_no_preview() {
        // C# lets NotSupportedException escape: no preview arrives
        let path = crate::preview::test_path("sparse.tar");
        let mut header = vec![0u8; 512];
        header[..6].copy_from_slice(b"sparse");
        header[124..136].copy_from_slice(b"00000000000\0");
        header[148..156].copy_from_slice(b"0000001\0");
        header[156] = b'S';
        std::fs::write(&path, header).unwrap();

        assert_eq!(get_preview_lines(&path.to_string_lossy(), &CancelToken::new()), None);
        assert_eq!(get_stats(&path.to_string_lossy(), &CancelToken::new()), None);
    }
}
