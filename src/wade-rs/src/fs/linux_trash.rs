//! Rust-only: moving items to the Trash on Linux (and other non-macOS Unix)
//! per the freedesktop.org Trash specification 1.0:
//! <https://specifications.freedesktop.org/trash-spec/latest/>
//!
//! - Items on the same device as the home trash (`$XDG_DATA_HOME/Trash`,
//!   default `~/.local/share/Trash`) go there, with an absolute `Path=`.
//! - Items on another mount go to that mount's top directory: `.Trash/$uid`
//!   when `.Trash` is a sticky directory (not a symlink), else `.Trash-$uid`
//!   (created 0700). `Path=` is relative to the top directory there.
//! - Nothing is copied across devices: when no trash on the item's device
//!   is usable, the item stays where it is and the error is returned.
//!
//! The `.trashinfo` file is created exclusively before the item is renamed
//! into `files/`, which reserves the name; it is removed again when the
//! rename fails. The optional `directorysizes` cache is not written.

use std::ffi::{OsStr, OsString};
use std::fs::DirBuilder;
use std::io::{self, Write};
use std::os::unix::ffi::{OsStrExt, OsStringExt};
use std::os::unix::fs::{DirBuilderExt, MetadataExt, OpenOptionsExt};
use std::path::{Path, PathBuf};

/// Moves `path` (a link itself, not its target) to the Trash; returns where
/// it went.
pub fn trash(path: &str) -> io::Result<PathBuf> {
    let data_home = std::env::var_os("XDG_DATA_HOME")
        .filter(|dir| Path::new(dir).is_absolute())
        .map(PathBuf::from)
        .or_else(|| std::env::var_os("HOME").map(|home| Path::new(&home).join(".local").join("share")))
        .ok_or_else(|| io::Error::new(io::ErrorKind::NotFound, "no home directory for the Trash"))?;

    // SAFETY: geteuid has no preconditions and cannot fail
    let uid = unsafe { libc::geteuid() };
    trash_with(Path::new(path), &data_home, uid)
}

/// `trash` with the home trash's parent and the user id passed in.
fn trash_with(path: &Path, data_home: &Path, uid: u32) -> io::Result<PathBuf> {
    let name = path.file_name().ok_or_else(|| invalid("nothing to trash"))?;
    let parent = match path.parent() {
        Some(parent) if !parent.as_os_str().is_empty() => parent.canonicalize()?,
        _ => std::env::current_dir()?,
    };
    let original = parent.join(name);

    // Fails for a missing item before anything is written
    std::fs::symlink_metadata(&original)?;
    let device = std::fs::metadata(&parent)?.dev();

    let home_trash = data_home.join("Trash");
    let home_trash_device = DirBuilder::new()
        .recursive(true)
        .mode(0o700)
        .create(&home_trash)
        .and_then(|()| std::fs::metadata(&home_trash))
        .map(|metadata| metadata.dev());

    if home_trash_device.as_ref().is_ok_and(|&dev| dev == device) {
        return move_into(&home_trash, &original, original.as_os_str());
    }

    let top = top_dir(&parent, device)?;
    let trash_dir = top_dir_trash(&top, uid)?;
    let relative = original.strip_prefix(&top).map_err(|_| invalid("item is outside its mount"))?;
    move_into(&trash_dir, &original, relative.as_os_str())
}

/// The highest ancestor of `dir` (canonical) still on `device`: the mount's
/// top directory.
fn top_dir(dir: &Path, device: u64) -> io::Result<PathBuf> {
    let mut top = dir.to_path_buf();

    while let Some(parent) = top.parent() {
        if std::fs::metadata(parent)?.dev() != device {
            break;
        }

        top = parent.to_path_buf();
    }

    Ok(top)
}

