# TODO

## Bugs (open)

(none)

---

## Features

### File action progress indicator

Progress indicator for large copy/move operations.

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

#### Phase 2a: Stage / Unstage

- Stage file(s) via `git add` — action palette entry, context-sensitive (visible when selection has Modified/Untracked status)
- Unstage file(s) via `git restore --staged` — action palette entry (visible when selection has Staged status)
- Stage all changes — action palette entry (visible when any Modified/Untracked exist)
- Multi-select support: operate on `_markedPaths` when non-empty, otherwise focused entry
- Async via new `GitActionRunner` class (same pattern as `GitStatusLoader` / `DirectorySizeLoader`)
- Add `RunGitCommand` private helper to `GitUtils.cs` to reduce boilerplate
- Add `GitActionCompleteEvent` to `InputEvent.cs`
- Key files: `GitUtils.cs`, `GitActionRunner.cs` (new), `InputEvent.cs`, `InputReader.cs` (new `AppAction` values), `App.cs` (palette entries + dispatch)

#### Phase 2b: Commit

- Commit staged changes with message via input dialog (`Ctrl+G`)
- Reuses existing `TextInput` / `InputMode.TextInput` / `RenderTextInputDialog` machinery (same as Rename)
- Single-line commit message (multi-line deferred)
- Only available when staged changes exist
- Key files: `GitUtils.cs` (`Commit` method), `App.cs`, `InputReader.cs` (`Ctrl+G` binding), `HelpOverlay.cs`

#### Phase 2c: Push / Pull

- Push to remote (`git push`) and pull from remote (`git pull`)
- Network operations — may block on auth; use 30s timeout with kill
- Display stderr as error message for auth/network failures
- Action palette entries only (no direct key bindings)
- Key files: `GitUtils.cs` (`Push`/`Pull` methods), `App.cs`

#### Completed

- Phase 1: Readonly status display ✅
- Diff preview for modified files ✅

### Drive type detection

Detect whether a drive is SSD, HDD, or network. Some features (like directory size in the file browser) should behave differently based on drive speed.

- On Windows, use WMI or `DeviceIoControl` / `IOCTL_STORAGE_QUERY_PROPERTY` to distinguish SSD vs HDD; `DriveInfo.DriveType == Network` for network drives
- `DriveInfo` is already used in `DirectoryContents.GetDriveEntries()` and `PropertiesOverlay`
- If feasible, add per-drive-type settings for showing directory size inline in the file list:
  - Show directory size for SSD (default true)
  - Show directory size for HDD (default false)
  - Show directory size for network drives (default false)
- Must be NativeAOT-compatible (no reflection-heavy WMI wrappers)

### Remote/cloud file handling (OneDrive, etc.)

OneDrive and similar cloud sync tools use placeholder files that aren't fully downloaded locally. Wade should detect and handle these gracefully.

- Detect cloud placeholder status via `FileAttributes.Offline` / `FileAttributes.ReparsePoint` with `IO_REPARSE_TAG_CLOUD` reparse tags (Windows-specific)
- Display placeholder status visually (e.g. icon overlay or status indicator)
- Handle file size correctly — local size vs cloud size may differ for placeholders
- Don't attempt to read placeholder-only files: skip image previews, skip text preview, show a "[cloud file — not downloaded]" message instead
- Preview loading in `PreviewLoader.LoadPreview()` should check placeholder status before reading

### Zip — other archive formats

Support additional archive formats in the preview pane (`.tar`, `.gz`, `.tar.gz`). Zip preview is already implemented via `System.IO.Compression.ZipFile`.
