//! A minimal port of the parts of `System.Reflection.PortableExecutable`
//! (`PEReader`/`PEHeaders`) and `System.Reflection.Metadata`
//! (`MetadataReader`) that `ExecutableMetadataProvider` uses: COFF and
//! optional headers, section mapping, the CLI header, and the Assembly,
//! AssemblyRef, TypeRef, MemberRef and CustomAttribute tables.
//!
//! Errors mirror the .NET exception split: `PeError::BadImage` is what
//! `PEHeaders` or a metadata read throws (the provider shows nothing),
//! while a malformed metadata root only drops the .NET sections, as C#
//! catches `BadImageFormatException` from `GetMetadataReader`.

use std::fs::File;
use std::io::{Read, Seek, SeekFrom};

/// `BadImageFormatException` (or an I/O failure) while reading.
#[derive(Debug, PartialEq, Eq)]
pub struct BadImage;

type Result<T> = std::result::Result<T, BadImage>;

const DOS_SIGNATURE: u16 = 0x5A4D;
const PE_SIGNATURE: u32 = 0x0000_4550;
const PE32_MAGIC: u16 = 0x10B;
const PE32_PLUS_MAGIC: u16 = 0x20B;
const COR_HEADER_SIZE: i64 = 72;

/// Random access over the image (`PEBinaryReader` bounds checks: reading
/// past the end is a bad image).
pub struct Image {
    file: File,
    len: i64,
}

impl Image {
    pub fn open(path: &str) -> std::io::Result<Self> {
        let file = File::open(path)?;
        let len = i64::try_from(file.metadata()?.len()).unwrap_or(i64::MAX);
        Ok(Self { file, len })
    }

    fn read(&mut self, offset: i64, count: usize) -> Result<Vec<u8>> {
        let end = offset.checked_add(i64::try_from(count).map_err(|_| BadImage)?).ok_or(BadImage)?;

        if offset < 0 || end > self.len {
            return Err(BadImage);
        }

        let mut buf = vec![0u8; count];
        self.file.seek(SeekFrom::Start(offset as u64)).map_err(|_| BadImage)?;
        self.file.read_exact(&mut buf).map_err(|_| BadImage)?;
        Ok(buf)
    }
}

fn u16_at(b: &[u8], at: usize) -> u16 {
    u16::from_le_bytes([b[at], b[at + 1]])
}

fn u32_at(b: &[u8], at: usize) -> u32 {
    u32::from_le_bytes([b[at], b[at + 1], b[at + 2], b[at + 3]])
}

fn i32_at(b: &[u8], at: usize) -> i64 {
    i64::from(u32_at(b, at) as i32)
}

#[derive(Clone, Copy, Debug)]
struct Section {
    name: [u8; 8],
    virtual_size: i64,
    virtual_address: i64,
    size_of_raw_data: i64,
    pointer_to_raw_data: i64,
}

#[derive(Clone, Copy, Debug, Default)]
struct Directory {
    rva: i64,
    size: i64,
}

/// The `PEHeader` fields the provider reads.
#[derive(Clone, Copy, Debug, PartialEq, Eq)]
pub struct OptionalHeader {
    pub is_pe32_plus: bool,
    pub subsystem: u16,
}

/// Port of the `PEHeaders` facts the provider reads.
#[derive(Clone, Debug)]
pub struct PeHeaders {
    pub machine: u16,
    pub characteristics: u16,
    /// `CoffHeader.TimeDateStamp` (a signed int in .NET).
    pub time_date_stamp: i32,
    pub pe_header: Option<OptionalHeader>,
    /// Metadata block (file offset, size); size 0 means no metadata.
    pub metadata_start: i64,
    pub metadata_size: i64,
}

impl PeHeaders {
    /// `PEReader.HasMetadata`.
    #[must_use]
    pub const fn has_metadata(&self) -> bool {
        self.metadata_size > 0
    }
}

