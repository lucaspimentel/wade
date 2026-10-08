# Known deviations from C# wade

While the port is a strict behavioral clone (see `docs/rust-port-plan.md`),
deliberate deviations are recorded here. The C# retirement trigger requires
this file to be empty or fully accepted.

## Temporary (phase-scoped, expected to be removed by later phases)

None. Phase 9 closed the last temporary entries.

## Accepted (deliberate, permanent)
- **The file-operation progress overlay is enhanced (Phase 4c).** C# shows
  only the operation label; the Rust overlay also shows the item count and
  current file name. No golden fixture covers this overlay.
- **No golden fixture covers the Properties overlay (Phase 4d).** Its rows
  come from live filesystem metadata (created/accessed dates, attributes),
  so no fixture renders identically from both runners without a test seam
  in the frozen C# code. Parity evidence is the port of
  `PropertiesOverlayTests` in `src/ui/properties_overlay.rs`.
- **Unix input details (Phase 9f).** Bracketed pastes are decoded as
  UTF-8; C#'s `VtParser` appends each byte as a Latin-1 char, so
  non-ASCII pastes arrive mangled there. The reader polls /dev/tty with a
  100ms timeout (C# blocks in `read`) so the input thread can be stopped
  on exit; keys, the 50ms lone-ESC wait and resize events are unchanged.
  Opening a file with the default app on unix follows .NET's rules (run
  executables directly, otherwise xdg-open/gnome-open/kfmclient or
  /usr/bin/open) but does not inherit the TUI's stdout/stderr.
- **MSI files are read with the `msi` crate on every OS (Phase 9e).** C#
  queries msi.dll, so "Installer files" and "MSI metadata" exist only on
  Windows there (on Linux/macOS C# lists the metadata provider but it
  returns nothing). Rust parses the database itself and shows both
  everywhere. The summary Template ("Platform") is rebuilt from the
  crate's architecture and language list as `arch;lang,lang`, files with
  equal names keep table order when sorted, and there is no C# golden
  (msi.dll cannot run here); `src/preview/msi.rs` tests pin the output on
  installers built with the crate's writer.
- **Office/NuGet XML is read with roxmltree (Phase 9d).** Element values
  follow `XDocument.Load` (whitespace-only text segments dropped unless
  `xml:space="preserve"`, CDATA kept), pinned by
  `tests/golden/preview/documents.golden.txt`. Differences: a malformed
  part makes C# throw an uncaught `XmlException` that faults the preview
  load (pane stuck on loading); Rust shows no document metadata for that
  provider. Core-property dates are reformatted only when they are ISO
  8601 (`yyyy-MM-dd[THH:mm[:ss[.f]]][Z|+hh:mm]`); other text that
  `DateTimeOffset.TryParse` would accept (e.g. "January 5, 2024") prints
  as written. Package parts over 64 MiB are not decompressed.
- **Unix has an OS file clipboard (Phase 9b).** C# `SystemClipboard`
  publishes and reads files only on Windows, so on Linux/macOS its Copy,
  Cut and Paste stay inside wade. Rust also writes Linux
  `x-special/gnome-copied-files` (copy/cut + `file://` URIs) through
  wl-copy or xclip and reads it back, falling back to `text/uri-list`;
  macOS goes through NSPasteboard file URLs (`osascript -l JavaScript`),
  always as a copy. Paste reads the OS clipboard first, as C# does on
  Windows.
- **Linux drive types use a reduced file-system table (Phase 9a).** .NET
  maps the root mount's file-system type to a `DriveType` through a long
  table; Rust recognises network (nfs, cifs, sshfs, 9p, ...), RAM/virtual
  (tmpfs, proc, sysfs, ...) and optical (iso9660, udf) families and treats
  every other type as Fixed. Only the inline directory-size gating reads
  it on Linux, and rare file systems may land in a different family.
- **Image preview pixels differ from C# (Phase 8b).** Previews decode
  with the `image` crate and scale with a triangle filter instead of
  ImageSharp's bicubic resampler, and the median-cut palette sorts
  without .NET's introsort tie order, so Sixel bytes differ from C#.
  Fit size (never upscaled), labels, layout and when the Sixel is written
  match C#; exact pixels were judged unnecessary for previews.
