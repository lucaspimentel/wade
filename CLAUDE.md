# wade

TUI file browser in .NET 10, targeting Windows Terminal first.

## Build & Test

```bash
dotnet build Wade.slnx
dotnet test Wade.slnx
dotnet run --project src/Wade [path]
pwsh -NoProfile -File tests/Test-Config.ps1   # config integration tests
```

## Key Conventions

- **No TUI frameworks** — rendering uses raw VT/ANSI escape sequences only (no Terminal.Gui, no Spectre.Console)
- **NativeAOT-ready** — avoid reflection-heavy patterns; `[GeneratedRegex]` is AOT-safe and used in `MarkdownLanguage`
- **Platform separator** — use `Path.DirectorySeparatorChar`, not hardcoded `/` or `\`
- **Windows console input** — do NOT enable `ENABLE_VIRTUAL_TERMINAL_INPUT`; input uses Win32 `ReadConsoleInput` (not `Console.ReadKey`). VT processing is output-side only. Mouse input uses `ENABLE_MOUSE_INPUT` (Windows) and SGR mouse mode VT sequences (Unix).
- **Sixel image preview** — uses SixLabors.ImageSharp for decoding/scaling and a custom median-cut Sixel encoder (`Wade.Imaging`). Sixel data bypasses `ScreenBuffer` (written directly to stdout after flush). Gated by `ImagePreviewsEnabled` config flag (default: on).
- Uses `UseArtifactsOutput` — build output goes to `artifacts/`, not `bin/`/`obj/`