/// Port of the `PEHeaders` constructor over a file image.
pub fn read_headers(image: &mut Image) -> Result<PeHeaders> {
    let size = image.len;
    let dos = image.read(0, 2)?;
    let dos_signature = u16_at(&dos, 0);
    let mut offset = 0i64;

    let is_coff_only = if dos_signature == DOS_SIGNATURE {
        false
    } else if dos_signature != 0 || u16_at(&image.read(2, 2)?, 0) != 0xFFFF {
        true
    } else {
        // A COFF file with an anonymous object header
        return Err(BadImage);
    };

    if !is_coff_only {
        let nt_offset = i32_at(&image.read(0x3C, 4)?, 0);

        if nt_offset < 0 || nt_offset > size {
            return Err(BadImage);
        }

        if u32_at(&image.read(nt_offset, 4)?, 0) != PE_SIGNATURE {
            return Err(BadImage);
        }

        offset = nt_offset + 4;
    }

    let coff = image.read(offset, 20)?;
    offset += 20;
    let machine = u16_at(&coff, 0);
    let number_of_sections = i64::from(u16_at(&coff, 2) as i16);
    let time_date_stamp = u32_at(&coff, 4) as i32;
    let characteristics = u16_at(&coff, 18);

    let mut pe_header = None;
    let mut directories = [Directory::default(); 16];

    if !is_coff_only {
        let magic = u16_at(&image.read(offset, 2)?, 0);
        let fixed = match magic {
            PE32_MAGIC => 96,
            PE32_PLUS_MAGIC => 112,
            _ => return Err(BadImage),
        };
        // .NET reads every field plus all 16 data directories
        let header = image.read(offset, fixed + 128)?;
        offset += i64::try_from(fixed + 128).unwrap_or(0);

        for (i, directory) in directories.iter_mut().enumerate() {
            let at = fixed + i * 8;
            *directory = Directory { rva: i32_at(&header, at), size: i32_at(&header, at + 4) };
        }

        pe_header = Some(OptionalHeader { is_pe32_plus: magic == PE32_PLUS_MAGIC, subsystem: u16_at(&header, 68) });
    }

    if number_of_sections < 0 {
        return Err(BadImage);
    }

    let mut sections = Vec::new();

    for _ in 0..number_of_sections {
        let raw = image.read(offset, 40)?;
        offset += 40;
        let mut name = [0u8; 8];
        name.copy_from_slice(&raw[..8]);
        sections.push(Section {
            name,
            virtual_size: i32_at(&raw, 8),
            virtual_address: i32_at(&raw, 12),
            size_of_raw_data: i32_at(&raw, 16),
            pointer_to_raw_data: i32_at(&raw, 20),
        });
    }

    let (metadata_start, metadata_size) = if is_coff_only {
        let Some(cormeta) = sections.iter().find(|s| s.name == *b".cormeta") else {
            return Ok(PeHeaders { machine, characteristics, time_date_stamp, pe_header, metadata_start: -1, metadata_size: 0 });
        };
        (cormeta.pointer_to_raw_data, cormeta.size_of_raw_data)
    } else {
        let Some(cor_offset) = directory_offset(&sections, directories[14])? else {
            return Ok(PeHeaders { machine, characteristics, time_date_stamp, pe_header, metadata_start: 0, metadata_size: 0 });
        };

        if directories[14].size < COR_HEADER_SIZE {
            return Err(BadImage);
        }

        let cor = image.read(cor_offset, 72)?;
        let metadata = Directory { rva: i32_at(&cor, 8), size: i32_at(&cor, 12) };
        (directory_offset(&sections, metadata)?.ok_or(BadImage)?, metadata.size)
    };

    if metadata_start < 0 || metadata_start >= size || metadata_size <= 0 || metadata_start > size - metadata_size {
        return Err(BadImage);
    }

    Ok(PeHeaders { machine, characteristics, time_date_stamp, pe_header, metadata_start, metadata_size })
}

