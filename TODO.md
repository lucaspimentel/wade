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

- Commit staged changes with message via input dialog (action palette entry)
- Reuses existing `TextInput` / `InputMode.TextInput` / `RenderTextInputDialog` machinery (same as Rename)
- Single-line commit message (multi-line deferred)
- Only available when staged changes exist
- Key files: `GitUtils.cs` (`Commit` method), `App.cs`

#### Phase 2c: Push / Pull

- Push to remote (`git push`) and pull from remote (`git pull`)
- Network operations — may block on auth; use 30s timeout with kill
- Display stderr as error message for auth/network failures
- Action palette entries only (no direct key bindings)
- Key files: `GitUtils.cs` (`Push`/`Pull` methods), `App.cs`

#### Completed

- Phase 1: Readonly status display ✅
- Diff preview for modified files ✅
- Git action menu (`Ctrl+G`) ✅

### Drive type detection

Detect whether a drive is SSD, HDD, or network. Some features (like directory size in the file browser) should behave differently based on drive speed.

- On Windows, use WMI or `DeviceIoControl` / `IOCTL_STORAGE_QUERY_PROPERTY` to distinguish SSD vs HDD; `DriveInfo.DriveType == Network` for network drives
- `DriveInfo` is already used in `DirectoryContents.GetDriveEntries()` and `PropertiesOverlay`
- If feasible, add per-drive-type settings for showing directory size inline in the file list:
  - Show directory size for SSD (default true)
  - Show directory size for HDD (default false)
  - Show directory size for network drives (default false)
- Must be NativeAOT-compatible (no reflection-heavy WMI wrappers)

### Zip — other archive formats

Support additional archive formats in the preview pane (`.tar`, `.gz`, `.tar.gz`). Zip preview is already implemented via `System.IO.Compression.ZipFile`.

### Git actions in action palette

Git actions are integrated into the global action palette (`Ctrl+P`), prefixed with "Git:" and shown only when actionable. No dedicated git menu or keybinding.

- Diff preview toggle ✅
- Stage / Unstage (Phase 2a)
- Commit with message (Phase 2b)
- Push / Pull (Phase 2c)

#### Completed

- Moved git actions from dedicated `Ctrl+G` menu into global action palette ✅
- Diff preview toggle as "Git: Toggle diff preview" (only shown for modified/staged files) ✅
