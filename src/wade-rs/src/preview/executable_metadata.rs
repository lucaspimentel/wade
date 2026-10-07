//! Port of `ExecutableMetadataProvider`: PE headers, Windows version info
//! (`FileVersionInfo`, Windows only) and .NET assembly metadata, read
//! through the in-tree `pe` module.

use super::pe::{self, BadImage, Image, MetadataReader};
use super::{MetadataEntry, MetadataProvider, MetadataResult, MetadataSection, PreviewContext};
use crate::input::CancelToken;

pub struct ExecutableMetadataProvider;

fn extension(path: &str) -> String {
    std::path::Path::new(path).extension().map(|e| e.to_string_lossy().into_owned()).unwrap_or_default()
}

impl MetadataProvider for ExecutableMetadataProvider {
    fn label(&self) -> &'static str {
        "Executable metadata"
    }

    fn can_provide_metadata(&self, path: &str, _context: &PreviewContext) -> bool {
        let ext = extension(path);
        ext.eq_ignore_ascii_case("exe") || ext.eq_ignore_ascii_case("dll")
    }

    fn get_metadata(&self, path: &str, _context: &PreviewContext, cancel: &CancelToken) -> Option<MetadataResult> {
        if cancel.is_cancelled() {
            return None;
        }

        read_metadata(path, cancel).ok().flatten()
    }
}

/// `Err` and `Ok(None)` are both the C# `null` result.
fn read_metadata(path: &str, cancel: &CancelToken) -> Result<Option<MetadataResult>, BadImage> {
    let mut image = Image::open(path).map_err(|_| BadImage)?;
    let headers = pe::read_headers(&mut image)?;
    let mut sections = Vec::new();

    // ── PE Headers ──
    let mut pe_entries = Vec::new();
    let bitness = if headers.pe_header.is_some_and(|h| h.is_pe32_plus) { "PE32+" } else { "PE32" };
    pe_entries.push(MetadataEntry::new("Architecture", &format!("{} ({bitness})", format_machine(headers.machine))));

    if let Some(header) = headers.pe_header {
        pe_entries.push(MetadataEntry::new("Subsystem", &format_subsystem(header.subsystem)));
    }

    let is_dll = headers.characteristics & 0x2000 != 0;
    pe_entries.push(MetadataEntry::new("Type", if is_dll { "DLL" } else { "Executable" }));

    if headers.time_date_stamp == 0 {
        pe_entries.push(MetadataEntry::new("Timestamp", "Reproducible build"));
    } else if let Some(timestamp) = chrono::DateTime::from_timestamp(i64::from(headers.time_date_stamp), 0) {
        use chrono::Datelike;

        if (2000..=2100).contains(&timestamp.year()) {
            pe_entries.push(MetadataEntry::new("Timestamp", &format!("{} UTC", timestamp.format("%Y-%m-%d %H:%M:%S"))));
        }
    }

    sections.push(MetadataSection {
        header: Some("Executable Info".to_string()),
        entries: pe_entries,
    });

    if cancel.is_cancelled() {
        return Ok(None);
    }

    // ── FileVersionInfo (Windows only) ──
    #[cfg(windows)]
    if let Some(section) = version_info::section(path) {
        sections.push(section);
    }

    // ── .NET Assembly Metadata ──
    if headers.has_metadata() {
        let block = pe::read_metadata_block(&mut image, &headers)?;

        // C# catches BadImageFormatException from GetMetadataReader only
        if let Ok(reader) = MetadataReader::new(&block)
            && !assembly_sections(&mut sections, &reader, cancel)?
        {
            return Ok(None);
        }
    }

    Ok(Some(MetadataResult {
        sections,
        file_type_label: Some(extension(path).to_ascii_uppercase()),
    }))
}