- **PDF previews clean up their temp directory; pdfinfo dates stay raw
  (Phase 8d).** C# deletes the rendered PNG but leaves its
  `wade-pdf-*` directory behind; Rust removes the directory. C# reformats
  CreationDate/ModDate when `DateTimeOffset.TryParse` accepts pdfinfo's
  text (local offset); Rust shows the dates as pdfinfo prints them.
- **Image metadata comes from the `image` crate and `kamadak-exif`
  (Phase 8c).** Entries, labels and EXIF formatting follow C#, but the
  values come from those crates: "Color depth" is the decoder's original
  color type (e.g. 8 bpp for indexed PNGs where ImageSharp may report a
  different pixel type), "Format" maps the detected format to ImageSharp's
  name, and "Frames" counts GIF image descriptors, the APNG `acTL` frame
  count and WebP `ANMF` chunks (multi-page TIFFs report no frame count).
  EXIF `0.#` values round half-to-even rather than .NET's half-away.
- **Markdown parses with pulldown-cmark, not Markdig (Phase 7d).** The
  renderer reproduces `MarkdigRenderer` on an equivalent tree (silent
  link-definition and HTML blocks, dropped entities, first-word fence
  info, non-paragraph first list blocks skipped) and matches the Markdig
  golden over the corpus in `tests/golden/markdown/` except where the
  parsers differ. Recorded difference: Markdig drops the `[` of an
  undefined full reference at the end of a bracket chain (`H[m][n]`
  renders `Hm][n]`); pulldown-cmark keeps the text as CommonMark
  specifies. `tests/markdown_golden.rs` pins each difference as an exact
  substitution.
- **GNU sparse tar entries don't stall the preview (Phase 7b).** .NET's
  `TarReader` throws `NotSupportedException` for type `S`, which escapes
  `TarPreview` and faults the C# preview load: the pane stays on
  "[loading…]" with no metadata. Rust gives no archive preview and no
  archive metadata for such files; the file-info metadata still shows.
- **Properties for a vanished entry show N/A (Phase 4d).** When the entry
  no longer exists, C# `FileInfo` does not throw; it shows sentinel values
  (Created/Accessed `1601-01-01 12:00 AM` in local time, every attribute
  flag, Read-only "Yes"). Rust shows "N/A" for dates and attributes and
  "No" for Read-only, as C# does for its own exception path.
- **Directory size of a vanished directory reports 0 B (Phase 4d).** C#
  `DirectorySizeLoader.Calculate` does not catch
  `DirectoryNotFoundException`, so the task faults and the overlay keeps
  "Calculating…" until it is closed. Rust treats the missing directory as
  inaccessible and reports 0 B.
- **Watcher full-refresh requests survive the debounce window (Phase 4d).**
  C# replaces the debounce timer on each event with that event's
  `fullRefresh` flag, so a buffer overflow followed within 300ms by an
  ordinary change loses the full refresh. Rust ORs the flag across the
  window.
- **Text positions count code points (Phases 5-7).** The finder scorer,
  the syntax highlighters and the preview renderer run over `char`s, so
  match positions, span `start`/`len` and `char_styles` index code points;
  C# indexes UTF-16 units. They agree for
  BMP text. For astral-plane characters (emoji) C# finder rows draw split
  surrogate halves, while Rust draws whole characters; the highlight
  golden harness maps Rust positions to UTF-16 units and matches C#
  exactly, so the difference is only that mapping.
