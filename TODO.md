# TODO

## Shared UI foundations

These are prerequisites or enablers for multiple features below. Build them first.

### Dialog / overlay framework

Generalize `HelpOverlay` into a reusable dialog primitive.

- Box drawing + centering utility (extract from `HelpOverlay`)
- Title row, scrollable body content area, footer hint row
- Variants:
  - **Message dialog** — static content, dismiss on any key (replaces current `HelpOverlay`)
  - **Confirm dialog** — yes/no prompt (needed by delete, paste overwrite)
  - **Input dialog** — single-line text field with cursor (needed by rename, go-to-path)
- NativeAOT-compatible — no reflection

### Text input widget

Single-line editable text field for use inside dialogs.

- Character buffer + cursor position (insert, delete, Home, End, left/right)
- Render into a dialog row with a visible cursor
- Emit the final string on Enter, cancel on Escape
- **Required by:** Input dialog (rename, go-to-path)

### Modal input mode

Allow dialogs to intercept keyboard input without leaking events to the main navigation loop.

- `InputMode` enum: `Normal`, `Confirm`, `TextInput` (extensible)
- Main loop switches dispatch based on current mode
- Each mode handles only its relevant keys; unhandled keys are ignored or close the dialog
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

## Mouse support

Enable mouse click and scroll interaction.

**Protocol**
- Enable mouse reporting: `ESC [ ? 1000 h` (X10 button events), `ESC [ ? 1006 h` (SGR extended for large terminals)
- Disable on exit: `ESC [ ? 1000 l`, `ESC [ ? 1006 l`
- SGR mouse report format: `ESC [ < Cb ; Cx ; Cy M` (press) / `m` (release)
  - `Cb`: button/modifier bitmask — 0=left, 1=middle, 2=right, 64=scroll-up, 65=scroll-down
  - `Cx`, `Cy`: 1-based column and row

**Interactions**
- Click left pane: focus left pane and move cursor to clicked row
- Click right pane: focus right pane (if directory listing)
- Scroll up/down: move cursor up/down in focused pane
- Click path breadcrumb: navigate to that ancestor directory

**Constraints**
- NativeAOT-compatible — raw byte parsing only, no reflection
- Disable mouse reporting when entering a subprocess (e.g. editor launch) and re-enable on return
- **Depends on:** Raw input pipeline

## Buffered rendering and dirty-region tracking

Reduce flicker and unnecessary redraws by buffering output and only updating changed regions.

**Buffered rendering**
- Compose the full frame in a `StringBuilder` (or similar buffer) before writing to stdout in one shot
- Wrap in `Console.Out.Write(buffer)` instead of many small writes scattered across pane renderers
- Hide cursor during frame write (`ESC [ ? 25 l`) and restore after (`ESC [ ? 25 h`) to eliminate cursor flicker

**Dirty-region tracking**
- Maintain a logical cell grid (previous frame vs. current frame)
- On each render pass, diff the two buffers and only emit escape sequences + characters for changed cells/lines
- Skip rewriting lines that haven't changed since last frame
- Full repaint on terminal resize; incremental updates otherwise

**Constraints**
- NativeAOT-compatible — no reflection
- Must not break existing VT sequence output (colors, box-drawing, etc.)

## Sixel image preview

Show image thumbnails in the right preview pane; optionally open a larger view in a centered dialog.

**Protocol**
- Sixel is a DCS escape sequence: `ESC P ... q <data> ESC \`
- Supported by Windows Terminal v1.22+ (released August 2024)

**Library candidates**
- [SixPix](https://www.nuget.org/packages/SixPix) — pure .NET Sixel encoder/decoder
- [Webmaster442.WindowsTerminal.ImageSharp](https://libraries.io/nuget/Webmaster442.WindowsTerminal.ImageSharp) — ImageSharp-based wrapper
- Evaluate NativeAOT compatibility before committing to either

**Terminal capability detection**
- Query DA2 (`ESC [ > c`) and parse response; Sixel support is indicated by parameter 4 in the DA1 response
- Simpler: check `WT_SESSION` env var (Windows Terminal) combined with version check, or let user enable via config
- Provide `sixel = false` config opt-out for terminals that lie or partially support it

**Preview pane integration**
- Detect image extensions (`.png`, `.jpg`, `.gif`, `.bmp`, `.webp`, …)
- Scale image to fit right pane dimensions (cells × cell pixel size)
- Cell pixel size: query via `ESC [ 16 t` (reports cell height/width in pixels)
- Fall back to text preview (file type + dimensions) if Sixel unavailable

**Full-size dialog**
- Press Enter on an image to open a centered overlay (reuse `HelpOverlay` pattern)
- Render at max terminal size minus border
- Any key dismisses

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
- Disable conflicting keybindings when a rename prompt or confirm dialog is active