/// The trash directory under a mount's top directory, created if needed:
/// `$top/.Trash/$uid` when `$top/.Trash` is a sticky, non-symlink
/// directory, else `$top/.Trash-$uid`.
fn top_dir_trash(top: &Path, uid: u32) -> io::Result<PathBuf> {
    let shared = top.join(".Trash");
    let shared_ok = std::fs::symlink_metadata(&shared)
        .is_ok_and(|metadata| metadata.is_dir() && !metadata.file_type().is_symlink() && metadata.mode() & 0o1000 != 0);

    if shared_ok {
        let dir = shared.join(uid.to_string());
        if ensure_private_dir(&dir, uid).is_ok() {
            return Ok(dir);
        }
    }

    let dir = top.join(format!(".Trash-{uid}"));
    ensure_private_dir(&dir, uid)?;
    Ok(dir)
}

/// Creates `dir` (0700) if missing; it must be a real directory owned by
/// `uid`.
fn ensure_private_dir(dir: &Path, uid: u32) -> io::Result<()> {
    match DirBuilder::new().mode(0o700).create(dir) {
        Ok(()) => {}
        Err(err) if err.kind() == io::ErrorKind::AlreadyExists => {}
        Err(err) => return Err(err),
    }

    let metadata = std::fs::symlink_metadata(dir)?;
    if !metadata.is_dir() || metadata.file_type().is_symlink() || metadata.uid() != uid {
        return Err(invalid("unusable trash directory"));
    }

    Ok(())
}

/// Reserves a name in `trash_dir` with its `.trashinfo`, then renames
/// `original` into `files/`.
fn move_into(trash_dir: &Path, original: &Path, info_path: &OsStr) -> io::Result<PathBuf> {
    let files = trash_dir.join("files");
    let info = trash_dir.join("info");
    for dir in [&files, &info] {
        DirBuilder::new().recursive(true).mode(0o700).create(dir)?;
    }

    let name = original.file_name().ok_or_else(|| invalid("nothing to trash"))?;
    let contents = trash_info(info_path, &chrono::Local::now().format("%Y-%m-%dT%H:%M:%S").to_string());

    for n in 1u32.. {
        let candidate = numbered_name(name, n);
        let destination = files.join(&candidate);
        let mut info_name = candidate.clone();
        info_name.push(".trashinfo");
        let info_file = info.join(info_name);

        if std::fs::symlink_metadata(&destination).is_ok() {
            continue;
        }

        let mut file = match std::fs::OpenOptions::new().write(true).create_new(true).mode(0o600).open(&info_file) {
            Ok(file) => file,
            Err(err) if err.kind() == io::ErrorKind::AlreadyExists => continue,
            Err(err) => return Err(err),
        };

        let moved = file
            .write_all(contents.as_bytes())
            .and_then(|()| file.sync_all())
            .and_then(|()| std::fs::rename(original, &destination));

        return match moved {
            Ok(()) => Ok(destination),
            Err(err) => {
                let _ = std::fs::remove_file(&info_file);
                Err(err)
            }
        };
    }

    Err(invalid("no free name in the Trash"))
}

/// `name` for n=1, then gio style: `a.txt` -> `a.2.txt`, `dir` -> `dir.2`,
/// `.bashrc` -> `.bashrc.2`.
fn numbered_name(name: &OsStr, n: u32) -> OsString {
    if n == 1 {
        return name.to_os_string();
    }

    let bytes = name.as_bytes();
    let split = bytes.iter().rposition(|&b| b == b'.').filter(|&dot| dot > 0).unwrap_or(bytes.len());
    let mut numbered = bytes[..split].to_vec();
    numbered.extend_from_slice(format!(".{n}").as_bytes());
    numbered.extend_from_slice(&bytes[split..]);
    OsString::from_vec(numbered)
}

/// The `.trashinfo` contents.
fn trash_info(path: &OsStr, deletion_date: &str) -> String {
    format!("[Trash Info]\nPath={}\nDeletionDate={deletion_date}\n", escape_path(path))
}

