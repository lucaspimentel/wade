# Changelog

## [1.8.0] - 2026-03-28

### Added
- Add syntax highlighting and "URL Shortcut" file type label for `.url` files (uses existing INI/TOML highlighter)
- Add directories to file finder (Ctrl+F) results with file-type icons
- Add Wade.Search library with subsequence matching and boundary-aware scoring for file path search

### Changed
- Change go-to-path keybinding from `g` to `Ctrl+G` for consistency with other dialog shortcuts
- Use breadth-first search in file finder so current-directory entries appear before deeper nested ones
- Stream file finder results incrementally as directories are scanned instead of waiting for the full scan to complete

## [1.7.0] - 2026-03-25

### Added
- Add built-in Markdig markdown preview — renders headings, code blocks with syntax highlighting, lists, blockquotes, tables, links, and more without external tools

### Removed
- Remove Glow markdown preview provider — built-in Markdig renderer is now the only markdown preview

### Changed
- Replace `disabled_tools` list with individual boolean config settings (`pdf_preview_enabled`, `pdf_metadata_enabled`, `markdown_preview_enabled`, `ffprobe_enabled`, `mediainfo_enabled`)

### Fixed
- Fix file list flickering in git repos — ignore `.git` directory changes in filesystem watcher to break feedback loop with git status refresh

## [1.6.0] - 2026-03-24

### Added
- Add right-click context menu — position-anchored floating menu with contextual file actions (open, rename, delete, copy, cut, paste, copy path, properties, git stage/unstage)

## [1.5.0] - 2026-03-22

### Added
- Add inline directory sizes in file list — computed asynchronously with progressive display, gated per drive type (SSD on by default, HDD/Network off)
- Add drive type detection — distinguish SSD from HDD on Windows (DeviceIoControl seek penalty query) and Linux (/sys/block rotational), with Network and Removable detection via DriveInfo
- Add SHA256 checksum verification to remote installer

### Changed
- Bump and pin GitHub Actions to latest versions

### Fixed
- Fix `SHFILEOPSTRUCT.fAnyOperationsAborted` struct layout — use `[MarshalAs(UnmanagedType.Bool)]` for correct Win32 BOOL (4-byte) marshaling
- Fix memory leak in clipboard SetClipboardData failure path — free hEffect when system does not take ownership
- Fix MsiSummaryInfoGetProperty P/Invoke — replace `out long` with proper FILETIME struct
- Remove unused EnableVirtualTerminalInput constant

## [1.4.0] - 2026-03-20

### Added
- Add scrollable properties overlay — arrow keys, Page Up/Down, Home/End for vertical scrolling when content exceeds screen height
- Add filesystem auto-refresh via `FileSystemWatcher` — file list updates automatically when files are created, deleted, or renamed externally
- Add `--version` CLI flag
- Add MSI metadata provider showing ProductName, Version, Manufacturer, ProductCode, UpgradeCode, and summary info via Windows Installer database API (Windows only)
- Add MSI preview provider showing installer file table listing with sizes (Windows only)
- Add wrap-around navigation in file list and settings dialog — pressing Up at the top jumps to the bottom, and Down at the bottom jumps to the top

### Changed
- Remove redundant size and modified date from `FileMetadataProvider` — already shown in detail columns and properties overlay
- Hide `FileMetadataProvider` sections from properties overlay — information is already shown in the overlay's own fields

### Fixed
- Fix in-flight preview subprocesses (glow, ffprobe, pdftopng, pdfinfo, git diff) not being killed on cancellation — processes are now terminated immediately when navigating away, eliminating preview lag during rapid scrolling
- Fix PDF preview being disabled when "Show Image Previews" is toggled off — PDF preview now only requires Sixel terminal support

## [1.3.0] - 2026-03-18

### Added
- Separate `IMetadataProvider` system for structured file metadata (NuGet, Executable, Office, Media, PDF)
- Metadata displayed as styled header above preview pane and in properties overlay
- Add `FileMetadataProvider` showing filename, size, modified date, and git status for all files and directories
- Add PDF metadata provider showing document properties (title, author, page count) via pdfinfo
- Add image metadata provider showing resolution, format, color depth, frame count, and EXIF data (camera, exposure, GPS)
- Add 'Download cloud file' action for OneDrive/Dropbox placeholder files (Windows)
- Add 'p' keybinding to open 'Change preview' submenu directly
- Add F5 as alias for Ctrl+R to refresh the current directory
- Add action palette submenu support for grouped actions (used by 'Change preview')
- Extend zip archive preview to .docx, .xlsx, .pptx, .odt, .ods, .odp, .apk, .vsix, .whl, .epub, and other zip-based formats
- Show 'Reproducible build' label for executables with zeroed PE timestamps
- Add cloud file metadata display (filename, size, modified date, "Cloud: not downloaded")
- Add archive metadata provider showing file count, total/compressed size, and compression ratio for all zip-based archives
- Add strikethrough (SGR 9) rendering support
- Add `file_metadata_enabled` config toggle for file metadata display in the right pane
- Add `file_previews_enabled` config toggle for file preview display in the right pane
- Add `archive_metadata_enabled` config toggle for archive metadata (file count, sizes, compression ratio)

