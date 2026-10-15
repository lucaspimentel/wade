//! File icons: the Nerd Font mapping, plus the Unicode, emoji and ASCII
//! sets.

use crate::fs::directory_contents::FileSystemEntry;

// Extension -> Nerd Font icon (any case).
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

/// Which glyphs mark file types (`icons = nerd|unicode|emoji|ascii|none`).
#[derive(Clone, Copy, Debug, Default, PartialEq, Eq)]
pub enum IconSet {
    /// Nerd Fonts v3 glyphs (needs a patched font).
    #[default]
    Nerd,
    /// Single-width symbols found in standard fonts.
    Unicode,
    /// Two-cell emoji.
    Emoji,
    /// Plain characters, `ls -F` style.
    Ascii,
    /// No icon column.
    None,
}

impl IconSet {
    pub const ALL: [Self; 5] = [Self::Nerd, Self::Unicode, Self::Emoji, Self::Ascii, Self::None];

    #[must_use]
    pub fn parse(value: &str) -> Option<Self> {
        Self::ALL.into_iter().find(|set| set.name().eq_ignore_ascii_case(value))
    }

    #[must_use]
    pub const fn name(self) -> &'static str {
        match self {
            Self::Nerd => "nerd",
            Self::Unicode => "unicode",
            Self::Emoji => "emoji",
            Self::Ascii => "ascii",
            Self::None => "none",
        }
    }

    #[must_use]
    pub fn next(self) -> Self {
        let index = Self::ALL.iter().position(|&set| set == self).unwrap_or(0);
        Self::ALL[(index + 1) % Self::ALL.len()]
    }

    #[must_use]
    pub fn prev(self) -> Self {
        let index = Self::ALL.iter().position(|&set| set == self).unwrap_or(0);
        Self::ALL[(index + Self::ALL.len() - 1) % Self::ALL.len()]
    }

    /// Cells before a file name: the icon and a space, or the one-cell
    /// directory marker when there is no icon.
    #[must_use]
    pub const fn column_width(self) -> i32 {
        match self {
            Self::None => 1,
            Self::Emoji => 3,
            _ => 2,
        }
    }

    /// The set for the git, cloud and branch symbols: emoji fall back to
    /// the single-width Unicode symbols (the status column is one cell), and
    /// `None` hides only file-type icons, so status keeps ASCII symbols.
    const fn status_set(self) -> Self {
        match self {
            Self::Emoji => Self::Unicode,
            Self::None => Self::Ascii,
            other => other,
        }
    }
}

/// File categories shared by every icon set; Nerd Fonts uses them only for
/// extensions missing from its per-extension table.
#[derive(Clone, Copy, Debug, PartialEq, Eq)]
enum Category {
    Code,
    Script,
    Data,
    Text,
    Document,
    Image,
    Audio,
    Video,
    Archive,
    Executable,
    Font,
    Database,
    Other,
}