/// Percent-encodes a path's bytes as an RFC 2396 path (`/` and unreserved
/// characters stay; everything else, including non-UTF-8 bytes, is `%XX`).
fn escape_path(path: &OsStr) -> String {
    let mut escaped = String::new();

    for &byte in path.as_bytes() {
        if byte.is_ascii_alphanumeric() || b"/-_.!~*'()".contains(&byte) {
            escaped.push(char::from(byte));
        } else {
            escaped.push_str(&format!("%{byte:02X}"));
        }
    }

    escaped
}

fn invalid(message: &str) -> io::Error {
    io::Error::other(message.to_string())
}

#[cfg(test)]
mod tests {
    use super::*;
    use std::os::unix::fs::PermissionsExt;

    fn temp_dir(name: &str) -> PathBuf {
        let dir = std::env::temp_dir().join(format!("wade-trash-test-{}-{name}", std::process::id()));
        let _ = std::fs::remove_dir_all(&dir);
        std::fs::create_dir_all(&dir).unwrap();
        dir.canonicalize().unwrap()
    }

    fn uid() -> u32 {
        unsafe { libc::geteuid() }
    }

    fn info_of(trash: &Path, name: &str) -> String {
        std::fs::read_to_string(trash.join("info").join(format!("{name}.trashinfo"))).unwrap()
    }

    #[test]
    fn escapes_paths_like_a_uri() {
        assert_eq!(escape_path(OsStr::new("/tmp/a b/\u{fc}%.txt")), "/tmp/a%20b/%C3%BC%25.txt");
        assert_eq!(escape_path(OsStr::new("rel/x-y_z.~!*'()")), "rel/x-y_z.~!*'()");
        assert_eq!(escape_path(OsStr::from_bytes(b"/bad\xff#?")), "/bad%FF%23%3F");
    }

    #[test]
    fn numbers_names_like_gio() {
        let numbered = |name: &str, n| numbered_name(OsStr::new(name), n).into_string().unwrap();
        assert_eq!(numbered("a.txt", 1), "a.txt");
        assert_eq!(numbered("a.txt", 2), "a.2.txt");
        assert_eq!(numbered("a.tar.gz", 3), "a.tar.3.gz");
        assert_eq!(numbered("dir", 2), "dir.2");
        assert_eq!(numbered(".bashrc", 2), ".bashrc.2");
    }

    #[test]
    fn moves_a_file_to_the_home_trash_with_its_info() {
        let root = temp_dir("home");
        let data_home = root.join("data");
        let file = root.join("a b.txt");
        std::fs::write(&file, "x").unwrap();

        let trashed = trash_with(&file, &data_home, uid()).unwrap();
        let trash = data_home.join("Trash");
        assert_eq!(trashed, trash.join("files").join("a b.txt"));
        assert!(!file.exists());
        assert_eq!(std::fs::read_to_string(&trashed).unwrap(), "x");

        let info = info_of(&trash, "a b.txt");
        let lines: Vec<&str> = info.lines().collect();
        assert_eq!(lines[0], "[Trash Info]");
        assert_eq!(lines[1], format!("Path={}", escape_path(file.as_os_str())));
        assert!(lines[1].contains("a%20b.txt"));
        let date = lines[2].strip_prefix("DeletionDate=").unwrap();
        assert!(chrono::NaiveDateTime::parse_from_str(date, "%Y-%m-%dT%H:%M:%S").is_ok(), "{date}");

        let mode = |path: &Path| std::fs::metadata(path).unwrap().permissions().mode() & 0o777;
        assert_eq!(mode(&trash), 0o700);
        assert_eq!(mode(&trash.join("info").join("a b.txt.trashinfo")), 0o600);
        let _ = std::fs::remove_dir_all(&root);
    }

