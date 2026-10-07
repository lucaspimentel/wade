//! Port of `FilePreview`: binary/encoding/line-ending detection and the
//! first lines of a text file for the preview pane.

use std::io::Read;

use super::file_type_labels;

const MAX_PREVIEW_LINES: usize = 100;
const BINARY_CHECK_SIZE: usize = 512;

/// Extensions treated as binary without reading the file.
const BINARY_EXTENSIONS: &[&str] = &[
    // Images
    ".png", ".jpg", ".jpeg", ".gif", ".svg", ".webp", ".ico", ".bmp", ".tga", ".tiff", ".pbm", ".pdn",
    // Documents
    ".pdf", // Archives
    ".zip", ".tar", ".gz", ".7z", ".rar", ".nupkg", ".snupkg", ".jar", ".war", ".ear", ".docx", ".xlsx", ".pptx",
    ".odt", ".ods", ".odp", ".apk", ".vsix", ".whl", ".epub", // Binaries
    ".exe", ".dll", ".so", ".dylib", ".pdb", ".wasm",
];

/// Port of `FileMetadata`.
#[derive(Clone, Debug, PartialEq, Eq)]
pub struct FileMetadata {
    pub is_binary: bool,
    /// "UTF-8", "UTF-8 BOM", "UTF-16 LE", "UTF-16 BE", or "" for binary.
    pub encoding: String,
    /// "CRLF", "LF", "CR", "Mixed", or None when no line break was seen.
    pub line_ending: Option<String>,
    pub placeholder_message: Option<String>,
}

impl FileMetadata {
    fn new(is_binary: bool, encoding: &str, line_ending: Option<&str>) -> Self {
        Self {
            is_binary,
            encoding: encoding.to_string(),
            line_ending: line_ending.map(str::to_string),
            placeholder_message: None,
        }
    }

    fn binary() -> Self {
        Self::new(true, "", None)
    }
}

/// Port of `GetFileTypeLabel`.
#[must_use]
pub fn get_file_type_label(path: &str) -> Option<&'static str> {
    file_type_labels::get_file_type_label(path)
}

/// `Path.GetExtension` of the file name (with the dot; empty when none).
pub(crate) fn extension(path: &str) -> &str {
    let name = path.rsplit(crate::search::is_separator).next().unwrap_or(path);

    match name.rfind('.') {
        Some(index) if index + 1 < name.len() => &name[index..],
        _ => "",
    }
}

/// Port of `DetectFileMetadata`: known binary extensions, BOMs, a BOM-less
/// UTF-16 heuristic, then a null-byte and line-ending scan of the first
/// 512 bytes. Any I/O error counts as binary.
#[must_use]
pub fn detect_file_metadata(path: &str) -> FileMetadata {
    let ext = extension(path);
    if !ext.is_empty() && BINARY_EXTENSIONS.iter().any(|known| known.eq_ignore_ascii_case(ext)) {
        return FileMetadata::binary();
    }

    let Ok(mut file) = std::fs::File::open(path) else {
        return FileMetadata::binary();
    };

    let mut buffer = [0u8; BINARY_CHECK_SIZE];
    let mut bytes_read = 0;

    // One Stream.Read call reads what is available; loop to fill like a file read does
    while bytes_read < BINARY_CHECK_SIZE {
        match file.read(&mut buffer[bytes_read..]) {
            Ok(0) => break,
            Ok(n) => bytes_read += n,
            Err(_) => return FileMetadata::binary(),
        }
    }

    if bytes_read == 0 {
        return FileMetadata::new(false, "UTF-8", None);
    }

    let buffer = &buffer[..bytes_read];

    // Encoding via BOM
    let mut encoding = "UTF-8";
    let mut data_start = 0;

    if buffer.starts_with(&[0xEF, 0xBB, 0xBF]) {
        encoding = "UTF-8 BOM";
        data_start = 3;
    } else if buffer.starts_with(&[0xFF, 0xFE]) {
        // UTF-16 LE BOM: text; skip the null scan (UTF-16 has 0x00 bytes)
        return FileMetadata::new(false, "UTF-16 LE", None);
    } else if buffer.starts_with(&[0xFE, 0xFF]) {
        return FileMetadata::new(false, "UTF-16 BE", None);
    }

    // BOM-less UTF-16 heuristic (only without a BOM)
    if data_start == 0
        && let Some(bomless) = detect_bomless_utf16(buffer)
    {
        return FileMetadata::new(false, bomless, None);
    }

    // Null bytes (binary) and line endings
    let (mut has_crlf, mut has_lf, mut has_cr) = (false, false, false);
    let mut i = data_start;

    while i < bytes_read {
        match buffer[i] {
            0 => return FileMetadata::binary(),
            b'\r' => {
                if i + 1 < bytes_read && buffer[i + 1] == b'\n' {
                    has_crlf = true;
                    i += 1; // skip the \n
                } else {
                    has_cr = true;
                }
            }
            b'\n' => has_lf = true,
            _ => {}
        }

        i += 1;
    }

    let line_ending = match (has_crlf, has_lf, has_cr) {
        (true, false, false) => Some("CRLF"),
        (false, true, false) => Some("LF"),
        (false, false, true) => Some("CR"),
        (false, false, false) => None,
        _ => Some("Mixed"),
    };

    FileMetadata::new(false, encoding, line_ending)
}