const CATEGORY_EXTENSIONS: &[(Category, &[&str])] = &[
    (
        Category::Code,
        &[
            ".c", ".h", ".cpp", ".cc", ".cxx", ".hpp", ".hh", ".cs", ".csx", ".fs", ".fsx", ".vb", ".go", ".rs",
            ".java", ".kt", ".kts", ".scala", ".swift", ".m", ".mm", ".rb", ".php", ".pl", ".lua", ".r", ".dart",
            ".zig", ".hs", ".ml", ".ex", ".exs", ".erl", ".clj", ".py", ".js", ".mjs", ".cjs", ".jsx", ".ts", ".tsx",
            ".html", ".htm", ".css", ".scss", ".sass", ".less", ".vue", ".svelte", ".sql",
        ],
    ),
    (Category::Script, &[".sh", ".bash", ".zsh", ".fish", ".ps1", ".psm1", ".psd1", ".bat", ".cmd"]),
    (
        Category::Data,
        &[
            ".json", ".jsonc", ".toml", ".yaml", ".yml", ".xml", ".ini", ".cfg", ".conf", ".env", ".properties",
            ".csv", ".tsv", ".plist", ".lock", ".sln", ".slnx", ".csproj", ".fsproj", ".vbproj", ".props", ".targets",
            ".gradle", ".editorconfig", ".gitignore", ".gitattributes", ".dockerfile", ".dockerignore",
        ],
    ),
    (Category::Text, &[".txt", ".md", ".markdown", ".rst", ".log"]),
    (
        Category::Document,
        &[".pdf", ".doc", ".docx", ".xls", ".xlsx", ".ppt", ".pptx", ".odt", ".ods", ".odp", ".rtf", ".epub"],
    ),
    (
        Category::Image,
        &[
            ".png", ".jpg", ".jpeg", ".gif", ".bmp", ".svg", ".webp", ".ico", ".tif", ".tiff", ".heic", ".avif", ".psd",
        ],
    ),
    (
        Category::Audio,
        &[".mp3", ".flac", ".wav", ".ogg", ".oga", ".m4a", ".aac", ".opus", ".wma", ".aiff", ".mid", ".midi"],
    ),
    (Category::Video, &[".mp4", ".mkv", ".mov", ".avi", ".webm", ".wmv", ".m4v", ".flv", ".mpg", ".mpeg"]),
    (
        Category::Archive,
        &[
            ".zip", ".tar", ".gz", ".tgz", ".bz2", ".xz", ".zst", ".7z", ".rar", ".nupkg", ".snupkg", ".jar", ".war",
            ".ear", ".apk", ".vsix", ".whl", ".deb", ".rpm", ".iso", ".dmg", ".cab",
        ],
    ),
    (Category::Executable, &[".exe", ".dll", ".so", ".dylib", ".msi", ".com", ".app", ".bin", ".appimage"]),
    (Category::Font, &[".ttf", ".otf", ".woff", ".woff2", ".eot", ".fon"]),
    (Category::Database, &[".db", ".sqlite", ".sqlite3", ".mdb", ".accdb"]),
];

fn category(ext: &str, name_lower: &str) -> Category {
    if name_lower == "dockerfile" {
        return Category::Data;
    }

    CATEGORY_EXTENSIONS
        .iter()
        .find(|(_, extensions)| extensions.contains(&ext))
        .map_or(Category::Other, |&(category, _)| category)
}

/// Columns: Nerd Fonts, Unicode, emoji, ASCII (ASCII marks executables only).
fn category_glyphs(category: Category) -> (u32, char, char, char) {
    match category {
        Category::Code => (0xF1C9, '\u{0192}', '\u{1F4DC}', '.'), // nf-fa-file_code_o, ƒ, 📜
        Category::Script => (0xF489, '\u{203A}', '\u{1F41A}', '.'), // nf-oct-terminal, ›, 🐚
        Category::Data => (0xF1C9, '\u{229E}', '\u{1F527}', '.'), // nf-fa-file_code_o, ⊞, 🔧
        Category::Text => (0xF15C, '\u{25AB}', '\u{1F4DD}', '.'), // nf-fa-file_text, ▫, 📝
        Category::Document => (0xF0219, '\u{25AA}', '\u{1F4D5}', '.'), // nf-md-file_document, ▪, 📕
        Category::Image => (0xF1C5, '\u{25E9}', '\u{1F3A8}', '.'), // nf-fa-file_image_o, ◩, 🎨
        Category::Audio => (0xF1C7, '\u{223F}', '\u{1F3B5}', '.'), // nf-fa-file_audio_o, ∿, 🎵
        Category::Video => (0xF1C8, '\u{25BB}', '\u{1F3AC}', '.'), // nf-fa-file_video_o, ▻, 🎬
        Category::Archive => (0xF1C6, '\u{22A0}', '\u{1F4E6}', '.'), // nf-fa-file_archive_o, ⊠, 📦
        Category::Executable => (0xF013, '\u{2217}', '\u{26A1}', '*'), // nf-fa-cog, ∗, ⚡
        Category::Font => (0xF031, '\u{A730}', '\u{1F524}', '.'), // nf-fa-font, ꜰ, 🔤
        Category::Database => (0xF1C0, '\u{229C}', '\u{1F4BE}', '.'), // nf-fa-database, ⊜, 💾
        Category::Other => (0xF15B, '\u{25E6}', '\u{1F4C4}', '.'), // nf-fa-file, ◦, 📄
    }
}

