//! Port of the file-type label dictionaries from
//! `src/Wade/FileSystem/FilePreview.cs` (`s_extensionLabels`,
//! `s_filenameLabels`, and `GetFileTypeLabel`). Phase 7 reuses this for
//! previews; Phase 4d uses it for the Properties overlay Type row.

/// Port of `s_filenameLabels` (StringComparer.Ordinal, so exact case).
const FILENAME_LABELS: &[(&str, &str)] = &[
    ("Dockerfile", "Docker"),
    ("Makefile", "Makefile"),
    ("Jenkinsfile", "Jenkinsfile"),
    ("Brewfile", "Brewfile"),
    ("Gemfile", "Ruby"),
    ("Rakefile", "Ruby"),
    ("Procfile", "Procfile"),
];

/// Port of `s_extensionLabels` (StringComparer.OrdinalIgnoreCase).
const EXTENSION_LABELS: &[(&str, &str)] = &[
    // Programming
    (".cs", "C#"),
    (".py", "Python"),
    (".js", "JavaScript"),
    (".ts", "TypeScript"),
    (".go", "Go"),
    (".rs", "Rust"),
    (".java", "Java"),
    (".kt", "Kotlin"),
    (".cpp", "C++"),
    (".c", "C"),
    (".h", "C/C++ Header"),
    (".rb", "Ruby"),
    (".php", "PHP"),
    (".swift", "Swift"),
    (".fs", "F#"),
    (".vb", "Visual Basic"),
    (".lua", "Lua"),
    (".r", "R"),
    (".scala", "Scala"),
    (".ex", "Elixir"),
    (".exs", "Elixir"),
    (".erl", "Erlang"),
    (".hs", "Haskell"),
    (".ml", "OCaml"),
    // Web
    (".html", "HTML"),
    (".htm", "HTML"),
    (".css", "CSS"),
    (".scss", "SCSS"),
    (".sass", "Sass"),
    (".jsx", "React JSX"),
    (".tsx", "React TSX"),
    (".vue", "Vue"),
    (".svelte", "Svelte"),
    // Data/Config
    (".json", "JSON"),
    (".toml", "TOML"),
    (".yaml", "YAML"),
    (".yml", "YAML"),
    (".xml", "XML"),
    (".xsd", "XML Schema Definition"),
    (".ini", "INI"),
    (".url", "URL Shortcut"),
    (".lnk", "Windows Shortcut"),
    (".env", "Environment Variables"),
    (".csv", "Comma-Separated Values"),
    (".tsv", "Tab-Separated Values"),
    (".sql", "SQL Script"),
    // Docs
    (".md", "Markdown"),
    (".txt", "Text"),
    (".rst", "reStructuredText"),
    (".pdf", "PDF"),
    (".tex", "LaTeX"),
    // Shell
    (".sh", "Shell"),
    (".bash", "Shell"),
    (".zsh", "Shell"),
    (".fish", "Shell"),
    (".ps1", "PowerShell"),
    (".psm1", "PowerShell"),
    (".psd1", "PowerShell"),
    (".bat", "Batch"),
    (".cmd", "Batch"),
    // Project
    (".sln", "Visual Studio Solution"),
    (".slnx", "Visual Studio Solution"),
    (".csproj", "C# Project"),
    (".fsproj", "F# Project"),
    (".vbproj", "VB Project"),
    (".props", "MSBuild"),
    (".targets", "MSBuild"),
    // Images
    (".png", "Image"),
    (".jpg", "Image"),
    (".jpeg", "Image"),
    (".gif", "Image"),
    (".svg", "SVG Image"),
    (".webp", "Image"),
    (".ico", "Icon"),
    (".bmp", "Image"),
    (".pdn", "Paint.NET Image"),
    // Archives
    (".zip", "Archive"),
    (".tar", "Archive"),
    (".gz", "Archive"),
    (".7z", "Archive"),
    (".rar", "Archive"),
    (".nupkg", "NuGet Package"),
    (".snupkg", "NuGet Symbols Package"),
    (".jar", "Java Archive"),
    (".war", "Java Web Archive"),
    (".ear", "Java Enterprise Archive"),
    (".docx", "Word Document"),
    (".xlsx", "Excel Spreadsheet"),
    (".pptx", "PowerPoint"),
    (".odt", "OpenDocument Text"),
    (".ods", "OpenDocument Spreadsheet"),
    (".odp", "OpenDocument Presentation"),
    (".apk", "Android Package"),
    (".vsix", "VS Extension"),
    (".whl", "Python Wheel"),
    (".epub", "eBook"),
    // Binaries
    (".exe", "Executable"),
    (".dll", "Library"),
    (".so", "Shared Library"),
    (".dylib", "Shared Library"),
    (".pdb", "Debug Symbols"),
    (".wasm", "WebAssembly"),
];

/// Port of `GetFileTypeLabel`: filename lookup first (exact case, matching
/// C#'s Ordinal comparer), then extension (case-insensitive).
#[must_use]
pub fn get_file_type_label(path: &str) -> Option<&'static str> {
    let file_name = crate::app::dialogs::file_name_of(path);

    if let Some((_, label)) = FILENAME_LABELS.iter().find(|(name, _)| *name == file_name) {
        return Some(label);
    }

    let ext = std::path::Path::new(path)
        .extension()
        .and_then(|e| e.to_str())
        .map(|e| format!(".{e}"))?;

    EXTENSION_LABELS
        .iter()
        .find(|(known, _)| known.eq_ignore_ascii_case(&ext))
        .map(|(_, label)| *label)
}

#[cfg(test)]
mod tests {
    use super::get_file_type_label;

    #[test]
    fn extension_labels_per_category() {
        assert_eq!(get_file_type_label("src/App.cs"), Some("C#"));
        assert_eq!(get_file_type_label("README.md"), Some("Markdown"));
        assert_eq!(get_file_type_label("data.csv"), Some("Comma-Separated Values"));
        assert_eq!(get_file_type_label("Cargo.toml"), Some("TOML"));
        assert_eq!(get_file_type_label("app.exe"), Some("Executable"));
        assert_eq!(get_file_type_label("photo.svg"), Some("SVG Image"));
        assert_eq!(get_file_type_label("Cargo.lock"), None);
    }

    #[test]
    fn extension_match_is_case_insensitive() {
        assert_eq!(get_file_type_label("README.MD"), Some("Markdown"));
        assert_eq!(get_file_type_label("Program.CS"), Some("C#"));
        assert_eq!(get_file_type_label("a.Js"), Some("JavaScript"));
    }

    #[test]
    fn filename_labels_win_exactly() {
        assert_eq!(get_file_type_label("Dockerfile"), Some("Docker"));
        assert_eq!(get_file_type_label("dockerfile"), None);
        assert_eq!(get_file_type_label("Makefile"), Some("Makefile"));
        assert_eq!(get_file_type_label("Gemfile"), Some("Ruby"));
        // Filename match is on the whole name, not a prefix
        assert_eq!(get_file_type_label("Gemfile.txt"), Some("Text"));
        assert_eq!(get_file_type_label("src/Dockerfile"), Some("Docker"));
    }
}
