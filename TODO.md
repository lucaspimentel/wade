# TODO

## Bugs

### ~~Help dialog doesn't block mouse input~~ ✅

Fixed: Help dialog now uses `InputMode.Help` instead of a separate `_showHelp` boolean,
so it participates in the modal mouse-blocking guard like all other dialogs.

### ~~Image preview renders on top of help dialog~~ ✅

Fixed: Sixel write is now suppressed when any modal overlay is active (Help, Confirm,
TextInput, GoToPath). Only Normal, Search, and ExpandedPreview modes allow Sixel output.

### ~~Status bar notifications hide file metadata unnecessarily~~ ✅

Fixed: Metadata is now always rendered right-aligned. Notifications render in the
gap between the left-side content and metadata, truncating when space is tight.

---

## Shared UI foundations

These are prerequisites or enablers for multiple features below. Build them first.

### ~~Dialog / overlay framework~~ ✅ (message variant)

Implemented: `DialogBox` utility renders centered box chrome (border, background, title, footer) and returns a content `Rect` for callers. `HelpOverlay` refactored to use it.

- Remaining variants (modal input mode now supports these; wire up to file actions when ready):
  - **Confirm dialog** — yes/no prompt (needed by delete, paste overwrite). Rendering and input handling implemented via `ShowConfirmDialog`.
  - **Input dialog** — single-line text field with cursor (needed by rename, go-to-path). Rendering and input handling implemented via `ShowTextInputDialog`.

### ~~Text input widget~~ ✅

Implemented: `TextInput` class manages a character buffer + cursor position with insert, delete, Home/End, left/right editing operations. Renders into a `ScreenBuffer` at a given position with visible cursor (inverse style). Supports horizontal scrolling when text exceeds the visible width.

- ~~Remaining: Emit the final string on Enter, cancel on Escape~~ ✅ — handled by modal input mode's `HandleTextInputKey` (Enter completes, Escape cancels)
- **Required by:** Input dialog (rename, go-to-path)

### ~~Modal input mode~~ ✅

Implemented: `InputMode` enum (`Normal`, `Confirm`, `TextInput`, `Search`, `ExpandedPreview`, `GoToPath`) in `Terminal` namespace. Main loop dispatches key events by mode before normal `AppAction` processing. Modal handlers consume all keys — nothing leaks to navigation. `ShowConfirmDialog` and `ShowTextInputDialog` helper methods enter modal modes; Escape/N dismiss, Y confirms, Enter completes text input. Modal dialogs render as overlays via `DialogBox`.

- **Required by:** Confirm dialog, Input dialog

### ~~Multi-select model~~ ✅

Implemented: `HashSet<string>` of marked absolute paths (`_markedPaths`) in `App` state. Space toggles mark on the current entry and advances cursor (ranger/mc convention). Marked entries render with a dark yellow background; marked+selected entries get a combined highlight. Mark count shown in status bar when non-zero. Marks clear on directory change (Open, Back, Refresh, mouse navigation) and filter clear.

- **Required by:** File actions (copy, cut, delete-multiple)

### ~~Status bar notifications~~ ✅

Implemented: `Notification` record (`Message`, `Kind`, `Timestamp`) with `IsExpired()` tick-based check. `NotificationKind` enum (`Info`, `Success`, `Error`) controls color (gray, green, red). `ShowNotification()` helper on `App` sets the notification; expiry check runs at the top of the main loop (clears on next input after 4 s). Notification renders in the status bar gap between left content and right-aligned metadata, truncating when space is tight. Auto-clears on directory change (Open, Back, Refresh).

- **Required by:** File actions

---

## ~~Raw input pipeline~~ ✅

Implemented: `InputPipeline` + `IInputSource` abstraction with `WindowsInputSource` (Win32 `ReadConsoleInput`) and `UnixInputSource` (raw VT parser). Main loop uses `pipeline.Take()` instead of `Console.ReadKey`.

**Required by:** Async preview loading, Mouse support

## ~~Async preview loading~~ ✅

Implemented: `PreviewLoader` runs file I/O + syntax highlighting on a background `Task.Run`, injects a `PreviewReadyEvent` into the `InputPipeline` on completion. Shows `[loading…]` placeholder while pending. Stale results are discarded by path comparison. Cancellation checked before I/O, after I/O, and after highlighting.

**Depends on:** Raw input pipeline

## ~~Mouse support~~ ✅

Implemented: Click to navigate folders and select files in all three panes. Scroll wheel moves selection up/down. Windows uses `ENABLE_MOUSE_INPUT` console mode; Unix uses SGR mouse reporting (`ESC[?1000h` + `ESC[?1006h`). Mouse reporting is disabled on exit to restore normal terminal behavior.

