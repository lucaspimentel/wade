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

Detect whether a drive is SSD, HDD, or network. Some features (like directory size in the file browser) should behave differently based on drive speed.

- On Windows, use WMI or `DeviceIoControl` / `IOCTL_STORAGE_QUERY_PROPERTY` to distinguish SSD vs HDD; `DriveInfo.DriveType == Network` for network drives
- `DriveInfo` is already used in `DirectoryContents.GetDriveEntries()` and `PropertiesOverlay`
- If feasible, add per-drive-type settings for showing directory size inline in the file list:
  - Show directory size for SSD (default true)
  - Show directory size for HDD (default false)
  - Show directory size for network drives (default false)
- Must be NativeAOT-compatible (no reflection-heavy WMI wrappers)

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

### Metadata and preview for .msi Windows Installer files

Add metadata extraction and optional preview for `.msi` (Windows Installer) files.

**Metadata provider (`MsiMetadataProvider`):**
- Extract metadata from MSI property table: product name, version, manufacturer, description, upgrade code, install scope (per-user/per-machine), install location, etc.
- MSI files are OLE compound documents; use `System.IO.Packaging.Package` or manual OLE parsing to read the property set stream (`\x05SummaryInformation` and `Property` table from the database)
- Common properties to extract: `ProductName`, `ProductVersion`, `Manufacturer`, `Subject` (description), `UpgradeCode`, `InstallScope`, `InstallLocation`, `Comments`
- Alternative: shell out to `msiexec /a <msi> /qb TARGETDIR=<tmpdir>` to extract (heavy), or use WiX toolset's `dark.exe` if available (lighter, but external dependency)
- Simplest approach: WMI query `Win32_InstallerProductFile` or `Win32_Product` if the MSI is already installed, but won't work for arbitrary MSI files
- **Best approach:** Parse OLE compound document manually or use a library like `OpenMcdf` (NuGet) to read MSI internal structure (property/summary info streams)

**Preview provider (optional):**
- List installable files (File table from MSI database): filename, target directory, size, version
- Or show a table of contents / installation summary (destination directories, features)
- Simpler alternative: show only metadata, no preview (metadata-only display like some other formats)

**Windows-only:** MSI is a Windows-specific format; on Unix, can detect but skip gracefully.

**Files to touch:**
- New `src/Wade/Preview/MsiMetadataProvider.cs` — implement `IMetadataProvider`
- `src/Wade/Preview/MetadataProviderRegistry.cs` — register the provider
- Optional: New `src/Wade/Preview/MsiPreviewProvider.cs` if preview is desired

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

### Cancel in-flight preview when navigating to another file

**Related to:** "Fix stale/leftover content when switching preview types" — both address stale preview artifacts from different angles (cancellation vs rendering cleanup). "Preview for Office/document formats" also benefits from this since it shells out to slow subprocesses (LibreOffice).

Pressing up/down to change selection while a file preview is still generating should cancel the in-progress load immediately and respond to the navigation without delay.

- The cancellation infrastructure already exists: `PreviewLoader` holds a `CancellationTokenSource`; each `BeginLoad()` call (`src/Wade/PreviewLoader.cs:20`) cancels the previous token before starting a new background task
- Selection change (`NavigateUp`/`NavigateDown` in `App.cs:523-537`) updates `_selectedIndex` immediately, and the next render frame calls `ReloadActiveProvider` (`App.cs:1077`) which triggers `BeginLoad` and cancels the old CTS
- Issue: subprocess-based providers (Glow, ffprobe/mediainfo, pdftopng) may ignore the cancellation token because the `Process` is not explicitly killed when the token fires — add `ct.Register(() => process.Kill())` in each subprocess helper
- Also audit `TextPreviewProvider` and `HexPreviewProvider` for large files — ensure they check `ct.IsCancellationRequested` between chunks, not just at the top of the method
- Stale events: events injected by a cancelled task carry the old path; verify they are silently discarded (check `_pendingPreviewPath` / `_cachedPreviewPath` guards when handling `PreviewReadyEvent`, `ImagePreviewReadyEvent`, `CombinedPreviewReadyEvent`, `MetadataReadyEvent`)
- Key files: `src/Wade/PreviewLoader.cs`, `src/Wade/App.cs:1061`, `src/Wade/Highlighting/GlowRenderer.cs`, `src/Wade/Preview/MediaMetadataProvider.cs`, `src/Wade/FileSystem/HexPreview.cs`

### Investigate oversized image preview rendering

Look into cases where images are too large to fit in the preview pane. `ImagePreview.Load()` already scales down to fit pane pixel dimensions and avoids upscaling (`scale = 1.0` cap), but verify that the sixel output doesn't exceed pane bounds after encoding — especially in expanded preview mode or with unusual cell pixel sizes.

### Refresh when cloud file finishes downloading

When a cloud placeholder file (OneDrive/Dropbox) finishes downloading, automatically refresh the file entry so the cloud icon is removed and the preview/metadata become available. Currently the user must manually refresh (`Ctrl+R` / `F5`) after a cloud file download completes.

- The `FileSystemWatcherManager` (now implemented) watches the current directory and will detect attribute changes when a cloud file finishes downloading, triggering an auto-refresh
- Alternatively, if the "Download cloud file" action is used from within wade, poll or watch for the attribute change after triggering the download
- Windows-only (cloud placeholders are Windows-only)
