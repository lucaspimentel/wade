//! Port of `LanguageMap`: picks the tokenizer for a file path by full file
//! name, then extension (both ASCII case-insensitive), then a few
//! extensionless shell-like names.

use super::languages::{
    c::CLanguage, cpp::CppLanguage, csharp::CSharpLanguage, go::GoLanguage, java::JavaLanguage,
    javascript::JavaScriptLanguage, powershell::PowerShellLanguage, python::PythonLanguage, rust::RustLanguage,
    shell::ShellLanguage, typescript::TypeScriptLanguage,
};
use super::Language;

static C: CLanguage = CLanguage;
static CPP: CppLanguage = CppLanguage;
static CSHARP: CSharpLanguage = CSharpLanguage;
static JAVASCRIPT: JavaScriptLanguage = JavaScriptLanguage;
static TYPESCRIPT: TypeScriptLanguage = TypeScriptLanguage;
static PYTHON: PythonLanguage = PythonLanguage;
static GO: GoLanguage = GoLanguage;
static RUST: RustLanguage = RustLanguage;
static JAVA: JavaLanguage = JavaLanguage;
static SHELL: ShellLanguage = ShellLanguage;
static POWERSHELL: PowerShellLanguage = PowerShellLanguage;

/// Port of `ByExtension` (keys lowercase, with the dot).
fn by_extension(extension: &str) -> Option<&'static dyn Language> {
    let language: &'static dyn Language = match extension {
        ".c" => &C,
        ".h" | ".cpp" | ".cxx" | ".cc" | ".c++" | ".hpp" | ".hxx" | ".hh" | ".h++" | ".ino" => &CPP,
        ".cs" | ".csx" => &CSHARP,
        ".js" | ".mjs" | ".cjs" | ".jsx" => &JAVASCRIPT,
        ".ts" | ".tsx" => &TYPESCRIPT,
        ".py" => &PYTHON,
        ".go" => &GO,
        ".rs" => &RUST,
        ".java" => &JAVA,
        ".sh" | ".bash" | ".zsh" | ".fish" => &SHELL,
        ".ps1" | ".psm1" | ".psd1" => &POWERSHELL,
        _ => return None,
    };

    Some(language)
}

/// Port of `ByFilename` (keys lowercase).
fn by_file_name(_file_name: &str) -> Option<&'static dyn Language> {
    None
}

/// Port of `NoExtensionShellNames`.
const NO_EXTENSION_SHELL_NAMES: [&str; 3] = ["makefile", "jenkinsfile", "brewfile"];

/// Port of `LanguageMap.GetLanguage`.
#[must_use]
pub fn get_language(file_path: &str) -> Option<&'static dyn Language> {
    let name = file_name(file_path);
    let lower_name = name.to_ascii_lowercase();

    // Full file name first (dotfiles such as .gitignore, whose "extension"
    // is the whole name)
    if let Some(language) = by_file_name(&lower_name) {
        return Some(language);
    }

    let extension = extension(name).to_ascii_lowercase();

    if !extension.is_empty() {
        return by_extension(&extension);
    }

    NO_EXTENSION_SHELL_NAMES.contains(&lower_name.as_str()).then_some(&SHELL as &'static dyn Language)
}

/// `Path.GetFileName`: the text after the last directory separator.
fn file_name(path: &str) -> &str {
    path.rsplit(crate::search::is_separator).next().unwrap_or(path)
}

/// `Path.GetExtension` of a file name: from the last '.', or empty when
/// there is none or it is the final character.
fn extension(name: &str) -> &str {
    match name.rfind('.') {
        Some(index) if index + 1 < name.len() => &name[index..],
        _ => "",
    }
}

#[cfg(test)]
mod tests {
    use super::{extension, file_name};

    #[test]
    fn path_helpers_match_dotnet() {
        assert_eq!(extension("a.tar.gz"), ".gz");
        assert_eq!(extension("trailing."), "");
        assert_eq!(extension(".cs"), ".cs");
        assert_eq!(extension("noext"), "");
        assert_eq!(file_name("Makefile"), "Makefile");
        let sep = std::path::MAIN_SEPARATOR;
        assert_eq!(file_name(&format!("src{sep}lib.rs")), "lib.rs");
    }
}