/// Picks the glyph for `set` from a (Nerd Fonts, Unicode, emoji, ASCII) row.
fn pick(set: IconSet, (nerd, unicode, emoji, ascii): (u32, char, char, char)) -> Option<char> {
    match set {
        IconSet::Nerd => Some(icon(nerd)),
        IconSet::Unicode => Some(unicode),
        IconSet::Emoji => Some(emoji),
        IconSet::Ascii => Some(ascii),
        IconSet::None => None,
    }
}

/// Mirrors `FileIcons.GetIcon(FileSystemEntry)`, per icon set; `None` for
/// [`IconSet::None`].
#[must_use]
pub fn get_icon(entry: &FileSystemEntry, set: IconSet) -> Option<char> {
    let dir = std::path::MAIN_SEPARATOR;
    if entry.is_drive {
        return pick(set, (0xF0A0, '\u{229F}', '\u{1F4BD}', ':')); // nf-fa-hdd_o, ⊟, 💽
    }

    if entry.is_app_exec_link {
        return pick(set, (0xF0614, '\u{25B9}', '\u{1F680}', '&')); // nf-md-application_outline, ▹, 🚀
    }

    if entry.is_junction_point {
        return pick(set, (0xF19EE, '\u{21E2}', '\u{1F517}', '@')); // nf-md-folder_arrow_right, ⇢, 🔗
    }

    if entry.is_symlink() {
        return if entry.is_directory {
            pick(set, (0xF482, '\u{21AA}', '\u{1F517}', '@')) // nf-oct-file_symlink_directory, ↪, 🔗
        } else {
            pick(set, (0xF481, '\u{21B3}', '\u{1F517}', '@')) // nf-oct-file_symlink_file, ↳, 🔗
        };
    }

    if entry.is_directory {
        return pick(set, (0xF114, '\u{25B8}', '\u{1F4C1}', dir)); // nf-fa-folder, ▸, 📁
    }

    let ext = get_extension(&entry.name).to_ascii_lowercase();
    let name_lower = entry.name.to_ascii_lowercase();
    if set == IconSet::Nerd
        && let Some(code) = nerd_extension_icon(&ext, &name_lower)
    {
        return Some(icon(code));
    }

    pick(set, category_glyphs(category(&ext, &name_lower)))
}

/// The per-extension Nerd Fonts table and its special file names.
fn nerd_extension_icon(ext: &str, name_lower: &str) -> Option<u32> {
    if !ext.is_empty()
        && let Some(&(_, code)) = EXTENSION_ICONS.iter().find(|(e, _)| *e == ext)
    {
        return Some(code);
    }

    // Special filenames without extension
    if name_lower == "dockerfile" {
        return Some(0xF308);
    }

    if name_lower == ".gitignore" || name_lower == ".gitattributes" {
        return Some(0xF1D3);
    }

    None
}

/// Mirrors `FileIcons.GetGitStatusIcon(GitFileStatus)`, per icon set.
#[must_use]
pub fn get_git_status_icon(status: crate::fs::directory_contents::GitFileStatus, set: IconSet) -> Option<char> {
    use crate::fs::directory_contents::GitFileStatus as S;
    let glyphs = if status.contains(S::CONFLICT) {
        (0xF0026, '\u{203C}', '!') // nf-md-alert, ‼
    } else if status.contains(S::STAGED) {
        (0xF06D3, '\u{2214}', '+') // nf-oct-diff_added, ∔
    } else if status.contains(S::MODIFIED) {
        (0xF06D7, '\u{2206}', '~') // nf-oct-diff_modified, ∆
    } else if status.contains(S::UNTRACKED) {
        (0xEB90, '?', '?') // nf-cod-question
    } else {
        return None;
    };
    Some(status_glyph(set, glyphs))
}