/// Port of `IsBinary`.
#[must_use]
pub fn is_binary(path: &str) -> bool {
    detect_file_metadata(path).is_binary
}

/// Port of `TryDetectBomlessUtf16`: at least 8 bytes; one byte of each
/// code unit mostly null (>= 80%) and the other mostly not (<= 10%).
fn detect_bomless_utf16(buffer: &[u8]) -> Option<&'static str> {
    if buffer.len() < 8 {
        return None;
    }

    let analysis_length = buffer.len() & !1;
    let pair_count = analysis_length / 2;
    let (mut even_nulls, mut odd_nulls) = (0, 0);

    for pair in buffer[..analysis_length].as_chunks::<2>().0 {
        if pair[0] == 0 {
            even_nulls += 1;
        }

        if pair[1] == 0 {
            odd_nulls += 1;
        }
    }

    let high_threshold = pair_count * 80 / 100;
    let low_threshold = pair_count * 10 / 100;

    if odd_nulls >= high_threshold && even_nulls <= low_threshold {
        return Some("UTF-16 LE");
    }

    if even_nulls >= high_threshold && odd_nulls <= low_threshold {
        return Some("UTF-16 BE");
    }

    None
}

/// Port of `GetPreviewLines`: up to 100 lines with tabs expanded to four
/// spaces, or a single placeholder line (`[binary file]`, `[empty file]`,
/// `[access denied]`, `[error: ...]`). Returns the lines and the metadata
/// (with `placeholder_message` set for placeholders).
#[must_use]
pub fn get_preview_lines(path: &str) -> (Vec<String>, FileMetadata) {
    let mut metadata = detect_file_metadata(path);

    if metadata.is_binary {
        metadata.placeholder_message = Some("[binary file]".to_string());
        return (vec!["[binary file]".to_string()], metadata);
    }

    let placeholder = |message: String| {
        let metadata = FileMetadata {
            is_binary: false,
            encoding: "UTF-8".to_string(),
            line_ending: None,
            placeholder_message: Some(message.clone()),
        };
        (vec![message], metadata)
    };

    let lines = match read_lines(path, &metadata.encoding) {
        Ok(lines) => lines,
        Err(err) if err.kind() == std::io::ErrorKind::PermissionDenied => {
            return placeholder("[access denied]".to_string());
        }
        Err(err) => return placeholder(format!("[error: {err}]")),
    };

    if lines.is_empty() {
        metadata.placeholder_message = Some("[empty file]".to_string());
        return (vec!["[empty file]".to_string()], metadata);
    }

    (lines, metadata)
}