/// Port of `TryGetDirectoryOffset(canCrossSectionBoundary: false)`.
fn directory_offset(sections: &[Section], directory: Directory) -> Result<Option<i64>> {
    let Some(section) = sections.iter().find(|s| {
        directory.rva >= s.virtual_address && directory.rva < s.virtual_address + s.virtual_size
    }) else {
        return Ok(None);
    };

    let relative = directory.rva - section.virtual_address;

    if directory.size > section.virtual_size - relative {
        return Err(BadImage);
    }

    Ok(Some(section.pointer_to_raw_data + relative))
}

/// Reads the metadata block `PEReader.GetMetadata` would expose.
pub fn read_metadata_block(image: &mut Image, headers: &PeHeaders) -> Result<Vec<u8>> {
    image.read(headers.metadata_start, usize::try_from(headers.metadata_size).map_err(|_| BadImage)?)
}

// ── Metadata tables ──────────────────────────────────────────────────────

const TABLE_MODULE: usize = 0x00;
const TABLE_TYPE_REF: usize = 0x01;
const TABLE_TYPE_DEF: usize = 0x02;
const TABLE_FIELD: usize = 0x04;
const TABLE_METHOD_DEF: usize = 0x06;
const TABLE_PARAM: usize = 0x08;
const TABLE_INTERFACE_IMPL: usize = 0x09;
const TABLE_MEMBER_REF: usize = 0x0A;
const TABLE_CUSTOM_ATTRIBUTE: usize = 0x0C;
const TABLE_DECL_SECURITY: usize = 0x0E;
const TABLE_STAND_ALONE_SIG: usize = 0x11;
const TABLE_EVENT: usize = 0x14;
const TABLE_PROPERTY: usize = 0x17;
const TABLE_MODULE_REF: usize = 0x1A;
const TABLE_TYPE_SPEC: usize = 0x1B;
const TABLE_ASSEMBLY: usize = 0x20;
const TABLE_ASSEMBLY_REF: usize = 0x23;
const TABLE_FILE: usize = 0x26;
const TABLE_EXPORTED_TYPE: usize = 0x27;
const TABLE_MANIFEST_RESOURCE: usize = 0x28;
const TABLE_GENERIC_PARAM: usize = 0x2A;
const TABLE_METHOD_SPEC: usize = 0x2B;
const TABLE_GENERIC_PARAM_CONSTRAINT: usize = 0x2C;
const TABLE_COUNT: usize = 0x2D;

/// Coded index families (ECMA-335 II.24.2.6): tag bits and tables.
const TYPE_DEF_OR_REF: (u32, &[usize]) = (2, &[TABLE_TYPE_DEF, TABLE_TYPE_REF, TABLE_TYPE_SPEC]);
const HAS_CONSTANT: (u32, &[usize]) = (2, &[TABLE_FIELD, TABLE_PARAM, TABLE_PROPERTY]);
const HAS_CUSTOM_ATTRIBUTE: (u32, &[usize]) = (
    5,
    &[
        TABLE_METHOD_DEF,
        TABLE_FIELD,
        TABLE_TYPE_REF,
        TABLE_TYPE_DEF,
        TABLE_PARAM,
        TABLE_INTERFACE_IMPL,
        TABLE_MEMBER_REF,
        TABLE_MODULE,
        TABLE_DECL_SECURITY,
        TABLE_PROPERTY,
        TABLE_EVENT,
        TABLE_STAND_ALONE_SIG,
        TABLE_MODULE_REF,
        TABLE_TYPE_SPEC,
        TABLE_ASSEMBLY,
        TABLE_ASSEMBLY_REF,
        TABLE_FILE,
        TABLE_EXPORTED_TYPE,
        TABLE_MANIFEST_RESOURCE,
        TABLE_GENERIC_PARAM,
        TABLE_GENERIC_PARAM_CONSTRAINT,
        TABLE_METHOD_SPEC,
    ],
);
const HAS_FIELD_MARSHAL: (u32, &[usize]) = (1, &[TABLE_FIELD, TABLE_PARAM]);
const HAS_DECL_SECURITY: (u32, &[usize]) = (2, &[TABLE_TYPE_DEF, TABLE_METHOD_DEF, TABLE_ASSEMBLY]);
const MEMBER_REF_PARENT: (u32, &[usize]) =
    (3, &[TABLE_TYPE_DEF, TABLE_TYPE_REF, TABLE_MODULE_REF, TABLE_METHOD_DEF, TABLE_TYPE_SPEC]);
