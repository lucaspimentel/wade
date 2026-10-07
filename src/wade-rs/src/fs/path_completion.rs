//! Port of `src/Wade/FileSystem/PathCompletion.cs`.

/// Expands a leading `~` to the user's home directory.
#[must_use]
pub fn expand_tilde(path: &str) -> String {
    if let Some(rest) = path.strip_prefix('~')
        && let Some(home) = std::env::var_os("USERPROFILE").or_else(|| std::env::var_os("HOME"))
    {
        return join_home(&home.to_string_lossy(), rest);
    }

    path.to_string()
}

fn join_home(home: &str, rest: &str) -> String {
    if rest.is_empty() {
        return home.to_string();
    }

    // Path.Join semantics: no separator if `rest` already starts with one
    let sep = std::path::MAIN_SEPARATOR;
    if rest.starts_with(sep) || rest.starts_with('/') {
        format!("{home}{rest}")
    } else {
        format!("{home}{sep}{rest}")
    }
}

/// Normalizes path separators to the platform's preferred separator. On
/// Windows this converts `/` to `\`; on Unix it is a no-op.
#[must_use]
pub fn normalize_separators(path: &str) -> String {
    if std::path::MAIN_SEPARATOR == '\\' && path.contains('/') {
        path.replace('/', "\\")
    } else {
        path.to_string()
    }
}

/// Given a partial path input, returns the first matching filesystem entry's
/// full path, or `None` if no match is found.
///
/// When `show_hidden` is false, hidden entries (dot-prefixed or the Windows
/// Hidden attribute) are excluded unless the user is already typing a
/// dot-prefixed name. When `show_system_files` is false on Windows, entries
/// with the System attribute are excluded.
#[must_use]
pub fn get_suggestion(input: &str, show_hidden: bool, show_system_files: bool) -> Option<String> {
    if input.is_empty() {
        return None;
    }

    let input = normalize_separators(&expand_tilde(input));

    // If input ends with a separator and the directory exists, suggest the
    // first child entry
    if input.ends_with('\\') || input.ends_with('/') {
        if std::path::Path::new(&input).is_dir() {
            return first_entry(&input, show_hidden, show_system_files);
        }

        return None;
    }

    // Split into parent + partial name
    let path = std::path::Path::new(&input);
    let parent_dir = path.parent()?;
    if !parent_dir.is_dir() {
        return None;
    }

    let partial = path.file_name().and_then(|n| n.to_str())?;
    if partial.is_empty() {
        return None;
    }

    // If the user is typing a dot-prefixed name, don't filter hidden entries
    let skip_hidden = !show_hidden && !partial.starts_with('.');
    let skip_system = !show_system_files && cfg!(windows);

    let Ok(entries) = std::fs::read_dir(parent_dir) else {
        return None;
    };

    for entry in entries.flatten() {
        let name = entry.file_name().to_string_lossy().to_string();
        let metadata = entry.metadata().ok();
        if skip_hidden && is_hidden(&name, metadata.as_ref()) {
            continue;
        }

        if skip_system && metadata.is_some_and(|m| is_system(&m)) {
            continue;
        }

        if name.to_ascii_lowercase().starts_with(&partial.to_ascii_lowercase()) {
            return Some(entry.path().to_string_lossy().to_string());
        }
    }

    None
}

fn first_entry(dir_path: &str, show_hidden: bool, show_system_files: bool) -> Option<String> {
    let skip_system = !show_system_files && cfg!(windows);

    let Ok(entries) = std::fs::read_dir(dir_path) else {
        return None;
    };

    for entry in entries.flatten() {
        let name = entry.file_name().to_string_lossy().to_string();
        let metadata = entry.metadata().ok();
        if !show_hidden && is_hidden(&name, metadata.as_ref()) {
            continue;
        }

        if skip_system && metadata.is_some_and(|m| is_system(&m)) {
            continue;
        }

        return Some(entry.path().to_string_lossy().to_string());
    }

    None
}

fn is_hidden(name: &str, metadata: Option<&std::fs::Metadata>) -> bool {
    if name.starts_with('.') {
        return true;
    }

    #[cfg(windows)]
    if let Some(metadata) = metadata {
        use std::os::windows::fs::MetadataExt;
        const FILE_ATTRIBUTE_HIDDEN: u32 = 0x2;
        return metadata.file_attributes() & FILE_ATTRIBUTE_HIDDEN != 0;
    }

    let _ = metadata;
    false
}

#[cfg(windows)]
fn is_system(metadata: &std::fs::Metadata) -> bool {
    use std::os::windows::fs::MetadataExt;
    const FILE_ATTRIBUTE_SYSTEM: u32 = 0x4;
    metadata.file_attributes() & FILE_ATTRIBUTE_SYSTEM != 0
}

#[cfg(not(windows))]
fn is_system(_metadata: &std::fs::Metadata) -> bool {
    false
}

#[cfg(test)]
mod tests {
    use super::*;
    use std::path::PathBuf;

