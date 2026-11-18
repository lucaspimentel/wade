# TODO

## Manual checks

The Rust port replaced the C# tree on 2026-10-11 (`docs/rust-port-plan.md` has the history). What is
left is the checks that need a real machine or a real terminal.

Windows needs a real console; none of these are automated:

- [ ] the `wd` wrapper (`--cwd-file`)
- [ ] keyboard, mouse, window resize and bracketed paste
- [ ] Sixel image previews and terminal capability detection
- [ ] "Download cloud file" from the action palette on a OneDrive placeholder
- [ ] paste into Explorer after copy/cut in wade, and copy/cut from Explorer
- [ ] SSD inline directory sizes and the drive list on screen
- [ ] filesystem auto-refresh on screen (the PTY tests cover it under ConPTY; a
  real Windows Terminal check is still worth doing)
- [ ] the tab title returns to the pre-wade title on exit and when
  `terminal_title_enabled` is turned off
- [ ] PDF and Sixel previews end to end, no TUI needed (`pdftopng`, `pdfinfo`,
  `ffprobe` and `mediainfo` are installed on this machine)

macOS needs a real terminal:

- [ ] keyboard and Option-key input, mouse, resize, kitty/iTerm2/Sixel images,
  clipboard round trip with Finder, open with the default app, Trash and
  Terminal/iTerm launch, the `/Volumes` drive list with an external disk

Linux needs a real terminal:

- [ ] the drive list (going up from `/`) with `/mnt/c` under WSL, a USB stick and
  a network mount
- [ ] restoring a file from a desktop file manager's Trash

Image protocols, implemented but never exercised against the real terminal:

- [ ] kitty and Ghostty, which take the Unicode-placeholder path
- [ ] tmux: the Sixel its own version draws, and passthrough with
  `allow-passthrough` on

- [ ] Two weeks of daily use on the Rust binary (ongoing verification, not a gate)

## Features

### Format-specific metadata providers

#### Backlog

- **Font files** (`.ttf`, `.otf`, `.woff2`) — font family, style, weight, glyph count. Parse OpenType/TrueType `name` and `head` tables.
- **OpenDocument** (`.odt`, `.ods`, `.odp`) — title, author, dates, page/sheet count. Extract from `meta.xml` inside the ODF zip archive (similar to Office OOXML approach).
- **EPUB** (`.epub`) — title, author, publisher, language, identifier. Extract from `content.opf` metadata inside the zip archive.

## Backlog

### UNC shares in the drive list

- [ ] List connected network shares as drives on Windows, so a `\\server\share` root is reachable
  from the drive list. Going up from a UNC share root does nothing today (`Path::parent` is `None`
  there and the share is not a drive letter), and the list only holds drive letters, so such a
  share can only be reached through Go to path or a bookmark. Needs the connected shares
  enumerated (WNet/mapped drives) with free and total space where Windows reports them; the
  drive-root rule in `is_drive_root` would accept a share root as well.

### Preview for Office/document formats (DOCX, XLSX, PPTX, etc.)

Research and implement image previews for Office Open XML formats (`.docx`, `.xlsx`, `.pptx`, `.dotx`, `.xltx`, `.potx`) by converting the first page to an image, similar to PDF preview.

- `OfficeMetadataProvider` (`preview/document_metadata.rs`) already extracts metadata (title, author, dates) from DOCX/XLSX/PPTX — reuse for preview metadata
- Investigate toolchain:
  - **LibreOffice headless**: `soffice --headless --convert-to pdf --outdir /tmp /path/to/docx` → PDF, then `pdftopng` to PNG. Heavyweight but comprehensive.
  - **Lighter alternatives**: Research if lighter/faster converters exist (e.g., `pandoc` for DOCX→HTML, but visual fidelity TBD)
  - **Availability**: Check if LibreOffice or alternatives are commonly available on Windows/macOS/Linux
- Add an Office converter next to `imaging/pdf.rs` (or extend its `can_convert`/`convert_to_image` to handle Office files)
- Add an `OfficePreviewProvider` (or extend `PdfPreviewProvider` to handle both PDF and Office)
- Preview registry order (`preview/registry.rs`): insert after `PdfPreviewProvider` so Office files are attempted
- Performance concern: LibreOffice startup is slow (~1-2s); consider debouncing or async user feedback
- Start with DOCX (most common); extend to XLSX/PPTX after proving the concept

### Consolidate settings sources

- [ ] Settings come from `config.toml` and the config dialog only (plus `--config-file`). Add one layer that also reads environment variables and CLI flags (e.g. `WADE_ICONS`, `--icons=`), with a fixed precedence (CLI > env > file > default), instead of adding each override by hand. Useful per terminal: a Nerd Font in one terminal, none in another.
- [ ] Later, behavior changes: save keeping comments and unknown keys; reporting invalid values

### Release builds do not restore the terminal on a panic

`Cargo.toml` sets `panic = "abort"` for the release profile, so a panic runs no `Drop` and skips the restore path: the terminal is left in raw mode on the alternate screen, possibly inside a synchronized update. Install a panic hook (`std::panic::set_hook`) that writes the restore sequence and resets the tty modes before aborting; it needs the saved termios reachable from the hook (a global or a shared handle). Left out of the synchronized-output change.
