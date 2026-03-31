# TODO

## Bugs (open)

(none)

---

## Refactoring

(none)

---

## Features

### File action progress indicator

Progress indicator for large copy/move operations.

### System clipboard — Unix/macOS file interop

Windows file clipboard interop is implemented. Remaining: Unix/macOS file clipboard interop via `xclip`/`xsel`/`wl-copy`/`pbcopy` with `text/uri-list` MIME type.

### Format-specific metadata providers

#### Backlog

- **Font files** (`.ttf`, `.otf`, `.woff2`) — font family, style, weight, glyph count. Parse OpenType/TrueType `name` and `head` tables.
- **OpenDocument** (`.odt`, `.ods`, `.odp`) — title, author, dates, page/sheet count. Extract from `meta.xml` inside the ODF zip archive (similar to Office OOXML approach). These are zip-based; **benefits from** "Support multiple metadata providers per file" to show document metadata alongside archive metadata.
- **EPUB** (`.epub`) — title, author, publisher, language, identifier. Extract from `content.opf` metadata inside the zip archive. Also zip-based; **benefits from** "Support multiple metadata providers per file" for the same reason.
- **Windows shortcuts** (`.lnk`) — target path, working directory, arguments, timestamps, icon location, hotkey, file attributes, volume info, extra data (environment variables, known folder, tracker info). Reuse existing parser from [lucaspimentel/windows-shortcut-parser](https://github.com/lucaspimentel/windows-shortcut-parser) — pure C#, NativeAOT-safe, zero dependencies. Entry point: `LnkFile.Parse(path)` → `GetTargetPath()`, `Header` (timestamps, size, attributes), `StringData` (name, relative path, working dir, args, icon location), `LinkInfo` (local/network path, volume info), `ExtraDataBlocks`. Add as a project reference or publish as NuGet package. Preview could show the resolved target path prominently, with metadata sections for shortcut properties. Windows-only (`.lnk` is a Windows format; on Unix, detect but skip gracefully).

### Zip — other archive formats

Support additional archive formats in the preview pane (`.tar`, `.gz`, `.tar.gz`). Zip preview is already implemented via `System.IO.Compression.ZipFile`.

Archive summary metadata is now handled by `ArchiveMetadataProvider` — new formats should follow the same metadata+file-list pattern.

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

### Reparse point type detection — junction points (`IO_REPARSE_TAG_MOUNT_POINT`)

- [ ] Detect junction points via `FSCTL_GET_REPARSE_POINT` / `DeviceIoControl` and parse the `REPARSE_DATA_BUFFER` to extract the target path (substitute name field, UTF-16).
  - Currently wade only shows `ReparsePoint` as a raw attribute flag in `PropertiesOverlay.cs:358` — no type distinction or target resolution.
  - Display the resolved target path in properties overlay and preview pane (similar to symlink target display).
  - Windows-only (junctions don't exist on Unix).

### Reparse point type detection — app execution aliases (`IO_REPARSE_TAG_APPEXECLINK`)

- [ ] Detect app execution aliases (reparse tag `0x8000001B`) via `FSCTL_GET_REPARSE_POINT` and parse the custom reparse data buffer to extract the target executable path, package family name, and app entry point (three null-terminated UTF-16 strings).
  - These are zero-byte reparse points used by Windows for MSIX/UWP packaged apps (e.g., `C:\Users\...\WindowsApps\wt.exe`).
  - Display the resolved target and package info in properties overlay and preview pane.
  - Windows-only.

### Fix stale/leftover content when switching preview types

When switching preview providers (e.g., from Glow-rendered Markdown to None, or from an image to text), residual content from the previous render can remain visible.

- Sixel images bypass the `ScreenBuffer` cell grid (written directly to stdout after flush); when switching away from an image preview, the sixel graphics are not explicitly erased — need to emit a Sixel erase/overwrite or use DEC terminal erase sequence (`\e[2J` scoped to the pane region) before rendering the new content
- Text previews of different lengths: if the new preview is shorter than the previous, leftover lines below the new content stay visible — ensure the pane rect is fully cleared (fill with spaces) before writing new content in `PaneRenderer.RenderPreview`
- Reproduce: select a Markdown file → switch to Glow preview → switch to None → observe residual rendered lines
- Key files: `App.cs` (render dispatch ~line 1096), `src/Wade/UI/PaneRenderer.cs` (`RenderPreview`), sixel write path (~line 215)

### Drive list view improvements (Windows)

When at the top level (listing drives via `DirectoryContents.GetDriveEntries()`):

- [ ] Hide the date column (not meaningful for drives)
- [ ] Show drive info columns instead: file system type, free space, total size, percent-full bar (see reference screenshot)
  - Key files: `PaneRenderer` (column rendering), `DirectoryContents.cs:53` (`GetDriveEntries`), `FileSystemEntry` (may need additional drive properties)
  - `DriveInfo` already provides `DriveFormat`, `AvailableFreeSpace`, `TotalSize`
- [ ] Add drive details to the properties overlay (`i` key) — file system, free/total space, percent full, volume label, drive type
  - Key file: `PropertiesOverlay.cs`

### Keyboard shortcut convention audit

- [ ] Review remaining keybinding consistency. Current mix: some dialogs/tools use `Ctrl+` (`Ctrl+F` finder, `Ctrl+T` terminal, `Ctrl+L` symlink, `Ctrl+R` refresh, `Ctrl+P` command palette, `Ctrl+G` go-to-path) while others use bare keys (`n`/`N` new file/dir, `b`/`B` bookmarks, `/` filter, `,` config, `?` help, `i` properties). Convention: `Ctrl+<key>` for opening tools/dialogs/overlays, bare keys for direct actions.
