# TODO

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