const HAS_SEMANTICS: (u32, &[usize]) = (1, &[TABLE_EVENT, TABLE_PROPERTY]);
const METHOD_DEF_OR_REF: (u32, &[usize]) = (1, &[TABLE_METHOD_DEF, TABLE_MEMBER_REF]);
const MEMBER_FORWARDED: (u32, &[usize]) = (1, &[TABLE_FIELD, TABLE_METHOD_DEF]);
const IMPLEMENTATION: (u32, &[usize]) = (2, &[TABLE_FILE, TABLE_ASSEMBLY_REF, TABLE_EXPORTED_TYPE]);
const CUSTOM_ATTRIBUTE_TYPE: (u32, &[usize]) = (3, &[TABLE_METHOD_DEF, TABLE_MEMBER_REF]);
const RESOLUTION_SCOPE: (u32, &[usize]) = (2, &[TABLE_MODULE, TABLE_MODULE_REF, TABLE_ASSEMBLY_REF, TABLE_TYPE_REF]);
const TYPE_OR_METHOD_DEF: (u32, &[usize]) = (1, &[TABLE_TYPE_DEF, TABLE_METHOD_DEF]);

#[derive(Clone, Copy)]
enum Col {
    U8x2,
    U16,
    U32,
    Str,
    Guid,
    Blob,
    Table(usize),
    Coded((u32, &'static [usize])),
}

/// Column schema of every table up to GenericParamConstraint.
fn schema(table: usize) -> &'static [Col] {
    use Col::{Blob, Coded, Guid, Str, Table, U16, U32, U8x2};

    match table {
        0x00 => &[U16, Str, Guid, Guid, Guid],
        0x01 => &[Coded(RESOLUTION_SCOPE), Str, Str],
        0x02 => &[U32, Str, Str, Coded(TYPE_DEF_OR_REF), Table(TABLE_FIELD), Table(TABLE_METHOD_DEF)],
        0x03 => &[Table(TABLE_FIELD)],
        0x04 => &[U16, Str, Blob],
        0x05 => &[Table(TABLE_METHOD_DEF)],
        0x06 => &[U32, U16, U16, Str, Blob, Table(TABLE_PARAM)],
        0x07 => &[Table(TABLE_PARAM)],
        0x08 => &[U16, U16, Str],
        0x09 => &[Table(TABLE_TYPE_DEF), Coded(TYPE_DEF_OR_REF)],
        0x0A => &[Coded(MEMBER_REF_PARENT), Str, Blob],
        0x0B => &[U8x2, Coded(HAS_CONSTANT), Blob],
        0x0C => &[Coded(HAS_CUSTOM_ATTRIBUTE), Coded(CUSTOM_ATTRIBUTE_TYPE), Blob],
        0x0D => &[Coded(HAS_FIELD_MARSHAL), Blob],
        0x0E => &[U16, Coded(HAS_DECL_SECURITY), Blob],
        0x0F => &[U16, U32, Table(TABLE_TYPE_DEF)],
        0x10 => &[U32, Table(TABLE_FIELD)],
        0x11 => &[Blob],
        0x12 => &[Table(TABLE_TYPE_DEF), Table(TABLE_EVENT)],
        0x13 => &[Table(TABLE_EVENT)],
        0x14 => &[U16, Str, Coded(TYPE_DEF_OR_REF)],
        0x15 => &[Table(TABLE_TYPE_DEF), Table(TABLE_PROPERTY)],
        0x16 => &[Table(TABLE_PROPERTY)],
        0x17 => &[U16, Str, Blob],
        0x18 => &[U16, Table(TABLE_METHOD_DEF), Coded(HAS_SEMANTICS)],
        0x19 => &[Table(TABLE_TYPE_DEF), Coded(METHOD_DEF_OR_REF), Coded(METHOD_DEF_OR_REF)],
        0x1A => &[Str],
        0x1B => &[Blob],
        0x1C => &[U16, Coded(MEMBER_FORWARDED), Str, Table(TABLE_MODULE_REF)],
        0x1D => &[U32, Table(TABLE_FIELD)],
        0x1E => &[U32, U32],
        0x1F => &[U32],
        0x20 => &[U32, U16, U16, U16, U16, U32, Blob, Str, Str],
        0x21 => &[U32],
        0x22 => &[U32, U32, U32],
        0x23 => &[U16, U16, U16, U16, U32, Blob, Str, Str, Blob],
        0x24 => &[U32, Table(TABLE_ASSEMBLY_REF)],
        0x25 => &[U32, U32, U32, Table(TABLE_ASSEMBLY_REF)],
        0x26 => &[U32, Str, Blob],
        0x27 => &[U32, U32, Str, Str, Coded(IMPLEMENTATION)],
        0x28 => &[U32, U32, Str, Coded(IMPLEMENTATION)],
        0x29 => &[Table(TABLE_TYPE_DEF), Table(TABLE_TYPE_DEF)],
        0x2A => &[U16, U16, Coded(TYPE_OR_METHOD_DEF), Str],
        0x2B => &[Coded(METHOD_DEF_OR_REF), Blob],
        0x2C => &[Table(TABLE_GENERIC_PARAM), Coded(TYPE_DEF_OR_REF)],
        _ => &[],
    }
}

/// Port of the `MetadataReader` subset: heaps plus the tables stream.
pub struct MetadataReader<'a> {
    strings: &'a [u8],
    blobs: &'a [u8],
    rows: [u32; TABLE_COUNT],
    /// Per table: (offset into the tables stream, row size, column widths).
    layout: Vec<(usize, usize, Vec<usize>)>,
    tables: &'a [u8],
}

