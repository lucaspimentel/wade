# TODO

## Bugs (open)

(none)

---

## Refactoring

### ~~Deduplicate action dispatch~~ ✅

Extracted `DispatchFileAction` method shared by both the main key handler and action palette dispatch. Removed ~350 lines of duplicated logic.

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

#### ~~Phase 2a: Stage / Unstage~~ ✅

Implemented stage/unstage via action palette entries ("Git: Stage", "Git: Unstage", "Git: Stage all changes"). Async via `GitActionRunner`, multi-select supported, status auto-refreshes after each action.

#### ~~Unstage all~~ ✅

Added "Git: Unstage all" action palette entry via `git reset HEAD`. Mirrors "Git: Stage all changes" pattern.

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
- Phase 2a: Stage / Unstage ✅

### Drive type detection

Detect whether a drive is SSD, HDD, or network. Some features (like directory size in the file browser) should behave differently based on drive speed.

- On Windows, use WMI or `DeviceIoControl` / `IOCTL_STORAGE_QUERY_PROPERTY` to distinguish SSD vs HDD; `DriveInfo.DriveType == Network` for network drives
- `DriveInfo` is already used in `DirectoryContents.GetDriveEntries()` and `PropertiesOverlay`
- If feasible, add per-drive-type settings for showing directory size inline in the file list:
  - Show directory size for SSD (default true)
  - Show directory size for HDD (default false)
  - Show directory size for network drives (default false)
- Must be NativeAOT-compatible (no reflection-heavy WMI wrappers)

### ~~Keybinding and naming cleanup~~ ✅

- Removed `'p'` paste shortcut (now `'v'` only) in `InputReader.cs`, action palette, help overlay, `--help`, and README
- Renamed `/` from "Search / filter" to "Filter" everywhere
- Renamed `Ctrl+F` from "Find file" to "Search / Find file" everywhere

### ~~File finder: ignore .git folders~~ ✅

Switched `ScanFilesForFinder` to manual recursive directory walk. `.git` directories are always skipped (even with hidden files enabled). Dot-prefixed and hidden/system directories are also skipped when their respective settings are off.

### Show git status in properties dialog

Display the file's git status (e.g. "Modified", "Staged", "Untracked") in the properties overlay (`i` key).

- Add a "Git status" label/value row to `PropertiesOverlay.cs` (`Labels` array at line 13, `BuildValues` at line 73)
- `Render` signature (`PropertiesOverlay.cs:29`) needs a `GitFileStatus?` parameter (or the status string)
- Caller in `App.cs` (the `ShowProperties` / `InputMode.Properties` handler) already has access to `_gitStatuses` — look up the entry's path and pass the status
- Format flags as comma-separated labels (e.g. "Modified, Staged") or show "Clean" / empty when no status
- Color the value to match the existing git status colors (yellow=modified, cyan=staged, green=untracked, red=conflict)

### Zip — other archive formats

Support additional archive formats in the preview pane (`.tar`, `.gz`, `.tar.gz`). Zip preview is already implemented via `System.IO.Compression.ZipFile`.

### Git actions in action palette

Git actions are integrated into the global action palette (`Ctrl+P`), prefixed with "Git:" and shown only when actionable. No dedicated git menu or keybinding.

- Diff preview toggle ✅
- Stage / Unstage (Phase 2a) ✅
- Commit with message (Phase 2b)
- Push / Pull (Phase 2c)

#### Completed

- Moved git actions from dedicated `Ctrl+G` menu into global action palette ✅
- Diff preview toggle as "Git: Toggle diff preview" (only shown for modified/staged files) ✅
- Stage / Unstage via "Git: Stage", "Git: Unstage", "Git: Stage all changes" ✅