/// `StreamReader.ReadLine` x 100: decodes as UTF-16 when detected (BOM or
/// not), otherwise UTF-8 (BOM skipped, invalid bytes as U+FFFD); lines end
/// at `\r`, `\n` or `\r\n`, and a final unterminated line counts.
fn read_lines(path: &str, encoding: &str) -> std::io::Result<Vec<String>> {
    let mut file = std::fs::File::open(path)?;
    let mut lines = Vec::new();
    let mut decoder = Decoder::new(encoding);
    let mut current: Vec<char> = Vec::new();
    let mut pending_cr = false;
    let mut chunk = vec![0u8; 64 * 1024];

    loop {
        let n = file.read(&mut chunk)?;
        let chars = if n == 0 { decoder.finish() } else { decoder.decode(&chunk[..n]) };

        for ch in chars {
            if pending_cr {
                pending_cr = false;

                if ch == '\n' {
                    continue; // \r\n is one break
                }
            }

            match ch {
                '\r' | '\n' => {
                    lines.push(take_line(&mut current));
                    pending_cr = ch == '\r';

                    if lines.len() == MAX_PREVIEW_LINES {
                        return Ok(lines);
                    }
                }
                _ => current.push(ch),
            }
        }

        if n == 0 {
            break;
        }
    }

    if !current.is_empty() {
        lines.push(take_line(&mut current));
    }

    Ok(lines)
}

fn take_line(current: &mut Vec<char>) -> String {
    let line: String = current.drain(..).collect();
    line.replace('\t', "    ")
}

/// Streaming byte -> char decoder for the encodings `FilePreview` reads.
struct Decoder {
    kind: Encoding,
    pending: Vec<u8>,
    at_start: bool,
}

#[derive(Clone, Copy)]
enum Encoding {
    Utf8,
    Utf16Le,
    Utf16Be,
}

impl Decoder {
    fn new(encoding: &str) -> Self {
        let kind = match encoding {
            "UTF-16 LE" => Encoding::Utf16Le,
            "UTF-16 BE" => Encoding::Utf16Be,
            _ => Encoding::Utf8,
        };

        Self {
            kind,
            pending: Vec::new(),
            at_start: true,
        }
    }

    fn decode(&mut self, bytes: &[u8]) -> Vec<char> {
        self.pending.extend_from_slice(bytes);
        self.skip_bom();

        match self.kind {
            Encoding::Utf8 => {
                // Keep an incomplete trailing sequence for the next chunk
                let complete = utf8_complete_prefix(&self.pending);
                let text: String = String::from_utf8_lossy(&self.pending[..complete]).into_owned();
                self.pending.drain(..complete);
                text.chars().collect()
            }
            Encoding::Utf16Le | Encoding::Utf16Be => self.decode_utf16(false),
        }
    }

    fn finish(&mut self) -> Vec<char> {
        self.skip_bom();

        match self.kind {
            Encoding::Utf8 => {
                let text = String::from_utf8_lossy(&self.pending).into_owned();
                self.pending.clear();
                text.chars().collect()
            }
            Encoding::Utf16Le | Encoding::Utf16Be => self.decode_utf16(true),
        }
    }

    /// StreamReader drops a leading BOM matching its encoding.
    fn skip_bom(&mut self) {
        if !self.at_start {
            return;
        }

        let bom: &[u8] = match self.kind {
            Encoding::Utf8 => &[0xEF, 0xBB, 0xBF],
            Encoding::Utf16Le => &[0xFF, 0xFE],
            Encoding::Utf16Be => &[0xFE, 0xFF],
        };

        if self.pending.len() < bom.len() && bom.starts_with(&self.pending) {
            return; // need more bytes to decide
        }

        if self.pending.starts_with(bom) {
            self.pending.drain(..bom.len());
        }

        self.at_start = false;
    }

    fn decode_utf16(&mut self, last: bool) -> Vec<char> {
        let big_endian = matches!(self.kind, Encoding::Utf16Be);
        let mut usable = self.pending.len() & !1;

        // Hold back a trailing high surrogate until its pair arrives
        if !last && usable >= 2 {
            let pair = [self.pending[usable - 2], self.pending[usable - 1]];
            let unit = if big_endian { u16::from_be_bytes(pair) } else { u16::from_le_bytes(pair) };

            if (0xD800..0xDC00).contains(&unit) {
                usable -= 2;
            }
        }

        let units: Vec<u16> = self.pending[..usable]
            .as_chunks::<2>()
            .0
            .iter()
            .map(|&pair| if big_endian { u16::from_be_bytes(pair) } else { u16::from_le_bytes(pair) })
            .collect();
        self.pending.drain(..usable);

        let mut chars: Vec<char> =
            char::decode_utf16(units).map(|unit| unit.unwrap_or(char::REPLACEMENT_CHARACTER)).collect();

        // An odd trailing byte at EOF decodes to U+FFFD
        if last && !self.pending.is_empty() {
            self.pending.clear();
            chars.push(char::REPLACEMENT_CHARACTER);
        }

        chars
    }
}

