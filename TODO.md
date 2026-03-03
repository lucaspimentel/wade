# TODO

## Async preview loading

Load file previews on a background thread so navigation stays responsive for large files.

**Problem**
- `FilePreview.GetPreviewLines` blocks the UI thread: reads file content and scans for metadata
- The main loop blocks on `Console.ReadKey`, so a background result needs to trigger a re-render

**Approach**
- Move `GetPreviewLines` to a background `Task` using `Task.Run`
- Show a `[loading…]` placeholder in the preview pane immediately on cache miss
- Store the in-flight `Task<(string[], int)>` alongside the cached result
- Break the `Console.ReadKey` block: poll `Console.KeyAvailable` in a loop with a short sleep, or use a dedicated input thread that posts to a shared queue — so the main loop can check task completion between keypresses
- On task completion, store result in cache and trigger a re-render; discard result if the selected path has changed

**Constraints**
- NativeAOT-compatible — no reflection; `Task`/`CancellationToken` are fine
- `DirectoryContents` cache uses a plain `Dictionary` — make it `ConcurrentDictionary` or guard with a lock if accessed from background threads

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
