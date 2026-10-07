//! Port of `src/Wade/FileSystem/FileOperations.cs` and `src/Wade/FileSystem/Shell32.cs`:
//! recursive copy, move, delete (recycle bin on Windows), with C#'s
//! symlink-first semantics.

use crate::input::CancelToken;
use std::path::Path;

/// True when `path` is a symlink (does NOT follow the link). The C# code
/// checks `FileInfo.LinkTarget` before any Directory.Exists call, because
/// Directory.Exists follows links on Windows.
#[must_use]
pub fn is_symlink(path: &str) -> bool {
    std::fs::symlink_metadata(path).is_ok_and(|meta| meta.file_type().is_symlink())
}

/// Port of `FileOperations.Delete`: deletes the paths; on Windows with
/// permanent=false, sends them to the Recycle Bin via SHFileOperation.
/// Returns (successCount, errorCount) using the C# runner's accounting.
pub fn delete_paths(paths: &[String], permanent: bool, cancel: &CancelToken) -> (usize, usize) {
    if paths.is_empty() {
        return (0, 0);
    }

    let mut success = 0;
    let mut errors = 0;

    for path in paths {
        if cancel.is_cancelled() {
            break;
        }

        if !permanent && cfg!(windows) {
            #[cfg(windows)]
            {
                if recycle_files(std::slice::from_ref(path)) == 0 {
                    success += 1;
                } else {
                    errors += 1;
                }
            }

            #[cfg(not(windows))]
            {
                let _ = path;
            }
        } else if delete_one(path) {
            success += 1;
        } else {
            errors += 1;
        }
    }

    (success, errors)
}

/// Delete a single path with the C# symlink-first rules: a symlink (file or
/// dir) is removed non-recursively via its link; real dirs are removed
/// recursively; missing paths count as errors.
fn delete_one(path: &str) -> bool {
    if is_symlink(path) {
        // Symlinks: remove the link itself. C# uses Directory.Delete(path,
        // false) for dir-links and File.Delete otherwise.
        return remove_symlink(path).is_ok();
    }

    match std::fs::symlink_metadata(path) {
        Ok(meta) if meta.is_dir() => remove_dir_all_forced(path).is_ok(),
        Ok(_) => remove_file_forced(path).is_ok(),
        Err(_) => false,
    }
}

/// `remove_file` that also deletes a read-only file on Windows by clearing
/// the attribute and retrying, as Explorer does.
fn remove_file_forced(path: &str) -> Result<(), std::io::Error> {
    match std::fs::remove_file(path) {
        #[cfg(windows)]
        Err(err) if is_permission_denied(&err) && clear_readonly(Path::new(path)) => std::fs::remove_file(path),
        result => result,
    }
}

/// `remove_dir_all` that also deletes read-only files and directories inside
/// the tree on Windows by clearing their attribute and retrying.
fn remove_dir_all_forced(path: &str) -> Result<(), std::io::Error> {
    match std::fs::remove_dir_all(path) {
        #[cfg(windows)]
        result => result,
    }
}

/// Clears the read-only attribute; true when it was set and is now cleared.
#[cfg(windows)]
fn clear_readonly(path: &Path) -> bool {
    let Ok(meta) = std::fs::symlink_metadata(path) else {
        return false;
    };

    let mut permissions = meta.permissions();
    if !permissions.readonly() {
        return false;
    }

    // On Windows this only clears FILE_ATTRIBUTE_READONLY
    #[allow(clippy::permissions_set_readonly_false)]
    permissions.set_readonly(false);
    std::fs::set_permissions(path, permissions).is_ok()
}

/// Clears the read-only attribute on `path` and everything below it,
/// without following links.
#[cfg(windows)]
fn clear_readonly_tree(path: &Path) {
    clear_readonly(path);

    let is_real_dir = std::fs::symlink_metadata(path).is_ok_and(|meta| meta.is_dir());
    if !is_real_dir {
        return;
    }

    if let Ok(entries) = std::fs::read_dir(path) {
        for entry in entries.flatten() {
            clear_readonly_tree(&entry.path());
        }
    }
}

