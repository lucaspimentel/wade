//! Port of `src/Wade/LnkParser`: the parts of a Windows Shell Link (.lnk)
//! that `ShortcutMetadataProvider` shows. The header, LinkTargetIDList,
//! LinkInfo and StringData are parsed with the C# reader's semantics
//! (`BinaryReader` over a seekable file: reads past the end fail, seeks
//! past the end don't, short `ReadBytes` return what is there).
//!
//! ExtraData blocks are not parsed: the C# parser catches every error
//! inside `ExtraDataBlock.ParseAll` and nothing reads the blocks, so they
//! never change the result.

use std::fs::File;
use std::io::{self, BufReader, Seek, SeekFrom};

const HEADER_SIZE: u32 = 0x4c;
/// `00021401-0000-0000-C000-000000000046` in .NET's mixed-endian byte order.
const LINK_CLSID: [u8; 16] =
    [0x01, 0x14, 0x02, 0x00, 0x00, 0x00, 0x00, 0x00, 0xc0, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x46];

const HAS_LINK_TARGET_ID_LIST: u32 = 0x01;
const HAS_LINK_INFO: u32 = 0x02;
const HAS_NAME: u32 = 0x04;
const HAS_RELATIVE_PATH: u32 = 0x08;
const HAS_WORKING_DIR: u32 = 0x10;
const HAS_ARGUMENTS: u32 = 0x20;
const HAS_ICON_LOCATION: u32 = 0x40;
const IS_UNICODE: u32 = 0x80;

const VOLUME_ID_AND_LOCAL_BASE_PATH: u32 = 0x01;

/// `ShowCommand.Normal`.
pub const SHOW_NORMAL: u32 = 1;

/// Any failure: the C# provider catches everything and shows nothing.
#[derive(Debug)]
pub struct LnkError;

impl From<io::Error> for LnkError {
    fn from(_: io::Error) -> Self {
        Self
    }
}

type Result<T> = std::result::Result<T, LnkError>;

/// `BinaryReader` over a `FileStream`.
struct Reader {
    file: BufReader<File>,
    position: u64,
}

impl Reader {
    fn open(path: &str) -> Result<Self> {
        Ok(Self {
            file: BufReader::new(File::open(path)?),
            position: 0,
        })
    }

    fn set_position(&mut self, position: u64) -> Result<()> {
        self.position = position;
        self.file.seek(SeekFrom::Start(position))?;
        Ok(())
    }

    /// `ReadBytes(count)`: up to `count` bytes.
    fn read_bytes(&mut self, count: usize) -> Result<Vec<u8>> {
        let mut buf = vec![0u8; count];
        let read = crate::fs::gzip::read_fully(&mut self.file, &mut buf)?;
        buf.truncate(read);
        self.position += read as u64;
        Ok(buf)
    }

    /// Exactly `N` bytes, else `EndOfStreamException`.
    fn read_array<const N: usize>(&mut self) -> Result<[u8; N]> {
        let bytes = self.read_bytes(N)?;
        bytes.try_into().map_err(|_| LnkError)
    }

    fn read_u8(&mut self) -> Result<u8> {
        Ok(self.read_array::<1>()?[0])
    }

    fn read_u16(&mut self) -> Result<u16> {
        Ok(u16::from_le_bytes(self.read_array()?))
    }

    fn read_u32(&mut self) -> Result<u32> {
        Ok(u32::from_le_bytes(self.read_array()?))
    }

    /// ANSI (`Encoding.Default`, UTF-8 on .NET Core) up to a NUL.
    fn read_null_terminated(&mut self) -> Result<String> {
        let mut bytes = Vec::new();
        loop {
            let b = self.read_u8()?;
            if b == 0 {
                break;
            }
            bytes.push(b);
        }

        Ok(String::from_utf8_lossy(&bytes).into_owned())
    }

    /// UTF-16LE up to a 0x0000 unit.
    fn read_null_terminated_unicode(&mut self) -> Result<String> {
        let mut bytes = Vec::new();
        loop {
            let b1 = self.read_u8()?;
            let b2 = self.read_u8()?;
            if b1 == 0 && b2 == 0 {
                break;
            }
            bytes.extend([b1, b2]);
        }

        Ok(decode_utf16le(&bytes))
    }
}

/// `Encoding.Unicode.GetString`: lone surrogates and an odd trailing byte
/// become U+FFFD.
#[must_use]
pub fn decode_utf16le(bytes: &[u8]) -> String {
    let units: Vec<u16> = bytes.chunks_exact(2).map(|pair| u16::from_le_bytes([pair[0], pair[1]])).collect();
    let mut text = String::from_utf16_lossy(&units);
    if bytes.len() % 2 == 1 {
        text.push('\u{FFFD}');
    }
    text
}

