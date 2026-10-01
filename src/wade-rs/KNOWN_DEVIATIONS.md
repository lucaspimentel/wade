# Known deviations from C# wade

While the port is a strict behavioral clone (see `docs/rust-port-plan.md`),
deliberate deviations are recorded here. The C# retirement trigger requires
this file to be empty or fully accepted.

## Temporary (phase-scoped, expected to be removed by later phases)
- **Open external goes through cmd /C start (Phase 4c).** C# uses
  `UseShellExecute = true`; Rust std has no ShellExecute, so the Windows
  path shells through `cmd /C start` and the unix path is deferred to
  Phase 9 with the rest of the unix work.
- **The file-operation progress overlay is enhanced (Phase 4c).** C# shows
  only the operation label; the Rust overlay also shows the item count and
  current file name. Deliberate divergence, so no golden fixture covers
  this overlay.

- **The filesystem watcher is a no-op on unix (Phase 4d).** C# uses
  `FileSystemWatcher` on every OS; the Rust watcher is hand-rolled on
  `ReadDirectoryChangesW` and the inotify backend is deferred to Phase 9
  with the rest of the unix work. Unix listings refresh only on manual
  refresh or navigation.
- **Unix Read-only/ReadOnly follows std, not .NET (Phase 4d).** The
  Properties overlay uses `Permissions::readonly()` (no write bit for
  anyone); .NET reports ReadOnly when the current user lacks write
  permission. Revisit with the Phase 9 unix work.
- **Later-phase preview and metadata providers are absent (Phase 7a).**
  The registries hold only the ported providers, in C# order. Missing until
  their phases: the MSI preview plus Office, NuGet, media and MSI
  metadata (Phase 9). Files of those types fall through to the next applicable
  provider (usually None, Text or Hex).
- **Unix terminals report default capabilities (Phase 8a).** Windows
  detects Sixel via `WT_SESSION` like C#; the Unix DA1/cell-size query
  needs the raw tty setup of the Phase 9 Unix input work, so Unix runs
  with no Sixel and 8x16 px cells until then (the response parser is
  already ported).
- **No golden fixture covers the Properties overlay (Phase 4d).** Its rows
  come from live filesystem metadata (created/accessed dates, attributes),
  so no fixture renders identically from both runners without a test seam
  in the frozen C# code. Parity evidence is the port of
  `PropertiesOverlayTests` in `src/ui/properties_overlay.rs`. Phase 5
  established that behavior-preserving C# render seams are allowed (see
  `App.RenderFileFinderView`), so a seam that injects the facts could add
  a fixture later.

- **Config toggles for unported subsystems are inert (Phase 3c).** The
  config dialog ports all 27 settings and persists them, but toggles
  gating providers that land in later phases (media metadata) have no
  runtime effect until Phase 9.

## Accepted (deliberate, permanent)
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
