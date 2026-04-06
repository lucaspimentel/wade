# TODO

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