## ~~Buffered rendering and dirty-region tracking~~ ✅

Implemented: `ScreenBuffer` double-buffers a front/back cell grid, diffs per-cell on flush, skips clean rows via a bitfield, emits only changed cells with style diffing, and writes the full frame in a single `StdOut.Write`. `ForceFullRedraw()` available via Ctrl+R.

## Sixel image preview (preview pane ✅ · full-size dialog pending)

Implemented: Image thumbnails render in the right preview pane using Sixel graphics. Uses SixLabors.ImageSharp for decoding/scaling and a custom median-cut quantization Sixel encoder (`Wade.Imaging.SixelEncoder`). Sixel data bypasses `ScreenBuffer` (written directly to stdout after flush). Gated by `ImagePreviewsEnabled` config flag (default: on). Supported image formats: PNG, JPG, GIF, BMP, WebP, TGA, TIFF, PBM. Re-renders on terminal resize.

**Depends on:** Async preview loading

**Remaining**
- ~~Terminal capability detection: query DA1/DA2 or check `WT_SESSION` for auto-enable~~ ✅
- ~~Cell pixel size query via `ESC [ 16 t` (currently assumes 8×16 px/cell)~~ ✅

## ~~`--help` CLI switch~~ ✅

Implemented: `--help` / `-h` prints usage info (available flags, keybindings, config file location, environment variables) and exits.

## ~~Search / filter~~ ✅

Implemented: Press `/` to enter search mode. Typing narrows visible entries by case-insensitive substring match in real-time. Enter persists the filter (returns to normal navigation within filtered results). Escape clears the filter and restores the full list. Filter auto-clears on directory change (Open, Back, Refresh, mouse navigation). Search bar renders inline at the bottom of the center pane with a yellow `/` prefix. In normal mode with an active filter, Escape/q clears the filter instead of quitting.

- **Depends on:** Text input widget, Modal input mode

## ~~Sort order~~ ✅

Implemented: Configurable sort modes (name, modified time, size, extension) with ascending/descending toggle. `s` cycles sort mode, `S` toggles direction. Directories always sorted before files regardless of direction. Sort preference configurable via `sort_mode` and `sort_ascending` in config file. Current sort mode shown in status bar with direction arrow (e.g. `name↑`, `size↓`).

## ~~File actions~~ ✅

Implemented: Open with default app (`o`), rename (`F2`), delete (`Del`), copy (`c`/`Ctrl+C`), cut (`x`/`Ctrl+X`), paste (`p`/`v`). Multi-select supported for delete/copy/cut. Internal clipboard with status bar indicator. Confirm dialog for delete. Error notifications for name collisions and failed operations.

**Remaining**
- ~~Recycle Bin support on Windows (use `SHFileOperation` or shell API instead of permanent delete)~~ ✅
- ~~Overwrite confirmation on paste when destination already exists~~ ✅
- ~~OS file clipboard interop~~ ✅ (Windows) → see **System clipboard** section below
- Progress indicator for large copy/move operations

## ~~Show/hide hidden files~~ ✅

Implemented: Hidden files (dotfiles on all platforms, `Hidden` attribute on Windows) are excluded by default. Toggle visibility at runtime with `.` keybinding. Config flag `show_hidden_files` (default: `false`) in config file. `DirectoryContents.ShowHiddenFiles` controls filtering; cache is invalidated on toggle.

## ~~Config dialog~~ ✅

Implemented: Press `,` to open an in-app config dialog. Displays all 8 settings (show icons, show hidden files, sort mode, sort ascending, confirm delete, preview pane, image previews, detail columns) with toggle/cycle controls. Up/Down to navigate, Space to toggle booleans, Left/Right to cycle sort mode, Enter to save, Escape to cancel. Changes are applied immediately to runtime state and written to the config file on save. Image previews is nested under preview pane (indented label) and only toggleable when preview pane is enabled.

- **Depends on:** Dialog / overlay framework, Modal input mode

## ~~Bookmarks / favorites~~ ✅

Implemented: `b` opens a filterable bookmarks dialog, `B` toggles the current directory as a bookmark. Dialog supports `1-9` quick jump, `Enter` to navigate, `d`/`Delete` to remove, `Escape` to close. Bookmarks persist to `~/.config/wade/bookmarks` (one path per line, MRU order). Non-existent bookmarks rendered dimmed.

## ~~Go-to-path bar~~ ✅

Implemented: Press `g` to open a path input dialog. Tab auto-completes from the filesystem (first matching entry). Suggestion shown on a second row in dimmed text. Enter navigates to the path (directories open directly; files navigate to parent and select the file). Escape cancels. Error notification shown for invalid/nonexistent paths.