    #[test]
    fn same_names_get_numbered() {
        let root = temp_dir("dupes");
        let data_home = root.join("data");
        let files = data_home.join("Trash").join("files");

        for expected in ["a.txt", "a.2.txt", "a.3.txt"] {
            std::fs::write(root.join("a.txt"), expected).unwrap();
            assert_eq!(trash_with(&root.join("a.txt"), &data_home, uid()).unwrap(), files.join(expected));
            assert_eq!(std::fs::read_to_string(files.join(expected)).unwrap(), expected);
        }

        std::fs::create_dir(root.join("dir")).unwrap();
        std::fs::write(root.join("dir").join("inner"), "x").unwrap();
        trash_with(&root.join("dir"), &data_home, uid()).unwrap();
        std::fs::create_dir(root.join("dir")).unwrap();
        assert_eq!(trash_with(&root.join("dir"), &data_home, uid()).unwrap(), files.join("dir.2"));
        assert!(files.join("dir").join("inner").is_file(), "directories move whole");

        // A stray info file without its item still reserves the name
        std::fs::write(data_home.join("Trash").join("info").join("b.txt.trashinfo"), "").unwrap();
        std::fs::write(root.join("b.txt"), "x").unwrap();
        assert_eq!(trash_with(&root.join("b.txt"), &data_home, uid()).unwrap(), files.join("b.2.txt"));
        let _ = std::fs::remove_dir_all(&root);
    }

    #[test]
    fn symlinks_are_trashed_as_links() {
        let root = temp_dir("links");
        let data_home = root.join("data");
        let target = root.join("target.txt");
        std::fs::write(&target, "x").unwrap();
        std::os::unix::fs::symlink(&target, root.join("link")).unwrap();
        std::os::unix::fs::symlink(root.join("missing"), root.join("broken")).unwrap();

        let trashed = trash_with(&root.join("link"), &data_home, uid()).unwrap();
        assert!(std::fs::symlink_metadata(&trashed).unwrap().file_type().is_symlink());
        assert!(target.is_file(), "the target stays");

        let trashed = trash_with(&root.join("broken"), &data_home, uid()).unwrap();
        assert!(std::fs::symlink_metadata(&trashed).unwrap().file_type().is_symlink());
        assert!(std::fs::symlink_metadata(root.join("broken")).is_err());
        let _ = std::fs::remove_dir_all(&root);
    }

    #[test]
    fn failures_leave_the_item_and_no_info() {
        let root = temp_dir("fail");
        let data_home = root.join("data");

        assert!(trash_with(&root.join("missing.txt"), &data_home, uid()).is_err());
        assert!(!data_home.exists(), "nothing written for a missing item");

        // Trash/files is a file: the item stays and its info is removed
        std::fs::create_dir_all(data_home.join("Trash").join("info")).unwrap();
        std::fs::write(data_home.join("Trash").join("files"), "").unwrap();
        std::fs::write(root.join("keep.txt"), "x").unwrap();
        assert!(trash_with(&root.join("keep.txt"), &data_home, uid()).is_err());
        assert!(root.join("keep.txt").is_file());
        assert_eq!(std::fs::read_dir(data_home.join("Trash").join("info")).unwrap().count(), 0);

        // The rename itself fails (across devices, where /dev/shm is its own
        // tmpfs): the reserved .trashinfo is removed again
        let shm_trash = Path::new("/dev/shm").join(format!("wade-trash-test-{}-rename", std::process::id()));
        let other_device =
            std::fs::metadata("/dev/shm").is_ok_and(|shm| shm.dev() != std::fs::metadata(&root).unwrap().dev());
        if other_device && std::fs::create_dir(&shm_trash).is_ok() {
            let err = move_into(&shm_trash, &root.join("keep.txt"), OsStr::new("keep.txt")).unwrap_err();
            assert_eq!(err.raw_os_error(), Some(libc::EXDEV));
            assert!(root.join("keep.txt").is_file());
            assert_eq!(std::fs::read_dir(shm_trash.join("info")).unwrap().count(), 0);
            let _ = std::fs::remove_dir_all(&shm_trash);
        }

        let _ = std::fs::remove_dir_all(&root);
    }

