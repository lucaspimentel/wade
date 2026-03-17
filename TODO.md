# TODO

## Bugs (open)

(none)

---

## Refactoring

### ~~External CLI tool abstraction~~ ✅

Extracted `CliTool` static helper for shared process spawning (availability cache + run). Replaced per-tool `Lazy<bool>` and `RunProcess` boilerplate in `GlowRenderer`, `XpdfPdfTool`, `PdfMetadataProvider`, `MediaMetadataProvider`. Added `CliToolHints` for missing-tool messages in preview pane. Replaced `GlowMarkdownPreviewEnabled`/`PdfPreviewEnabled` config flags with unified `disabled_tools` (per-tool toggles in settings overlay).

### ~~Suppress placeholder previews when metadata is present~~ ✅

When a metadata provider returns structured data and the active preview is just a placeholder (`[binary file]`, `[empty file]`, etc.), render metadata full-pane instead of splitting with the useless placeholder.

### ~~Extract shared `WrapText` helper~~ ✅

`OfficeMetadataProvider.WrapText` and `NuGetMetadataProvider.WrapText` are identical private static methods. Extract to a shared utility.

### ~~Deduplicate action dispatch~~ ✅

Extracted `DispatchFileAction` method shared by both the main key handler and action palette dispatch. Removed ~350 lines of duplicated logic.

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

### Git integration

Show git status in the file browser and eventually support git actions.

#### ~~Phase 2a: Stage / Unstage~~ ✅

Implemented stage/unstage via action palette entries ("Git: Stage", "Git: Unstage", "Git: Stage all changes"). Async via `GitActionRunner`, multi-select supported, status auto-refreshes after each action.

#### ~~Unstage all~~ ✅

Added "Git: Unstage all" action palette entry via `git reset HEAD`. Mirrors "Git: Stage all changes" pattern.

#### ~~Phase 2b: Commit~~ ✅

Implemented "Git: Commit" action palette entry. Uses existing TextInput dialog for commit message input. Only shown when staged changes exist. Async via GitActionRunner, status auto-refreshes after commit.

#### ~~Phase 2c: Push / Pull / Fetch~~ ✅

Implemented "Git: Push", "Git: Push (force with lease)", "Git: Pull", "Git: Pull (rebase)", and "Git: Fetch" action palette entries. 30s timeout for network operations. Ahead/behind commit counts shown in status bar next to branch name.

#### Completed

- Phase 1: Readonly status display ✅
- Diff preview for modified files ✅
- Git action menu (`Ctrl+G`) ✅
- Phase 2a: Stage / Unstage ✅
- Phase 2b: Commit ✅
- Phase 2c: Push / Pull / Fetch ✅

### Drive type detection

Detect whether a drive is SSD, HDD, or network. Some features (like directory size in the file browser) should behave differently based on drive speed.

- On Windows, use WMI or `DeviceIoControl` / `IOCTL_STORAGE_QUERY_PROPERTY` to distinguish SSD vs HDD; `DriveInfo.DriveType == Network` for network drives
- `DriveInfo` is already used in `DirectoryContents.GetDriveEntries()` and `PropertiesOverlay`
- If feasible, add per-drive-type settings for showing directory size inline in the file list:
  - Show directory size for SSD (default true)
  - Show directory size for HDD (default false)
  - Show directory size for network drives (default false)
- Must be NativeAOT-compatible (no reflection-heavy WMI wrappers)

### ~~Keybinding and naming cleanup~~ ✅

- Removed `'p'` paste shortcut (now `'v'` only) in `InputReader.cs`, action palette, help overlay, `--help`, and README
- Renamed `/` from "Search / filter" to "Filter" everywhere
- Renamed `Ctrl+F` from "Find file" to "Search / Find file" everywhere

### ~~File finder: ignore .git folders~~ ✅

Switched `ScanFilesForFinder` to manual recursive directory walk. `.git` directories are always skipped (even with hidden files enabled). Dot-prefixed and hidden/system directories are also skipped when their respective settings are off.

### ~~Show git status in properties dialog~~ ✅

Added "Git status" row to properties overlay. Shows comma-separated flag labels (Modified, Staged, Untracked, Conflict) with matching colors (yellow, cyan, green, red). Shows em-dash for clean/untracked files.

### ~~Preview provider system~~ ✅

Refactored into a dual-interface architecture: `IPreviewProvider` for visual previews (text/image) and `IMetadataProvider` for structured metadata extraction. Metadata providers return `MetadataResult` with `MetadataSection[]`/`MetadataEntry[]`; `MetadataRenderer` converts to styled lines. Metadata displays as a header above any applicable preview. `PreviewLoader` loads metadata first, then preview. `PropertiesOverlay` also shows extracted metadata.

#### Future: Format-specific metadata providers

Add metadata providers for specific formats. Each is an `IMetadataProvider` implementation registered in `MetadataProviderRegistry`. They display structured metadata as a header above any applicable preview providers. Consider delegating to CLI tools where pure .NET parsing would be too complex.

##### Completed

- NuGet metadata (`.nupkg`, `.snupkg`) — extracts from embedded `.nuspec` XML ✅
- Executable metadata (`.exe`, `.dll`) — PE headers, .NET assembly info, Win32 version info ✅
- MS Office documents (`.docx`, `.xlsx`, `.pptx`) — title, author, created/modified dates, page/sheet/slide count ✅
- Media files (`.mp3`, `.mp4`, `.flac`, `.mkv`, `.avi`, `.wav`, `.ogg`, etc.) — duration, codec, bitrate, resolution, sample rate ✅

##### Backlog

- ~~**PDF metadata** — title, author, page count, producer, creation date. Shells out to `pdfinfo` CLI (Poppler), following the `MediaMetadataProvider` pattern.~~ ✅
- **Font files** (`.ttf`, `.otf`, `.woff2`) — font family, style, weight, glyph count. Parse OpenType/TrueType `name` and `head` tables.
- **OpenDocument** (`.odt`, `.ods`, `.odp`) — title, author, dates, page/sheet count. Extract from `meta.xml` inside the ODF zip archive (similar to Office OOXML approach).
- **EPUB** (`.epub`) — title, author, publisher, language, identifier. Extract from `content.opf` metadata inside the zip archive.
- ~~**PE timestamp indicator** — when the PE timestamp is zeroed (reproducible builds) or out of range, show "Reproducible build" or similar instead of silently omitting the field.~~ ✅

### Zip — other archive formats

Support additional archive formats in the preview pane (`.tar`, `.gz`, `.tar.gz`). Zip preview is already implemented via `System.IO.Compression.ZipFile`.

### Git actions in action palette

Git actions are integrated into the global action palette (`Ctrl+P`), prefixed with "Git:" and shown only when actionable. No dedicated git menu or keybinding.

- Diff preview toggle ✅
- Stage / Unstage (Phase 2a) ✅
- Commit with message (Phase 2b) ✅
- Push / Push force-with-lease / Pull / Pull rebase / Fetch (Phase 2c) ✅

#### Completed

- Moved git actions from dedicated `Ctrl+G` menu into global action palette ✅
- Diff preview toggle as "Git: Toggle diff preview" (only shown for modified/staged files) ✅
- Stage / Unstage via "Git: Stage", "Git: Unstage", "Git: Stage all changes" ✅
- Commit via "Git: Commit" with TextInput message dialog ✅
- Push / Pull / Fetch via "Git: Push", "Git: Push (force with lease)", "Git: Pull", "Git: Pull (rebase)", "Git: Fetch" ✅
- Ahead/behind commit counts displayed in status bar next to branch name ✅
