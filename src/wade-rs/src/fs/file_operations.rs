//! Port of `src/Wade/FileSystem/FileOperations.cs` and `src/Wade/FileSystem/Shell32.cs`:
//! recursive copy, move, delete (recycle bin on Windows), with C#'s
//! symlink-first semantics.

use crate::input::CancelToken;
use std::path::Path;

/// True when `path` is a symlink (does NOT follow the link). The C# code
/// checks `FileInfo.LinkTarget` before any Directory.Exists call, because
/// Directory.Exists follows links on Windows.
#[must_use]
fn is_symlink(path: &str) -> bool {
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
        Ok(meta) if meta.is_dir() => std::fs::remove_dir_all(path).is_ok(),
        Ok(_) => std::fs::remove_file(path).is_ok(),
        Err(_) => false,
    }
}

/// Port of `FileOperationRunner.RunPaste`'s copy branch for one entry:
/// symlink-preserve when requested (falling through to a content copy on
/// permission errors), directory recursion, file copy. C# File.Copy does not
/// overwrite; callers delete the destination first when overwrite is set.
pub fn copy_path(source: &str, dest: &str, preserve_symlinks: bool) -> Result<(), std::io::Error> {
    if preserve_symlinks && is_symlink(source) {
        let Some(target) = std::fs::read_link(source).ok().map(|p| p.to_string_lossy().to_string()) else {
            return copy_by_content(source, dest);
        };

        return match create_symlink(dest, &target, Path::new(source).is_dir()) {
            Ok(()) => Ok(()),
            // C# catches UnauthorizedAccessException and falls through to a
            // content copy
            Err(err) if is_permission_denied(&err) => copy_by_content(source, dest),
            Err(err) => Err(err),
        };
    }

    copy_by_content(source, dest)
}

fn copy_by_content(source: &str, dest: &str) -> Result<(), std::io::Error> {
    let meta = std::fs::symlink_metadata(source)?;
    if meta.is_dir() {
        copy_directory(source, dest, true)?;
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
            let target = std::fs::read_link(&source_path)
                .map(|p| p.to_string_lossy().to_string())
                .ok();
            if let Some(target) = target {
                match create_symlink(&dest_path, &target, entry.path().is_dir()) {
                    Ok(()) => continue,
                    Err(err) if is_permission_denied(&err) => {}
                    Err(err) => return Err(err),
                }
            }
        }

        let meta = std::fs::symlink_metadata(&source_path)?;
        if meta.is_dir() {
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
        std::fs::remove_dir_all(path)
    } else {
        std::fs::remove_file(path)
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
        SHFileOperationW, FOF_ALLOWUNDO, FOF_NOCONFIRMATION, FOF_NOERRORUI, FOF_SILENT,
        FO_DELETE, SHFILEOPSTRUCTW,
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

    fn supports_symlinks(dir: &Path) -> bool {
        let link = dir.join("_probe_link");
        let target = dir.join("_probe_target");
        std::fs::write(&target, b"x").expect("probe target");
        let ok = create_symlink(&p(&link), &p(&target), false).is_ok();
        let _ = std::fs::remove_file(&link);
        let _ = std::fs::remove_file(&target);
        ok
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
        let (success, errors) = delete_paths(
            &[p(&file), p(&nested), p(&dir.join("missing.txt"))],
            true,
            &cancel,
        );
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