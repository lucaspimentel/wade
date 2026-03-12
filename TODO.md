# TODO

## Bugs (open)

(none)

---

## Features

### File action progress indicator

Progress indicator for large copy/move operations.

### Directory size calculation

Compute and display total size of a folder.

- Async calculation (can be slow for large trees)
- Show progress/spinner while calculating
- Display result in properties panel or inline

### Improve symbolic link handling

Wade currently has no symlink awareness — symlinks appear as plain files/directories with no visual distinction, and some operations are unsafe.

#### Safety: Fix Windows deletion bug (high priority)

`FileOperations.Delete` calls `Directory.Delete(path, true)` for symlink-to-directory entries on Windows, which **recursively deletes the target's contents** instead of just removing the link. Fix: check `FileAttributes.ReparsePoint` (or `LinkTarget != null`) and use non-recursive delete for symlinks.

#### Model: Add symlink fields to `FileSystemEntry`

- Add `bool IsSymlink` and `string? LinkTarget` to `FileSystemEntry`
- Populate in `DirectoryContents.LoadEntries` via `FileSystemInfo.LinkTarget` (.NET 6+)
- Detect broken symlinks: `ResolveLinkTarget(returnFinalTarget: true)` returns null or throws

#### Display: Visual distinction for symlinks

- **Icon**: Dedicated symlink icon (e.g. Nerd Font `nf-oct-file_symlink_file` / `nf-oct-file_symlink_directory`)
- **Color**: Cyan text (matches Unix `ls` convention)
- **Name suffix**: Append ` → <target>` (truncated) or `@` suffix
- **Broken symlinks**: Red color / broken-link icon when target doesn't exist

#### Properties overlay

- Show "Type" as `"Symlink → Directory"` / `"Symlink → File"` / `"Broken Symlink"`
- Add a "Target" row showing the raw `LinkTarget` path
- Include `ReparsePoint` in the Windows attributes formatter

#### Navigation

- Show error notification when opening a broken symlink-to-directory (instead of silently failing)

#### Copy: Preserve symlinks

- In `CopyDirectory`, detect symlinks via `FileSystemInfo.LinkTarget` and recreate them at the destination rather than copying resolved content

#### Symlink creation

Create symbolic links from within the file manager.

- Keybinding (e.g. `Ctrl+L`) to create a symlink to the selected item
- Uses input dialog for the link name/destination

### Right-click context menu

Popup menu at click position with contextual actions.

- Trigger on right-click (mouse button 2)
- Show actions relevant to the clicked item: open, rename, delete, copy, cut, paste, properties
- Render as a floating menu overlay at the click coordinates
- Keyboard dismissal (Escape) and selection (Enter / click)

### System clipboard — Unix/macOS file interop

Windows file clipboard interop is implemented. Remaining: Unix/macOS file clipboard interop via `xclip`/`xsel`/`wl-copy`/`pbcopy` with `text/uri-list` MIME type.

### File finder

Searchable file finder dialog. User types to filter files in the current directory tree by name, Up/Down to navigate, Enter to open.

- Add `InputMode.FileFinder` to enum
- Add `AppAction.ShowFileFinder` + key mapping in `InputReader.MapKey()`
- State: `_fileFinderSelectedIndex`, `_fileFinderTextInput`, filtered file list, `_fileFinderEntries` (full list)
- Item list: recursively enumerate files under `_currentPath` (up to a reasonable depth/count limit to stay responsive — e.g. max 10,000 entries, max depth 8)
- Display: relative path from `_currentPath` as label, parent directory as right-aligned hint
- On Enter: navigate to the selected file's parent directory and select the file (same as GoToPath does for file targets)
- Consider async enumeration to avoid blocking on large trees — could show "[scanning...]" while building the list, similar to preview loading pattern
- Shares the same UI pattern as the action palette (text input at top, filtered list below)
