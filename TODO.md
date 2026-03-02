# TODO

## Config system

Unified config that merges three sources (in increasing priority): config file → environment variables → command-line flags.

**Config file**
- Format: TOML — readable, no-dependency, increasingly standard for CLI tools
- Location: `~/.config/wade/config.toml` (works on both Windows and Unix)
- Library: [Tomlyn](https://github.com/xoofx/Tomlyn) — NativeAOT-compatible via `TomlModelAttribute` source generator
- Alternatively roll our own minimal TOML/INI parser to keep zero dependencies

**Environment variables**
- Prefix: `WADE_` (e.g. `WADE_ICONS=false`)

**Command-line flags**
- Already handled by `Program.cs`; extend as settings grow

**Initial settings to expose**
- `icons` — enable/disable file icons (bool, default: true if Nerd Font detected or explicitly set)
- `sixel` — enable/disable Sixel image preview (bool, default: auto-detect)

---

## File icons (Nerd Fonts)

Render a small glyph before each filename based on file extension or type.

**Approach**
- Hand-rolled `Dictionary<string, char>` mapping extension → Unicode codepoint (no library needed)
- Fallback icon for unknown files; separate icons for directories, drives, symlinks
- Nerd Fonts occupy Unicode Private Use Area (U+E000–F8FF, U+F0000+)
- Source mappings from [nerdfonts.com/cheat-sheet](https://www.nerdfonts.com/cheat-sheet) or `glyphnames.json` in the Nerd Fonts repo

**Detection / opt-out**
- No reliable runtime font detection; default to **off**, enable via config (`icons = true`)
- Or: check `WT_SESSION` / `TERM_PROGRAM` as a heuristic for capable terminals

**Display**
- Icon + space prefix in `PaneRenderer`, replacing the current `/` / ` ` prefix
- Keep single-char width assumption (all Nerd Font glyphs are 1 or 2 columns — handle both)

---

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