/// Mirrors `FileIcons.GetCloudIcon()`, per icon set.
#[must_use]
pub fn get_cloud_icon(set: IconSet) -> char {
    status_glyph(set, (0xF0163, '\u{21E3}', '^')) // nf-md-cloud_outline, ⇣
}

/// The status-bar branch symbol; ASCII has none (the name stands alone).
#[must_use]
pub fn get_branch_icon(set: IconSet) -> Option<char> {
    match set.status_set() {
        IconSet::Nerd => Some('\u{E0A0}'), // nf-pl-branch
        IconSet::Ascii => None,
        _ => Some('\u{2387}'), // ⎇
    }
}

fn status_glyph(set: IconSet, (nerd, unicode, ascii): (u32, char, char)) -> char {
    match set.status_set() {
        IconSet::Nerd => icon(nerd),
        IconSet::Ascii => ascii,
        _ => unicode,
    }
}

/// Text from the final dot, including the dot (unlike `Path::extension`,
/// which treats ".name" as no extension).
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

#[cfg(test)]
mod tests {

    use super::{IconSet, get_branch_icon, get_cloud_icon, get_git_status_icon, get_icon};
    use crate::fs::FileSystemEntry;
    use crate::fs::directory_contents::GitFileStatus;
    use crate::rune_width::rune_width;

    fn entry(name: &str, is_directory: bool) -> FileSystemEntry {
        FileSystemEntry {
            name: name.to_string(),
            full_path: name.to_string(),
            is_directory,
            size: 0,
            last_modified: crate::ui::format_helpers::DateParts::default(),
            link_target: None,
            is_broken_symlink: false,
            is_drive: false,
            is_cloud_placeholder: false,
            is_junction_point: false,
            is_app_exec_link: false,
            app_exec_link_target: None,
            drive_media_type: crate::fs::DriveMediaType::default(),
            drive_format: None,
            drive_label: None,
            drive_free_space: 0,
            drive_total_size: 0,
        }
    }

    fn code(name: &str) -> u32 {
        nerd(&entry(name, false))
    }

    fn nerd(entry: &FileSystemEntry) -> u32 {
        u32::from(get_icon(entry, IconSet::Nerd).unwrap())
    }

    #[test]
    fn directories_drives_and_links() {
        assert_eq!(nerd(&entry("src", true)), 0xF114);
        assert_eq!(nerd(&FileSystemEntry { is_drive: true, ..entry("C:", true) }), 0xF0A0);
        assert_eq!(
            nerd(&FileSystemEntry {
                link_target: Some("t".into()),
                ..entry("l", true)
            }),
            0xF482
        );
        assert_eq!(
            nerd(&FileSystemEntry {
                link_target: Some("t".into()),
                ..entry("l", false)
            }),
            0xF481
        );
        let junction = FileSystemEntry {
            link_target: Some("t".into()),
            is_junction_point: true,
            ..entry("j", true)
        };
        assert_eq!(nerd(&junction), 0xF19EE, "junction wins over symlink");
        assert_eq!(
            nerd(&FileSystemEntry {
                is_app_exec_link: true,
                ..entry("wt.exe", false)
            }),
            0xF0614
        );
    }