    #[test]
    fn top_dir_trash_follows_the_spec() {
        let uid = uid();

        // No .Trash: .Trash-$uid is created, 0700
        let top = temp_dir("top-none");
        let dir = top_dir_trash(&top, uid).unwrap();
        assert_eq!(dir, top.join(format!(".Trash-{uid}")));
        assert_eq!(std::fs::metadata(&dir).unwrap().permissions().mode() & 0o777, 0o700);

        // A sticky .Trash: .Trash/$uid
        let top = temp_dir("top-sticky");
        std::fs::create_dir(top.join(".Trash")).unwrap();
        std::fs::set_permissions(top.join(".Trash"), std::fs::Permissions::from_mode(0o1777)).unwrap();
        assert_eq!(top_dir_trash(&top, uid).unwrap(), top.join(".Trash").join(uid.to_string()));

        // .Trash without the sticky bit, or a symlink: .Trash-$uid
        let top = temp_dir("top-plain");
        std::fs::create_dir(top.join(".Trash")).unwrap();
        std::fs::set_permissions(top.join(".Trash"), std::fs::Permissions::from_mode(0o777)).unwrap();
        assert_eq!(top_dir_trash(&top, uid).unwrap(), top.join(format!(".Trash-{uid}")));

        let top = temp_dir("top-link");
        let elsewhere = temp_dir("top-link-target");
        std::fs::set_permissions(&elsewhere, std::fs::Permissions::from_mode(0o1777)).unwrap();
        std::os::unix::fs::symlink(&elsewhere, top.join(".Trash")).unwrap();
        assert_eq!(top_dir_trash(&top, uid).unwrap(), top.join(format!(".Trash-{uid}")));

        // .Trash-$uid owned by someone else, or a file: unusable
        let top = temp_dir("top-other");
        let other = uid + 1;
        std::fs::create_dir(top.join(format!(".Trash-{other}"))).unwrap();
        assert!(top_dir_trash(&top, other).is_err(), "owned by {uid}, not {other}");
        std::fs::write(top.join(format!(".Trash-{}", other + 1)), "").unwrap();
        assert!(top_dir_trash(&top, other + 1).is_err(), "a file");

        for name in ["top-none", "top-sticky", "top-plain", "top-link", "top-link-target", "top-other"] {
            let _ = std::fs::remove_dir_all(temp_dir(name));
        }
    }

    /// End to end across devices where /dev/shm is a separate tmpfs.
    #[test]
    fn items_on_another_mount_use_its_top_dir_trash() {
        let root = temp_dir("mount");
        let shm = Path::new("/dev/shm");
        let (Ok(shm_meta), Ok(root_meta)) = (std::fs::metadata(shm), std::fs::metadata(&root)) else {
            return;
        };
        let probe = shm.join(format!("wade-trash-test-{}", std::process::id()));
        if shm_meta.dev() == root_meta.dev() || std::fs::create_dir(&probe).is_err() {
            return;
        }

        let top = top_dir(&probe, shm_meta.dev()).unwrap();
        let file = probe.join("x.txt");
        std::fs::write(&file, "x").unwrap();

        let trashed = trash_with(&file, &root.join("data"), uid()).unwrap();
        let trash = trashed.parent().unwrap().parent().unwrap().to_path_buf();
        assert!(trash.starts_with(&top), "{} under {}", trash.display(), top.display());
        assert!(!root.join("data").join("Trash").join("files").join("x.txt").exists());

        let relative = file.strip_prefix(&top).unwrap();
        let info = std::fs::read_to_string(
            trash.join("info").join(format!("{}.trashinfo", trashed.file_name().unwrap().to_string_lossy())),
        )
        .unwrap();
        assert!(info.contains(&format!("\nPath={}\n", escape_path(relative.as_os_str()))), "{info}");

        // Remove only what this test made; a trash that was already there
        // keeps its other items (remove_dir fails on non-empty directories)
        let _ = std::fs::remove_file(&trashed);
        let _ = std::fs::remove_file(
            trash.join("info").join(format!("{}.trashinfo", trashed.file_name().unwrap().to_string_lossy())),
        );
        for dir in [trash.join("files"), trash.join("info"), trash.clone()] {
            let _ = std::fs::remove_dir(dir);
        }
        let _ = std::fs::remove_dir(&probe);
        let _ = std::fs::remove_dir_all(&root);
    }
}
