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

### Right-click context menu

Popup menu at click position with contextual actions.

- Trigger on right-click (mouse button 2)
- Show actions relevant to the clicked item: open, rename, delete, copy, cut, paste, properties
- Render as a floating menu overlay at the click coordinates
- Keyboard dismissal (Escape) and selection (Enter / click)

### System clipboard — Unix/macOS file interop

Windows file clipboard interop is implemented. Remaining: Unix/macOS file clipboard interop via `xclip`/`xsel`/`wl-copy`/`pbcopy` with `text/uri-list` MIME type.

### Drive type detection

Detect whether a drive is SSD, HDD, or network. Infrastructure for gating features by drive speed.

- On Windows, use `DeviceIoControl` / `IOCTL_STORAGE_QUERY_PROPERTY` to distinguish SSD vs HDD; `DriveInfo.DriveType == Network` for network drives
- On Linux, use `/sys/block/<dev>/queue/rotational` (0 = SSD, 1 = HDD)
- `DriveInfo` is already used in `DirectoryContents.GetDriveEntries()` and `PropertiesOverlay`
- Store detected drive type on `FileSystemEntry` or as a lookup available to consumers
- Must be NativeAOT-compatible (no reflection-heavy WMI wrappers)

### Inline directory sizes in file list

Show computed directory sizes in the size column of the center pane file list. Gated per drive type so slow drives don't trigger expensive recursive scans.

- **Depends on:** Drive type detection
- Background async computation following the `GitStatusLoader` / `DirectorySizeLoader` pattern
- Per-drive-type config settings:
  - Show directory size for SSD (default true)
  - Show directory size for HDD (default false)
  - Show directory size for network drives (default false)
- Add settings to `WadeConfig` and `ConfigDialogState`
- Cancel/restart computation on directory navigation
- Sizes displayed in the existing size column (`SizeColumnEnabled`)

### Format-specific metadata providers

#### Backlog