    fn temp_dir(name: &str) -> PathBuf {
        let dir = std::env::temp_dir().join(format!("wade-pathcomp-test-{}-{name}", std::process::id()));
        let _ = std::fs::remove_dir_all(&dir);
        std::fs::create_dir_all(&dir).expect("mkdir");
        dir
    }

    #[test]
    fn suggests_prefix_match() {
        let dir = temp_dir("prefix");
        std::fs::File::create(dir.join("alpha.txt")).expect("touch");
        std::fs::File::create(dir.join("beta.txt")).expect("touch");

        let sep = std::path::MAIN_SEPARATOR;
        let input = format!("{}{sep}alp", dir.to_string_lossy());
        let suggestion = get_suggestion(&input, false, false).expect("suggestion");
        assert!(suggestion.ends_with("alpha.txt"));

        // No match
        let input = format!("{}{sep}zzz", dir.to_string_lossy());
        assert!(get_suggestion(&input, false, false).is_none());
    }

    #[test]
    fn empty_input_yields_none() {
        assert!(get_suggestion("", false, false).is_none());
    }

    #[test]
    fn trailing_separator_suggests_first_child() {
        let dir = temp_dir("trailing");
        std::fs::File::create(dir.join("child.txt")).expect("touch");

        let input = format!("{}{}", dir.to_string_lossy(), std::path::MAIN_SEPARATOR);
        let suggestion = get_suggestion(&input, false, false).expect("first child");
        assert!(suggestion.ends_with("child.txt"));
    }

    #[test]
    fn hidden_gating() {
        let dir = temp_dir("hidden");
        std::fs::File::create(dir.join(".hidden")).expect("touch");
        std::fs::File::create(dir.join("visible.txt")).expect("touch");

        let sep = std::path::MAIN_SEPARATOR;

        // Typing a dot-prefixed partial: hidden entries are not filtered
        let input = format!("{}{sep}.hid", dir.to_string_lossy());
        assert!(get_suggestion(&input, false, false).is_some());

        // Non-dot partial with show_hidden=false: hidden entries excluded,
        // visible files match
        let input = format!("{}{sep}v", dir.to_string_lossy());
        assert!(get_suggestion(&input, false, false).is_some());
        let input = format!("{}{sep}z", dir.to_string_lossy());
        assert!(get_suggestion(&input, false, false).is_none());

        // show_hidden=true: dot files match
        let input = format!("{}{sep}.h", dir.to_string_lossy());
        assert!(get_suggestion(&input, true, false).is_some());
    }

    fn home() -> String {
        std::env::var_os("USERPROFILE")
            .or_else(|| std::env::var_os("HOME"))
            .expect("home")
            .to_string_lossy()
            .into_owned()
    }

    #[test]
    fn nonexistent_parent_yields_none() {
        let missing = std::env::temp_dir().join(format!("wade-pathcomp-missing-{}", std::process::id())).join("abc");
        assert!(get_suggestion(&missing.to_string_lossy(), true, false).is_none());
    }

    #[test]
    fn matching_ignores_case() {
        let dir = temp_dir("case");
        std::fs::create_dir(dir.join("Alpha")).expect("mkdir");
        let input = dir.join("alp");
        let suggestion = get_suggestion(&input.to_string_lossy(), true, false).expect("suggestion");
        assert!(suggestion.ends_with("Alpha"), "{suggestion}");
    }

    #[test]
    fn empty_directory_with_trailing_separator_yields_none() {
        let dir = temp_dir("empty");
        let input = format!("{}{}", dir.to_string_lossy(), std::path::MAIN_SEPARATOR);
        assert!(get_suggestion(&input, true, false).is_none());
    }

    #[test]
    fn forward_slashes_still_match() {
        let dir = temp_dir("slashes");
        std::fs::create_dir(dir.join("alpha")).expect("mkdir");
        let input = format!("{}/alp", dir.to_string_lossy().replace('\\', "/"));
        let suggestion = get_suggestion(&input, true, false).expect("suggestion");
        assert!(suggestion.ends_with("alpha"), "{suggestion}");
    }

    #[test]
    fn expand_tilde_uses_the_home_directory() {
        let home = home();
        assert_eq!(expand_tilde("~"), home);
        let expanded = expand_tilde("~/Downloads");
        assert!(expanded.starts_with(&home) && expanded.ends_with("Downloads"), "{expanded}");
        assert_eq!(expand_tilde("/some/path"), "/some/path");
    }

    #[test]
    fn normalize_separators_cases() {
        assert_eq!(normalize_separators("foo"), "foo");
        let expected = if std::path::MAIN_SEPARATOR == '\\' { r"C:\Users\foo" } else { "C:/Users/foo" };
        assert_eq!(normalize_separators("C:/Users/foo"), expected);
    }

    #[test]
    fn expand_tilde_noop_for_absolute() {
        assert_eq!(expand_tilde("C:\\foo"), "C:\\foo");
        assert_eq!(expand_tilde("/foo"), "/foo");
        let expanded = expand_tilde("~");
        assert!(!expanded.is_empty());
        assert!(!expanded.starts_with('~'));
    }
}