/// Canonical form of `path` for location comparisons; lowercase on Windows,
/// where names are case-insensitive.
fn location_key(path: &Path) -> Option<std::path::PathBuf> {
    let canonical = std::fs::canonicalize(path).ok()?;
    if cfg!(windows) {
        Some(std::path::PathBuf::from(canonical.to_string_lossy().to_lowercase()))
    } else {
        Some(canonical)
    }
}

/// True when `source` sits directly in `dest_dir`, so pasting it there would
/// target the source itself.
#[must_use]
pub fn same_location(source: &str, dest_dir: &str) -> bool {
    let Some(parent) = Path::new(source).parent() else {
        return false;
    };

    match (location_key(parent), location_key(Path::new(dest_dir))) {
        (Some(parent), Some(dest)) => parent == dest,
        _ => false,
    }
}

/// True when `dest_dir` is `source` or inside it (links resolved), so a
/// recursive copy or move of `source` into `dest_dir` can never finish.
#[must_use]
pub fn is_within(dest_dir: &str, source: &str) -> bool {
    match (location_key(Path::new(dest_dir)), location_key(Path::new(source))) {
        (Some(dest), Some(source)) => dest.starts_with(source),
        _ => false,
    }
}

/// Explorer-style name for a copy pasted next to its source: `a.txt` becomes
/// `a - Copy.txt`, then `a - Copy (2).txt` and so on until a free name is
/// found. Directories and dotfiles keep the whole name as the stem.
#[must_use]
pub fn unique_copy_name(dir: &str, name: &str, is_directory: bool) -> String {
    let (stem, extension) = match name.rfind('.') {
        Some(index) if index > 0 && !is_directory => name.split_at(index),
        _ => (name, ""),
    };

    let mut number = 1;
    loop {
        let candidate = if number == 1 {
            format!("{stem} - Copy{extension}")
        } else {
            format!("{stem} - Copy ({number}){extension}")
        };

        if Path::new(dir).join(&candidate).symlink_metadata().is_err() {
            return candidate;
        }

        number += 1;
    }
}

/// Port of `FileOperationRunner.RunPaste`'s copy branch for one entry:
/// symlink-preserve when requested (falling through to a content copy on
/// permission errors), directory recursion, file copy. C# File.Copy does not
/// overwrite; callers delete the destination first when overwrite is set.
pub fn copy_path(source: &str, dest: &str, preserve_symlinks: bool) -> Result<(), std::io::Error> {
    if preserve_symlinks && is_symlink(source) {
        let Some(target) = std::fs::read_link(source).ok().map(|p| p.to_string_lossy().to_string()) else {
            return copy_by_content(source, dest, preserve_symlinks);
        };

        return match create_symlink(dest, &target, Path::new(source).is_dir()) {
            Ok(()) => Ok(()),
            // C# catches UnauthorizedAccessException and falls through to a
            // content copy
            Err(err) if is_permission_denied(&err) => copy_by_content(source, dest, preserve_symlinks),
            Err(err) => Err(err),
        };
    }

    copy_by_content(source, dest, preserve_symlinks)
}

/// True for a directory or a link to one. `Directory.Exists` follows links,
/// so a directory link is copied by content when links are not preserved.
fn is_directory_following_links(path: &str) -> bool {
    std::fs::metadata(path).is_ok_and(|meta| meta.is_dir())
}

fn copy_by_content(source: &str, dest: &str, preserve_symlinks: bool) -> Result<(), std::io::Error> {
    std::fs::symlink_metadata(source)?;
    if is_directory_following_links(source) {
        copy_directory(source, dest, preserve_symlinks)?;
        Ok(())
    } else {
        std::fs::copy(source, dest)?;
        Ok(())
    }
}