/// Length of the prefix of `bytes` that does not end inside a possibly
/// incomplete UTF-8 sequence (a truncated sequence waits for more bytes).
fn utf8_complete_prefix(bytes: &[u8]) -> usize {
    // Look back at most 3 bytes for a lead byte whose sequence is cut off
    for back in 1..=bytes.len().min(3) {
        let byte = bytes[bytes.len() - back];

        if byte & 0xC0 == 0x80 {
            continue; // continuation byte
        }

        let needed = match byte {
            0xC2..=0xDF => 2,
            0xE0..=0xEF => 3,
            0xF0..=0xF4 => 4,
            _ => 1,
        };

        return if needed > back { bytes.len() - back } else { bytes.len() };
    }

    bytes.len()
}

#[cfg(test)]
mod tests {
    use super::{detect_bomless_utf16, extension, utf8_complete_prefix};

    #[test]
    fn extension_matches_dotnet() {
        assert_eq!(extension("a.TXT"), ".TXT");
        assert_eq!(extension("noext"), "");
        assert_eq!(extension("dir.d/file"), "");
    }

    #[test]
    fn bomless_utf16_needs_eight_bytes() {
        assert_eq!(detect_bomless_utf16(b"a\0b\0"), None);
        assert_eq!(detect_bomless_utf16(b"a\0b\0c\0d\0"), Some("UTF-16 LE"));
        assert_eq!(detect_bomless_utf16(b"\0a\0b\0c\0d"), Some("UTF-16 BE"));
    }

    #[test]
    fn utf8_prefix_holds_back_truncated_sequences() {
        assert_eq!(utf8_complete_prefix(b"ab\xe2\x82"), 2);
        assert_eq!(utf8_complete_prefix(b"ab\xe2\x82\xac"), 5);
        assert_eq!(utf8_complete_prefix(b"ab\xff"), 3);
    }

    #[test]
    fn file_type_labels_match_csharp_theories() {
        for (extension, label) in [
            (".cs", "C#"),
            (".py", "Python"),
            (".json", "JSON"),
            (".md", "Markdown"),
            (".ts", "TypeScript"),
            (".go", "Go"),
            (".rs", "Rust"),
            (".html", "HTML"),
            (".yaml", "YAML"),
            (".yml", "YAML"),
            (".sh", "Shell"),
            (".ps1", "PowerShell"),
        ] {
            assert_eq!(super::get_file_type_label(&format!("file{extension}")), Some(label), "{extension}");
        }

        for extension in [".tmp", ".xyz", ".unknown"] {
            assert_eq!(super::get_file_type_label(&format!("file{extension}")), None, "{extension}");
        }

        for (name, label) in [("Dockerfile", "Docker"), ("Makefile", "Makefile"), ("Jenkinsfile", "Jenkinsfile")] {
            assert_eq!(super::get_file_type_label(name), Some(label), "{name}");
        }
    }

    #[cfg(windows)]
    #[test]
    fn a_locked_file_previews_as_a_placeholder() {
        use std::os::windows::fs::OpenOptionsExt;

        let dir = std::env::temp_dir().join(format!("wade-preview-locked-{}", std::process::id()));
        let _ = std::fs::remove_dir_all(&dir);
        std::fs::create_dir_all(&dir).unwrap();
        let file = dir.join("locked.txt");
        std::fs::write(&file, b"hello").unwrap();
        let lock = std::fs::OpenOptions::new().read(true).share_mode(0).open(&file).unwrap();

        let (lines, metadata) = super::get_preview_lines(&file.to_string_lossy());
        drop(lock);
        let _ = std::fs::remove_dir_all(&dir);
        assert_eq!(lines.len(), 1, "{lines:?}");
        assert!(metadata.placeholder_message.is_some(), "{lines:?}");
        assert_ne!(lines[0], "hello");
    }
}