    #[test]
    fn known_extensions() {
        for (name, expected) in [
            ("Program.cs", 0xF031B),
            ("app.js", 0xF031E),
            ("app.ts", 0xF06E6),
            ("main.py", 0xF0320),
            ("main.go", 0xF07D3),
            ("lib.rs", 0xF1617),
            ("Main.java", 0xF0B37),
            ("Main.kt", 0xF1219),
            ("index.html", 0xF13B),
            ("styles.css", 0xF031C),
            ("data.json", 0xF1C9),
            ("config.toml", 0xF1C9),
            ("pipeline.yml", 0xF1C9),
            ("README.md", 0xF48A),
            ("notes.txt", 0xF15C),
            ("report.pdf", 0xF1C1),
            ("deploy.sh", 0xF489),
            ("build.ps1", 0xF489),
            ("setup.bat", 0xF17A),
            ("App.sln", 0xF0610),
            ("Wade.csproj", 0xF0610),
            ("photo.png", 0xF1C5),
            ("image.jpg", 0xF1C5),
            ("archive.zip", 0xF1C6),
            ("release.tar", 0xF1C6),
            ("app.exe", 0xF17A),
            ("library.dll", 0xF17A),
        ] {
            assert_eq!(code(name), expected, "{name}");
        }
    }

    #[test]
    fn extensions_are_case_insensitive() {
        assert_eq!(code("file.CS"), 0xF031B);
        assert_eq!(code("file.JSON"), 0xF1C9);
    }

    #[test]
    fn unknown_extensions_and_special_names() {
        for name in ["file.xyz", "file.abc", "noextension"] {
            assert_eq!(code(name), 0xF15B, "{name}");
        }
        for name in ["Dockerfile", "dockerfile"] {
            assert_eq!(code(name), 0xF308, "{name}");
        }
        for name in [".gitignore", ".gitattributes"] {
            assert_eq!(code(name), 0xF1D3, "{name}");
        }
    }

    /// One entry per kind and category.
    fn samples() -> Vec<FileSystemEntry> {
        let mut entries = vec![
            entry("src", true),
            FileSystemEntry { is_drive: true, ..entry("C:", true) },
            FileSystemEntry {
                link_target: Some("t".into()),
                ..entry("l", true)
            },
            FileSystemEntry {
                link_target: Some("t".into()),
                ..entry("l", false)
            },
            FileSystemEntry {
                link_target: Some("t".into()),
                is_junction_point: true,
                ..entry("j", true)
            },
            FileSystemEntry {
                is_app_exec_link: true,
                ..entry("wt.exe", false)
            },
        ];
        for name in [
            "main.c", "run.sh", "a.json", "a.txt", "a.pdf", "a.png", "a.mp3", "a.mkv", "a.zip", "a.msi", "a.ttf",
            "a.sqlite", "a.xyz",
        ] {
            entries.push(entry(name, false));
        }
        entries
    }

    #[test]
    fn nerd_falls_back_to_the_category_glyph() {
        for (name, expected) in [
            ("song.mp3", 0xF1C7),
            ("movie.mkv", 0xF1C8),
            ("font.ttf", 0xF031),
            ("data.sqlite", 0xF1C0),
            ("main.c", 0xF1C9),
            ("setup.msi", 0xF013),
            ("Notes.LOG", 0xF15C),
        ] {
            assert_eq!(code(name), expected, "{name}");
        }
        assert_eq!(code("Program.cs"), 0xF031B, "the per-extension glyph wins");
    }