### Changed
- Add "None" preview option available for all files; binary files now default to "None" instead of hex dump
- Hex dump preview is always available but must be explicitly selected from the "Change preview" menu
- "Change preview" menu hides Text for binary files and Hex for text files
- Migrated `ExecutablePreviewProvider`, `OfficePreviewProvider`, `MediaPreviewProvider`, `NuGetPreviewProvider` to metadata providers (`IMetadataProvider`)
- `PreviewLoader` supports combined metadata + preview loading via new `BeginLoad` overload
- `PropertiesOverlay` renders extracted metadata sections below file system properties
- Support combining sections from multiple metadata providers per file
- Rename 'Source' preview label to 'Text'
- Hide unavailable preview providers from 'Change preview' menu
- Default secondary archive formats (.docx, .nupkg, etc.) to "None" preview instead of "Archive Contents"
- Move archive summary (total size, compressed size, ratio, file count) from preview pane to metadata provider
- Simplify help dialog to reference action palette instead of listing all hotkeys
- Draw horizontal separator line between metadata header and preview content
- Refactor config dialog to data-driven model (`ConfigItem`/`ConfigDialogState` in `Wade.UI`) — eliminates hardcoded index math
- Reorganize config dialog: rename "Show Preview Pane" to "Show Right Pane", group file details and file previews as nested sub-items with independent enable/disable
- Replace per-tool config booleans (`glow_markdown_preview_enabled`, `pdf_preview_enabled`) with unified `disabled_tools` list

### Fixed
- Fix PDF preview image overlapping metadata in preview pane
- Fix stale preview content when switching files
- Fix stale content when scrolling in preview pane
- Fix syntax highlighting for dotfiles (e.g. .bashrc, .zshrc)
- Fix UTF-16 files incorrectly detected as binary

## [1.2.0] - 2026-03-15

### Added
- Git stage/unstage actions to action palette
- Git commit via action palette
- Git push/pull/fetch and ahead/behind display in status bar
- Git status in properties overlay
- Ctrl+K as action palette alias

### Fixed
- Ctrl+R refresh not reloading git status
- Skip .git and hidden dirs in file finder

### Changed
- Clean up keybindings and action names
- Trim `--help` keybindings to just `?` reference

## [1.1.0] - 2026-03-15

### Added
- Symlink awareness: visual distinction, properties overlay, create symlink (Ctrl+L), `CopySymlinksAsLinks` config
- Async directory size calculation in properties overlay
- Show System Files setting (Windows only)
- Zip file preview with entry listing
- Hex preview for binary files with per-character styling
- Ellipsis indicator for truncated filenames and symlink targets
- Terminal title set to current directory
- File finder dialog (Ctrl+F) for recursive file search
- PDF preview via convert-to-image pipeline
- Git integration: status display, diff preview toggle, branch name in status bar
- Cloud file placeholder detection (Windows)
- Glow markdown preview with underline support
- Syntax highlighting for gitignore files

### Fixed
- Windows symlink deletion bug
- Config dialog toggle offset bug
- Crash in `FindRepoRoot` at drive root
- Notification cropping in status bar

### Changed
- Move git actions into global action palette (removed Ctrl+G menu)
- Convert hex preview from config toggle to action palette toggle
- Use file type labels consistently for all files
- Use green for successful action notifications
- Deduplicate action dispatch in App.cs

## [1.0.0] - 2026-03-10

### Added
- Action palette (Ctrl+P)
- Copy path to clipboard (y/Y)
- Windows OS file clipboard interop
- Bookmarks feature
- Independent size and date column toggles
- File path shown in expanded preview status bar

### Changed
- Remap new file/dir and copy/cut keys

## [1.0.0-beta.4] - 2026-03-09

### Fixed
- Buffer overflow crash in `FormatSize`

## [1.0.0-beta.3] - 2026-03-09

### Added
- Two-column help dialog layout
- Three new config options: `confirm_delete`, `preview_enabled`, `detail_columns`
- File properties overlay (`i` key)

### Fixed
- Invalidate filtered entries after file actions
- CLI path argument handling
- Modifier-only keys no longer close dialog boxes

### Changed
- Capitalize drive letters on Windows
- Never show system+hidden filesystem entries on Windows
- Rename `preview_enabled` to `preview_pane_enabled`; gray out Image Previews when preview pane is off

## [1.0.0-beta.2] - 2026-03-08

### Added
- Open terminal here and cd-on-exit shell wrappers
- Quit without cd (Q) feature

## [1.0.0-beta.1] - 2026-03-08

### Added
- TUI file browser with three-pane layout (parent, current, preview)
- NativeAOT-ready build with optimized publish settings
- Three-tier config system (JSON) with `--show-config` output
- File-type icons using Nerd Fonts v3 codepoints
- Syntax highlighting in preview pane
- Sixel image preview with capability detection and cell pixel size query
- Raw input pipeline (Windows Win32 + Unix raw mode)
- Async preview loading
- Mouse support
- Search/filter feature
- Date and size columns in center pane
- Expanded preview mode for full-width file viewing
- Unix/WSL keyboard input with raw mode and SS3 arrow keys
- Show/hide hidden files toggle
- Status bar notifications
- Multi-select model
- Configurable sort order
- Go-to-path bar with autocomplete
- File actions: copy, cut, paste, delete (Recycle Bin on Windows), create file/directory
- Overwrite confirmation on paste
- In-app config dialog
- Help overlay
- `--help` / `-h` CLI switch

### Fixed
- Text wrapping on wide characters (emoji, CJK, fullwidth)
- Stale Sixel pixels when switching between images
- StartPath trailing separator stripping on Linux