/// Port of `ShellLinkHeader` (the fields the provider reads).
#[derive(Clone, Debug, PartialEq, Eq)]
pub struct ShellLinkHeader {
    pub link_flags: u32,
    pub show_command: u32,
    pub hot_key: u16,
}

fn parse_header(reader: &mut Reader) -> Result<ShellLinkHeader> {
    if reader.read_u32()? != HEADER_SIZE {
        return Err(LnkError);
    }

    if reader.read_bytes(16)? != LINK_CLSID {
        return Err(LnkError);
    }

    let link_flags = reader.read_u32()?;
    reader.read_u32()?; // file attributes
    reader.read_array::<24>()?; // creation, access, write times
    reader.read_u32()?; // file size
    reader.read_u32()?; // icon index
    let show_command = reader.read_u32()?;
    let hot_key = reader.read_u16()?;
    reader.read_u16()?; // reserved
    reader.read_u32()?;
    reader.read_u32()?;

    Ok(ShellLinkHeader { link_flags, show_command, hot_key })
}

/// Port of `LinkTargetIdList.Parse` reduced to what `GetLaunchUri` needs:
/// the item walk (which can fail) and the raw-bytes string scan.
fn parse_id_list(reader: &mut Reader) -> Result<Vec<String>> {
    let id_list_size = reader.read_u16()?;
    if id_list_size == 0 {
        return Ok(Vec::new());
    }

    let start = reader.position;
    let end = start + u64::from(id_list_size);

    while reader.position < end {
        let item_size = reader.read_u16()?;
        if item_size == 0 {
            break;
        }

        // ReadBytes(itemIdSize - 2) throws for a size of 1
        let data_size = usize::from(item_size).checked_sub(2).ok_or(LnkError)?;
        reader.read_bytes(data_size)?;
    }

    reader.set_position(start)?;
    let raw = reader.read_bytes(usize::from(id_list_size))?;
    Ok(extract_string_list(&raw))
}

/// Port of `LinkTargetIdList.ExtractStringList`: UTF-16 then ASCII runs.
/// Lengths include the terminator, so the strings end with a NUL char.
fn extract_string_list(data: &[u8]) -> Vec<String> {
    let mut strings: Vec<String> = Vec::new();

    let mut i = 0;
    while i + 8 < data.len() {
        if let Some(length) = likely_unicode_string(data, i) {
            let text = decode_utf16le(&data[i..i + length]);
            if !text.trim().is_empty() {
                strings.push(text);
                i += length;
                continue;
            }
        }
        i += 1;
    }

    let mut i = 0;
    while i + 4 < data.len() {
        if let Some(length) = likely_ascii_string(data, i) {
            let text: String = data[i..i + length].iter().map(|&b| char::from(b)).collect();
            if !text.trim().is_empty() && !strings.contains(&text) {
                strings.push(text);
                i += length;
                continue;
            }
        }
        i += 1;
    }

    strings
}

/// `string.IsNullOrWhiteSpace` treats NUL as non-white; `str::trim` agrees
/// for the characters these scans produce.
fn likely_unicode_string(data: &[u8], offset: usize) -> Option<usize> {
    let mut length = 0;
    let mut null_terminators = 0;
    let mut char_count = 0;
    let mut i = offset;

    while i + 1 < data.len() {
        let (b1, b2) = (data[i], data[i + 1]);

        if b1 == 0 && b2 == 0 {
            null_terminators += 1;
            length = i - offset + 2;
            break;
        }

        if b2 == 0 && (0x20..=0x7e).contains(&b1) {
            char_count += 1;
        } else {
            return None;
        }

        if char_count >= 4 && i - offset > 20 {
            // Look ahead for the terminator
            let mut j = i + 2;
            while j < (data.len() - 1).min(i + 200) {
                if data[j] == 0 && data[j + 1] == 0 {
                    return Some(j - offset + 2);
                }
                j += 2;
            }
            return None;
        }

        i += 2;
    }

    (null_terminators > 0 && char_count >= 4).then_some(length)
}

fn likely_ascii_string(data: &[u8], offset: usize) -> Option<usize> {
    let mut char_count = 0;

    for (i, &b) in data.iter().enumerate().take((offset + 200).min(data.len())).skip(offset) {
        if b == 0 {
            return (char_count >= 4).then_some(i - offset + 1);
        }

        if (0x20..=0x7e).contains(&b) {
            char_count += 1;
        } else {
            break;
        }
    }

    None
}

/// Port of `LinkInfo` (the fields the provider reads).
#[derive(Clone, Debug, Default, PartialEq, Eq)]
pub struct LinkInfo {
    pub volume_label: Option<String>,
    pub local_base_path: Option<String>,
    pub common_path_suffix: Option<String>,
    pub local_base_path_unicode: Option<String>,
    pub common_path_suffix_unicode: Option<String>,
}

