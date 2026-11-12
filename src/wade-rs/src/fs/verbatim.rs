//! File-system calls in the spelling that reaches the entry they name.
//!
//! Win32 parses a path's last component before the file system sees it, so a
//! directory entry whose name Windows cannot express is unreachable by its
//! ordinary spelling:
//!
//! - `D:\dir\NUL` names the NUL device. Win32 documents the same for `CON`,
//!   `PRN`, `AUX`, `COM1`-`COM9` and `LPT1`-`LPT9`, with or without an
//!   extension (`nul.txt`); measured on Windows 11 build 26200, only `NUL`
//!   still resolves to a device once the path carries a directory and the
//!   others create ordinary files. That rule has changed between Windows
//!   versions, so wade treats every documented name the same way.
//! - A trailing dot or space is stripped, so `D:\dir\Report.` reads whatever
//!   `D:\dir\Report` holds instead.
//!
//! Such an entry still appears in a directory listing (FindFirstFile reports
//! the on-disk name), but a call made with the ordinary spelling fails,
//! reaches a different file, or - worst - writes to a device and reports
//! success. The `\\?\` verbatim prefix turns the parsing off, so only that
//! spelling reaches the entry itself.
//!
//! Every file-system call in wade goes through this module (`clippy.toml`
//! bans `std::fs` outside it) so each one picks a spelling that reaches what
//! it names. The rule is Win32's, so on any other platform every helper below
//! is plain `std::fs`:
//!
//! - A path with a component Windows cannot express uses the verbatim
//!   spelling and nothing else, because the ordinary spelling would reach the
//!   device or another file instead of failing. When such a path has no
//!   verbatim spelling (a relative path, or one rooted without a drive), the
//!   call fails with `InvalidInput` instead of running anyway.
//! - Any other path uses the ordinary spelling, and a failure is retried with
//!   the verbatim spelling. That covers names these rules do not know about
//!   (Win32 keeps a second list for the superscript `COM`/`LPT` digits) and
//!   costs one extra call, only after a call has already failed.
//! - Calls that create, write or rename behave the same way, so the retry
//!   never runs the ordinary spelling against a name that needs the verbatim
//!   one. A retry calls the same function again, which the create, copy and
//!   remove calls tolerate: the attempt it repeats had already failed.
//!
//! Nothing here follows a link or rewrites a path:
//!
//! - `canonicalize` is deliberately left out of the retry scheme: it already
//!   answers in the verbatim form on Windows, and the drive list compares
//!   drive roots lexically for that reason (see `KNOWN_DEVIATIONS.md`).
//! - `read_dir` returns the ordinary spelling of the entries it lists, and
//!   paths handed to other programs (ShellExecute, git, the terminal, 7z)
//!   keep the ordinary spelling, which those programs expect.

// The wrappers below are the one place allowed to call `std::fs` directly;
// clippy.toml bans it everywhere else.
#![allow(clippy::disallowed_methods)]

use std::io;
use std::path::{Path, PathBuf};

/// The Windows rule for one name: `Some(reason)` when Win32 cannot express
/// `name` (`CON`, `nul.txt`, `COM1`, a trailing dot or space). Compiled on
/// every OS so it can be tested anywhere.
#[must_use]
pub fn windows_name_problem(name: &str) -> Option<String> {
    const RESERVED: [&str; 22] = [
        "CON", "PRN", "AUX", "NUL", "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9", "LPT1",
        "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9",
    ];

    let base = name.split('.').next().unwrap_or(name).trim_end_matches(' ');
    if RESERVED.iter().any(|reserved| reserved.eq_ignore_ascii_case(base)) {
        return Some(format!("'{name}' is a reserved name on Windows"));
    }

    if name.ends_with(['.', ' ']) {
        return Some("Name cannot end with a dot or space".to_string());
    }

    None
}

/// True when any component of `path` is a name Win32 cannot express, so the
/// ordinary spelling of the path does not necessarily reach what it names.
/// This is Win32's rule; the helpers that act on it are Windows-only.
#[must_use]
pub fn needs_verbatim(path: &str) -> bool {
    path.split(['\\', '/']).any(|component| windows_name_problem(component).is_some())
}