- **Font files** (`.ttf`, `.otf`, `.woff2`) — font family, style, weight, glyph count. Parse OpenType/TrueType `name` and `head` tables.
- **OpenDocument** (`.odt`, `.ods`, `.odp`) — title, author, dates, page/sheet count. Extract from `meta.xml` inside the ODF zip archive (similar to Office OOXML approach). These are zip-based; **benefits from** "Support multiple metadata providers per file" to show document metadata alongside archive metadata.
- **EPUB** (`.epub`) — title, author, publisher, language, identifier. Extract from `content.opf` metadata inside the zip archive. Also zip-based; **benefits from** "Support multiple metadata providers per file" for the same reason.
- **Windows shortcuts** (`.lnk`) — target path, working directory, arguments, timestamps, icon location, hotkey, file attributes, volume info, extra data (environment variables, known folder, tracker info). Reuse existing parser from [lucaspimentel/windows-shortcut-parser](https://github.com/lucaspimentel/windows-shortcut-parser) — pure C#, NativeAOT-safe, zero dependencies. Entry point: `LnkFile.Parse(path)` → `GetTargetPath()`, `Header` (timestamps, size, attributes), `StringData` (name, relative path, working dir, args, icon location), `LinkInfo` (local/network path, volume info), `ExtraDataBlocks`. Add as a project reference or publish as NuGet package. Preview could show the resolved target path prominently, with metadata sections for shortcut properties. Windows-only (`.lnk` is a Windows format; on Unix, detect but skip gracefully).
- **URL shortcuts** (`.url`) — URL and icon location. Reuse `UrlFile.Parse(path)` from [lucaspimentel/windows-shortcut-parser](https://github.com/lucaspimentel/windows-shortcut-parser). Simple INI-style format; metadata section showing the target URL prominently. Cross-platform (plain text format, works on Unix too).

### Zip — other archive formats

Support additional archive formats in the preview pane (`.tar`, `.gz`, `.tar.gz`). Zip preview is already implemented via `System.IO.Compression.ZipFile`.

Archive summary metadata is now handled by `ArchiveMetadataProvider` — new formats should follow the same metadata+file-list pattern.

---

## Backlog

### ~~Uncouple "Show Image Previews" and "Show PDF Previews" configs~~ ✅

PDF preview now gates on `SixelSupported` (terminal capability) instead of `ImagePreviewsEnabled` (user preference). Disabling image previews no longer disables PDF previews.

### ~~Remove file size and modified date from default file metadata~~ ✅

Removed Size and Modified entries from `FileMetadataProvider` — they were redundant with the center pane detail columns and properties overlay. Metadata section now only contains filename (header), cloud status, and git status.

### Replace hand-coded P/Invoke with Microsoft.Windows.CsWin32

Consider using the [CsWin32](https://github.com/microsoft/CsWin32) source generator to auto-generate P/Invoke signatures instead of hand-coding `[DllImport]` declarations. Files with manual P/Invoke:
- `src/Wade/FileSystem/SystemClipboard.cs` — user32 (clipboard), kernel32 (GlobalAlloc/Lock/Free), shell32 (DragQueryFileW)
- `src/Wade/FileSystem/Shell32.cs` — shell32 (SHFileOperation)
- `src/Wade/Terminal/TerminalSetup.cs` — kernel32 (GetStdHandle, Get/SetConsoleMode)
- `src/Wade/Terminal/WindowsInputSource.cs` — kernel32 (ReadConsoleInput, WaitForSingleObject)
- `src/Wade/Terminal/LibC.cs` — libc (open, read, poll, tcgetattr, tcsetattr, cfmakeraw) — Unix only, CsWin32 won't cover these

### ~~Metadata and preview for .msi Windows Installer files~~ ✅

Implemented via `MsiMetadataProvider` (ProductName, Version, Manufacturer, ProductCode, UpgradeCode from Property table; Subject, Author, Comments, Platform from summary info stream) and `MsiPreviewProvider` (file table listing with sizes). Uses `msi.dll` P/Invoke (`MsiOpenDatabase` + SQL queries) — Windows-only, returns null on other platforms.

### Preview for Office/document formats (DOCX, XLSX, PPTX, etc.)

**Benefits from:** "Cancel in-flight preview when navigating to another file" — LibreOffice subprocess conversion is slow (~1-2s); proper subprocess cancellation prevents stale Office previews from running after the user navigates away.

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

### Fix stale/leftover content when switching preview types

**Related to:** "Cancel in-flight preview when navigating to another file" — both deal with stale preview artifacts. Faster cancellation from that task may expose more rendering cleanup edge cases here, so doing both together is recommended.

When switching preview providers (e.g., from Glow-rendered Markdown to None, or from an image to text), residual content from the previous render can remain visible.

- Sixel images bypass the `ScreenBuffer` cell grid (written directly to stdout after flush); when switching away from an image preview, the sixel graphics are not explicitly erased — need to emit a Sixel erase/overwrite or use DEC terminal erase sequence (`\e[2J` scoped to the pane region) before rendering the new content
- Text previews of different lengths: if the new preview is shorter than the previous, leftover lines below the new content stay visible — ensure the pane rect is fully cleared (fill with spaces) before writing new content in `PaneRenderer.RenderPreview`
- Reproduce: select a Markdown file → switch to Glow preview → switch to None → observe residual rendered lines
- Key files: `App.cs` (render dispatch ~line 1096), `src/Wade/UI/PaneRenderer.cs` (`RenderPreview`), sixel write path (~line 215)

### ~~Filesystem-change event subscription (auto-refresh)~~ ✅

Implemented via `FileSystemWatcherManager` — watches current directory with 300ms debounce, injects `FileSystemChangedEvent` into the input pipeline. Selection preserved by name across refreshes. Buffer overflow falls back to full cache invalidation. Watcher automatically re-subscribes on directory navigation.

### ~~Cancel in-flight preview when navigating to another file~~ ✅

Implemented — subprocess-based providers (Glow, ffprobe/mediainfo, pdftopng) now kill the process on cancellation token fire. Text/hex providers check cancellation between chunks. Stale events are discarded via path guards.

### ~~Investigate oversized image preview rendering~~ ✅

Fixed — `ImagePreview.Load()` scales down to fit pane pixel dimensions correctly.

### ~~Refresh when cloud file finishes downloading~~ ✅

Handled by `FileSystemWatcherManager` — attribute changes when a cloud file finishes downloading trigger an auto-refresh.