/// Port of `GetAssemblyMetadataSections`; false when the image is not an
/// assembly (C# `GetAssemblyDefinition` throws InvalidOperationException,
/// which nulls the whole result).
fn assembly_sections(
    sections: &mut Vec<MetadataSection>,
    reader: &MetadataReader,
    cancel: &CancelToken,
) -> Result<bool, BadImage> {
    let Some((name, version)) = reader.assembly()? else {
        return Ok(false);
    };

    let mut entries = Vec::new();

    if !name.is_empty() {
        entries.push(MetadataEntry::new("Name", &name));
    }

    entries.push(MetadataEntry::new("Version", &format_version(version)));

    if let Some(tfm) = reader.target_framework()? {
        entries.push(MetadataEntry::new("Framework", &tfm));
    }

    sections.push(MetadataSection {
        header: Some(".NET Assembly".to_string()),
        entries,
    });

    if cancel.is_cancelled() {
        return Ok(true);
    }

    let refs = reader.assembly_references()?;

    if !refs.is_empty() {
        sections.push(MetadataSection {
            header: Some(format!("Referenced Assemblies ({})", refs.len())),
            entries: refs
                .iter()
                .map(|(name, version)| MetadataEntry::new("", &format!("{name} {}", format_version(*version))))
                .collect(),
        });
    }

    Ok(true)
}

fn format_version(version: [u16; 4]) -> String {
    format!("{}.{}.{}.{}", version[0], version[1], version[2], version[3])
}

/// Port of `FormatMachine`; other values print as .NET's `Machine` enum
/// `ToString()` (member name, or the number when undefined).
#[must_use]
pub fn format_machine(machine: u16) -> String {
    let name = match machine {
        0x014C => "x86",
        0x8664 => "x64",
        0x01C0 => "ARM",
        0xAA64 => "ARM64",
        0x0000 => "Unknown",
        0x0169 => "WceMipsV2",
        0x0184 => "Alpha",
        0x01A2 => "SH3",
        0x01A3 => "SH3Dsp",
        0x01A4 => "SH3E",
        0x01A6 => "SH4",
        0x01A8 => "SH5",
        0x01C2 => "Thumb",
        0x01C4 => "ArmThumb2",
        0x01D3 => "AM33",
        0x01F0 => "PowerPC",
        0x01F1 => "PowerPCFP",
        0x0200 => "IA64",
        0x0266 => "MIPS16",
        0x0284 => "Alpha64",
        0x0366 => "MipsFpu",
        0x0466 => "MipsFpu16",
        0x0520 => "Tricore",
        0x0EBC => "Ebc",
        0x9041 => "M32R",
        0x6232 => "LoongArch32",
        0x6264 => "LoongArch64",
        0x5032 => "RiscV32",
        0x5064 => "RiscV64",
        0x5128 => "RiscV128",
        _ => return machine.to_string(),
    };
    name.to_string()
}

/// Port of `FormatSubsystem`; other values print as .NET's `Subsystem`
/// enum `ToString()`.
#[must_use]
pub fn format_subsystem(subsystem: u16) -> String {
    let name = match subsystem {
        3 => "Console",
        2 => "GUI",
        10 => "EFI Application",
        11 => "EFI Boot Service Driver",
        12 => "EFI Runtime Driver",
        0 => "Unknown",
        1 => "Native",
        5 => "OS2Cui",
        7 => "PosixCui",
        8 => "NativeWindows",
        9 => "WindowsCEGui",
        13 => "EfiRom",
        14 => "Xbox",
        16 => "WindowsBootApplication",
        _ => return subsystem.to_string(),
    };
    name.to_string()
}

/// Port of `GetFileVersionInfoSection` over `FileVersionInfo`'s Win32
/// lookup: the `\VarFileInfo\Translation` code page, then .NET's fallback
/// code pages until one yields a FileVersion string.
#[cfg(windows)]
mod version_info {
    use super::{MetadataEntry, MetadataSection};
    use windows_sys::Win32::Storage::FileSystem::{GetFileVersionInfoSizeW, GetFileVersionInfoW, VerQueryValueW};

    const FALLBACK_CODE_PAGES: [u32; 3] = [0x0409_04B0, 0x0409_04E4, 0x0409_0000];