impl LinkInfo {
    /// Port of `GetFullPath`.
    #[must_use]
    pub fn full_path(&self) -> String {
        let base = self.local_base_path_unicode.as_deref().or(self.local_base_path.as_deref()).unwrap_or("");
        let suffix = self.common_path_suffix_unicode.as_deref().or(self.common_path_suffix.as_deref()).unwrap_or("");
        format!("{base}{suffix}")
    }
}

/// Port of `VolumeID.Parse`'s label (Unicode at 0x14, else ANSI at the
/// given offset).
fn parse_volume_label(reader: &mut Reader) -> Result<String> {
    let start = reader.position;
    let size = reader.read_u32()?;
    reader.read_u32()?; // drive type
    reader.read_u32()?; // serial number
    let label_offset = reader.read_u32()?;

    let mut label = String::new();
    if label_offset == 0x14 && size > 0x14 {
        reader.set_position(start + u64::from(label_offset))?;
        label = reader.read_null_terminated_unicode()?;
    } else if label_offset < size {
        reader.set_position(start + u64::from(label_offset))?;
        label = reader.read_null_terminated()?;
    }

    reader.set_position(start + u64::from(size))?;
    Ok(label)
}

fn parse_link_info(reader: &mut Reader) -> Result<LinkInfo> {
    let start = reader.position;
    let size = reader.read_u32()?;
    let header_size = reader.read_u32()?;
    let flags = reader.read_u32()?;
    let volume_id_offset = reader.read_u32()?;
    let local_base_path_offset = reader.read_u32()?;
    reader.read_u32()?; // common network relative link offset
    let common_path_suffix_offset = reader.read_u32()?;

    let (mut local_base_path_offset_unicode, mut common_path_suffix_offset_unicode) = (0, 0);
    if header_size >= 0x24 {
        local_base_path_offset_unicode = reader.read_u32()?;
        common_path_suffix_offset_unicode = reader.read_u32()?;
    }

    let mut info = LinkInfo::default();
    let at = |offset: u32| start + u64::from(offset);

    if flags & VOLUME_ID_AND_LOCAL_BASE_PATH != 0 {
        if volume_id_offset > 0 {
            reader.set_position(at(volume_id_offset))?;
            info.volume_label = Some(parse_volume_label(reader)?);
        }

        if local_base_path_offset > 0 {
            reader.set_position(at(local_base_path_offset))?;
            info.local_base_path = Some(reader.read_null_terminated()?);
        }

        if local_base_path_offset_unicode > 0 {
            reader.set_position(at(local_base_path_offset_unicode))?;
            info.local_base_path_unicode = Some(reader.read_null_terminated_unicode()?);
        }
    }

    if common_path_suffix_offset > 0 {
        reader.set_position(at(common_path_suffix_offset))?;
        info.common_path_suffix = Some(reader.read_null_terminated()?);
    }

    if common_path_suffix_offset_unicode > 0 {
        reader.set_position(at(common_path_suffix_offset_unicode))?;
        info.common_path_suffix_unicode = Some(reader.read_null_terminated_unicode()?);
    }

    reader.set_position(at(size))?;
    Ok(info)
}

/// Port of `StringData`.
#[derive(Clone, Debug, Default, PartialEq, Eq)]
pub struct StringData {
    pub name: Option<String>,
    pub relative_path: Option<String>,
    pub working_dir: Option<String>,
    pub command_line_arguments: Option<String>,
    pub icon_location: Option<String>,
}

fn parse_string_data(reader: &mut Reader, flags: u32) -> Result<StringData> {
    let unicode = flags & IS_UNICODE != 0;
    let mut field = |flag: u32| -> Result<Option<String>> {
        if flags & flag == 0 {
            return Ok(None);
        }

        let count = usize::from(reader.read_u16()?);
        Ok(Some(if unicode {
            decode_utf16le(&reader.read_bytes(count * 2)?)
        } else {
            String::from_utf8_lossy(&reader.read_bytes(count)?).into_owned()
        }))
    };

    Ok(StringData {
        name: field(HAS_NAME)?,
        relative_path: field(HAS_RELATIVE_PATH)?,
        working_dir: field(HAS_WORKING_DIR)?,
        command_line_arguments: field(HAS_ARGUMENTS)?,
        icon_location: field(HAS_ICON_LOCATION)?,
    })
}

/// Port of `LnkFile`.
#[derive(Clone, Debug, PartialEq, Eq)]
pub struct LnkFile {
    pub header: ShellLinkHeader,
    /// `LinkTargetIdList.ExtractedStringList`, when the list is present.
    pub id_list_strings: Option<Vec<String>>,
    pub link_info: Option<LinkInfo>,
    pub string_data: StringData,
}

