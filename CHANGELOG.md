# Changelog

## [Unreleased]

### Added
- Separate `IMetadataProvider` system for structured file metadata (NuGet, Executable, Office, Media)
- Metadata displayed as styled header above preview pane and in properties overlay
- `MetadataRenderer` for consistent colored label/value metadata rendering
- `MetadataProviderRegistry` for file-type-based metadata provider matching

### Changed
- Add "None" preview option available for all files; binary files now default to "None" instead of hex dump
- Hex dump preview is always available but must be explicitly selected from the "Change preview" menu
- "Change preview" menu hides Text for binary files and Hex for text files
- Migrated `ExecutablePreviewProvider`, `OfficePreviewProvider`, `MediaPreviewProvider`, `NuGetPreviewProvider` to metadata providers (`IMetadataProvider`)
- `PreviewLoader` supports combined metadata + preview loading via new `BeginLoad` overload
- `PropertiesOverlay` renders extracted metadata sections below file system properties

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