    fn wide(text: &str) -> Vec<u16> {
        text.encode_utf16().chain(std::iter::once(0)).collect()
    }

    fn query(data: &[u8], sub_block: &str) -> Option<(*const u8, u32)> {
        let block = wide(sub_block);
        let mut ptr: *mut core::ffi::c_void = std::ptr::null_mut();
        let mut len = 0u32;
        let ok = unsafe { VerQueryValueW(data.as_ptr().cast(), block.as_ptr(), &mut ptr, &mut len) };
        (ok != 0 && !ptr.is_null()).then_some((ptr.cast_const().cast::<u8>(), len))
    }

    fn string(data: &[u8], code_page: &str, name: &str) -> Option<String> {
        let (ptr, len) = query(data, &format!("\\StringFileInfo\\{code_page}\\{name}"))?;
        let units = unsafe { std::slice::from_raw_parts(ptr.cast::<u16>(), len as usize) };
        let end = units.iter().position(|&u| u == 0).unwrap_or(units.len());
        Some(String::from_utf16_lossy(&units[..end]))
    }

    #[derive(Default)]
    struct Strings {
        product: Option<String>,
        description: Option<String>,
        company: Option<String>,
        copyright: Option<String>,
        file_version: Option<String>,
        product_version: Option<String>,
        original_name: Option<String>,
    }

    fn read_code_page(data: &[u8], code_page: u32) -> Strings {
        let cp = format!("{code_page:08X}");
        Strings {
            product: string(data, &cp, "ProductName"),
            description: string(data, &cp, "FileDescription"),
            company: string(data, &cp, "CompanyName"),
            copyright: string(data, &cp, "LegalCopyright"),
            file_version: string(data, &cp, "FileVersion"),
            product_version: string(data, &cp, "ProductVersion"),
            original_name: string(data, &cp, "OriginalFilename"),
        }
    }

    fn load(path: &str) -> Option<Strings> {
        let file = wide(path);
        let mut handle = 0u32;
        let size = unsafe { GetFileVersionInfoSizeW(file.as_ptr(), &mut handle) };

        if size == 0 {
            return None;
        }

        let mut data = vec![0u8; size as usize];

        if unsafe { GetFileVersionInfoW(file.as_ptr(), 0, size, data.as_mut_ptr().cast()) } == 0 {
            return None;
        }

        let lang_id =
            query(&data, "\\VarFileInfo\\Translation")
                .filter(|&(_, len)| len >= 4)
                .map_or(0x0409_04E4, |(ptr, _)| {
                    let raw = unsafe { std::slice::from_raw_parts(ptr, 4) };
                    (u32::from(u16::from_le_bytes([raw[0], raw[1]])) << 16)
                        | u32::from(u16::from_le_bytes([raw[2], raw[3]]))
                });

        let mut strings = read_code_page(&data, lang_id);

        if strings.file_version.as_deref().is_none_or(str::is_empty) {
            for code_page in FALLBACK_CODE_PAGES.into_iter().filter(|&cp| cp != lang_id) {
                strings = read_code_page(&data, code_page);

                if strings.file_version.as_deref().is_some_and(|v| !v.is_empty()) {
                    break;
                }
            }
        }

        Some(strings)
    }

    fn present(value: Option<&String>) -> Option<&String> {
        value.filter(|v| !v.trim().is_empty())
    }

    pub(super) fn section(path: &str) -> Option<MetadataSection> {
        let fvi = load(path)?;

        if present(fvi.product.as_ref()).is_none()
            && present(fvi.description.as_ref()).is_none()
            && present(fvi.company.as_ref()).is_none()
            && present(fvi.file_version.as_ref()).is_none()
        {
            return None;
        }

        let mut entries = Vec::new();
        let mut add = |label: &str, value: Option<&String>| {
            if let Some(value) = present(value) {
                entries.push(MetadataEntry::new(label, value));
            }
        };

        add("Product", fvi.product.as_ref());
        add("Description", fvi.description.as_ref());
        add("Company", fvi.company.as_ref());
        add("Copyright", fvi.copyright.as_ref());
        add("File version", fvi.file_version.as_ref());

        if present(fvi.product_version.as_ref()).is_some() && fvi.product_version != fvi.file_version {
            add("Product ver.", fvi.product_version.as_ref());
        }

        add("Original name", fvi.original_name.as_ref());
        Some(MetadataSection {
            header: Some("Version Info".to_string()),
            entries,
        })
    }
}