impl LnkFile {
    /// Port of `LnkFile.Parse`.
    pub fn parse(path: &str) -> Result<Self> {
        let mut reader = Reader::open(path)?;
        let header = parse_header(&mut reader)?;

        let id_list_strings = if header.link_flags & HAS_LINK_TARGET_ID_LIST != 0 {
            Some(parse_id_list(&mut reader)?)
        } else {
            None
        };

        let link_info = if header.link_flags & HAS_LINK_INFO != 0 {
            Some(parse_link_info(&mut reader)?)
        } else {
            None
        };

        let string_data = parse_string_data(&mut reader, header.link_flags)?;

        Ok(Self {
            header,
            id_list_strings,
            link_info,
            string_data,
        })
    }

    /// Port of `GetTargetPath`: the LinkInfo path, else the relative path.
    #[must_use]
    pub fn target_path(&self) -> Option<String> {
        if let Some(full_path) = self.link_info.as_ref().map(LinkInfo::full_path).filter(|p| !p.is_empty()) {
            return Some(full_path);
        }

        self.string_data.relative_path.clone()
    }

    /// Port of `GetLaunchUri`: the first msgamelaunch://, ms-xbl-* or
    /// other "://" string in the ID list.
    #[must_use]
    pub fn launch_uri(&self) -> Option<String> {
        self.id_list_strings
            .as_ref()?
            .iter()
            .find(|s| {
                starts_with_ignore_case(s, "msgamelaunch://")
                    || starts_with_ignore_case(s, "ms-xbl-")
                    || s.contains("://")
            })
            .cloned()
    }
}

fn starts_with_ignore_case(text: &str, prefix: &str) -> bool {
    text.len() >= prefix.len()
        && text.is_char_boundary(prefix.len())
        && text[..prefix.len()].eq_ignore_ascii_case(prefix)
}

/// Port of `ShowCommand.ToString()`: names for defined values, else the
/// number.
#[must_use]
pub fn show_command_name(show_command: u32) -> String {
    match show_command {
        1 => "Normal".to_string(),
        3 => "Maximized".to_string(),
        7 => "MinNoActive".to_string(),
        other => other.to_string(),
    }
}

/// Port of `HotKeyHelper.Decode`.
#[must_use]
pub fn decode_hot_key(hot_key: u16) -> String {
    if hot_key == 0 {
        return "UNSET - UNSET {0x0000}".to_string();
    }

    let [key, modifier] = hot_key.to_le_bytes();
    let key_name = match key {
        0x30..=0x39 | 0x41..=0x5a => char::from(key).to_string(),
        0x70..=0x87 => format!("F{}", key - 0x6f),
        0x90 => "NUM LOCK".to_string(),
        0x91 => "SCROLL LOCK".to_string(),
        _ => format!("Unknown (0x{key:02X})"),
    };

    let modifiers: Vec<&str> = [(0x01, "SHIFT"), (0x02, "CTRL"), (0x04, "ALT")]
        .iter()
        .filter(|(bit, _)| modifier & bit != 0)
        .map(|(_, name)| *name)
        .collect();
    let modifier_name = if modifiers.is_empty() { "UNSET".to_string() } else { modifiers.join(" + ") };

    format!("{modifier_name} - {key_name} {{0x{hot_key:04X}}}")
}

#[cfg(test)]
mod tests {
    //! The shortcut golden covers whole files; these pin the helpers.

    use super::{decode_hot_key, decode_utf16le, extract_string_list, show_command_name};

    #[test]
    fn utf16_decoding_replaces_odd_bytes_and_lone_surrogates() {
        assert_eq!(decode_utf16le(b"a\0b\0"), "ab");
        assert_eq!(decode_utf16le(b"a\0b"), "a\u{FFFD}");
        assert_eq!(decode_utf16le(&[0x00, 0xd8, 0x41, 0x00]), "\u{FFFD}A");
    }

    #[test]
    fn string_scan_keeps_terminators_and_skips_duplicates() {
        let mut data = b"xx".to_vec();
        data.extend("https://example.com/x".encode_utf16().flat_map(u16::to_le_bytes));
        data.extend([0, 0]);
        data.extend(b"ascii-run\0");

        let strings = extract_string_list(&data);
        assert_eq!(strings, ["https://example.com/x\0", "ascii-run\0"]);
    }

    #[test]
    fn show_command_and_hot_key_names() {
        assert_eq!(show_command_name(3), "Maximized");
        assert_eq!(show_command_name(7), "MinNoActive");
        assert_eq!(show_command_name(2), "2");
        assert_eq!(decode_hot_key(0x024E), "CTRL - N {0x024E}");
        assert_eq!(decode_hot_key(0x0080), "UNSET - F17 {0x0080}");
    }
}