/// Port of `FileOperations.CopyDirectory`: recursive copy with optional
/// symlink preservation per entry.
pub fn copy_directory(source: &str, destination: &str, preserve_symlinks: bool) -> Result<(), std::io::Error> {
    std::fs::create_dir_all(destination)?;

    let entries = std::fs::read_dir(source)?;
    for entry in entries {
        let entry = entry?;
        let name = entry.file_name().to_string_lossy().to_string();
        let source_path = entry.path().to_string_lossy().to_string();
        let dest_path = Path::new(destination).join(&name).to_string_lossy().to_string();

        if preserve_symlinks && is_symlink(&source_path) {
            let target = std::fs::read_link(&source_path).map(|p| p.to_string_lossy().to_string()).ok();
            if let Some(target) = target {
                match create_symlink(&dest_path, &target, entry.path().is_dir()) {
                    Ok(()) => continue,
                    Err(err) if is_permission_denied(&err) => {}
                    Err(err) => return Err(err),
                }
            }
        }

        std::fs::symlink_metadata(&source_path)?;
        if is_directory_following_links(&source_path) {
            copy_directory(&source_path, &dest_path, preserve_symlinks)?;
        } else {
            std::fs::copy(&source_path, &dest_path)?;
        }
    }

    Ok(())
}

/// Port of `Directory.Move`/`File.Move` (same-directory renames and
/// cross-directory moves).
pub fn move_path(source: &str, dest: &str) -> Result<(), std::io::Error> {
    std::fs::rename(source, dest)
}

/// Removes a symlink without touching its target. On Windows a directory
/// symlink needs `remove_dir`; `remove_file` fails with access denied.
fn remove_symlink(path: &str) -> Result<(), std::io::Error> {
    let result = std::fs::remove_file(path);

    #[cfg(windows)]
    if result.is_err() && std::fs::remove_dir(path).is_ok() {
        return Ok(());
    }

    result
}

/// Port of the runner's `DeleteExisting`: removes an existing destination
/// before an overwrite; symlinks are removed as links, dirs recursively.
pub fn delete_existing(path: &str) -> Result<(), std::io::Error> {
    if is_symlink(path) {
        return remove_symlink(path);
    }

    let meta = std::fs::symlink_metadata(path)?;
    if meta.is_dir() {
        remove_dir_all_forced(path)
    } else {
        remove_file_forced(path)
    }
}

/// Port of `Directory.CreateSymbolicLink`/`File.CreateSymbolicLink`.
fn create_symlink(link_path: &str, target: &str, is_directory: bool) -> Result<(), std::io::Error> {
    #[cfg(windows)]
    {
        if is_directory {
            std::os::windows::fs::symlink_dir(target, link_path)
        } else {
            std::os::windows::fs::symlink_file(target, link_path)
        }
    }

    #[cfg(not(windows))]
    {
        let _ = is_directory;
        std::os::unix::fs::symlink(target, link_path)
    }
}

fn is_permission_denied(err: &std::io::Error) -> bool {
    err.kind() == std::io::ErrorKind::PermissionDenied
}

/// Windows recycle-bin delete via `SHFileOperationW` (port of Shell32.cs).
#[cfg(windows)]
fn recycle_files(paths: &[String]) -> i32 {
    use windows_sys::Win32::UI::Shell::{
        FO_DELETE, FOF_ALLOWUNDO, FOF_NOCONFIRMATION, FOF_NOERRORUI, FOF_SILENT, SHFILEOPSTRUCTW, SHFileOperationW,
    };

    // pFrom requires a double-null-terminated wide string: paths separated by
    // NUL, ending with two NULs
    let mut wide: Vec<u16> = Vec::new();
    for path in paths {
        wide.extend(path.encode_utf16());
        wide.push(0);
    }

    wide.push(0);

    let mut op = SHFILEOPSTRUCTW {
        hwnd: std::ptr::null_mut(),
        wFunc: FO_DELETE,
        pFrom: wide.as_ptr(),
        pTo: std::ptr::null(),
        fFlags: (FOF_ALLOWUNDO | FOF_NOCONFIRMATION | FOF_SILENT | FOF_NOERRORUI) as u16,
        fAnyOperationsAborted: 0,
        hNameMappings: std::ptr::null_mut(),
        lpszProgressTitle: std::ptr::null(),
    };

    unsafe { SHFileOperationW(&mut op) }
}

#[cfg(test)]
mod tests {
    use super::*;
    use std::path::PathBuf;

    fn temp_dir(name: &str) -> PathBuf {
        let dir = std::env::temp_dir().join(format!("wade-fileops-test-{}-{name}", std::process::id()));
        let _ = std::fs::remove_dir_all(&dir);
        std::fs::create_dir_all(&dir).expect("mkdir");
        dir
    }

    fn p(path: &Path) -> String {
        path.to_string_lossy().to_string()
    }