- **Character classes follow std outside ASCII (Phases 5-6).** The search
  scorer's boundary bonuses and the highlighters' identifier, digit and
  whitespace tests use .NET `char.IsUpper`/`IsLower`/`IsDigit`/`IsLetter`/
  `IsLetterOrDigit`/`IsWhiteSpace` (Unicode general categories); Rust
  (`crate::text`) uses std `is_uppercase`/`is_lowercase`/`is_numeric`/
  `is_alphabetic`/`is_alphanumeric`/`is_whitespace`, which use different
  Unicode properties (for example Roman numerals count as uppercase and
  numeric). Case folding takes the first char of std's lowercase mapping
  and only single-char uppercase mappings (matching the .NET simple
  mappings for common cases such as `İ` and `ß`). ASCII behavior is
  identical, pinned by `tests/golden/search/scorer.golden.txt` and
  `tests/golden/highlight/`; non-ASCII names can tie-break differently and
  rare non-ASCII identifiers or digits can tokenize differently.
- **Finder results appear once their entries arrive (Phase 5).** A result
  can reach the finder before the walk's batch carrying its entry. C# does
  not rebuild its cached display list when entries arrive, so such results
  stay hidden until another result batch (or never, when it was the last).
  Rust rebuilds on either event.
- **The finder renders below 78 columns (Phase 5).** C#
  `Math.Clamp(width * 3 / 4, 70, width - 8)` throws when `width - 8 < 70`,
  so Ctrl+F crashes on narrow terminals; Rust lets the upper bound win.
- **The finder reopens with its last query (Rust-only addition).** Closing
  Ctrl+F (Esc or Enter) remembers the query for the session, and the next
  Ctrl+F starts with it, cursor at the end, and searches at once. C# always
  opens empty. Not persisted across runs; no history navigation.
- **The finder accepts fzf-style query syntax (Rust-only addition).**
  Space-separated terms are ANDed; `'foo` is an exact substring, `^foo` an
  exact match at the start of any path segment (fzf anchors at the string
  start), `foo$` an exact match at the end of the path, and `!` excludes
  (exact, prefix or suffix; never fuzzy). `\ ` is a literal space. Every
  term uses smart case, fuzzy terms included (C# fuzzy is always
  case-insensitive). Multi-term scores are the sum of the term scores with
  one depth penalty; highlighted positions are the union. A query of lone
  operators (`'`, `!`) matches everything, where C# `'` matches nothing.
  Single lowercase terms score exactly as in C# (the scorer golden).
- **Ctrl+C clears search and filter boxes (Rust-only addition).** In the
  Ctrl+F finder, the `/` filter, the Ctrl+P palette and the `b` bookmarks
  list, Ctrl+C empties the input and lists everything again; C# ignores it.
- **The sort is remembered per directory (Rust-only addition).** `s` and
  `S` change only the current directory's sort, saved in
  `~/.config/wade/sorts` (`<mode> <asc|desc> <path>` per line) and applied in
  every pane that lists that directory. Other directories use the config
  `sort_mode`/`sort_ascending`, which `s`/`S` no longer change. The status bar
  adds `*` after the sort marker for a saved sort, and the palette's "Reset
  sort for this directory" forgets it. C# has one global sort.