fn read_u32(data: &[u8], at: usize) -> Result<u32> {
    data.get(at..at + 4).map(|b| u32_at(b, 0)).ok_or(BadImage)
}

fn read_u16(data: &[u8], at: usize) -> Result<u16> {
    data.get(at..at + 2).map(|b| u16_at(b, 0)).ok_or(BadImage)
}

impl<'a> MetadataReader<'a> {
    /// Port of the `MetadataReader` constructor's validation.
    pub fn new(metadata: &'a [u8]) -> Result<Self> {
        if read_u32(metadata, 0)? != 0x424A_5342 {
            return Err(BadImage);
        }

        let version_length = read_u32(metadata, 12)? as usize;
        let mut at = 16usize.checked_add(version_length).ok_or(BadImage)?;
        at += 2; // flags
        let stream_count = read_u16(metadata, at)?;
        at += 2;

        let mut strings: &[u8] = &[];
        let mut blobs: &[u8] = &[];
        let mut tables: Option<&[u8]> = None;

        for _ in 0..stream_count {
            let offset = read_u32(metadata, at)? as usize;
            let size = read_u32(metadata, at + 4)? as usize;
            at += 8;
            let name_len = metadata.get(at..).ok_or(BadImage)?.iter().take(32).position(|&b| b == 0).ok_or(BadImage)?;
            let name = &metadata[at..at + name_len];
            at += (name_len + 4) & !3;
            let data = metadata.get(offset..offset.checked_add(size).ok_or(BadImage)?).ok_or(BadImage)?;

            match name {
                b"#Strings" => strings = data,
                b"#Blob" => blobs = data,
                b"#~" | b"#-" => tables = Some(data),
                _ => {}
            }
        }

        let tables = tables.ok_or(BadImage)?;
        let heap_sizes = *tables.get(6).ok_or(BadImage)?;
        let valid = tables.get(8..16).map(|b| u64::from_le_bytes(b.try_into().unwrap_or_default())).ok_or(BadImage)?;

        if valid >> TABLE_COUNT != 0 {
            return Err(BadImage);
        }

        let mut rows = [0u32; TABLE_COUNT];
        let mut at = 24usize;

        for (table, count) in rows.iter_mut().enumerate() {
            if valid & (1 << table) != 0 {
                *count = read_u32(tables, at)?;
                at += 4;
            }
        }

        if heap_sizes & 0x40 != 0 {
            at += 4; // extra data
        }

        let heap = |bit: u8| if heap_sizes & bit != 0 { 4 } else { 2 };
        let width = |col: Col| -> usize {
            match col {
                Col::U8x2 | Col::U16 => 2,
                Col::U32 => 4,
                Col::Str => heap(0x01),
                Col::Guid => heap(0x02),
                Col::Blob => heap(0x04),
                Col::Table(t) => if rows[t] <= 0xFFFF { 2 } else { 4 },
                Col::Coded((bits, targets)) => {
                    let limit = 1u32 << (16 - bits);
                    if targets.iter().all(|&t| rows[t] < limit) { 2 } else { 4 }
                }
            }
        };

        let mut layout = Vec::with_capacity(TABLE_COUNT);

        for (table, &count) in rows.iter().enumerate() {
            let widths: Vec<usize> = schema(table).iter().map(|&col| width(col)).collect();
            let row_size: usize = widths.iter().sum();
            layout.push((at, row_size, widths));
            at = at.checked_add(row_size.checked_mul(count as usize).ok_or(BadImage)?).ok_or(BadImage)?;
        }

        if at > tables.len() {
            return Err(BadImage);
        }

        Ok(Self { strings, blobs, rows, layout, tables })
    }