## ~~File details / properties panel~~ ✅

Implemented: Press `i` to show a Properties overlay dialog for the selected file/directory. Displays name, full path, type (File/Directory/Drive), formatted size with raw bytes, created/modified/accessed timestamps, file attributes, and read-only status. Any key dismisses the dialog. Sixel image previews are suppressed while the overlay is visible.

## Directory size calculation

Compute and display total size of a folder.

- Async calculation (can be slow for large trees)
- Show progress/spinner while calculating
- Display result in properties panel or inline
- **Depends on:** File details / properties panel

## ~~Column toggles~~ ✅

Implemented: Size and date columns are now independently toggleable via the config dialog (`,`). Replaced single `detail_columns_enabled` with `size_column_enabled` and `date_column_enabled` config keys (both default `true`). Old `detail_columns_enabled` key still works for backward compatibility (sets both). Config dialog shows "Size Column" and "Date Column" as separate items.

## ~~Create new file / directory~~ ✅

Implemented: `Shift+N` creates a new empty file, `F7` creates a new directory. Both use the text input dialog to enter the name. Validates against invalid characters and existing names. Selects the newly created entry after creation.

- **Depends on:** Text input widget, Modal input mode

## Symlink creation

Create symbolic links from within the file manager.

- Keybinding (e.g. `Ctrl+L`) to create a symlink to the selected item
- Uses input dialog for the link name/destination
- **Depends on:** Text input widget, Modal input mode

## ~~Open terminal here / cd here~~ ✅

Implemented: `Ctrl+T` opens a new terminal in the current directory (Windows Terminal `wt.exe` with `cmd.exe` fallback; Unix uses `$SHELL`). `--cwd-file=<path>` CLI flag writes the final directory path to a file on exit, enabling shell wrapper functions (`wd`) to cd after exit. Shell wrappers provided for bash/zsh (`shell/wd.sh`), fish (`shell/wd.fish`), and PowerShell (`shell/wd.ps1`).

## Right-click context menu

Popup menu at click position with contextual actions.

- Trigger on right-click (mouse button 2)
- Show actions relevant to the clicked item: open, rename, delete, copy, cut, paste, properties
- Render as a floating menu overlay at the click coordinates
- Keyboard dismissal (Escape) and selection (Enter / click)
- **Depends on:** Dialog / overlay framework, Mouse support, File actions


## ~~Help dialog layout~~ ✅

Reformatted help dialog from a single column (29 rows) to 2 columns side by side (~15 rows). `ContentWidth` doubled to 96 (two 46-char columns + 4-char gap). `Bindings` array unchanged; `Render()` splits entries across left and right columns.

## ~~Config audit~~ ✅

Reviewed all 5 existing config options — all are actively used and appropriate. Added 3 new options: `confirm_delete_enabled` (skip delete confirmation), `preview_pane_enabled` (hide right preview pane for 2-pane layout), `detail_columns_enabled` (hide size/date columns in center pane). All default to `true`. Config dialog updated to show 8 options.

## System clipboard

Shared infrastructure for all features that interact with the OS clipboard. No system clipboard support exists yet — wade currently uses an internal in-memory clipboard (`_clipboardPaths` list) for file copy/cut/paste.

### Shared foundation: `FileSystem/SystemClipboard.cs`

All clipboard features below share the same P/Invoke surface on Windows (`user32.dll`: `OpenClipboard`, `EmptyClipboard`, `SetClipboardData`, `GetClipboardData`, `CloseClipboard`) and the same external-process approach on Unix/macOS. Build this once, expose format-specific methods:

**Windows (P/Invoke, NativeAOT-safe):**
- Text: `SetClipboardData(CF_UNICODETEXT, ...)` with `GlobalAlloc`/`GlobalLock` marshalling
- Files: `SetClipboardData(CF_HDROP, ...)` (DROPFILES struct + null-terminated path list) + `SetClipboardData(RegisterClipboardFormat("Preferred DropEffect"), ...)` for cut vs copy
- Reading files: `GetClipboardData(CF_HDROP)` → `DragQueryFile` to extract paths, check `Preferred DropEffect` for cut vs copy

**Unix/macOS (external processes):**
- Text: `xclip -selection clipboard` / `xsel --clipboard --input` / `wl-copy` (Wayland) / `pbcopy` (macOS) — try in order of availability
- Files: `xclip -selection clipboard -t text/uri-list` with `file://` URI list (works with GNOME/KDE file managers). macOS Finder interop is limited — `pbcopy` doesn't support file references natively.
- Reading files: `xclip -selection clipboard -t text/uri-list -o` to read URIs, parse `file://` paths

