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
  their phases: archive contents and archive metadata (7b), shortcut
  properties (7c), rendered Markdown (7d), image and PDF previews and
  metadata (Phase 8), and the MSI preview plus executable, Office, NuGet,
  media and MSI metadata (Phase 9). Files of those types fall through to
  the next applicable provider (usually None, Text or Hex). Image and
  combined (text + Sixel) results, Sixel detection and cell-pixel-size
  detection are Phase 8; `PreviewContext` carries 8x16 px cells and
  image/Sixel flags off until then.
- **Expanded preview y/Y copy nothing (Phase 7a).** The copy-path and
  copy-git-relative-path keys need the OS clipboard (Phase 9).
- **No golden fixture covers the Properties overlay (Phase 4d).** Its rows
  come from live filesystem metadata (created/accessed dates, attributes),
  so no fixture renders identically from both runners without a test seam
  in the frozen C# code. Parity evidence is the port of
  `PropertiesOverlayTests` in `src/ui/properties_overlay.rs`. Phase 5
  established that behavior-preserving C# render seams are allowed (see
  `App.RenderFileFinderView`), so a seam that injects the facts could add
  a fixture later.

- **Unported actions are stubbed with a notification (Phase 3a).** Actions
  whose subsystems land in later phases (clipboard, cloud download: Phase
  9) show a status-bar notification ("Not yet ported") instead of
  performing the action. The action palette omits Copy, Cut, Paste, "Copy
  absolute path" and "Download cloud file" until Phase 9; every other C#
  entry, including the "Change preview" submenu, is listed in C# order.
  Git actions are ported (Phase 4b), except "Git: Copy relative path" (Y),
  which needs the OS clipboard and is omitted until Phase 9 alongside the
  context menu Paste/Copy/Cut entries.
- **Config toggles for unported subsystems are inert (Phase 3c).** The
  config dialog ports all 27 settings and persists them, but toggles
  gating providers that land in later phases (image/PDF/Markdown/archive
  previews, archive/PDF/media metadata) have no runtime effect until those
  phases land.
- **Dates carry no local timezone offset (Phase 3a).**
  `FileSystemEntry.last_modified` converts `SystemTime` without a local
  timezone offset (C# `LastWriteTime` is local). No timezone crate has been
  chosen yet; date display and Modified sort may differ by the UTC offset.
- **Drive media-type detection returns Unknown (Phase 3a).** The C#
  `DriveTypeDetector` (seek-penalty query) port is Phase 9; drive entries
  carry `DriveMediaType.Unknown`. Consequences until then: inline directory
  sizes (Phase 4d) never run, because `should_compute_inline_dir_sizes`
  disables Unknown media; and the Properties overlay's drive Attributes row
  omits the leading media type ("SSD"/"HDD"/`DriveType` text), showing only
  format and volume label.

## Accepted (deliberate, permanent)
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
