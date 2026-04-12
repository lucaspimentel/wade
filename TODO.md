# TODO

## Features

### ~~File action progress indicator~~ (Done)

Progress dialog with file count, progress bar, current filename, and Esc to cancel. Copy, move, and delete operations run in background via `FileOperationRunner`.

### System clipboard — Unix/macOS file interop

Windows file clipboard interop is implemented. Remaining: Unix/macOS file clipboard interop via `xclip`/`xsel`/`wl-copy`/`pbcopy` with `text/uri-list` MIME type.

### Format-specific metadata providers

#### Backlog

- **Font files** (`.ttf`, `.otf`, `.woff2`) — font family, style, weight, glyph count. Parse OpenType/TrueType `name` and `head` tables.
- **OpenDocument** (`.odt`, `.ods`, `.odp`) — title, author, dates, page/sheet count. Extract from `meta.xml` inside the ODF zip archive (similar to Office OOXML approach). These are zip-based; **benefits from** "Support multiple metadata providers per file" to show document metadata alongside archive metadata.
- **EPUB** (`.epub`) — title, author, publisher, language, identifier. Extract from `content.opf` metadata inside the zip archive. Also zip-based; **benefits from** "Support multiple metadata providers per file" for the same reason.
- ~~**Windows shortcuts** (`.lnk`)~~ (Done) — parser source copied from [lucaspimentel/windows-shortcut-parser](https://github.com/lucaspimentel/windows-shortcut-parser) into `src/Wade/LnkParser/`. `ShortcutMetadataProvider` extracts target path, working dir, arguments, description, icon, hotkey, volume label. `ShortcutPreviewProvider` shows target and key properties as styled text.

### ~~Zip — other archive formats~~ (Done)

- Tar/gzip preview implemented via `System.Formats.Tar` + `System.IO.Compression.GZipStream` in `src/Wade/FileSystem/TarPreview.cs` (NativeAOT-safe, no new packages). Covers `.tar`, `.tar.gz`, `.tgz`, and plain `.gz`.
- `TarContentsPreviewProvider` registered in `PreviewProviderRegistry` ahead of `TextPreviewProvider`.
- Plain `.gz` is probed for ustar magic at offset 257 of the decompressed stream; `.gz`-wrapping-tar is auto-detected as `tar.gz`. Single-member gzip shows original filename + ISIZE-based uncompressed hint + head of decompressed text when textual.
- `ArchiveMetadataProvider` extended to surface tar/gz metadata (Files, Total size, Format, Compressed, Ratio) through the existing `ArchiveMetadataEnabled` gate.

---

## Backlog

### Preview for Office/document formats (DOCX, XLSX, PPTX, etc.)

Research and implement image previews for Office Open XML formats (`.docx`, `.xlsx`, `.pptx`, `.dotx`, `.xltx`, `.potx`) by converting the first page to an image, similar to PDF preview.

- `OfficeMetadataProvider` already extracts metadata (title, author, dates) from DOCX/XLSX/PPTX — reuse for preview metadata
- Investigate toolchain:
  - **LibreOffice headless**: `soffice --headless --convert-to pdf --outdir /tmp /path/to/docx` → PDF, then `pdftopng` to PNG. Heavyweight but comprehensive.
  - **Lighter alternatives**: Research if lighter/faster converters exist (e.g., `pandoc` for DOCX→HTML, but visual fidelity TBD)
  - **Availability**: Check if LibreOffice or alternatives are commonly available on Windows/macOS/Linux
- Create `OfficeImageConverter : IImageConverter` (or extend `ImageConverter` with Office support)
- Register in `ImageConverter.CanConvert()` fallback logic
- Add `OfficePreviewProvider : IPreviewProvider` (or extend `PdfPreviewProvider` to handle both PDF and Office)
- Preview registry order: insert after `PdfPreviewProvider` so Office files are attempted
- Performance concern: LibreOffice startup is slow (~1-2s); consider debouncing or async user feedback
- Start with DOCX (most common); extend to XLSX/PPTX after proving the concept

### ~~Reparse point type detection — junction points (`IO_REPARSE_TAG_MOUNT_POINT`)~~ (Done)

- [x] Detect junction points via `GetFileInformationByHandleEx` with `FileAttributeTagInfo` to read the reparse tag.
  - `ReparsePointDetector` queries the reparse tag; `IsJunctionPoint` field on `FileSystemEntry`.
  - Properties overlay shows "Junction -> Directory" for type and "Junction" instead of "ReparsePoint" in attributes.
  - Junction-specific icon (`nf-md-folder_arrow_right`). Cyan symlink styling and " -> target" suffix in file list.
  - Windows-only (junctions don't exist on Unix).

### ~~Reparse point type detection — app execution aliases (`IO_REPARSE_TAG_APPEXECLINK`)~~ (Done)

- [x] Detect app execution aliases (reparse tag `0x8000001B`) via `FSCTL_GET_REPARSE_POINT` and parse the reparse data buffer to extract the target executable path.
  - `ReparsePointDetector.IsAppExecLink()` and `GetAppExecLinkTarget()` with `ParseAppExecLinkTarget` (internal static, testable).
  - Properties overlay shows "App Execution Alias" type and target path. Attributes show "AppExecLink" instead of "ReparsePoint".
  - Dedicated icon (`nf-md-application_outline`). Target shown with " → " suffix in file list.
  - Windows-only.

### Text input improvements

- [x] Support paste in text input fields (file finder, filter, go-to-path, rename dialog). Unix: bracketed paste mode (`ESC[200~` ... `ESC[201~`). Windows: heuristic batch detection from `ReadConsoleInput`.
- [x] Support word-navigation shortcuts in text input fields: `Ctrl+Left`/`Ctrl+Right` to skip words, `Ctrl+Backspace` to delete previous word.

### Windows Terminal VT input mode

Investigate enabling `ENABLE_VIRTUAL_TERMINAL_INPUT` on Windows Terminal (detected via `WT_SESSION`) to get proper bracketed paste and modifier key support via VT sequences instead of structured `ReadConsoleInput` records. This would unify the input pipeline with Unix but requires significant refactoring of `WindowsInputSource`.

### Keyboard shortcut convention audit

- [ ] Review remaining keybinding consistency. Current mix: some dialogs/tools use `Ctrl+` (`Ctrl+F` finder, `Ctrl+T` terminal, `Ctrl+L` symlink, `Ctrl+R` refresh, `Ctrl+P` command palette, `Ctrl+G` go-to-path) while others use bare keys (`n`/`N` new file/dir, `b`/`B` bookmarks, `/` filter, `,` config, `?` help, `i` properties). Convention: `Ctrl+<key>` for opening tools/dialogs/overlays, bare keys for direct actions.

### ~~CSS color swatches in preview~~ (Done)

- Hex color literals (`#RGB`, `#RGBA`, `#RRGGBB`, `#RRGGBBAA`) in CSS/SCSS/Sass previews now render a ` ██` swatch (U+2588 full-block × 2, colored to the literal) immediately after the hex text.
- New `TokenKind.HexColor`; detection added to `CssLanguage.ScanCss` with line-local value-position tracking (`afterColon` flag + `;`/`{`/`}` reset) so ID selectors like `#main` are not false-positived.
- Alpha discarded for v1. Invalid hex (`#gg0000`) and non-standard lengths (2, 5, 7) are rejected.
- Implementation emits a full `CellStyle[]` via `SyntaxTheme.GetStyle` lookups for lines with hex colors; lines without hex colors remain on the token-span path with zero extra allocation. Follows the `DiffLanguage` precedent — no renderer or `StyledLine` changes needed.
- Known v1 limitation: `:pseudo #abc { }` (pseudo-class followed by an ID selector that happens to be valid hex) can produce a false positive. Acceptable — future work can track brace depth multi-line via the `state` byte. `rgb()`/`hsl()` and named colors still TODO.

### CSS color swatches — disambiguate pseudo-class/element selectors

Follow-up to the shipped hex swatch feature in `src/Wade/Highlighting/Languages/CssLanguage.cs`. Current limitation: `a:hover #abc { }` incorrectly swatches `#abc` as a color because the line-local `afterColon` flag doesn't distinguish a property separator from a pseudo `:`/`::`.

- Track brace depth (or an in-block bit) across lines via the `state` byte — currently `state` only encodes `StateNormal`/`StateBlockComment`. Add a bit (e.g. `StateInBlock = 0b100`) and refactor `state == StateBlockComment` equality checks to mask-aware checks.
- Only treat `#hex` as a color when both `insideBraces` (across lines) AND `afterColon` (line-local) are true. Selectors live outside `{ }`, values live inside.
- Handle nested `@media { @supports { ... } }` — a single in-block bit is sufficient for value-position detection since any brace depth ≥ 1 means we're inside a declaration block.
- Update tests to cover `a:hover #abc { }`, multi-line rules (`color:\n  #abc;`), and `@media (min-width: 600px) { .x { color: #abc; } }`.

### CSS color swatches — `rgb()` / `rgba()` / `hsl()` / `hsla()` / named colors

Extend the swatch feature to cover the remaining CSS color notations.

- Detection sites in `CssLanguage.ScanCss`: `rgb(`, `rgba(`, `hsl(`, `hsla(` function-call syntax. Parse decimal/percent arguments, handle both comma-separated (legacy) and space-separated (CSS Colors Level 4) syntaxes, and both `hsl(360, 50%, 50%)` and `hsl(360deg 50% 50%)` forms.
- HSL → RGB conversion helper alongside `TryParseHexColor` in `CssLanguage`.
- Named CSS colors (`red`, `rebeccapurple`, `cornflowerblue`, etc.) — full list is ~148 entries. Use a `FrozenDictionary<string, Color>` for lookup. Only match when `afterColon` is true and the identifier is followed by a non-identifier char (so `.red { }` selectors are not swatched).
- Modern CSS Color 4 syntax (`color(display-p3 ...)`, `lab()`, `lch()`, `oklab()`, `oklch()`) — out of scope; these need proper color-space conversion math.
- Reuse the existing `BuildSwatchResult` pipeline — it already handles arbitrary `HexColorMatch` positions, so only the detection layer needs extension. Consider renaming `HexColorMatch` to `CssColorMatch` once non-hex sources are added.