**Methods:**
- `static bool SetText(string text)` — write plain text
- `static bool SetFiles(IReadOnlyList<string> paths, bool isCut)` — write file list for OS paste
- `static (List<string>? Paths, bool IsCut)? GetFiles()` — read file list from OS copy/cut
- Returns false/null if no clipboard mechanism is available

### ~~Copy path as text~~ ✅

Implemented: `y` copies the selected item's absolute path to the OS clipboard via `SystemClipboard.SetText()`. `Y` (Shift+Y) copies the git-relative path (forward-slash normalized). Git root detection via `GitUtils.FindRepoRoot()` walks up looking for `.git`. Error notification shown if not in a git repo or clipboard unavailable. Windows uses P/Invoke (`user32.dll`/`kernel32.dll`); Unix/macOS uses external tools (`pbcopy`, `wl-copy`, `xclip`, `xsel`).

### ~~OS file clipboard interop~~ ✅ (Windows)

Implemented: Files copied/cut in wade (`c`/`x`) are placed on the Windows clipboard using `CF_HDROP` + `Preferred DropEffect`, so they can be pasted in Explorer. Files copied/cut in Explorer can be pasted in wade (`p`) — wade reads `CF_HDROP` from the OS clipboard via `DragQueryFileW` and respects the cut/copy drop effect. Unix/macOS interop not yet implemented (returns no-op).

### Implementation steps

1. Create `FileSystem/SystemClipboard.cs` with `SetText`, `SetFiles`, `GetFiles`
2. Create `FileSystem/GitUtils.cs` with `FindRepoRoot(string)`
3. Add `AppAction.CopyAbsolutePath` and `AppAction.CopyGitRelativePath` to enum
4. Add key mappings in `InputReader.MapKey()`
5. Add case handlers in `App.cs` switch block
6. Wire `SetFiles`/`GetFiles` into existing copy/cut/paste handlers
7. Update `HelpOverlay.cs` keybindings array

---

## ~~Action palette~~ ✅

Implemented: Press `Ctrl+P` to open a searchable action palette. Lists all available actions with their keyboard shortcuts. Type to filter by case-insensitive substring match. Up/Down to navigate, Enter to execute the selected action, Escape to cancel. Context-sensitive: Paste only shown when clipboard is non-empty. Scrolling supported for filtered results.

## File finder (`Ctrl+P`)

Searchable file finder dialog. User types to filter files in the current directory tree by name, Up/Down to navigate, Enter to open.

- Add `InputMode.FileFinder` to enum
- Add `AppAction.ShowFileFinder` + key mapping in `InputReader.MapKey()`
- State: `_fileFinderSelectedIndex`, `_fileFinderTextInput`, filtered file list, `_fileFinderEntries` (full list)
- Item list: recursively enumerate files under `_currentPath` (up to a reasonable depth/count limit to stay responsive — e.g. max 10,000 entries, max depth 8)
- Display: relative path from `_currentPath` as label, parent directory as right-aligned hint
- On Enter: navigate to the selected file's parent directory and select the file (same as GoToPath does for file targets)
- Consider async enumeration to avoid blocking on large trees — could show "[scanning…]" while building the list, similar to preview loading pattern
- Shares the same UI pattern as the action palette (text input at top, filtered list below)

---

## ~~Status bar should show file path in expanded preview~~ ✅

Fixed: `RenderExpandedPreview()` now uses `_cachedPreviewPath ?? _cachedImagePath ?? _currentPath` for the status bar display path, so the previewed file's full path is shown instead of just the directory.

---

## Bugs (open)

### ~~Refresh file list after file actions~~ ✅

~~File actions (delete, paste, etc.) that change the directory contents should refresh the file list so the UI reflects the updated state immediately.~~

Implemented: Added `InvalidateFilteredEntries()` calls after `_directoryContents.Invalidate(_currentPath)` in all file action handlers (delete, rename, new file, new directory, paste) so the filtered entry cache is also refreshed.

### ~~Drive letters on Windows should always be capitalized~~ ✅

Fixed: `PathCompletion.CapitalizeDriveLetter()` applied at all `_currentPath` assignment sites in `App.cs` and in `DirectoryContents.GetDriveEntries()`. Ensures drive letters are always uppercase throughout the UI.

### ~~CLI path argument format handling~~ ✅

Fixed: `TrimEnd` no longer destroys root paths (`C:\` → `C:` or `/` → `""`). Root separators are preserved after trimming. Short flags like `-h` are no longer matched as start paths.

### ~~Never show system+hidden filesystem entries on Windows~~ ✅

Fixed: Entries with both `System` and `Hidden` file attributes (e.g. `$Recycle.Bin`, `System Volume Information`) are now unconditionally excluded on Windows, regardless of the "show hidden files" setting.
