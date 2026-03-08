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
- **Sixel image preview** — uses SixLabors.ImageSharp for decoding/scaling and a custom median-cut Sixel encoder (`Wade.Imaging`). Sixel data bypasses `ScreenBuffer` (written directly to stdout after flush). Gated by `ImagePreviewsEnabled` config flag (default: on) AND terminal Sixel capability detection (Windows Terminal via `WT_SESSION`, Unix via DA1 query). Suppressed during modal overlays (Help, Confirm, TextInput, GoToPath, Config). Cell pixel dimensions detected at startup for proper image scaling (defaults: 8×16 px/cell).
- Uses `UseArtifactsOutput` — build output goes to `artifacts/`, not `bin/`/`obj/`