#[cfg(test)]
mod tests {
    use super::*;
    use crate::preview::{registry, test_context};

    #[test]
    fn can_provide_metadata_for_executable_extensions() {
        for path in ["app.exe", "app.EXE", "lib.dll", "lib.DLL"] {
            assert!(ExecutableMetadataProvider.can_provide_metadata(path, &test_context()), "{path}");
        }

        for path in ["archive.zip", "readme.txt", "image.png", "package.nupkg"] {
            assert!(!ExecutableMetadataProvider.can_provide_metadata(path, &test_context()), "{path}");
        }
    }

    fn temp_file(name: &str, data: &[u8]) -> std::path::PathBuf {
        let path = std::env::temp_dir().join(format!("wade-exe-{}-{name}", std::process::id()));
        std::fs::write(&path, data).unwrap();
        path
    }

    #[test]
    fn non_pe_file_returns_none() {
        let path = temp_file("text.exe", b"This is not a PE file");
        assert_eq!(
            ExecutableMetadataProvider.get_metadata(&path.to_string_lossy(), &test_context(), &CancelToken::new()),
            None
        );
        std::fs::remove_file(path).unwrap();
    }

    /// Port of `GetMetadata_ZeroTimestamp_ShowsReproducibleBuild`.
    #[test]
    fn zero_timestamp_shows_reproducible_build() {
        let mut pe = vec![0u8; 512];
        pe[0] = b'M';
        pe[1] = b'Z';
        pe[60] = 64;
        pe[64] = b'P';
        pe[65] = b'E';
        pe[68] = 0x64;
        pe[69] = 0x86;
        pe[84] = 0x70;
        pe[88] = 0x0B;
        pe[89] = 0x02;
        pe[88 + 68] = 3;
        let path = temp_file("zero.dll", &pe);
        let result = ExecutableMetadataProvider
            .get_metadata(&path.to_string_lossy(), &test_context(), &CancelToken::new())
            .unwrap();
        assert!(result.sections[0].entries.iter().any(|e| e.value == "Reproducible build"));
        std::fs::remove_file(path).unwrap();
    }

    #[test]
    fn cancelled_token_returns_none() {
        let cancel = CancelToken::new();
        cancel.cancel();
        let exe = std::env::current_exe().unwrap();
        assert_eq!(ExecutableMetadataProvider.get_metadata(&exe.to_string_lossy(), &test_context(), &cancel), None);
    }

    #[test]
    fn registry_includes_executable_provider() {
        let labels: Vec<&str> = registry::applicable_metadata_providers("app.exe", &test_context())
            .iter()
            .map(|p| p.label())
            .collect();
        assert!(labels.contains(&"Executable metadata"));
    }

    #[test]
    fn enum_fallback_names() {
        assert_eq!(format_machine(0x1234), "4660");
        assert_eq!(format_machine(0x0EBC), "Ebc");
        assert_eq!(format_subsystem(14), "Xbox");
        assert_eq!(format_subsystem(99), "99");
    }

    #[cfg(windows)]
    #[test]
    fn system_dll_shows_version_info() {
        let root = std::env::var("SystemRoot").unwrap_or_else(|_| r"C:\Windows".to_string());
        let path = format!(r"{root}\System32\kernel32.dll");
        let result = ExecutableMetadataProvider.get_metadata(&path, &test_context(), &CancelToken::new()).unwrap();
        let version = result.sections.iter().find(|s| s.header.as_deref() == Some("Version Info")).unwrap();
        assert!(version.entries.iter().any(|e| e.label == "File version"), "{version:?}");
    }
}