/// The `\\?\` spelling of `path`, or `None` when the path has none: it is
/// relative (`dir\file`, `C:file`) or rooted without a drive (`\file`).
///
/// A path that has one is first written the way Win32 reads an ordinary path,
/// so the verbatim spelling names the same entry: forward slashes become
/// separators, repeated and trailing separators collapse, and `.`/`..`
/// resolve lexically. Win32 resolves those in the path text, without
/// consulting the file system, so a `..` after a link is not the link's
/// target's parent.
#[must_use]
pub fn verbatim_path(path: &str) -> Option<String> {
    let path = path.replace('/', "\\");
    if path.starts_with(r"\\?\") || path.starts_with(r"\\.\") {
        return None;
    }

    let (root, rest) = split_root(&path)?;

    let mut components: Vec<&str> = Vec::new();
    for component in rest.split('\\') {
        match component {
            "" | "." => {}
            ".." => {
                components.pop();
            }
            name => components.push(name),
        }
    }

    Some(format!("{root}{}", components.join("\\")))
}

/// The verbatim prefix and the part of `path` (already separator-normalized)
/// that `..` cannot climb out of, which keeps its trailing separator, plus the
/// rest of the path: `\\?\D:\` or `\\?\UNC\server\share\`. `None` for a path
/// with no such root.
fn split_root(path: &str) -> Option<(String, &str)> {
    if let Some(unc) = path.strip_prefix(r"\\") {
        // `\\server\share`: both names are the root, and `..` cannot climb
        // above a share
        let mut parts = unc.splitn(3, '\\');
        let server = parts.next().filter(|part| !part.is_empty())?;
        let share = parts.next().filter(|part| !part.is_empty())?;
        return Some((format!(r"\\?\UNC\{server}\{share}\"), parts.next().unwrap_or("")));
    }

    let bytes = path.as_bytes();
    let drive_absolute = bytes.len() >= 3 && bytes[0].is_ascii_alphabetic() && bytes[1] == b':' && bytes[2] == b'\\';
    if !drive_absolute {
        return None;
    }

    Some((format!(r"\\?\{}\", &path[..2]), &path[3..]))
}

/// The spelling to reach `path`: the verbatim form when the ordinary spelling
/// cannot, else the path itself. Windows only; elsewhere there is no second
/// spelling. Used by the Win32 calls this module does not wrap.
#[must_use]
pub fn reach_spelling(path: &str) -> String {
    if !cfg!(windows) {
        return path.to_string();
    }

    if needs_verbatim(path)
        && let Some(verbatim) = verbatim_path(path)
    {
        return verbatim;
    }

    path.to_string()
}

/// The error for a path whose name Win32 resolves elsewhere and which has no
/// verbatim spelling. The call must not run: the ordinary spelling would reach
/// the device or another entry, and succeed.
fn unspellable(path: &str) -> Option<io::Error> {
    if !cfg!(windows) || !needs_verbatim(path) || verbatim_path(path).is_some() {
        return None;
    }

    Some(io::Error::new(
        io::ErrorKind::InvalidInput,
        format!("Windows resolves a component of '{path}' before the file system sees it"),
    ))
}

/// The retry for a path that was tried with its ordinary spelling, `None`
/// when there is nothing to retry with. A path already tried verbatim is
/// never retried with the ordinary spelling: that can reach the device or
/// another file instead of failing.
fn second_spelling(path: &str, first: &str) -> Option<String> {
    if !cfg!(windows) {
        return None;
    }

    (first == path).then(|| verbatim_path(path)).flatten()
}

/// Runs one call, retrying with the verbatim spelling when the ordinary one
/// failed. A retry that also fails reports the first error: it came from the
/// spelling that should have worked.
fn call<T>(path: &str, op: impl Fn(&str) -> io::Result<T>) -> io::Result<T> {
    if let Some(error) = unspellable(path) {
        return Err(error);
    }

    let first = reach_spelling(path);
    match op(&first) {
        Ok(value) => Ok(value),
        Err(first_error) => match second_spelling(path, &first) {
            Some(second) => op(&second).map_err(|_| first_error),
            None => Err(first_error),
        },
    }
}

/// `call` for the calls that take two paths.
fn call_pair<T>(from: &str, to: &str, op: impl Fn(&str, &str) -> io::Result<T>) -> io::Result<T> {
    if let Some(error) = unspellable(from).or_else(|| unspellable(to)) {
        return Err(error);
    }

    let from_first = reach_spelling(from);
    let to_first = reach_spelling(to);
    match op(&from_first, &to_first) {
        Ok(value) => Ok(value),
        Err(first_error) => {
            let from_second = second_spelling(from, &from_first);
            let to_second = second_spelling(to, &to_first);
            if from_second.is_none() && to_second.is_none() {
                return Err(first_error);
            }

            let from_second = from_second.unwrap_or(from_first);
            let to_second = to_second.unwrap_or(to_first);
            op(&from_second, &to_second).map_err(|_| first_error)
        }
    }
}

/// A directory entry whose path keeps the spelling of the directory it was
/// listed from: a verbatim prefix must not leak into the listing, the history
/// or a bookmark. The name and the attributes come from the enumeration
/// itself, so they are right for an entry whose own name needs the verbatim
/// spelling.
#[derive(Debug)]
pub struct Entry {
    path: String,
    inner: std::fs::DirEntry,
}

impl Entry {
    /// The entry's name, as the operating system reports it.
    #[must_use]
    pub fn file_name(&self) -> std::ffi::OsString {
        self.inner.file_name()
    }

    /// The entry's full path.
    #[must_use]
    pub fn path(&self) -> String {
        self.path.clone()
    }

    pub fn file_type(&self) -> io::Result<std::fs::FileType> {
        self.inner.file_type()
    }

    pub fn metadata(&self) -> io::Result<std::fs::Metadata> {
        self.inner.metadata()
    }
}

/// Lists `dir`, with entries that keep the spelling of the directory they
/// were listed from. The name and the attributes come from the enumeration
/// itself, so they are right for an entry whose own name needs the verbatim
/// spelling.
pub fn read_dir(dir: &str) -> io::Result<impl Iterator<Item = io::Result<Entry>> + use<>> {
    let listing = call(dir, |path| std::fs::read_dir(path))?;
    let dir = dir.to_string();

    Ok(listing.map(move |entry| {
        let entry = entry?;
        let path = Path::new(&dir).join(entry.file_name()).to_string_lossy().into_owned();
        Ok(Entry { path, inner: entry })
    }))
}

pub fn metadata(path: &str) -> io::Result<std::fs::Metadata> {
    call(path, |path| std::fs::metadata(path))
}

pub fn symlink_metadata(path: &str) -> io::Result<std::fs::Metadata> {
    call(path, |path| std::fs::symlink_metadata(path))
}

pub fn read(path: &str) -> io::Result<Vec<u8>> {
    call(path, |path| std::fs::read(path))
}

pub fn read_to_string(path: &str) -> io::Result<String> {
    call(path, |path| std::fs::read_to_string(path))
}

pub fn read_link(path: &str) -> io::Result<PathBuf> {
    call(path, |path| std::fs::read_link(path))
}

pub fn write(path: &str, contents: impl AsRef<[u8]>) -> io::Result<()> {
    call(path, |path| std::fs::write(path, contents.as_ref()))
}

pub fn open(path: &str) -> io::Result<std::fs::File> {
    call(path, |path| std::fs::File::open(path))
}

pub fn create(path: &str) -> io::Result<std::fs::File> {
    call(path, |path| std::fs::File::create(path))
}

pub fn open_with(options: &std::fs::OpenOptions, path: &str) -> io::Result<std::fs::File> {
    call(path, |path| options.open(path))
}

pub fn create_dir(path: &str) -> io::Result<()> {
    call(path, |path| std::fs::create_dir(path))
}

pub fn create_dir_all(path: &str) -> io::Result<()> {
    call(path, |path| std::fs::create_dir_all(path))
}

pub fn remove_file(path: &str) -> io::Result<()> {
    call(path, |path| std::fs::remove_file(path))
}

pub fn remove_dir(path: &str) -> io::Result<()> {
    call(path, |path| std::fs::remove_dir(path))
}

pub fn remove_dir_all(path: &str) -> io::Result<()> {
    call(path, |path| std::fs::remove_dir_all(path))
}

pub fn set_permissions(path: &str, permissions: std::fs::Permissions) -> io::Result<()> {
    call(path, |path| std::fs::set_permissions(path, permissions.clone()))
}

pub fn canonicalize(path: &str) -> io::Result<PathBuf> {
    call(path, |path| std::fs::canonicalize(path))
}

pub fn rename(from: &str, to: &str) -> io::Result<()> {
    call_pair(from, to, |from, to| std::fs::rename(from, to))
}

pub fn copy(from: &str, to: &str) -> io::Result<u64> {
    call_pair(from, to, |from, to| std::fs::copy(from, to))
}

/// True when `path` is a directory, following a link to one.
#[must_use]
pub fn is_dir(path: &str) -> bool {
    metadata(path).is_ok_and(|metadata| metadata.is_dir())
}

/// True when `path` is a file, following a link to one.
#[must_use]
pub fn is_file(path: &str) -> bool {
    metadata(path).is_ok_and(|metadata| metadata.is_file())
}

/// True when `path` exists, following a link to one.
#[must_use]
pub fn exists(path: &str) -> bool {
    metadata(path).is_ok()
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn verbatim_path_spells_drive_absolute_and_unc_paths() {
        assert_eq!(verbatim_path(r"D:\dir\NUL").as_deref(), Some(r"\\?\D:\dir\NUL"));
        assert_eq!(verbatim_path(r"C:\a\b.txt").as_deref(), Some(r"\\?\C:\a\b.txt"));
        assert_eq!(verbatim_path("D:/dir/NUL").as_deref(), Some(r"\\?\D:\dir\NUL"));
        assert_eq!(verbatim_path(r"D:\").as_deref(), Some(r"\\?\D:\"));
        assert_eq!(verbatim_path(r"\\server\share\NUL").as_deref(), Some(r"\\?\UNC\server\share\NUL"));
        assert_eq!(verbatim_path(r"\\server\share").as_deref(), Some(r"\\?\UNC\server\share\"));
    }

    #[test]
    fn verbatim_path_normalizes_the_way_win32_reads_a_path() {
        assert_eq!(verbatim_path(r"D:\dir\NUL\").as_deref(), Some(r"\\?\D:\dir\NUL"), "trailing separator");
        assert_eq!(verbatim_path(r"D:\dir\\NUL").as_deref(), Some(r"\\?\D:\dir\NUL"), "repeated separator");
        assert_eq!(verbatim_path(r"D:\dir\.\NUL").as_deref(), Some(r"\\?\D:\dir\NUL"), "dot component");
        assert_eq!(verbatim_path(r"D:\dir\sub\..\NUL").as_deref(), Some(r"\\?\D:\dir\NUL"), "parent component");
        assert_eq!(verbatim_path(r"D:\..\NUL").as_deref(), Some(r"\\?\D:\NUL"), "above the drive root");
        assert_eq!(verbatim_path("D:/dir/NUL").as_deref(), Some(r"\\?\D:\dir\NUL"), "forward slashes");
    }

    #[test]
    fn verbatim_path_refuses_a_path_with_no_root() {
        for path in
            [r"\\?\D:\dir\NUL", r"\\.\NUL", r"dir\NUL", r"\rooted\NUL", r"C:NUL", r"\\server", r"\\server\", r""]
        {
            assert_eq!(verbatim_path(path), None, "{path}");
        }
    }

    #[test]
    fn needs_verbatim_finds_a_component_windows_cannot_express() {
        for path in [
            r"D:\dir\NUL", r"D:\dir\nul.txt", r"D:\dir\COM1", r"D:\dir\lpt9.log", r"D:\dir\trailing.",
            r"D:\dir\trailing ", r"D:\dir.\file.txt", r"/home/nul",
        ] {
            assert!(needs_verbatim(path), "{path}");
        }

        for path in [
            r"D:\dir\console", r"D:\dir\con2", r"D:\dir\COM10", r"D:\dir\my.con", r"D:\dir\file.txt", r"D:\dir",
            "/home/notes",
        ] {
            assert!(!needs_verbatim(path), "{path}");
        }
    }

    /// A path Win32 cannot spell (relative, so no verbatim form) is refused
    /// instead of reaching the device or a neighbour.
    #[test]
    fn unspellable_paths_are_refused() {
        let problem = unspellable(r"dir\trailing.").map(|error| error.kind());
        assert_eq!(problem, if cfg!(windows) { Some(io::ErrorKind::InvalidInput) } else { None });
        assert_eq!(reach_spelling("/home/nul"), "/home/nul", "no verbatim form: used as it is");
    }

    /// The verbatim spelling is a Win32 rule: elsewhere the helpers hand
    /// `std::fs` what they were given, including a `//`-rooted path that would
    /// otherwise look like a UNC share.
    #[cfg(not(windows))]
    #[test]
    fn the_helpers_do_not_rewrite_a_path_off_windows() {
        for path in ["/home/nul", "/home/report.", "//server/share/NUL"] {
            assert_eq!(reach_spelling(path), path);
            assert_eq!(second_spelling(path, path), None);
            assert!(unspellable(path).is_none());
        }
    }

    /// A scratch directory that cleans itself up even when an assertion
    /// fails: a leftover entry with a name Windows cannot express would be
    /// awkward to remove by hand.
    #[cfg(windows)]
    struct Scratch(PathBuf);

    #[cfg(windows)]
    impl Drop for Scratch {
        fn drop(&mut self) {
            let _ = remove_dir_all(&self.0.to_string_lossy());
        }
    }

    #[cfg(windows)]
    fn scratch(name: &str) -> (Scratch, String) {
        let dir = std::env::temp_dir().join(format!("wade-verbatim-test-{}-{name}", std::process::id()));
        let _ = remove_dir_all(&dir.to_string_lossy());
        std::fs::create_dir_all(&dir).expect("mkdir");
        let path = dir.to_string_lossy().into_owned();
        (Scratch(dir), path)
    }

    /// The names listed in `dir`.
    #[cfg(windows)]
    fn names_in(dir: &str) -> Vec<String> {
        read_dir(dir)
            .expect("list")
            .map(|entry| entry.expect("entry").file_name().to_string_lossy().into_owned())
            .collect()
    }

    /// Creates `name` in `dir` through the spelling only Windows accepts for
    /// these names.
    #[cfg(windows)]
    fn write_reserved(dir: &str, name: &str, contents: &[u8]) {
        let path = format!("{dir}\\{name}");
        std::fs::write(verbatim_path(&path).expect("verbatim spelling"), contents).expect("write");
    }

    #[cfg(windows)]
    #[test]
    fn calls_reach_a_device_named_entry() {
        let (_scratch, dir) = scratch("device");
        write_reserved(&dir, "NUL", b"payload");
        assert!(needs_verbatim(&format!("{dir}\\NUL")));
        assert!(std::fs::symlink_metadata(format!("{dir}\\NUL")).is_err(), "the ordinary spelling misses it");

        assert_eq!(read(&format!("{dir}\\NUL")).expect("read"), b"payload");
        assert_eq!(metadata(&format!("{dir}\\NUL")).expect("metadata").len(), 7);

        assert_eq!(names_in(&dir), ["NUL"]);
        let entry = read_dir(&dir).expect("list").next().expect("one entry").expect("entry");
        assert_eq!(entry.path(), format!("{dir}\\NUL"), "the caller's spelling is kept");
        assert_eq!(entry.metadata().expect("entry metadata").len(), 7);

        remove_file(&format!("{dir}\\NUL")).expect("delete");
        assert!(names_in(&dir).is_empty());
    }

    #[cfg(windows)]
    #[test]
    fn calls_reach_a_trailing_dot_entry_without_reading_its_neighbour() {
        let (_scratch, dir) = scratch("trailing");
        std::fs::write(format!("{dir}\\report"), b"neighbour").expect("write neighbour");
        write_reserved(&dir, "report.", b"dotted");

        // The ordinary spelling of `report.` is `report`, a different file
        assert_eq!(std::fs::read(format!("{dir}\\report.")).expect("plain read"), b"neighbour");
        assert_eq!(read(&format!("{dir}\\report.")).expect("read"), b"dotted");
        assert_eq!(metadata(&format!("{dir}\\report.")).expect("metadata").len(), 6);

        remove_file(&format!("{dir}\\report.")).expect("delete");
        assert_eq!(read(&format!("{dir}\\report")).expect("neighbour survives"), b"neighbour");
    }

    #[cfg(windows)]
    #[test]
    fn writes_and_copies_reach_a_device_named_destination() {
        let (_scratch, dir) = scratch("write");
        let source = format!("{dir}\\source.txt");
        std::fs::write(&source, b"payload").expect("write source");

        // The ordinary spelling of a NUL destination reports success and
        // sends the bytes to the device, so the wrapper must not use it
        std::fs::copy(&source, format!("{dir}\\NUL")).expect("plain copy");
        assert_eq!(names_in(&dir).len(), 1, "the plain copy created nothing");

        assert_eq!(copy(&source, &format!("{dir}\\NUL")).expect("copy"), 7);
        assert_eq!(read(&format!("{dir}\\NUL")).expect("read"), b"payload");

        write(&format!("{dir}\\NUL"), b"kept").expect("write");
        assert_eq!(read(&format!("{dir}\\NUL")).expect("read"), b"kept");

        rename(&format!("{dir}\\NUL"), &format!("{dir}\\PRN")).expect("rename");
        assert!(read(&format!("{dir}\\NUL")).is_err());
        assert_eq!(read(&format!("{dir}\\PRN")).expect("read"), b"kept");
    }

    #[cfg(windows)]
    #[test]
    fn a_relative_path_with_a_reserved_name_is_refused_before_any_call() {
        // Nothing may be created for a spelling Win32 resolves elsewhere
        let error = write(r"dir\trailing.", b"payload").expect_err("refused");
        assert_eq!(error.kind(), io::ErrorKind::InvalidInput);

        let error = remove_file(r"dir\NUL").expect_err("refused");
        assert_eq!(error.kind(), io::ErrorKind::InvalidInput);
    }
}
