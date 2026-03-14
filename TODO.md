# TODO

## Bugs (open)

(none)

---

## Features

### File action progress indicator

Progress indicator for large copy/move operations.

### Directory size calculation ✅

~~Compute and display total size of a folder.~~

- ~~Async calculation (can be slow for large trees)~~
- ~~Show progress/spinner while calculating~~
- ~~Display result in properties panel or inline~~

### Improve symbolic link handling

Wade currently has no symlink awareness — symlinks appear as plain files/directories with no visual distinction, and some operations are unsafe.

#### Safety: Fix Windows deletion bug ✅

~~`FileOperations.Delete` calls `Directory.Delete(path, true)` for symlink-to-directory entries on Windows, which **recursively deletes the target's contents** instead of just removing the link.~~ Fixed: symlinks are now detected via `FileInfo.LinkTarget` and deleted with non-recursive `Directory.Delete` / `File.Delete` before the `Directory.Exists` branch.

#### Model: Symlink fields ✅

- ~~Add `string? LinkTarget` to `FileSystemEntry`~~ — done
- ~~Populate in `DirectoryContents.LoadEntries` via `FileSystemInfo.LinkTarget`~~ — done
- ~~Detect broken symlinks: `ResolveLinkTarget(returnFinalTarget: true)` returns null or throws~~ — done (load-time field via `CheckBrokenSymlink`)

#### Display: Visual distinction for symlinks ✅

~~- **Icon**: Dedicated symlink icon (e.g. Nerd Font `nf-oct-file_symlink_file` / `nf-oct-file_symlink_directory`)~~
~~- **Color**: Cyan text (matches Unix `ls` convention)~~
~~- **Name suffix**: Append ` → <target>` (truncated) or `@` suffix~~
~~- **Broken symlinks**: Red color / broken-link icon when target doesn't exist~~

#### Properties overlay ✅

~~- Show "Type" as `"Symlink → Directory"` / `"Symlink → File"` / `"Broken Symlink"`~~
~~- Add a "Target" row showing the raw `LinkTarget` path~~
~~- Include `ReparsePoint` in the Windows attributes formatter~~

#### Navigation ✅

- ~~Show error notification when opening a broken symlink-to-directory (instead of silently failing)~~

#### Copy: Preserve symlinks ✅

~~In `CopyDirectory`, detect symlinks via `FileSystemInfo.LinkTarget` and recreate them at the destination rather than copying resolved content~~ Fixed: `CopyDirectory` and `ExecutePaste` now detect symlinks via `FileSystemInfo.LinkTarget` and recreate them with `File.CreateSymbolicLink`/`Directory.CreateSymbolicLink` instead of copying resolved content. Configurable via `copy_symlinks_as_links_enabled` (default: true); when disabled, resolved content is always copied.

#### Symlink creation ✅

~~Create symbolic links from within the file manager.~~

- ~~Keybinding (`Ctrl+L`) to create a symlink to the selected item~~
- ~~Uses input dialog for the link name/destination~~

### Right-click context menu

Popup menu at click position with contextual actions.

- Trigger on right-click (mouse button 2)
- Show actions relevant to the clicked item: open, rename, delete, copy, cut, paste, properties
- Render as a floating menu overlay at the click coordinates
- Keyboard dismissal (Escape) and selection (Enter / click)

### System clipboard — Unix/macOS file interop

Windows file clipboard interop is implemented. Remaining: Unix/macOS file clipboard interop via `xclip`/`xsel`/`wl-copy`/`pbcopy` with `text/uri-list` MIME type.

### Git integration

Show git status in the file browser and eventually support git actions.

#### Phase 1: Readonly status display

- Extend `GitUtils` (`src/Wade/FileSystem/GitUtils.cs`) — currently only has `FindRepoRoot()`; add file-level status querying (modified, untracked, staged, ignored, etc.)
- Add git status fields to `FileSystemEntry` record (`src/Wade/FileSystem/DirectoryContents.cs:177`)
- Add git-specific colors/styles in `PaneRenderer` (`src/Wade/UI/PaneRenderer.cs:11-29`) — e.g. modified=yellow, untracked=green, staged=cyan (common git UI conventions)
- Update style selection logic in `PaneRenderer.RenderFileList()` (~line 137) to use git status
- Consider: shell out to `git status --porcelain` or use a library like `LibGit2Sharp` (note: must be NativeAOT-compatible)
- Show git branch name in status bar
- Directory-level aggregate status (show modified if any child is modified)

#### Phase 2: Git actions

- Stage/unstage files (equivalent to `git add` / `git restore --staged`)
- Commit with message (input dialog)
- Push/pull
- Diff preview for modified files
- Keybindings for common actions (e.g. action palette entries)

