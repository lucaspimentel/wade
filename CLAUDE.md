# wade

TUI file browser in .NET 10. Runs on Windows Terminal and Unix/WSL terminals.

## Build & Test

```bash
dotnet build Wade.slnx
dotnet test Wade.slnx
dotnet run --project src/Wade [path]
```

## Key Conventions

- **No TUI frameworks** — rendering uses raw VT/ANSI escape sequences only (no Terminal.Gui, no Spectre.Console)
- **NativeAOT-ready** — avoid reflection-heavy patterns; `[GeneratedRegex]` is AOT-safe and used in `MarkdownLanguage`
- **Platform separator** — use `Path.DirectorySeparatorChar`, not hardcoded `/` or `\`
- **Windows console input** — do NOT enable `ENABLE_VIRTUAL_TERMINAL_INPUT`; input uses Win32 `ReadConsoleInput` (not `Console.ReadKey`). VT processing is output-side only. Mouse input uses `ENABLE_MOUSE_INPUT` (Windows) and SGR mouse mode VT sequences (Unix).
- **Unix console input** — raw mode via `cfmakeraw` P/Invoke on `/dev/tty`; input uses libc `read()`/`poll()` syscalls (not `Console.ReadKey` or `FileStream`). Arrow keys arrive as SS3 sequences (`ESC O A/B/C/D`) in alternate screen mode, not CSI.
- **Sixel image preview** — uses SixLabors.ImageSharp for decoding/scaling and a custom median-cut Sixel encoder (`Wade.Imaging`). Sixel data bypasses `ScreenBuffer` (written directly to stdout after flush). Gated by `ImagePreviewsEnabled` config flag (default: on) AND terminal Sixel capability detection (Windows Terminal via `WT_SESSION`, Unix via DA1 query). Only written in allowlisted modes (`Normal`, `Search`, `ExpandedPreview`) — all modal overlays suppress it automatically. Cell pixel dimensions detected at startup for proper image scaling (defaults: 8×16 px/cell). Non-image formats (PDF) use a convert-to-image pipeline (`IImageConverter` / `ImageConverter`) that shells out to external CLI tools (e.g. `pdftopng`) and feeds the result through `ImagePreview.Load()`.
- **Git status integration** — readonly git status display via `git status --porcelain=v1` (NativeAOT-safe, no LibGit2Sharp). Branch name read from `.git/HEAD` (supports worktrees). Async via `GitStatusLoader` following the `DirectorySizeLoader` pattern. File/directory coloring by status (modified=yellow, staged=cyan, untracked=green, conflict=red). Nerd Font status icons rendered in a dedicated 2-char `StatusColWidth` column between the filename and size column (not inline with the file-type icon). Branch name in status bar is prefixed with a powerline branch icon (`U+E0A0`). Directory-level aggregate status (OR of child statuses). Gated by `git_status_enabled` config (default: on). Status map is a separate `Dictionary<string, GitFileStatus>` passed to `PaneRenderer`, not stored on `FileSystemEntry`. Diff preview accessible via git action menu (`Ctrl+G`) shows colored `git diff` output in the preview pane for modified/staged files; `DiffLanguage` provides per-line `CharStyles` coloring (green=added, red=removed, cyan=hunk headers, dim gray=metadata). The git action menu reuses the action palette infrastructure (same `InputMode.ActionPalette`) with a parameterized `_actionPaletteTitle` field.
- **Cloud file placeholders** — OneDrive/Dropbox/cloud-synced files that aren't fully downloaded locally are detected via Windows Cloud Files API attributes (`FILE_ATTRIBUTE_RECALL_ON_DATA_ACCESS` 0x00400000, `FILE_ATTRIBUTE_RECALL_ON_OPEN` 0x00004000). Cast `FileInfo.Attributes` to `int` and check bits — no P/Invoke needed. Cloud placeholders keep their original file-type icon in the first column; a cloud icon (nf-md-cloud_outline U+F0163) is shown in the status column (same column as git status icons). Dim gray styling (140, 140, 160). Properties dialog shows the original type (File/Directory) and adds "Cloud file" to the Attributes field. Preview loading is skipped to avoid triggering downloads. Detection in `DirectoryContents.CheckIsCloudPlaceholder()`, stored on `FileSystemEntry.IsCloudPlaceholder`. Windows-only (always `false` on Unix).
- **Avoid Ctrl+Shift+&lt;key&gt; keybindings** — Windows Terminal reserves many `Ctrl+Shift+` combinations (`C`, `V`, `P`, `W`, `Tab`, etc.) and `Ctrl+Tab`. Do not assign these as wade keybindings; they will be intercepted by the terminal and never reach the app.
- Uses `UseArtifactsOutput` — build output goes to `artifacts/`, not `bin/`/`obj/`