    /// True when a file link can be created and read back: Windows without
    /// Developer Mode refuses to create one, and Wine creates links that
    /// cannot be followed.
    fn supports_symlinks(dir: &Path) -> bool {
        let link = dir.join("_probe_link");
        let target = dir.join("_probe_target");
        std::fs::write(&target, b"x").expect("probe target");
        let ok = create_symlink(&p(&link), &p(&target), false).is_ok()
            && is_symlink(&p(&link))
            && std::fs::read(&link).is_ok_and(|data| data == b"x");
        let _ = std::fs::remove_file(&link);
        let _ = std::fs::remove_file(&target);
        ok
    }

    #[test]
    fn unique_copy_name_follows_explorer() {
        let dir = temp_dir("copyname");
        let d = p(&dir);
        assert_eq!(unique_copy_name(&d, "a.txt", false), "a - Copy.txt");
        std::fs::write(dir.join("a - Copy.txt"), b"").expect("write");
        assert_eq!(unique_copy_name(&d, "a.txt", false), "a - Copy (2).txt");
        std::fs::write(dir.join("a - Copy (2).txt"), b"").expect("write");
        assert_eq!(unique_copy_name(&d, "a.txt", false), "a - Copy (3).txt");

        assert_eq!(unique_copy_name(&d, "a.tar.gz", false), "a.tar - Copy.gz");
        assert_eq!(unique_copy_name(&d, "Makefile", false), "Makefile - Copy");
        assert_eq!(unique_copy_name(&d, ".env", false), ".env - Copy");
        assert_eq!(unique_copy_name(&d, "v1.2", true), "v1.2 - Copy", "directories keep the whole name");
    }

    #[test]
    fn same_location_and_is_within_resolve_paths() {
        let dir = temp_dir("location");
        let sub = dir.join("sub");
        std::fs::create_dir_all(sub.join("deeper")).expect("mkdir");
        std::fs::write(dir.join("f.txt"), b"").expect("write");

        assert!(same_location(&p(&dir.join("f.txt")), &p(&dir)));
        assert!(same_location(&p(&dir.join("f.txt")), &p(&sub.join(".."))));
        assert!(!same_location(&p(&dir.join("f.txt")), &p(&sub)));

        assert!(is_within(&p(&sub), &p(&sub)));
        assert!(is_within(&p(&sub.join("deeper")), &p(&sub)));
        assert!(!is_within(&p(&dir), &p(&sub)));
        // A sibling sharing a name prefix is not inside
        std::fs::create_dir_all(dir.join("sub2")).expect("mkdir");
        assert!(!is_within(&p(&dir.join("sub2")), &p(&sub)));

        if cfg!(windows) {
            let upper = p(&sub).to_uppercase();
            assert!(is_within(&p(&sub.join("deeper")), &upper), "case-insensitive on Windows");
            assert!(same_location(&p(&dir.join("F.TXT")), &p(&dir).to_uppercase()));
        }
    }

    #[cfg(windows)]
    fn make_read_only(path: &Path) {
        let mut permissions = std::fs::metadata(path).expect("meta").permissions();
        permissions.set_readonly(true);
        std::fs::set_permissions(path, permissions).expect("set read-only");
    }

    #[cfg(windows)]
    #[test]
    fn permanent_delete_removes_read_only_files_and_trees() {
        let dir = temp_dir("readonly-delete");
        let file = dir.join("ro.txt");
        std::fs::write(&file, b"x").expect("write");
        make_read_only(&file);
        let tree = dir.join("tree");
        std::fs::create_dir_all(tree.join("inner")).expect("mkdir");
        std::fs::write(tree.join("inner").join("ro.txt"), b"x").expect("write");
        make_read_only(&tree.join("inner").join("ro.txt"));
        make_read_only(&tree.join("inner"));

        let (success, errors) = delete_paths(&[p(&file), p(&tree)], true, &CancelToken::new());
        assert_eq!((success, errors), (2, 0));
        assert!(!file.exists());
        assert!(!tree.exists());
    }