- **Tabs (Rust-only addition, issue #17).** `t` opens a tab at the current
  directory after the active one (at most 9), `{`/`}` and `1`-`9` switch,
  `w` closes it and quits like `q` on the last tab; a left click on the tab
  bar switches. Each tab keeps its path, selection (also per directory),
  scroll, marks and `/` filter; settings and the per-directory sorts are
  shared. The bar takes row 0 only with two or more tabs, so one-tab frames
  match C#. Tabs are not persisted. C# has no tabs.
- **Returning to a file while another preview loads reloads it at once
  (Rust-only fix).** Rust starts a preview load whenever the selected
  path is not the pending one. C# also skips the load when the path is the
  last cached one, so moving off a file and back before the other file's
  preview finishes leaves the pane loading. During that time it shows the
  other file's metadata and type label. The file is reloaded only after the
  abandoned load completes.
- **Broken symlinks show `[broken symlink]` (Rust-only fix).** C# leaves the
  right pane blank. Its `ClearPreviewCache` drops the preview context and
  the empty metadata provider list that the message branch checks, and Rust
  keeps them.
- **Kitty graphics and iTerm2 inline images for image previews (Rust-only addition).**
  - **Detection.** On Unix, startup also sends a kitty graphics query (`a=q`) and XTVERSION (`CSI > q`). Kitty is
    used only when the terminal answers `OK` and calls itself kitty or Ghostty. WezTerm and Konsole answer the query
    but have no Unicode placeholders.
  - **iTerm2 detection.** iTerm2 inline images (`OSC 1337 ; File=`) are used when the XTVERSION name starts with
    `iTerm2` or `WezTerm`. If there is no reply, `TERM_PROGRAM` = `iTerm.app` or `WezTerm` is used. On Windows,
    where replies can't be read, `TERM_PROGRAM=WezTerm` turns them on.
  - **Choosing a protocol.** The new `image_protocol = auto|kitty|iterm|sixel` key goes in the config file only; the
    dialog does not show it. With `auto`, the order is kitty, then iTerm2, then Sixel. A forced protocol must still
    be detected. PDF previews use whichever protocol is chosen.
  - **The new key in output.** The key is written to the file only when it is not `auto`, so a default file keeps
    the C# layout. `--show-config` gains `image_protocol` before `start_path`.
  - **How images are drawn.** The image is sent once, compressed with zlib, with a virtual placement. The image area
    is then U+10EEEE placeholder cells in the screen buffer: the foreground color is the image id, and two diacritics
    give the cell's row and column. Replaced images are deleted.
  - **How iTerm2 images are drawn.** The fitted image is sent as PNG (full color, unlike 256-color Sixel). Its size
    is given in cells, so a wrong cell-size guess can't push it past the pane, and `doNotMoveCursor=1` keeps the
    cursor in place. Like Sixel, it is written after every frame at the image position.
  - **Behaviour under dialogs.** Unlike Sixel, the uncovered part of a kitty image stays visible while a dialog is
    open. iTerm2 images are hidden under dialogs, like Sixel.
  - **Not supported.** Windows Terminal (no kitty support) and tmux (queries do not reach the outer terminal).
  - C# has Sixel only.
- **TOML lock files are highlighted (Rust-only addition).** `Cargo.lock`,
  `poetry.lock`, `uv.lock` and `pdm.lock` use the TOML highlighter; C# shows
  them as plain text.
- **Sort ties and extension case folding differ (Phase 10).** Sorting by
  size or modified time keeps entries with an equal key in name order (a
  stable sort); C# uses an unstable introsort, so its order among exact ties
  is arbitrary. Sorting by extension compares ASCII-lowercased extensions;
  C# compares them ordinal-ignore-case (uppercased), so extensions that
  differ at a character between `Z` and `a` (such as `_`) order differently.
  The same applies to the MSI file listing (sorted by name, equal names in
  table order). Found by diffing both implementations over about 3,000 real
  Windows directories: every other ordering matched.
- **Files open for writing elsewhere are readable (Phase 10).** Rust opens
  files with full sharing, so an in-use log (for example Steam's) still
  previews as text. C# opens with `FileShare.Read`, fails with a sharing
  violation and shows such a file as binary.
- **Image color depth and span offsets use different units (Phase 10).**
  Beyond the image-crate bit depths above, highlighter span offsets count
  Unicode scalar values in Rust and UTF-16 code units in C#; each renderer is
  consistent with its own units, so output is the same. Only the internal
  numbers differ for text with characters outside the BMP.
- **`--show-config` ends with a bare line feed (Phase 10).** C# writes the
  JSON with `Console.WriteLine`, which ends with CRLF on Windows; Rust ends
  with LF on every platform. The JSON itself is identical.
- **CSS swatches follow value positions (Rust-only change).** In `.css` and
  `.scss`, the tokenizer tracks brace depth in its line state, so a color
  needs a `:` inside a block. A value continues onto the next line until
  `;`, `{` or `}`. When a `{` follows, the colors found since the previous
  `;`, `{` or `}` on that line are dropped, because that run was a selector:
  `a:hover #abc { color: #fff; }` swatches only `#fff`. C# swatches any
  `#hex` after a `:` on the same line, which includes selectors and
  declarations outside any block.
  - Limit: a nested selector whose `{` is on a later line still swatches.
  - `.sass` (indented syntax, no braces) keeps the line-local rule.
  - Rust also swatches `rgb()`/`rgba()`/`hsl()`/`hsla()` and the 148 CSS
    named colors (not `transparent` or `currentcolor`). An unquoted
    `url(...)` is skipped.
  - The CSS highlight goldens are Rust-owned
    (`tests/golden/highlight/css.*` and `fuzz-css.golden.txt` under
    `src/wade-rs`). The shared C# CSS cases are skipped in the C#
    comparison.
- **The terminal title is restored on exit (Rust-only fix).** C# pushes the
  title onto the terminal's title stack at startup (`CSI 22;0 t`) but never
  pops it: on exit, and when `terminal_title_enabled` is turned off, it
  writes an empty title.
  - Rust writes the empty title and then pops the stack (`CSI 23;0 t`).
  - When the setting is turned off, Rust also pushes the title again so
    exit can still pop it.
  - On Windows, Rust also saves the console title with `GetConsoleTitleW`
    at startup and puts it back with `SetConsoleTitleW`, which works
    whether or not the host supports the title stack.
  - Unix terminals without a title stack (for example tmux) still end with
    an empty title, as in C#.
- **Pasting into the source's own folder makes a copy (Rust-only fix).** In
  C#, copying an item and pasting it into the folder it came from counts it
  as a conflict. Confirming Overwrite deletes the source before copying it,
  so the item is lost.
  - Rust does not count it as a conflict. A copy gets an Explorer-style
    name (`a - Copy.txt`, then `a - Copy (2).txt`); directories and
    dotfiles keep the whole name as the stem (`sub - Copy`, `.env - Copy`).
  - A cut pasted into its own folder does nothing and counts as a success.
- **A directory is not pasted into itself or its own subtree (Rust-only
  fix).** C# copies until the path is too long. Rust counts the item as an
  error and creates nothing. Moving a directory link into its target is
  still allowed, since only the link moves.
- **Read-only files are deleted and overwritten on Windows.** C# fails on
  the read-only attribute when deleting permanently or overwriting on
  paste. Rust's `std::fs::remove_file` and `remove_dir_all` delete
  read-only files and directories on Windows, as Explorer does, so no
  attribute clearing is needed. The Recycle Bin path already handled them.
- **Case-only rename works on Windows (Rust-only fix).** C# reports that
  `Foo` already exists when renaming `foo`, because Windows file names are
  case-insensitive. Rust skips that check when only the case changes.
- **Windows-only names are refused (Rust-only).** On Windows, the new file,
  new directory, symlink and rename dialogs refuse device names (`CON`,
  `PRN`, `AUX`, `NUL`, `COM1`-`COM9`, `LPT1`-`LPT9`, with or without an
  extension, such as `nul.txt`) and names ending in a dot or space, which
  Win32 would strip silently. C# lets them through and the call fails or
  creates a different name.
  - The rename dialog also checks for invalid characters on every OS. C#
    does not, and fails in the rename itself.
- **Enter opens the full-screen preview with the right pane hidden
  (Rust-only).** In C#, Enter on a file opens the expanded preview only when
  the right pane has already picked a preview for it, so with the pane
  hidden Enter does nothing. Rust picks the preview for the selected file
  on Enter when the pane is hidden. Leaving the expanded preview then clears
  the preview state instead of reloading it into the hidden pane. Files with
  no preview (binary files, or previews turned off) still do nothing.
- **Preview limits depend on the view (Rust-only).** C# reads 100 text
  lines, 100 archive (zip, tar, MSI) entries and 30 lines from the first
  4 KB of a gzipped text, in both the right pane and the full-screen
  preview, and stops silently.
  - The right pane reads only as many lines or entries as it has rows.
  - The full-screen preview reads up to `preview_max_lines` lines or
    entries (default 10,000, minimum 100) and `preview_max_bytes` bytes of
    text or gzip payload (default 4 MiB, minimum 64 KiB). Both keys go in
    the config file only; like `image_protocol`, they are saved only when
    changed, and `--show-config` lists them before `start_path`.
  - A cut-off text or gzip preview in full-screen ends with a dim
    "… preview limited to N lines" (or "to SIZE") line, drawn without a
    line number. Archive listings keep their "... and N more entries" line.
  - The archive golden test reads plain `.gz` files with C#'s 30 lines and
    4 KB to stay comparable.
- **CSV/TSV table preview (Rust-only).** C# shows `.csv` and `.tsv` as plain
  text. Rust adds a "Table" preview for `.csv`, `.tsv` and `.tab`, ahead of
  Text (which stays available through `p`).
  - Separators: tab for `.tsv` and `.tab`; comma for `.csv`, unless the first
    20 records are more consistently split by `;` or `|`.
  - Parsing follows RFC 4180: quoted fields, `""` escapes, separators inside
    quotes, and line breaks inside quotes shown as `↵`. Blank lines are
    skipped and a UTF-8 BOM is ignored.
  - Layout: the first record is a bold header followed by a `─` rule; each
    column is as wide as its widest loaded cell, up to 30 cells, with `…` on
    longer cells; a dim `│` separates columns; numbers are right-aligned;
    record numbers sit in a dim gutter (the header is not counted). It
    uses the preview limits (the right pane's height, or
    `preview_max_lines` in full-screen, with the truncation marker).
  - File details gain a "Table" section: Rows (data rows; `N+` when
    `preview_max_bytes` stopped the count), Columns, and Separator when
    sniffing picked one other than the extension's.
  - Setting: `csv_preview_enabled` (default on), "Show CSV/TSV Tables"
    under "Show File Previews" in the settings dialog. Like the other
    Rust-only keys, it is saved only when changed, and `--show-config` lists
    it.
  - The settings-dialog renderer test drops this item before comparing with
    the C# frames, since an extra item shifts the scrolled list on Windows.
- **macOS support (Rust-only).** C# was never run on macOS; Rust is tested
  by CI's `rust (macos-latest)` job (Apple Silicon) and adds:
  - Case-insensitive volumes (`pathconf(_PC_CASE_SENSITIVE)`, the APFS
    default) get the Windows case handling: a case-only rename is not a
    conflict, and paste's same-folder and own-subtree checks ignore case.
    Case-sensitive volumes keep exact matching.
  - Paths entering wade (start path, go to path, bookmarks and saved sorts
    on load, git status paths) are respelled as stored on disk on
    case-insensitive volumes, so path-keyed state matches whatever case was
    typed. Bookmarks and sort lines that differ only in case merge on load
    (first bookmark, last sort line wins; see also the bookmark entry). Links are not resolved. C# keeps
    the typed spelling and compares ordinally on macOS.
  - Drive type comes from `statfs` (smbfs/nfs/afpfs/webdav are Network)
    and SSD/HDD and removable media from `diskutil info -plist` (cached per
    volume), so inline directory sizes follow the SSD/HDD settings. C# uses
    `DriveInfo`, which reports Fixed and no media type there.
  - Delete without Shift moves items to the Trash (NSFileManager via
    `osascript`, one item per call, C#'s success/error accounting) and the
    confirmation drops "This cannot be undone!"; C# deletes permanently on
    every Unix.
  - "Open terminal here" runs `open -a Terminal <dir>` (`iTerm` when
    `TERM_PROGRAM` is `iTerm.app`) instead of starting `$SHELL` detached.
  - The drive list shows the volumes under `/Volumes`: name is the mount
    point (`/` for the boot volume, listed first), label the volume name,
    plus format, free and total space and media type. A volume mounted
    under `/Volumes` is its own drive root, so going up from it (or from
    `/`) opens the list. C# lists every mount `DriveInfo` reports. Linux
    keeps the empty list.
- **Duplicate bookmarks merge on load (Rust-only).** C#'s `BookmarkStore.Load`
  keeps every line, although `Contains`/`Remove` already treat equal paths
  as one bookmark. Rust drops later entries equal to an earlier one by the
  same comparison: case-insensitive on Windows, exact on Linux, and on
  macOS after respelling with the on-disk case. The file is rewritten on
  the next save.
