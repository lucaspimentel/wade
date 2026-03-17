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

**Recommended after:** "Refactor archive preview: move summary into metadata provider" — new formats would follow the cleaner metadata+file-list pattern from the start, avoiding a second refactor pass.

---

## Backlog

### Replace hand-coded P/Invoke with Microsoft.Windows.CsWin32

Consider using the [CsWin32](https://github.com/microsoft/CsWin32) source generator to auto-generate P/Invoke signatures instead of hand-coding `[DllImport]` declarations. Files with manual P/Invoke:
- `src/Wade/FileSystem/SystemClipboard.cs` — user32 (clipboard), kernel32 (GlobalAlloc/Lock/Free), shell32 (DragQueryFileW)
- `src/Wade/FileSystem/Shell32.cs` — shell32 (SHFileOperation)
- `src/Wade/Terminal/TerminalSetup.cs` — kernel32 (GetStdHandle, Get/SetConsoleMode)
- `src/Wade/Terminal/WindowsInputSource.cs` — kernel32 (ReadConsoleInput, WaitForSingleObject)
- `src/Wade/Terminal/LibC.cs` — libc (open, read, poll, tcgetattr, tcsetattr, cfmakeraw) — Unix only, CsWin32 won't cover these

### Support multiple metadata providers per file

Refactor the metadata provider system to allow multiple applicable metadata providers per file. Currently `MetadataProviderRegistry.GetProvider()` returns only the first match; refactor to return all applicable providers and **combine their metadata sections into a single view**.

**Key difference from preview providers:**
- **Metadata**: Combine and show all applicable providers' sections together (complementary information: document metadata + archive metadata)
- **Preview**: Cycle through applicable preview providers (alternative views; user picks one at a time)

**Changes:**
- `MetadataProviderRegistry.GetProvider()` → `GetApplicableProviders()` returning `List<IMetadataProvider>`
- `App.cs`: replace `IMetadataProvider? _activeMetadataProvider` with `List<IMetadataProvider>? _applicableMetadataProviders`
- No cycling keybinding needed for metadata (unlike previews) — all sections are shown together
- `PreviewLoader.BeginLoad()` signature updated to accept `List<IMetadataProvider>` instead of single provider
- Update metadata rendering: pass all applicable providers' sections to `MetadataRenderer.Render()` in order, merge all `MetadataSection[]` results into one combined array (preserves provider order)
- Metadata is shown in the right column when a file is selected and in the file details dialog (`i` key)

**Benefits:**
- Files like DOCX show both document metadata (title, author, dates) AND archive metadata (total size, file count, compression ratio) in one cohesive view
- Same for NUPKG (NuGet metadata + archive metadata), EPUB (EPUB metadata + archive metadata), etc.
- Cleaner architecture: each metadata aspect is its own provider, not crammed into one

**Files to touch:**
- `src/Wade/Preview/MetadataProviderRegistry.cs` — refactor `GetProvider()` to `GetApplicableProviders()`
- `src/Wade/Preview/IMetadataProvider.cs` — no interface changes
- `src/Wade/App.cs` — update metadata provider state management (list, no active index)
- `src/Wade/PreviewLoader.cs` — accept `List<IMetadataProvider>`, load all in sequence or parallel, combine results
- `src/Wade/UI/MetadataRenderer.cs` — no changes (already accepts `MetadataSection[]`, which can be merged from multiple providers)

**Prerequisite for:**
- "Refactor archive preview: move summary into metadata provider" — once multiple metadata providers are supported, archive metadata naturally fits as an additional provider for files with existing metadata (DOCX, NUPKG)

### Refactor archive preview: move summary into metadata provider

**⚠️ PREREQUISITE: "Support multiple metadata providers per file" must be completed first.**

Separate archive summary info (total size, compressed size, ratio, file count) from the file list preview. Show summary as metadata above the preview, consistent with PDF + image + metadata pattern.

**Current structure:**
- `ZipPreview.GetPreviewLines()` returns file list + summary line at bottom (see `ZipPreview.cs:107`)
- One preview pane showing everything together

**Desired structure:**
- `ZipPreview.GetPreviewLines()` returns only the file entries (columns: Size, Compressed, Ratio, Name)
- Archive metadata (total size, compressed size, ratio, file count) moves to metadata providers:
  - **For formats with existing metadata providers:** update `OfficeMetadataProvider` (DOCX, XLSX, PPTX, etc.) and `NuGetMetadataProvider` to also include archive summary metadata alongside their existing document metadata. Archive info becomes an additional `MetadataSection` in the results. Because multiple metadata providers are now supported, both document metadata and archive metadata can coexist.
  - **For pure archives:** create new `ArchiveMetadataProvider : IMetadataProvider` for formats without existing metadata (`.zip`, `.jar`, `.war`, `.ear`, `.apk`, `.vsix`, `.whl`, etc.). Handles archive summary extraction.
- Register `ArchiveMetadataProvider` in `MetadataProviderRegistry`
- Archive preview will then display: metadata (including archive summary) above → file list below (as usual)

**Files to touch:**
- `src/Wade/FileSystem/ZipPreview.cs` — refactor `GetPreviewLines()` to remove summary line
- `src/Wade/Preview/OfficeMetadataProvider.cs` — add archive metadata section to existing document metadata
- `src/Wade/Preview/NuGetMetadataProvider.cs` — add archive metadata section to existing NuGet metadata
- New `src/Wade/Preview/ArchiveMetadataProvider.cs` — implement `IMetadataProvider` for pure archive types
- `src/Wade/Preview/MetadataProviderRegistry.cs` — register the new provider

### Reduce hotkey listings in help dialog

Simplify the help dialog by removing most hardcoded hotkey references and instead directing users to the action list (accessible via `?` or action palette), which already displays hotkeys for every action.

- Help dialog duplicates hotkey information that's already available in the action list
- Users don't need a separate help reference if they can search/browse actions with keybindings shown inline
- Keep only the essential discovery info in help: "Press `?` to open the action list" with a brief summary of major modes/contexts
- Remove static hotkey tables from `HelpOverlay.cs` (`src/Wade/UI/HelpOverlay.cs`)
- Test UX: new user should be able to discover hotkeys by opening action palette (`?`) rather than reading a separate help page
- This also reduces maintenance burden when hotkeys change

### Add config option to show/hide file metadata

**Note:** If "Support multiple metadata providers per file" lands first, the toggle should hide/show all combined metadata sections, not just the first provider's output. Either order works, but be aware of the interaction.

Add a `file_metadata_enabled` config flag (like `file_preview_enabled`) to allow users to toggle the file metadata display in the right column.

- New config key: `file_metadata_enabled` (default: `true`)
- Add toggle keybinding (similar to preview toggle): suggest `Alt+M` or `Ctrl+Shift+M` (avoid reserved `Ctrl+Shift+<key>` in Windows Terminal)
- Toggle action: `AppAction.ToggleFileMetadata`
- When disabled: right column shows nothing (or falls back to preview-only if preview is enabled)
- Implementation: check `_config.FileMetadataEnabled` before rendering metadata in the right pane (similar to `_config.PreviewPaneEnabled` in `App.cs`)
- Status bar may show a visual indicator (like it does for preview pane)
- Keyboard help/action menu should list this toggle

**Files to touch:**
- `src/Wade/Config.cs` — add `bool FileMetadataEnabled { get; set; }` property
- `src/Wade/ConfigSchema.cs` — add YAML schema entry with default
- `src/Wade/App.cs` — add toggle action, check flag before rendering metadata
- `src/Wade/Terminal/InputReader.cs` — add keybinding for toggle
- Optional: `src/Wade/UI/StatusBar.cs` — show indicator if metadata display is off

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

### ~~F5 as alias for Ctrl+R (refresh)~~ ✓

~~Add F5 as a second keybinding for `AppAction.Refresh`, alongside the existing `Ctrl+R`.~~

~~- `InputReader.cs:132` handles `Ctrl+R → AppAction.Refresh`; add `ConsoleKey.F5 → AppAction.Refresh` in the same file~~
~~- On Unix, F5 is already decoded as `ConsoleKey.F5` in `UnixInputSource.cs:275` (VT sequence `\e[15~`)~~
~~- On Windows, `WindowsInputSource` passes `(ConsoleKey)k.wVirtualKeyCode` directly — `VK_F5 = 0x74` maps to `ConsoleKey.F5` automatically~~
~~- One-liner addition next to the `Ctrl+R` check in `InputReader.cs`~~

### Show filename in metadata header

When displaying file metadata in the right pane (above a preview, if any), include the filename as a header entry so it's immediately visible without looking at the file list.

- `MetadataRenderer.Render` in `src/Wade/UI/MetadataRenderer.cs` renders `MetadataSection[]` — add a filename entry either as a dedicated top section or as part of every metadata result
- Alternatively, inject it in `App.cs` before passing sections to `MetadataRenderer`
- Consider styling the filename prominently (e.g., bold, lighter color) to distinguish it from regular label/value entries
- Also show for the metadata-only display path (`_cachedMetadataSections` is not null, no preview provider) at `App.cs:1100`

### Image metadata provider

Show image metadata (resolution, format, color depth, EXIF) above the image preview, like PDF does.

- Add `ImageMetadataProvider : IMetadataProvider` and register it in `src/Wade/Preview/MetadataProviderRegistry.cs`
- `SixLabors.ImageSharp` is already a dependency for image rendering — use `Image.Identify()` (non-decoding) to read `IImageInfo` for width, height, pixel format, and metadata without loading pixel data
- For EXIF: `ImageSharp` exposes `ExifProfile` via `IImageInfo.Metadata.ExifProfile`; extract useful tags (e.g., Camera make/model, DateTimeOriginal, ISO, exposure, focal length, GPS coordinates)
- Hook into the existing `RenderMetadataWithImage` path in `App.cs:1866` — metadata will appear above the Sixel image automatically
- File types to handle: common raster formats already supported by `ImagePreviewProvider` (`.jpg`, `.jpeg`, `.png`, `.gif`, `.bmp`, `.webp`, `.tiff`, etc.)

### Fix stale/leftover content when switching preview types

**Related to:** "Cancel in-flight preview when navigating to another file" — both deal with stale preview artifacts. Faster cancellation from that task may expose more rendering cleanup edge cases here, so doing both together is recommended.

When switching preview providers (e.g., from Glow-rendered Markdown to None, or from an image to text), residual content from the previous render can remain visible.

- Sixel images bypass the `ScreenBuffer` cell grid (written directly to stdout after flush); when switching away from an image preview, the sixel graphics are not explicitly erased — need to emit a Sixel erase/overwrite or use DEC terminal erase sequence (`\e[2J` scoped to the pane region) before rendering the new content
- Text previews of different lengths: if the new preview is shorter than the previous, leftover lines below the new content stay visible — ensure the pane rect is fully cleared (fill with spaces) before writing new content in `PaneRenderer.RenderPreview`
- Reproduce: select a Markdown file → switch to Glow preview → switch to None → observe residual rendered lines
- Key files: `App.cs` (render dispatch ~line 1096), `src/Wade/UI/PaneRenderer.cs` (`RenderPreview`), sixel write path (~line 215)

### Filesystem-change event subscription (auto-refresh)

Subscribe to filesystem-change events and auto-refresh the view when files are created, modified, deleted, or renamed.

- Use `FileSystemWatcher` on the current directory to detect changes (Created, Changed, Deleted, Renamed events)
- On change, invalidate the affected path in `DirectoryContents` (see `Invalidate`/`InvalidateAll` in `src/Wade/FileSystem/DirectoryContents.cs`)
- Trigger re-render of the file listing pane so new/removed/renamed entries appear immediately
- Also refresh metadata and preview for the currently selected file if it was modified
- Debounce rapid events (e.g. bulk file operations) to avoid excessive re-renders
- Re-subscribe watcher when navigating to a different directory
- Consider watching subdirectories (for preview pane directory listings) but be mindful of performance on large trees
- Handle watcher buffer overflow (`Error` event) gracefully — fall back to full directory re-read
- NativeAOT-compatible (`FileSystemWatcher` is supported in NativeAOT)

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