### Drive type detection

Detect whether a drive is SSD, HDD, or network. Some features (like directory size in the file browser) should behave differently based on drive speed.

- On Windows, use WMI or `DeviceIoControl` / `IOCTL_STORAGE_QUERY_PROPERTY` to distinguish SSD vs HDD; `DriveInfo.DriveType == Network` for network drives
- `DriveInfo` is already used in `DirectoryContents.GetDriveEntries()` and `PropertiesOverlay`
- If feasible, add per-drive-type settings for showing directory size inline in the file list:
  - Show directory size for SSD (default true)
  - Show directory size for HDD (default false)
  - Show directory size for network drives (default false)
- Must be NativeAOT-compatible (no reflection-heavy WMI wrappers)

### Zip file preview ✅

~~Show zip/archive contents in the file preview pane (like a directory listing).~~

- ~~Use `System.IO.Compression.ZipFile` (built-in, NativeAOT-safe) to read the entry list~~
- ~~Render as a list of paths/sizes in the preview pane, similar to directory preview~~
- ~~Add a `zip_preview_enabled` config setting (default: true)~~
- ~~Preview loading should go through `PreviewLoader` async pattern to avoid blocking~~
- Consider supporting other archive formats (`.tar`, `.gz`, `.tar.gz`) later

### Remote/cloud file handling (OneDrive, etc.)

OneDrive and similar cloud sync tools use placeholder files that aren't fully downloaded locally. Wade should detect and handle these gracefully.

- Detect cloud placeholder status via `FileAttributes.Offline` / `FileAttributes.ReparsePoint` with `IO_REPARSE_TAG_CLOUD` reparse tags (Windows-specific)
- Display placeholder status visually (e.g. icon overlay or status indicator)
- Handle file size correctly — local size vs cloud size may differ for placeholders
- Don't attempt to read placeholder-only files: skip image previews, skip text preview, show a "[cloud file — not downloaded]" message instead
- Preview loading in `PreviewLoader.LoadPreview()` should check placeholder status before reading

### Hex preview for binary files

Read-only hex editor view for binary files, accessible via the action palette.

- Add an action palette entry (e.g. "Hex Preview") that appears when a binary file is selected
- Display hex dump format: offset column, hex bytes, ASCII representation (like `xxd` or HxD)
- `FilePreview.IsBinary()` already detects binary files (`src/Wade/FileSystem/FilePreview.cs`)
- Could render in the preview pane or as a full-screen overlay (like properties overlay)
- Read-only — no editing, just viewing
- Limit displayed size (e.g. first 4 KB–64 KB) to keep rendering fast
- Consider scrolling support for viewing deeper into the file

### Ellipsis for truncated filenames

When a filename is too long to fit in the name column, show `…` (or `...`) at the end instead of silently cutting off.

- Truncation happens in `PaneRenderer.RenderFileList()` (`src/Wade/UI/PaneRenderer.cs:206-218`) via `buffer.WriteString` with a `maxName` limit
- When `entry.Name.Length > maxName`, write `maxName - 1` chars + `…` instead of `maxName` chars
- Apply to both icon and non-icon code paths
- Also consider the symlink suffix path (~line 224) where the target can be truncated

### PDF preview

Preview PDF files using Sixel image rendering, similar to existing image previews.

- Consider shelling out to a PDF-to-image CLI tool (e.g. `mutool draw`, `pdftoppm` from poppler, or `magick` from ImageMagick) to convert the first page to a temporary PNG, then feed it through the existing `ImagePreview.Load()` pipeline
- Existing image preview flow: `PreviewLoader.cs:75-85` checks `ImagePreview.IsImageFile()`, calls `ImagePreview.Load()`, emits `ImagePreviewReadyEvent`
- Add a `pdf_previews_enabled` config setting (default: true), following the same pattern as `image_previews_enabled` in `WadeConfig.cs:8`
- Gated by Sixel capability detection (same as image previews) plus availability of the conversion tool on PATH
- `.pdf` is already in `FilePreview.s_binaryExtensions` (`src/Wade/FileSystem/FilePreview.cs:17`) — PDF preview should be checked before the binary fallback

### Set terminal title

Set the terminal window/tab title to reflect the current directory (e.g. `wade — D:\Projects`).

- Use OSC escape sequence `ESC ]0;<title> BEL` (or `ESC ]2;`) — widely supported across terminals
- Update title on directory navigation
- Restore original title on exit
- Add a `terminal_title_enabled` config setting (default: true)
- No terminal title handling exists in the codebase yet

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