    #[cfg(windows)]
    #[test]
    fn read_only_files_can_be_renamed() {
        let dir = temp_dir("readonly-rename");
        let file = dir.join("ro.txt");
        std::fs::write(&file, b"x").expect("write");
        make_read_only(&file);

        move_path(&p(&file), &p(&dir.join("renamed.txt"))).expect("rename");
        assert!(dir.join("renamed.txt").is_file());
    }

    #[cfg(windows)]
    #[test]
    fn locked_files_are_not_deleted_or_renamed() {
        use std::os::windows::fs::OpenOptionsExt;

        let dir = temp_dir("locked");
        let file = dir.join("locked.txt");
        std::fs::write(&file, b"x").expect("write");
        let lock = std::fs::OpenOptions::new().read(true).share_mode(0).open(&file).expect("lock");

        assert_eq!(delete_paths(&[p(&file)], true, &CancelToken::new()), (0, 1));
        assert!(move_path(&p(&file), &p(&dir.join("moved.txt"))).is_err());
        assert!(std::fs::read(&file).is_err(), "reads are refused while the file is locked");
        drop(lock);
        assert_eq!(std::fs::read(&file).expect("read"), b"x");
    }

    #[test]
    fn delete_file_dir_and_missing() {
        let dir = temp_dir("del");
        let file = dir.join("f.txt");
        std::fs::write(&file, b"x").expect("write");
        let nested = dir.join("n");
        std::fs::create_dir_all(&nested).expect("mkdir");
        std::fs::write(nested.join("g.txt"), b"y").expect("write");

        let cancel = CancelToken::new();
        let (success, errors) = delete_paths(&[p(&file), p(&nested), p(&dir.join("missing.txt"))], true, &cancel);
        assert_eq!((success, errors), (2, 1));
        assert!(!file.exists());
        assert!(!nested.exists());
    }

    #[test]
    fn delete_symlink_removes_link_not_target() {
        let dir = temp_dir("symlink");
        if !supports_symlinks(&dir) {
            return;
        }

        let target = dir.join("target.txt");
        std::fs::write(&target, b"data").expect("write");
        let link = dir.join("link.txt");
        create_symlink(&p(&link), &p(&target), false).expect("link");

        let cancel = CancelToken::new();
        let (success, errors) = delete_paths(&[p(&link)], true, &cancel);
        assert_eq!((success, errors), (1, 0));
        assert!(!link.exists());
        // Target untouched
        assert!(target.exists());
    }

    #[test]
    fn delete_directory_symlink_removes_link_not_target() {
        let dir = temp_dir("dirsymlink");
        if !supports_symlinks(&dir) {
            return;
        }
        let target = dir.join("target");
        std::fs::create_dir_all(&target).expect("mkdir");
        std::fs::write(target.join("inner.txt"), b"data").expect("write");
        let link = dir.join("link");

        if create_symlink(&p(&link), &p(&target), true).is_err() {
            return;
        }

        let cancel = CancelToken::new();
        assert_eq!(delete_paths(&[p(&link)], true, &cancel), (1, 0));
        assert!(std::fs::symlink_metadata(&link).is_err());
        assert!(target.join("inner.txt").exists());

        create_symlink(&p(&link), &p(&target), true).expect("link");
        delete_existing(&p(&link)).expect("delete_existing");
        assert!(std::fs::symlink_metadata(&link).is_err());
        assert!(target.join("inner.txt").exists());
    }

    #[test]
    fn copy_directory_link_without_preserve_copies_contents() {
        let dir = temp_dir("copydirlink");
        if !supports_symlinks(&dir) {
            return;
        }
        let target = dir.join("target");
        std::fs::create_dir_all(target.join("sub")).expect("mkdir");
        std::fs::write(target.join("sub").join("b.txt"), b"B").expect("write");
        let link = dir.join("link");

        if create_symlink(&p(&link), &p(&target), true).is_err() {
            return;
        }

        let dest = dir.join("dest");
        copy_path(&p(&link), &p(&dest), false).expect("copy link");
        assert!(!is_symlink(&p(&dest)));
        assert_eq!(std::fs::read(dest.join("sub").join("b.txt")).expect("read"), b"B");

        let tree = dir.join("tree");
        std::fs::create_dir_all(&tree).expect("mkdir");
        create_symlink(&p(&tree.join("inner")), &p(&target), true).expect("link");
        let tree_dest = dir.join("tree-dest");
        copy_path(&p(&tree), &p(&tree_dest), false).expect("copy tree");
        assert!(!is_symlink(&p(&tree_dest.join("inner"))));
        assert!(tree_dest.join("inner").join("sub").join("b.txt").is_file());
    }

