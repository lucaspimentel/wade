# Changelog

## [Unreleased]

### Added
- Image previews inside tmux (3.3+) with `allow-passthrough` on: kitty, iTerm2 and Sixel images reach the outer terminal, chosen by what tmux reports about it; Sixel that tmux shows itself works without passthrough
- Linux: Delete moves files and folders to the Trash (freedesktop.org spec: `~/.local/share/Trash`, or the drive's own `.Trash-$uid` on other mounts) instead of deleting them permanently; Shift+Delete still deletes permanently
- macOS: files and folders Finder hides (`chflags hidden`, e.g. `~/Library`) are hidden unless "Show Hidden Files" is on, in the listing, the file finder and path completion; Properties lists them as Hidden

### Changed
- Keybindings: `d`/`D` delete (to the Trash) and delete permanently, `r` renames, `T` opens a terminal, Ctrl+T opens a tab like `t`, and Ctrl+L refreshes like Ctrl+R/F5 (Create symlink moved to the action palette)
- Esc in the file list no longer quits: it clears the marks, then the filter, then shows "Press q to quit"; `q` always quits
- Shift, Ctrl or Alt with a letter no longer triggers that letter's action (Shift+J no longer moves down; Shift+Q no longer closes the full-screen preview)
- The preview pane shows `[file previews off]` when file previews are turned off (below the file details, or alone when details are off too, where the pane was blank)
- Read errors, empty files and binary files forced into the text preview show their message below the file details instead of only the details

### Fixed
- Keys typed or pasted faster than the screen redraws are no longer dropped: every queued key, mouse, paste and resize event is handled in order (only the last one of each batch was)
- macOS: wade no longer hangs at startup or ignores input; it waits on `/dev/tty` with `select()` because macOS `poll()` does not support it
- Linux/macOS: Alt+letter is read as one Alt key (it acted as Esc followed by the letter, so Alt+j moved down and cleared the marks)
- Linux/macOS: keys and characters cut by a terminal read (64 bytes) are no longer lost: long typed text, fast arrow-key repeats and the end of a bracketed paste
- Linux/macOS: emoji and other characters beyond U+FFFF typed into the filter, rename and other text boxes arrive intact
- Text boxes draw wide characters (CJK, emoji) two columns wide instead of overwriting their second half
- Linux/macOS: SIGTERM and SIGHUP (closing the terminal, `kill`) restore the terminal before wade exits, instead of leaving it in raw mode on the alternate screen; the exit status is 128 + the signal and no cwd file is written
- Linux: case-insensitive folders (FAT, exFAT, SMB, WSL's `/mnt/c`, ext4 casefold) are detected: a case-only rename works instead of failing as "cannot rename to an existing path", and names match whatever case was typed
- Windows: folders with per-directory case sensitivity (created by WSL, or `fsutil file setCaseSensitiveInfo`) are treated as case-sensitive: renaming `a.txt` to `A.txt` when both exist is refused instead of replacing the other file, and names are matched exactly
- Windows: pasting several lines no longer presses Enter between them (in the file list that opened the selected item); line breaks and tabs inside a paste stay part of it

### Removed
- Remove the C# implementation (`src/Wade`, `src/Wade.Search`); the Rust port in `src/wade-rs` is the only build, and the `install-local-csharp` scripts are gone

## [2.0.0] - 2026-10-11

### Added
- Add tabs — `t` opens a tab at the current directory (up to 9), `{`/`}` and `1`-`9` switch, `w` closes; each tab keeps its own path, selection, scroll, marks and filter (#17)
- Add per-directory sort — `s`/`S` change only the current directory's sort, which is remembered (`~/.config/wade/sorts`) and marked with `*` in the status bar; "Reset sort for this directory" in the action palette forgets it
- Add fzf-style query syntax to the file finder — space-separated terms are combined, `^foo` matches the start of a path segment, `foo$` the end of the path, `!foo` excludes; smart case now applies to fuzzy terms too
- Add query memory to the file finder — `Ctrl+F` reopens with the last query for the rest of the session
- Add `Ctrl+C` to clear the input in the file finder, the `/` filter, the action palette and the bookmarks list
- Add kitty graphics and iTerm2 inline image previews (kitty, Ghostty, iTerm2, WezTerm), chosen automatically ahead of Sixel; the `image_protocol` config key (`auto`, `kitty`, `iterm`, `sixel`) forces one
- Add a table preview for CSV and TSV files (`.csv`, `.tsv`, `.tab`) — aligned columns with a bold header, right-aligned numbers and row numbers; the separator is detected (`,`, `;`, `|`), and file details show the row and column counts. Turn it off with "Show CSV/TSV Tables" in settings (`csv_preview_enabled`)
- Add longer full-screen previews — up to `preview_max_lines` lines (default 10,000) and `preview_max_bytes` bytes (default 4 MiB) of text, gzip content or archive entries, ending with a "preview limited to …" line when cut off
- Add the full-screen preview with the right pane hidden — `Enter` on a file now opens it
- Add system clipboard file copy/paste on Linux (`wl-copy` or `xclip`) and macOS, so files copied in wade paste into the file manager and the other way around
- Add MSI installer preview and metadata on Linux and macOS (previously Windows only)
- Add `rgb()`, `rgba()`, `hsl()`, `hsla()` and named-color swatches in CSS/SCSS previews
- Add TOML highlighting for `Cargo.lock`, `poetry.lock`, `uv.lock` and `pdm.lock`
- Add the item count and current file name to the file-operation progress overlay
- Add macOS support (build from source with `install-local.sh`) — Trash on delete, "Open terminal here" in Terminal or iTerm, the volumes under `/Volumes` as the drive list, SSD/HDD and network drive detection, and case-insensitive volume handling
- Add `install-local.sh`, `install-local-csharp.sh` and `install-remote.sh`, POSIX shell versions of the PowerShell install scripts (no PowerShell needed on Linux or macOS); `install-remote.sh` keeps an existing install if the download fails

### Changed
- Release binaries and `install-local.ps1` now use the Rust port (`src/wade-rs`): a static Linux build (musl) and a Windows build with no Visual C++ runtime dependency; `install-local-csharp.ps1` installs the C# version until it is removed
- The right pane now reads only as many lines or archive entries as it can show
- Markdown previews are parsed with pulldown-cmark instead of Markdig; output is the same except for rare edge cases
- "Open terminal here" on Linux now opens a terminal emulator (`$TERMINAL`, `x-terminal-emulator`, then common emulators) instead of starting a shell inside wade's own terminal; "No terminal emulator found (set $TERMINAL)" is shown when none is installed
- The help overlay lists Settings (`,`) and Help (`?`); the file finder is labeled "Find files (Search)" and the settings entry "Settings (Configuration)" in the action palette

### Fixed
- Fix copy-pasting an item into the folder it came from deleting it when Overwrite was confirmed; it now makes a copy (`a - Copy.txt`), and a cut pasted there does nothing
- Fix pasting a directory into itself or its own subfolder copying until the path became too long; it is now refused
- Fix deleting or overwriting read-only files on Windows
- Fix renaming a file to change only its case on Windows (reported as "already exists")
- Fix new file, new directory, symlink and rename accepting Windows device names (`CON`, `NUL`, `COM1`, ...) and names ending in a dot or space; rename also checks for invalid characters
- Fix the terminal tab title not being restored on exit, or when `terminal_title_enabled` is turned off
- Fix git unstage failing in a repository with no commits
- Fix stage, unstage and commit sometimes failing with "Unable to create index.lock" while the background git status ran
- Fix files with non-ASCII names showing no git status
- Fix EFS-encrypted files shown as cloud placeholders, and some OneDrive placeholders not detected
- Fix "Download cloud file" not downloading online-only OneDrive files
- Fix the file finder crashing on terminals narrower than 78 columns
- Fix file finder results sometimes staying hidden until the next batch of results arrived
- Fix emoji and other characters outside the Basic Multilingual Plane drawn as broken halves in file finder rows
- Fix non-ASCII text pasted on Linux and macOS arriving garbled
- Fix files that another program has open for writing (such as logs) previewed as binary
- Fix the preview staying on "[loading…]" when returning to a file before another file's preview finished
- Fix the preview staying on "[loading…]" for Office/NuGet files with malformed XML and for tar archives with GNU sparse entries
- Fix broken symlinks leaving the right pane blank; it now shows "[broken symlink]"
- Fix the directory size of a deleted directory staying on "Calculating…", and properties of a deleted entry showing made-up dates and attributes
- Fix a full refresh being skipped when the watcher overflowed and another change followed within 300 ms
- Fix PDF previews leaving temporary directories behind
- Fix CSS swatches drawn for colors in selectors (such as `#id`) instead of only in property values
- Fix files whose names differ only in case (such as `a.txt` and `A.txt`) being confused on Linux when wade re-selected an entry
- Fix duplicate bookmarks kept on load, and bookmarks with non-ASCII paths that differ only in case not matching on Windows

## [1.14.0] - 2026-05-08

### Added
- Add YAML frontmatter rendering in the markdown preview — key/value pairs are displayed in a tabular block with aligned columns, per-type syntax coloring (quoted strings, booleans, numbers), and word-wrapped values
- Add fzf-style exact-substring matching in the file finder — prefix the query with `'` (e.g. `'foo`) to match a literal contiguous substring instead of fuzzy subsequence; smart-case applies (case-insensitive unless the query has an uppercase letter)

### Changed
- Update file finder scoring to reduce allocations — score-only paths are now zero-alloc; `WithFileNamePriority` methods allocate one array per match instead of two

### Fixed
- Fix accidental download of cloud-synced files (OneDrive, Dropbox) when previewing or selecting placeholders; encoding detection no longer opens the file, so cloud files are only fetched via the explicit "Download cloud file" action

## [1.13.0] - 2026-04-11

### Added
- Add `[` and `]` keybindings to toggle the left (parent) and right (preview) panes (session-only, not saved to config)
- Add preview and metadata support for tar and gzip archives (`.tar`, `.tar.gz`, `.tgz`, `.gz`); single-member gzip also shows the decompressed text
- Add syntax highlighting in single-member `.gz` previews — language detected from the inner filename (e.g. `script.py.gz` gets Python highlighting)
- Add inline color swatches in CSS/SCSS/Sass previews — a colored `██` block is shown next to each hex color literal

## [1.12.0] - 2026-04-08

### Added
- Add busy indicator for file operations — copy, move, and delete run in the background with a modal overlay and Esc to cancel
- Add Windows shortcut (`.lnk`) metadata — shows target path, working directory, arguments, description, icon, and hotkey in properties and preview pane
- Add paste support in text input fields (file finder, filter, go-to-path, rename)
- Add word-navigation shortcuts in text input fields — `Ctrl+Left`/`Ctrl+Right` to skip words, `Ctrl+Backspace` to delete previous word
- Add app execution alias detection — MSIX/UWP app aliases shown with resolved target path and dedicated icon (Windows)
- Add junction point detection — junction points distinguished from symlinks with dedicated icon and "Junction" label (Windows)
- Add C/C++ syntax highlighting (`.c`, `.h`, `.cpp`, `.hpp`, `.ino`, and more)
- Add Dockerfile syntax highlighting (`Dockerfile`, `Containerfile`, `.dockerfile`)
- Add XML syntax highlighting for `.xsd` files

### Changed
- Use descriptive file type labels for SVG, ICO, Paint.NET, CSV, TSV, SQL, JSX, TSX, and `.env` files

### Fixed
- Fix selection scrolling past the visible area when column headers are enabled
- Fix file finder skipping hidden directories (e.g. `AppData`) when "Show Hidden Files" is enabled
- Fix file finder ranking — files in the current directory now rank above deeper matches with the same name

## [1.11.0] - 2026-04-01

### Added
- Add CLI path validation — file paths open the parent directory with the file selected; non-existent paths print an error and exit with code 1

### Changed
- Update and pin all NuGet package references to latest exact versions
- Scale file finder dialog width to 75% of terminal width (was capped at 70 columns)
- Use JSON syntax highlighting for `.slnf` (Visual Studio Solution Filter) files

### Fixed
- Prevent expanded preview from opening when the active preview is "None"
- Fix crash on lone surrogate characters in preview rendering
- Normalize `/` and `\` in file finder search so either works as a path separator on Windows
- Add trailing gap penalty to fuzzy scorer so tighter path matches rank higher (e.g. `src/Foo` ranks above `src/Foo.Bar.Baz`)

## [1.10.0] - 2026-03-30

### Added
- Add column headers to all three panes — shows column labels (Name, Size, Date, or drive-specific: Label, Format, Free, Size, % Full) that adapt with responsive column tiers; togglable via `column_headers_enabled` config (default: on)
- Add drive list view with drive-specific columns: volume label, file system type, free space, total size, and a visual percent-full bar with centered percent text — replaces the normal date/size columns when viewing drives; columns hide responsively (label first, bar last)
- Add TB (terabyte) tier to `FormatSize` for drives and files larger than 1 TB

### Changed
- Enhance drive properties overlay — show free/total space with usage percentage, volume label, and file system type alongside drive media type

### Fixed
- Fix encoding and line endings not shown in status bar for non-text previews (e.g. rendered markdown) — detection moved from preview providers to metadata loading
- Fix BOM-less UTF-16 files incorrectly detected as binary — heuristic detects alternating null-byte pattern when no BOM is present
- Fix terminal title not cleared on exit on Linux — reorder cleanup to write after leaving alternate screen
- Fix go-to-path (Ctrl+G) failing for "/" on Linux — `TrimEnd` stripped the root path to an empty string
- Fix root mount "/" showing empty name in Linux drive list — same `TrimEnd` issue

## [1.9.0] - 2026-03-30

### Added
- Add `parent_pane_enabled` config to hide the left (parent directory) pane — supports 3-pane, 2-pane, or single-pane layouts

### Changed
- Redesign file finder dialog — show full relative paths in a single column with matched characters highlighted in green, and a live result count (`matching/total`)
- Move "Show File Previews" config group out from under "Show Right Pane" — previews work in expanded preview mode regardless of the right pane setting

### Fixed
- Fix expanded preview (Right/Enter on a file) showing empty screen when right pane is hidden
- Fix Sixel image previews not rendering in expanded preview when right pane is hidden
- Hide empty metadata divider line when file details section has only a filename and no entries

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
