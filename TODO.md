# TODO

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

Implemented: `InputMode` enum (`Normal`, `Confirm`, `TextInput`, `Search`, `ExpandedPreview`) in `Terminal` namespace. Main loop dispatches key events by mode before normal `AppAction` processing. Modal handlers consume all keys — nothing leaks to navigation. `ShowConfirmDialog` and `ShowTextInputDialog` helper methods enter modal modes; Escape/N dismiss, Y confirms, Enter completes text input. Modal dialogs render as overlays via `DialogBox`.

- **Required by:** Confirm dialog, Input dialog

### Multi-select model

Track a set of marked entries independent of the cursor position.

- `HashSet<string>` of marked absolute paths in app state
- Toggle mark on current entry (e.g. `Space`)
- Visual indicator on marked entries (e.g. different highlight color or `*` prefix)
- Clear marks after a paste, cut completion, or explicit cancel
- **Required by:** File actions (copy, cut, delete-multiple)

### Status bar notifications

Transient one-line feedback messages in the status bar.

- A message slot with an optional auto-clear timer (e.g. clear after 3 s or on next navigation)
- Used for success (`"3 items copied"`), errors (`"Permission denied"`), and progress (`"Deleting…"`)
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
- Full-size dialog: press Enter on an image to open a centered overlay (depends on dialog/overlay framework)
- ~~Expand preview: when user presses right arrow on a file with a preview, expand the preview to fill the display~~ ✅ — Right/Enter on a file expands the preview to full terminal width; scrollable text with line numbers; images re-render at expanded/collapsed size; Left/Escape/Q collapses back to 3-pane view
- Terminal capability detection: query DA1/DA2 or check `WT_SESSION` for auto-enable
- Cell pixel size query via `ESC [ 16 t` (currently assumes 8×16 px/cell)

## ~~`--help` CLI switch~~ ✅

Implemented: `--help` / `-h` prints usage info (available flags, keybindings, config file location, environment variables) and exits.

## ~~Search / filter~~ ✅

Implemented: Press `/` to enter search mode. Typing narrows visible entries by case-insensitive substring match in real-time. Enter persists the filter (returns to normal navigation within filtered results). Escape clears the filter and restores the full list. Filter auto-clears on directory change (Open, Back, Refresh, mouse navigation). Search bar renders inline at the bottom of the center pane with a yellow `/` prefix. In normal mode with an active filter, Escape/q clears the filter instead of quitting.

- **Depends on:** Text input widget, Modal input mode

## Sort order

Allow sorting the file list by different criteria.

- Sort by name (default), modification time, size, extension
- Toggle ascending / descending
- Keybinding to cycle sort mode (e.g. `s`) or open a sort picker
- Directories always sorted before files (existing behavior) unless overridden
- Persist sort preference per session or via config

## File actions

Common file operations on the selected item(s).

**Actions**
- Open: launch file with its default shell handler (`explorer.exe` / `xdg-open`)
- Rename: inline prompt to enter a new name
- Delete: confirm dialog, then delete (to Recycle Bin on Windows if possible)
- Copy: mark selected item(s) for copy
- Cut: mark selected item(s) for move
- Paste: copy/move marked items into the current directory

**Interactions**
- Keybindings (e.g. `F2` rename, `F8`/`Del` delete, `Ctrl+C` copy, `Ctrl+X` cut, `Ctrl+V` paste)
- Multi-select: allow marking multiple items before acting
- Show operation progress or errors inline

**Constraints**
- NativeAOT-compatible — use `System.IO` and `Process.Start` only, no reflection
- Confirm destructive operations (delete, overwrite on paste) via a dialog
- ~~Disable conflicting keybindings when a rename prompt or confirm dialog is active~~ ✅ — modal input mode consumes all keys

## Show/hide hidden files

Toggle visibility of hidden files (dotfiles on Unix, Hidden attribute on Windows).

- Keybinding to toggle (e.g. `.` or `Ctrl+H`)
- Hidden by default; persist preference via config
- Config flag: `show_hidden_files` (config file, env var `WADE_SHOW_HIDDEN_FILES`, CLI `--show-hidden-files` / `--no-show-hidden-files`)
- Update `HelpOverlay.Bindings` (`src/Wade/UI/HelpOverlay.cs`) and README keybindings table to include the new toggle

## Config audit

Review existing config options and evaluate whether any should be added, removed, or changed.

- Are all current options (`show_icons_enabled`, `image_previews_enabled`) still needed?
- New options to consider: `show_hidden_files`, sort order, default sort direction
- Ensure config file, env vars, and CLI flags stay in sync across all options
