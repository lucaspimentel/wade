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

#### Phase 1: Readonly status display ✅

- ~~Extend `GitUtils` — add file-level status querying (modified, untracked, staged, ignored, conflict)~~
- ~~Git status as separate `Dictionary<string, GitFileStatus>` map (not on `FileSystemEntry`) — async-friendly~~
- ~~Git-specific colors/styles in `PaneRenderer` — modified=yellow, untracked=green, staged=cyan, conflict=red~~
- ~~Nerd Font git status icons (modified dot, staged plus, untracked question, conflict alert)~~
- ~~Shell out to `git status --porcelain=v1` (NativeAOT-compatible)~~
- ~~Branch name read from `.git/HEAD` file (supports worktrees)~~
- ~~Show git branch name in status bar (purple, left side after path)~~
- ~~Directory-level aggregate status (OR child statuses into parent directories)~~
- ~~Async via `GitStatusLoader` following `DirectorySizeLoader` pattern~~
- ~~`git_status_enabled` config setting (default: true) with config dialog toggle~~

#### Phase 2: Git actions

- Stage/unstage files (equivalent to `git add` / `git restore --staged`)
- Commit with message (input dialog)
- Push/pull
- Diff preview for modified files ✅
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

### Hex preview for binary files ✅

~~Read-only hex editor view for binary files, accessible via the action palette.~~

- ~~Display hex dump format: offset column, hex bytes, ASCII representation (like `xxd` or HxD)~~
- ~~`FilePreview.IsBinary()` already detects binary files (`src/Wade/FileSystem/FilePreview.cs`)~~
- ~~Renders in the preview pane and expandable to full screen~~
- ~~Read-only — no editing, just viewing~~
- ~~Limit displayed size (first 64 KB) to keep rendering fast~~
- ~~Scrolling support via existing preview pane scrolling~~
- ~~Per-character styling: dim blue-gray offset, light gray hex, dimmer null bytes, green ASCII, dim non-printable dots~~
- ~~`hex_preview_enabled` config setting (default: true)~~

### Ellipsis for truncated filenames ✅

~~When a filename is too long to fit in the name column, show `…` (or `...`) at the end instead of silently cutting off.~~

~~- Truncation happens in `PaneRenderer.RenderFileList()` via `buffer.WriteString` with a `maxName` limit~~
~~- When `entry.Name.Length > maxName`, write `maxName - 1` chars + `…` instead of `maxName` chars~~
~~- Apply to both icon and non-icon code paths~~
~~- Also consider the symlink suffix path where the target can be truncated~~

### PDF preview ✅

~~Preview PDF files using Sixel image rendering, similar to existing image previews.~~

- ~~Shells out to `pdftopng` (xpdf) to convert the first page to a temporary PNG, then feeds it through the existing `ImagePreview.Load()` pipeline~~
- ~~General-purpose convert-to-image abstraction (`IImageConverter` / `ImageConverter`) that PDF is the first consumer of~~
- ~~`pdf_preview_enabled` config setting (default: true); effective preview requires image previews enabled + Sixel support + tool available~~
- ~~Gated by Sixel capability detection (same as image previews) plus availability of `pdftopng` on PATH~~

### Set terminal title ✅

~~Set the terminal window/tab title to reflect the current directory (e.g. `wade - D:\Projects`).~~

~~- Use OSC escape sequence `ESC ]0;<title> BEL` — widely supported across terminals~~
~~- Update title on directory navigation~~
~~- Restore original title on exit via xterm title stack (`ESC [22;0t` / `ESC [23;0t`)~~
~~- Add a `terminal_title_enabled` config setting (default: true)~~

### File finder ✅

~~Searchable file finder dialog. User types to filter files in the current directory tree by name, Up/Down to navigate, Enter to open.~~

- ~~Add `InputMode.FileFinder` to enum~~
- ~~Add `AppAction.ShowFileFinder` + key mapping in `InputReader.MapKey()`~~
- ~~State: `_fileFinderSelectedIndex`, `_fileFinderTextInput`, filtered file list, `_fileFinderEntries` (full list)~~
- ~~Item list: recursively enumerate files under `_currentPath` (up to a reasonable depth/count limit to stay responsive — e.g. max 10,000 entries, max depth 8)~~
- ~~Display: relative path from `_currentPath` as label, parent directory as right-aligned hint~~
- ~~On Enter: navigate to the selected file's parent directory and select the file (same as GoToPath does for file targets)~~
- ~~Async enumeration with "[scanning...]" indicator while building the list~~
- ~~Shares the same UI pattern as the action palette (text input at top, filtered list below)~~