    #[must_use]
    pub const fn row_count(&self, table: usize) -> u32 {
        self.rows[table]
    }

    /// Column `col` of 1-based `row`.
    fn cell(&self, table: usize, row: u32, col: usize) -> Result<u32> {
        if row == 0 || row > self.rows[table] {
            return Err(BadImage);
        }

        let (start, row_size, widths) = &self.layout[table];
        let at = start + row_size * (row as usize - 1) + widths[..col].iter().sum::<usize>();

        match widths[col] {
            2 => read_u16(self.tables, at).map(u32::from),
            _ => read_u32(self.tables, at),
        }
    }

    fn string(&self, index: u32) -> Result<String> {
        let data = self.strings.get(index as usize..).ok_or(BadImage)?;
        let len = data.iter().position(|&b| b == 0).unwrap_or(data.len());
        Ok(String::from_utf8_lossy(&data[..len]).into_owned())
    }

    fn blob(&self, index: u32) -> Result<&'a [u8]> {
        let data = self.blobs.get(index as usize..).ok_or(BadImage)?;
        let (len, header) = decode_compressed(data).ok_or(BadImage)?;
        data.get(header..header + len as usize).ok_or(BadImage)
    }

    /// `GetAssemblyDefinition()` name and version; None for a module
    /// without an Assembly row (.NET throws InvalidOperationException).
    pub fn assembly(&self) -> Result<Option<(String, [u16; 4])>> {
        if self.rows[TABLE_ASSEMBLY] == 0 {
            return Ok(None);
        }

        let version = self.version(TABLE_ASSEMBLY, 1, 1)?;
        Ok(Some((self.string(self.cell(TABLE_ASSEMBLY, 1, 7)?)?, version)))
    }

    fn version(&self, table: usize, row: u32, first_col: usize) -> Result<[u16; 4]> {
        let mut version = [0u16; 4];

        for (i, part) in version.iter_mut().enumerate() {
            *part = self.cell(table, row, first_col + i)? as u16;
        }

        Ok(version)
    }

    /// `AssemblyReferences` names and versions, in table order.
    pub fn assembly_references(&self) -> Result<Vec<(String, [u16; 4])>> {
        (1..=self.rows[TABLE_ASSEMBLY_REF])
            .map(|row| Ok((self.string(self.cell(TABLE_ASSEMBLY_REF, row, 6)?)?, self.version(TABLE_ASSEMBLY_REF, row, 0)?)))
            .collect()
    }

    /// Port of `ReadTargetFramework`: the first assembly-level attribute
    /// whose constructor is a MemberRef on a TypeRef named
    /// `TargetFrameworkAttribute`, decoded by `DecodeTargetFrameworkValue`.
    pub fn target_framework(&self) -> Result<Option<String>> {
        let assembly_parent = (1u32 << HAS_CUSTOM_ATTRIBUTE.0) | 14;

        for row in 1..=self.rows[TABLE_CUSTOM_ATTRIBUTE] {
            if self.cell(TABLE_CUSTOM_ATTRIBUTE, row, 0)? != assembly_parent {
                continue;
            }

            let constructor = self.cell(TABLE_CUSTOM_ATTRIBUTE, row, 1)?;

            // CustomAttributeType tag 3 = MemberRef
            if constructor & 0x7 != 3 {
                continue;
            }

            let member_ref = constructor >> 3;
            let parent = self.cell(TABLE_MEMBER_REF, member_ref, 0)?;

            // MemberRefParent tag 1 = TypeRef
            if parent & 0x7 != 1 {
                continue;
            }

            let type_name = self.string(self.cell(TABLE_TYPE_REF, parent >> 3, 1)?)?;

            if type_name == "TargetFrameworkAttribute" {
                return Ok(decode_target_framework_value(self.blob(self.cell(TABLE_CUSTOM_ATTRIBUTE, row, 2)?)?));
            }
        }

        Ok(None)
    }
}