    #[test]
    fn permanent_delete_does_not_recycle() {
        let dir = temp_dir("permanent");
        let file = dir.join("gone.txt");
        std::fs::write(&file, b"x").expect("write");

        let cancel = CancelToken::new();
        let (success, errors) = delete_paths(&[p(&file)], true, &cancel);
        assert_eq!((success, errors), (1, 0));
    }

    #[test]
    fn cancel_stops_midway() {
        let dir = temp_dir("cancel");
        let a = dir.join("a.txt");
        let b = dir.join("b.txt");
        std::fs::write(&a, b"x").expect("write");
        std::fs::write(&b, b"x").expect("write");

        let cancel = CancelToken::new();
        cancel.cancel();
        let (success, errors) = delete_paths(&[p(&a), p(&b)], true, &cancel);
        assert_eq!((success, errors), (0, 0));
        assert!(a.exists());
    }

    #[test]
    fn copy_file_and_tree() {
        let dir = temp_dir("copy");
        let src = dir.join("src");
        std::fs::create_dir_all(src.join("deep")).expect("mkdir");
        std::fs::write(src.join("a.txt"), b"A").expect("write");
        std::fs::write(src.join("deep").join("b.txt"), b"B").expect("write");

        let dest = dir.join("dest");
        copy_path(&p(&src), &p(&dest), true).expect("copy");

        assert!(dest.join("a.txt").is_file());
        assert!(dest.join("deep").join("b.txt").is_file());
        assert_eq!(std::fs::read(dest.join("a.txt")).expect("read"), b"A");
    }

    #[test]
    fn copy_symlink_preserves_link() {
        let dir = temp_dir("copysym");
        if !supports_symlinks(&dir) {
            return;
        }

        let target = dir.join("target.txt");
        std::fs::write(&target, b"payload").expect("write");
        let src = dir.join("src_link");
        create_symlink(&p(&src), &p(&target), false).expect("link");

        let dest = dir.join("dest_link");
        copy_path(&p(&src), &p(&dest), true).expect("copy");

        // The copy is itself a symlink pointing at the same target
        assert!(is_symlink(&p(&dest)));
        assert_eq!(
            std::fs::read_link(&dest).ok().map(|t| t.to_string_lossy().to_string()),
            std::fs::read_link(&src).ok().map(|t| t.to_string_lossy().to_string())
        );
    }

    #[test]
    fn copy_does_not_overwrite_via_runner_semantics() {
        // C# File.Copy throws when the destination exists; the runner deletes
        // the destination first when overwrite is set. copy_path itself
        // (fs::copy-based) DOES overwrite, so callers must check first.
        let dir = temp_dir("overwrite");
        let src = dir.join("src.txt");
        std::fs::write(&src, b"new").expect("write");
        let dest = dir.join("dest.txt");
        std::fs::write(&dest, b"old").expect("write");

        copy_path(&p(&src), &p(&dest), true).expect("copy");
        // Divergence documented: Rust std copy overwrites
        assert_eq!(std::fs::read(&dest).expect("read"), b"new");
    }

    #[test]
    fn move_file_and_dir() {
        let dir = temp_dir("move");
        let file = dir.join("f.txt");
        std::fs::write(&file, b"x").expect("write");
        let dest = dir.join("moved.txt");
        move_path(&p(&file), &p(&dest)).expect("move");
        assert!(!file.exists());
        assert!(dest.exists());

        let subdir = dir.join("d");
        std::fs::create_dir_all(&subdir).expect("mkdir");
        std::fs::write(subdir.join("g.txt"), b"y").expect("write");
        let dest_dir = dir.join("d2");
        move_path(&p(&subdir), &p(&dest_dir)).expect("move dir");
        assert!(dest_dir.join("g.txt").is_file());
    }

    fn link_target(path: &Path) -> Option<String> {
        std::fs::read_link(path).ok().map(|t| p(&t))
    }