    #[test]
    fn every_set_distinguishes_kinds_and_categories() {
        let glyphs = |set| samples().iter().map(|e| get_icon(e, set).unwrap()).collect::<String>();
        assert_eq!(
            glyphs(IconSet::Unicode),
            "\u{25B8}\u{229F}\u{21AA}\u{21B3}\u{21E2}\u{25B9}\u{0192}\u{203A}\u{229E}\u{25AB}\u{25AA}\u{25E9}\u{223F}\u{25BB}\u{22A0}\u{2217}\u{A730}\u{229C}\u{25E6}"
        );
        assert_eq!(
            glyphs(IconSet::Emoji),
            "\u{1F4C1}\u{1F4BD}\u{1F517}\u{1F517}\u{1F517}\u{1F680}\u{1F4DC}\u{1F41A}\u{1F527}\u{1F4DD}\u{1F4D5}\u{1F3A8}\u{1F3B5}\u{1F3AC}\u{1F4E6}\u{26A1}\u{1F524}\u{1F4BE}\u{1F4C4}"
        );
        let sep = std::path::MAIN_SEPARATOR;
        assert_eq!(
            glyphs(IconSet::Ascii),
            format!("{sep}:@@@&.........*..."),
            "ASCII marks kinds and executables only"
        );
        assert!(samples().iter().all(|e| get_icon(e, IconSet::None).is_none()));
    }

    #[test]
    fn glyphs_fit_their_column() {
        for set in IconSet::ALL {
            let width = if set == IconSet::Emoji { 2 } else { 1 };
            for entry in samples() {
                if let Some(glyph) = get_icon(&entry, set) {
                    assert_eq!(rune_width(glyph), width, "{set:?} {}", entry.name);
                    assert_eq!(i32::try_from(width).unwrap() + 1, set.column_width(), "{set:?}");
                }
            }
            assert_eq!(rune_width(get_cloud_icon(set)), 1, "{set:?} cloud");
            for status in
                [GitFileStatus::CONFLICT, GitFileStatus::STAGED, GitFileStatus::MODIFIED, GitFileStatus::UNTRACKED]
            {
                assert_eq!(rune_width(get_git_status_icon(status, set).unwrap()), 1, "{set:?} git");
            }
        }
    }

    #[test]
    fn status_symbols_follow_the_set() {
        let git = |set| {
            [GitFileStatus::CONFLICT, GitFileStatus::STAGED, GitFileStatus::MODIFIED, GitFileStatus::UNTRACKED]
                .map(|status| get_git_status_icon(status, set).unwrap())
                .iter()
                .collect::<String>()
        };
        assert_eq!(git(IconSet::Nerd), "\u{F0026}\u{F06D3}\u{F06D7}\u{EB90}");
        assert_eq!(git(IconSet::Unicode), "\u{203C}\u{2214}\u{2206}?");
        assert_eq!(git(IconSet::Emoji), git(IconSet::Unicode), "emoji are too wide for the status column");
        assert_eq!(git(IconSet::Ascii), "!+~?");
        assert_eq!(git(IconSet::None), git(IconSet::Ascii), "hiding icons keeps git status");
        assert_eq!(get_git_status_icon(GitFileStatus::NONE, IconSet::Nerd), None);

        assert_eq!(get_cloud_icon(IconSet::Nerd), '\u{F0163}');
        assert_eq!(get_cloud_icon(IconSet::Emoji), '\u{21E3}');
        assert_eq!(get_cloud_icon(IconSet::None), '^');

        assert_eq!(get_branch_icon(IconSet::Nerd), Some('\u{E0A0}'));
        assert_eq!(get_branch_icon(IconSet::Unicode), Some('\u{2387}'));
        assert_eq!(get_branch_icon(IconSet::Emoji), Some('\u{2387}'));
        assert_eq!(get_branch_icon(IconSet::Ascii), None);
        assert_eq!(get_branch_icon(IconSet::None), None);
    }

    #[test]
    fn icon_set_names_parse_and_cycle() {
        for set in IconSet::ALL {
            assert_eq!(IconSet::parse(set.name()), Some(set));
            assert_eq!(IconSet::parse(&set.name().to_ascii_uppercase()), Some(set));
            assert_eq!(set.next().prev(), set);
        }
        assert_eq!(IconSet::parse("fancy"), None);
        assert_eq!(IconSet::default(), IconSet::Nerd);
        assert_eq!(IconSet::None.next(), IconSet::Nerd);
        assert_eq!(IconSet::Nerd.prev(), IconSet::None);
    }
}
