//! Port of src/Wade/UI/FileIcons.cs: Nerd Font icon mapping.

use crate::fs::directory_contents::FileSystemEntry;

// Port of the C# extension -> icon table (OrdinalIgnoreCase).
const EXTENSION_ICONS: &[(&str, u32)] = &[
    (".cs", 0xF031B),
    (".csx", 0xF031B),
    (".sln", 0xF0610),
    (".slnx", 0xF0610),
    (".csproj", 0xF0610),
    (".fsproj", 0xF0610),
    (".vbproj", 0xF0610),
    (".html", 0xF13B),
    (".htm", 0xF13B),
    (".css", 0xF031C),
    (".scss", 0xF031C),
    (".sass", 0xF031C),
    (".js", 0xF031E),
    (".mjs", 0xF031E),
    (".cjs", 0xF031E),
    (".ts", 0xF06E6),
    (".tsx", 0xF06E6),
    (".jsx", 0xF031E),
    (".py", 0xF0320),
    (".go", 0xF07D3),
    (".rs", 0xF1617),
    (".java", 0xF0B37),
    (".kt", 0xF1219),
    (".gradle", 0xF0AD),
    (".json", 0xF1C9),
    (".toml", 0xF1C9),
    (".yaml", 0xF1C9),
    (".yml", 0xF1C9),
    (".xml", 0xF1C9),
    (".ini", 0xF1C9),
    (".env", 0xF462),
    (".md", 0xF48A),
    (".markdown", 0xF48A),
    (".txt", 0xF15C),
    (".rst", 0xF15C),
    (".pdf", 0xF1C1),
    (".sh", 0xF489),
    (".bash", 0xF489),
    (".zsh", 0xF489),
    (".fish", 0xF489),
    (".ps1", 0xF489),
    (".psm1", 0xF489),
    (".psd1", 0xF489),
    (".bat", 0xF17A),
    (".cmd", 0xF17A),
    (".lnk", 0xF1178),
    (".dockerfile", 0xF308),
    (".dockerignore", 0xF308),
    (".gitignore", 0xF1D3),
    (".gitattributes", 0xF1D3),
    (".png", 0xF1C5),
    (".jpg", 0xF1C5),
    (".jpeg", 0xF1C5),
    (".gif", 0xF1C5),
    (".svg", 0xF1C5),
    (".webp", 0xF1C5),
    (".ico", 0xF1C5),
    (".zip", 0xF1C6),
    (".tar", 0xF1C6),
    (".gz", 0xF1C6),
    (".bz2", 0xF1C6),
    (".xz", 0xF1C6),
    (".7z", 0xF1C6),
    (".rar", 0xF1C6),
    (".nupkg", 0xF1C6),
    (".snupkg", 0xF1C6),
    (".jar", 0xF1C6),
    (".war", 0xF1C6),
    (".ear", 0xF1C6),
    (".apk", 0xF1C6),
    (".vsix", 0xF1C6),
    (".whl", 0xF1C6),
    (".epub", 0xF02D),
    (".docx", 0xF1C2),
    (".xlsx", 0xF1C3),
    (".pptx", 0xF1C4),
    (".odt", 0xF1C2),
    (".ods", 0xF1C3),
    (".odp", 0xF1C4),
    (".exe", 0xF17A),
    (".dll", 0xF17A),
    (".so", 0xF17A),
    (".dylib", 0xF17A),
    (".pdb", 0xF188),
];

/// Mirrors `FileIcons.GetIcon(FileSystemEntry)`.
#[must_use]
pub fn get_icon(entry: &FileSystemEntry) -> char {
    if entry.is_drive {
        return icon(0xF0A0); // nf-fa-hdd_o
    }

    if entry.is_app_exec_link {
        return icon(0xF0614); // nf-md-application_outline
    }

    if entry.is_junction_point {
        return icon(0xF19EE); // nf-md-folder_arrow_right
    }

    if entry.is_symlink() {
        return icon(if entry.is_directory { 0xF482 } else { 0xF481 });
    }

    if entry.is_directory {
        return icon(0xF114); // nf-fa-folder
    }

    let ext = get_extension(&entry.name).to_ascii_lowercase();
    if !ext.is_empty()
        && let Some(&code) = EXTENSION_ICONS.iter().find(|(e, _)| *e == ext).map(|(_, c)| c) {
            return icon(code);
        }

    // Special filenames without extension
    let name_lower = entry.name.to_ascii_lowercase();
    if name_lower == "dockerfile" {
        return icon(0xF308);
    }

    if name_lower == ".gitignore" || name_lower == ".gitattributes" {
        return icon(0xF1D3);
    }

    icon(0xF15B) // nf-fa-file
}

/// Mirrors `FileIcons.GetGitStatusIcon(GitFileStatus)`.
#[must_use]
pub fn get_git_status_icon(status: crate::fs::directory_contents::GitFileStatus) -> Option<char> {
    use crate::fs::directory_contents::GitFileStatus as S;
    if status.contains(S::CONFLICT) {
        Some(icon(0xF0026)) // nf-md-alert
    } else if status.contains(S::STAGED) {
        Some(icon(0xF06D3)) // nf-oct-diff_added
    } else if status.contains(S::MODIFIED) {
        Some(icon(0xF06D7)) // nf-oct-diff_modified
    } else if status.contains(S::UNTRACKED) {
        Some(icon(0xEB90)) // nf-cod-question
    } else {
        None
    }
}

/// Mirrors `FileIcons.GetCloudIcon()`.
#[must_use]
pub fn get_cloud_icon() -> char {
    icon(0xF0163) // nf-md-cloud_outline
}

/// Mirrors .NET `Path.GetExtension`: text from the final dot, including the
/// dot (unlike Rust's Path::extension, which treats ".name" as no extension).
#[must_use]
pub fn get_extension(name: &str) -> &str {
    match name.rfind('.') {
        Some(pos) if pos > 0 => &name[pos..],
        Some(0) => name, // ".gitignore"-style names keep the whole text
        _ => "",
    }
}

fn icon(code_point: u32) -> char {
    char::from_u32(code_point).unwrap_or('\u{FFFD}')
}
