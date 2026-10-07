# TODO

## Rust port (src/wade-rs)

The C# TUI is being ported to Rust in `src/wade-rs`, following
`docs/rust-port-plan.md` (progress tracked there). The C# tree is
feature-frozen during the port; the items below the port checklist are the
backlog.

- [x] Phases 0-9: every C# feature ported; `src/wade-rs/KNOWN_DEVIATIONS.md`
  lists only accepted differences
- [x] Phase 10a: golden sweep (all goldens regenerated from C# with no diff;
  renderer scenarios 041-045 for link/junction/app-alias/cloud/marked rows,
  inline directory sizes and pane borders; `--version` matches C#)
- [ ] Phase 10b: switch `install-local.ps1` and `release.yml`/`install-remote.ps1`
  to the Rust binary (meanwhile `install-local-rust.ps1` installs the Rust
  build for the trial)
- [x] Run the test suites natively on Windows (the port was developed on
  Linux with Wine): `cargo test` and
  `dotnet test Wade.slnx` pass, including the three Wine-only symlink/git
  tests; `renderer_frames` (incl. config-dialog
  goldens 015, 016) passes
- [x] Native Windows verification of the library code (no TUI): junction, app
  alias and OneDrive detection, SSD detection, exe/msi/docx/nupkg/mp4
  metadata, clipboard round trip with PowerShell/WinForms (copy and cut),
  Recycle Bin delete, ShellExecuteEx, the file watcher, symlinks, junctions,
  system/hidden filtering on `C:\`, git status with CRLF, long and UNC
  paths, `install-local-rust.ps1` (built and installed under a temporary
  home, so your real `~/.local/bin` was not touched), config parity (`--show-config` on 8 config
  files), and a C# vs Rust diff of listings and sort orders over about 3,000
  real directories, previews and metadata for 470 real and generated files,
  the file finder over three large trees (identical entries and ranking for
  42 queries), and git status over 35 repos. Fixed: directory-symlink delete, relative symlinks shown
  as broken, copying directory links with links not preserved, sort by
  modified time ignoring seconds, archive preview of tiny or empty zip files
  (now "[invalid archive]" like .NET 10), git status for file names with
  non-ASCII characters (both versions), and the cloud download and
  `RECALL_ON_OPEN` bugs
- [x] Bug: sorting stuck on Name/ascending in the Rust port regardless of
  config or `s`/`S` keys (found during the trial). Fixed: `get_entries` now
  sorts with `sort_mode`/`sort_ascending` like C# `LoadEntries`, and cycling
  with `s` wraps from Extension back to Name (it stuck on Extension)
- [x] Bug: size column not populated (found during the trial). Fixed: the
  center pane now receives the inline directory sizes (`App::render` passed
  `None`), and finishing the size scan no longer empties the App's copy
  (C# shares one dictionary between `_inlineDirSizes` and `DirSizes`)
- [x] Test coverage review: every `AppConfig` setting is tested for its
  effect at startup and from the config dialog (`app/settings_tests.rs`);
  keys, mouse, paste and dialogs are driven through `App::handle_event`
  (`app/key_tests.rs`, including git stage/unstage/commit in a temp repo);
  `tests/cli.rs` runs the binary for `--version`/`--help`/`--show-config`
  and the missing-path error; `config_io` ports `WadeConfigTests`; and the
  TextInput, FormatHelpers, ContextMenu, FileIcons, CliTool(Hints),
  SearchFilter and ModalInput C# tests are ported. Fixed along the way:
  center-pane headers ignoring the status column, the center pane drawn
  with a different scroll than mouse clicks used, no full redraw after `[`,
  the start path not normalized (`~`, trailing separators), and background
  `git status` holding `index.lock` so a stage/commit could fail (both
  versions; now `--no-optional-locks`)
- [x] Ported the remaining DirectoryContents, FileActions, GitUtils,
  PathCompletion, ConfigDialogState and BookmarkStore C# tests (FilePreview
  was already covered by unit tests and the preview goldens). Symlink and
  real-git tests now skip where links or git are unavailable, so the Wine
  run no longer has known failures. The git network actions (push, pull,
  pull --rebase, force-with-lease, fetch, ahead/behind) are tested against
  a local bare remote, including palette push clearing the status bar's
  ahead count
- [ ] Still untested: terminal setup and raw input on both OSes, the real
  launchers (open with default app, open terminal, cloud download), the
  combined text+image preview event and macOS paths
- [ ] Manual Windows Terminal checks (need a real console; none are
  automated):
  - the `wd` wrapper (`--cwd-file`)
  - keyboard, mouse, window resize and bracketed paste
  - Sixel image previews and terminal capability detection
  - "Download cloud file" from the action palette on a OneDrive placeholder
  - paste into Explorer after copy/cut in wade, and copy/cut from Explorer
  - SSD inline directory sizes and the drive list on screen
  - filesystem auto-refresh on screen
- [ ] Windows follow-ups from the verification pass:
  - run `install-local-rust.ps1` for real (it overwrites `~/.local/bin/wade.exe`)
  - opening a file with an unknown extension reports success while Windows
    shows the "Open with" dialog; check whether C# behaves the same
- [ ] More native Windows checks that need no TUI (none run yet):
  - Windows file names: case-only rename, reserved names (`CON`, `NUL`),
    trailing dots and spaces, invalid characters
  - locked or in-use files, read-only attribute and access denied: delete,
    rename and read
  - paste conflicts: overwrite a read-only file, paste into itself, directory
    over a file
  - hardlinks and alternate data streams (listing and sizes)
  - C# vs Rust diff of inline and full directory sizes, the drive list and the
    properties overlay on real data
  - PDF and Sixel previews end to end (`pdftopng`, `pdfinfo`, `ffprobe` and
    `mediainfo` are installed on this machine)
- [ ] Optional: a ConPTY smoke harness (launch wade, send keys, read the
  screen) to automate the checks above
- [ ] macOS is untested (CI covers only Linux and Windows): clipboard via
  `osascript`, open via `open`, `notify` file watcher, drive detection
- [ ] Delete the merged remote branches (`phase-9a` to `phase-9g`, `phase-10a`,
  `install-local-rust`, `todo-*`, `palette-settings-label`, and older
  phase branches); the session proxy could not delete them
- [ ] Two weeks of daily use on the Rust binary
- [ ] Cutover: delete `src/Wade*`, remove dual-build CI, update
  README/CLAUDE.md/CHANGELOG

## Features

### ~~File action progress indicator~~ (Done)

Progress dialog with file count, progress bar, current filename, and Esc to cancel. Copy, move, and delete operations run in background via `FileOperationRunner`.

### ~~System clipboard — Unix/macOS file interop~~ (Done in Rust)

Windows file clipboard interop is implemented in both versions. The Rust port also implements Linux (`x-special/gnome-copied-files` / `text/uri-list` via `wl-copy`/`xclip`) and macOS (NSPasteboard via `osascript`); C# remains Windows-only.

### Format-specific metadata providers

#### Backlog

- **Font files** (`.ttf`, `.otf`, `.woff2`) — font family, style, weight, glyph count. Parse OpenType/TrueType `name` and `head` tables.
- **OpenDocument** (`.odt`, `.ods`, `.odp`) — title, author, dates, page/sheet count. Extract from `meta.xml` inside the ODF zip archive (similar to Office OOXML approach).
- **EPUB** (`.epub`) — title, author, publisher, language, identifier. Extract from `content.opf` metadata inside the zip archive.
- ~~**Windows shortcuts** (`.lnk`)~~ (Done) — parser source copied from [lucaspimentel/windows-shortcut-parser](https://github.com/lucaspimentel/windows-shortcut-parser) into `src/Wade/LnkParser/`. `ShortcutMetadataProvider` extracts target path, working dir, arguments, description, icon, hotkey, and volume label, surfaced in the metadata header above the preview pane and in the properties overlay.

### ~~Zip — other archive formats~~ (Done)

- Tar/gzip preview implemented via `System.Formats.Tar` + `System.IO.Compression.GZipStream` in `src/Wade/FileSystem/TarPreview.cs` (NativeAOT-safe, no new packages). Covers `.tar`, `.tar.gz`, `.tgz`, and plain `.gz`.
- `TarContentsPreviewProvider` registered in `PreviewProviderRegistry` ahead of `TextPreviewProvider`.
- Plain `.gz` is probed for ustar magic at offset 257 of the decompressed stream; `.gz`-wrapping-tar is auto-detected as `tar.gz`. Single-member gzip shows original filename + ISIZE-based uncompressed hint + head of decompressed text when textual.
- `ArchiveMetadataProvider` extended to surface tar/gz metadata (Files, Total size, Format, Compressed, Ratio) through the existing `ArchiveMetadataEnabled` gate.

---

## Backlog

### ~~Multiple tabs ([#17](https://github.com/lucaspimentel/wade/issues/17))~~ (Done in Rust)

`t` new tab, `{`/`}` and `1`-`9` switch, `w` close (quits on the last tab), click to switch; the bar
shows with two or more tabs; each tab keeps its path, selection, marks and filter; not persisted.
Still open from the issue: a README comparison with yazi.

### ~~Ctrl+C clears search and filter boxes~~ (Done in Rust)

Ctrl+C empties the input of the Ctrl+F finder, the `/` filter, the Ctrl+P palette and the `b`
bookmarks list. Go to path and the dialog boxes are unchanged (Esc cancels them).

### ~~Syntax highlighting for `Cargo.lock`~~ (Done in Rust)

`Cargo.lock`, `poetry.lock`, `uv.lock` and `pdm.lock` map to the TOML highlighter by file name.

### ~~Kitty graphics protocol for image previews~~ (Done in Rust)

Uses Unicode placeholders (https://sw.kovidgoyal.net/kitty/graphics-protocol/#unicode-placeholders) as an
alternative to Sixel.

- **Detection:** a graphics query plus XTVERSION; kitty and Ghostty only.
- **Selection:** the `image_protocol = auto|kitty|sixel` config key.
- **Rendering:** placeholder cells in `ScreenBuffer`.
- **Untested:** not yet checked in a real kitty or Ghostty.

### Image previews under tmux

Images are off inside tmux: the startup queries reach tmux, not the outer terminal. Supporting it needs DCS
passthrough (`ESC P tmux; … ESC \`, with `allow-passthrough on`) for both the detection queries and the
Sixel/kitty output.

### ~~iTerm2 inline image protocol for image previews~~ (Done in Rust)

Uses `OSC 1337 ; File=` (https://iterm2.com/documentation-images.html).

- **Detection:** the XTVERSION name (iTerm2, WezTerm), falling back to `TERM_PROGRAM`.
- **Selection:** `image_protocol = iterm`; `auto` orders kitty, then iTerm2, then Sixel.
- **Payload:** the fitted image as PNG, sized in cells.
- **Untested:** not yet checked in a real iTerm2 or WezTerm.

### File finder — deduplicate ScoreWithFileNamePriority paths (Done in Rust)

Rust: `scorer::term_score` dispatches on the query mode and shares one filename-priority helper; the C# copy
below is unchanged.

`FuzzyScorer.ExactScoreWithFileNamePriority` is a near-verbatim copy of `ScoreWithFileNamePriority` (only the inner scoring call differs). Acceptable at two modes; fix when adding a third `QueryMode` (the `^`/`$`/`!` work below will force this). Extracting a shared helper is non-trivial because `ReadOnlySpan<char>` cannot cross delegate boundaries, so the cleanest fix is likely an enum-dispatched private helper, or `Score`/`ExactScore` becoming overloads of a common generic over a strategy struct.

### ~~File finder — avoid allocating MatchPositions when discarded~~ (Done)

- Extracted `ScoreCore` private method writing into a caller-provided `Span<int>` (no heap alloc).
- `Score(query, target)` and `ExactScore(query, target, caseSensitive)` no-out overloads are now zero-alloc.
- `ScoreWithFileNamePriority` and `ExactScoreWithFileNamePriority` compare candidates via score-only calls first, then allocate positions once for the winner (was 2 arrays, now 1).
- `ScoreWithFileNamePriority` score-only overload is now truly zero-alloc (was delegating to the out overload, silently allocating and discarding).
- Added `FuzzyScorerBenchmarks` to `Wade.Benchmarks` confirming the reduction.

### ~~File finder — remember search terms~~ (Done in Rust)

Ctrl+F reopens with the last query of the session (cursor at the end, typing appends) and searches
at once. Not persisted across runs; a navigable history was left out. C# still opens empty
(`KNOWN_DEVIATIONS.md`).

### ~~File finder — fzf-style query syntax~~ (Done in Rust)

Space-separated AND terms, `'exact`, `^prefix` (at any path segment start), `suffix$`, `^whole$`, and `!`
negation (exact, prefix or suffix), with `\ ` for a literal space and per-term smart case (fuzzy included).
No OR (`|`). C# keeps the single-term `'` syntax (`KNOWN_DEVIATIONS.md`).

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

### ~~Text input improvements~~ (Done)

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

### ~~CSS color swatches — disambiguate pseudo-class/element selectors~~ (Done in Rust)

Rust tracks brace depth across lines and drops colors in a run that ends in `{`. A nested selector whose `{` is
on a later line still swatches.

### ~~CSS color swatches — `rgb()` / `rgba()` / `hsl()` / `hsla()` / named colors~~ (Done in Rust)

Modern CSS Color 4 functions (`color()`, `lab()`, `lch()`, `oklab()`, `oklch()`) are out of scope.