    #[test]
    fn copy_directory_preserves_links_inside_the_tree() {
        let dir = temp_dir("treelinks");
        if !supports_symlinks(&dir) {
            return;
        }
        let src = dir.join("src");
        std::fs::create_dir_all(src.join("real_dir")).expect("mkdir");
        std::fs::write(src.join("real.txt"), b"R").expect("write");
        std::fs::write(src.join("real_dir").join("inner.txt"), b"I").expect("write");
        let file_link = create_symlink(&p(&src.join("file_link")), &p(&src.join("real.txt")), false);
        let dir_link = create_symlink(&p(&src.join("dir_link")), &p(&src.join("real_dir")), true);
        let broken = create_symlink(&p(&src.join("broken")), &p(&dir.join("missing.txt")), false);
        if file_link.is_err() || dir_link.is_err() || broken.is_err() {
            return;
        }

        let dest = dir.join("dest");
        copy_directory(&p(&src), &p(&dest), true).expect("copy");

        for name in ["file_link", "dir_link", "broken"] {
            assert!(is_symlink(&p(&dest.join(name))), "{name} copied as a link");
            assert_eq!(link_target(&dest.join(name)), link_target(&src.join(name)), "{name} target");
        }
        assert_eq!(std::fs::read(dest.join("real.txt")).expect("read"), b"R");
        assert!(dest.join("real_dir").join("inner.txt").is_file());
        // The source tree is left as it was
        assert!(src.join("real.txt").is_file() && is_symlink(&p(&src.join("file_link"))));
    }

    #[test]
    fn copy_directory_without_preserve_copies_file_link_contents() {
        let dir = temp_dir("treefilelink");
        if !supports_symlinks(&dir) {
            return;
        }
        let src = dir.join("src");
        std::fs::create_dir_all(&src).expect("mkdir");
        let target = dir.join("target.txt");
        std::fs::write(&target, b"payload").expect("write");
        if create_symlink(&p(&src.join("link.txt")), &p(&target), false).is_err() {
            return;
        }

        let dest = dir.join("dest");
        copy_directory(&p(&src), &p(&dest), false).expect("copy");
        assert!(!is_symlink(&p(&dest.join("link.txt"))));
        assert_eq!(std::fs::read(dest.join("link.txt")).expect("read"), b"payload");
    }

    #[test]
    fn overwrite_replaces_an_existing_directory_and_file() {
        let dir = temp_dir("overwritetree");
        let src = dir.join("src");
        std::fs::create_dir_all(&src).expect("mkdir");
        std::fs::write(src.join("new.txt"), b"new").expect("write");
        let dest = dir.join("dest");
        std::fs::create_dir_all(&dest).expect("mkdir");
        std::fs::write(dest.join("old.txt"), b"old").expect("write");

        // The runner's overwrite: delete the destination, then copy
        delete_existing(&p(&dest)).expect("delete existing");
        copy_path(&p(&src), &p(&dest), true).expect("copy");
        assert!(dest.join("new.txt").is_file());
        assert!(!dest.join("old.txt").exists(), "old contents removed, not merged");
        assert!(src.join("new.txt").is_file(), "copy keeps the source");

        // And for a move onto an existing file
        let moving = dir.join("moving.txt");
        std::fs::write(&moving, b"moved").expect("write");
        let target = dir.join("target.txt");
        std::fs::write(&target, b"stale").expect("write");
        delete_existing(&p(&target)).expect("delete existing");
        move_path(&p(&moving), &p(&target)).expect("move");
        assert_eq!(std::fs::read(&target).expect("read"), b"moved");
        assert!(!moving.exists());
    }

    #[test]
    fn delete_existing_cleans_destination() {
        let dir = temp_dir("delexisting");
        let dest_file = dir.join("dest.txt");
        std::fs::write(&dest_file, b"old").expect("write");
        delete_existing(&p(&dest_file)).expect("delete existing");
        assert!(!dest_file.exists());

        let dest_dir = dir.join("dest_dir");
        std::fs::create_dir_all(&dest_dir).expect("mkdir");
        std::fs::write(dest_dir.join("x.txt"), b"x").expect("write");
        delete_existing(&p(&dest_dir)).expect("delete existing dir");
        assert!(!dest_dir.exists());
    }
}