/// ECMA-335 compressed unsigned integer: (value, encoded length).
fn decode_compressed(data: &[u8]) -> Option<(u32, usize)> {
    let first = *data.first()?;

    if first & 0x80 == 0 {
        Some((u32::from(first), 1))
    } else if first & 0xC0 == 0x80 {
        Some(((u32::from(first & 0x3F) << 8) | u32::from(*data.get(1)?), 2))
    } else if first & 0xE0 == 0xC0 {
        let b = data.get(1..4)?;
        Some(((u32::from(first & 0x1F) << 24) | (u32::from(b[0]) << 16) | (u32::from(b[1]) << 8) | u32::from(b[2]), 4))
    } else {
        None
    }
}

/// Port of `DecodeTargetFrameworkValue`: prolog 0x0001 then a SerString
/// (0xFF is a null string); any read failure is null.
fn decode_target_framework_value(blob: &[u8]) -> Option<String> {
    if blob.len() < 4 || u16_at(blob, 0) != 0x0001 {
        return None;
    }

    let rest = &blob[2..];

    if rest[0] == 0xFF {
        return None;
    }

    let (len, header) = decode_compressed(rest)?;
    let bytes = rest.get(header..header + len as usize)?;
    std::str::from_utf8(bytes).ok().map(str::to_string)
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn compressed_integers_decode() {
        assert_eq!(decode_compressed(&[0x03]), Some((3, 1)));
        assert_eq!(decode_compressed(&[0x80, 0x80]), Some((0x80, 2)));
        assert_eq!(decode_compressed(&[0xC0, 0x00, 0x40, 0x00]), Some((0x4000, 4)));
        assert_eq!(decode_compressed(&[0xFF]), None);
        assert_eq!(decode_compressed(&[]), None);
    }

    #[test]
    fn target_framework_blob_decodes() {
        let mut blob = vec![0x01, 0x00, 0x03];
        blob.extend_from_slice(b"net\x00\x00");
        assert_eq!(decode_target_framework_value(&blob).as_deref(), Some("net"));
        assert_eq!(decode_target_framework_value(&[0x01, 0x00, 0xFF, 0x00]), None);
        assert_eq!(decode_target_framework_value(&[0x02, 0x00, 0x01, 0x41]), None);
        assert_eq!(decode_target_framework_value(&[0x01, 0x00, 0x05]), None);
    }

    #[test]
    fn metadata_reader_rejects_bad_signature() {
        assert!(MetadataReader::new(&[0u8; 64]).is_err());
        assert!(MetadataReader::new(&[]).is_err());
    }
}
