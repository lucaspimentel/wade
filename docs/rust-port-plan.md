# Rust Port Plan

Phased plan for porting wade (C#/.NET 10) to Rust. The Rust implementation lives in
`src/wade-rs` and grows module by module on `main`; the C# build stays green throughout.
The C# code is the behavioral spec until cutover.

## Decisions (settled)

- **Motivation**: native binary wins + language consolidation. Not primarily a learning exercise.
- **End state**: temporary coexistence. C# is deleted after Rust reaches parity and has been
  daily-driven for 2 weeks.
- **Architecture**: keep wade's raw VT/ANSI design. No crossterm/ratatui. Port the architecture,
  not just the features: ScreenBuffer diffing, `IInputSource` seam, no TUI frameworks.
- **Code style**: fully idiomatic Rust *inside* module boundaries. `App.cs` is broken up; no 5K-line `impl`.
- **Behavior**: strict behavioral clone until cutover (keybindings, rendering, config keys, quirks).
  Deliberate deviations tracked in `src/wade-rs/KNOWN_DEVIATIONS.md`. UX changes happen after C# deletion.
- **Scope**: staged to full parity. Core browser first, exotic providers later. End goal is 100%
  feature parity, no silent permanent drops.
- **Platform**: Windows first (daily driver is Windows Terminal). Unix input deferred.
- **Verification**: golden-frame snapshot tests of rendered ScreenBuffer output validated against the
  C# binary, plus targeted new unit tests. No 1:1 port of the 16.6K-line C# test suite.
- **C# freeze**: no feature work in C# during the port. Critical fixes land in C# and are ported
  immediately as spec updates.
- **Config**: Rust reads the same `~/.config/wade/config.toml` (parser at `WadeConfig.cs:82-159`);
  daily-driver swap requires no migration.
- **Execution**: hand-built spine (Phases 0-3), then agent fan-out over leaf modules.

## Crate layout

```
src/wade-rs/
  Cargo.toml            # single crate, no workspace (Search is ~900 lines, not worth a subcrate)
  KNOWN_DEVIATIONS.md
  src/
    main.rs
    config.rs           # WadeConfig + hand-rolled TOML subset parser (clone of WadeConfig.cs behavior)
    terminal/           # input sources, TerminalSetup, AnsiCodes, InputPipeline
    screen/             # ScreenBuffer, StyledLine, RuneWidth
    fs/                 # DirectoryContents, FileOperations, drive/reparse/clipboard, GitUtils
    search/             # SearchIndex, FuzzyScorer, SearchQuery
    highlight/          # SyntaxHighlighter, SyntaxTheme, per-language modules
    preview/            # provider registry, preview + metadata providers, PreviewLoader
    imaging/            # image decode/scale, SixelEncoder, PDF converter
    ui/                 # PaneRenderer, overlays, dialogs, FormatHelpers, FileIcons
    app/                # App state machine, broken into modules (not one file)
  tests/                # golden frames + unit tests
```

Dependency replacements (verified against C# usage, crate choices inferred, not yet vetted):

| C# | Rust candidate |
|---|---|
| ImageSharp | `image` + `kamadak-exif` |
| Markdig | `pulldown-cmark` |
| `PEReader`/`MetadataReader` | `goblin` or `pelite` |
| Win32 P/Invoke | `windows-rs` |
| libc P/Invoke (`LibC.cs`) | `libc` crate |
| `System.Formats.Tar` + GZip | `tar` + `flate2` |
| `System.IO.Compression.ZipFile` | `zip` |
| `System.Xml.Linq` (nuspec/docProps) | `roxmltree` |

## Phases

Each phase ends with: `cargo test` green, `dotnet build Wade.slnx` still green, module merged to `main`.

**Progress** (updated as phases land): Phase 0 done, Phase 1 done, Phase 2
done (ScreenBuffer golden frames shipped with Phase 0's harness), Phase 3
done (split into 3a app spine, 3b modal overlays, 3c config dialog /
bookmarks / path completion / paste / mouse + context menu), Phase 4a
git status, 4b git actions, and 4c file operations. Next: 4d loaders,
watcher, and properties.

### Phase 0 — Scaffold + golden-frame harness

- Create `src/wade-rs` crate; CI builds and tests both implementations.
- Build the golden-frame capture: add a test-only ScreenBuffer dump to C# wade (render a fixed
  scenario to a canonical string/binary artifact), and a Rust harness that renders the same
  scenario and diffs.
- `KNOWN_DEVIATIONS.md`, this plan, feature freeze takes effect.
- **Verify**: both builds green; harness can capture and compare one trivial frame.

### Phase 1 — Terminal input (Windows)

- `windows-rs` port of `WindowsInputSource` (`ReadConsoleInputW`, `ENABLE_MOUSE_INPUT`,
  no `ENABLE_VIRTUAL_TERMINAL_INPUT`), `InputEvent`, `InputMode`, `InputReader`, `TerminalSetup`.
- **Verify**: unit tests for key/mouse event decoding (port the C# decoding cases; they are pure);
  manual key matrix in Windows Terminal.

### Phase 2 — ScreenBuffer + rendering primitives

- `ScreenBuffer` diffing, `StyledLine`/`CellStyle` (per-cell `CharStyles` semantics included),
  `RuneWidth`, `AnsiCodes`, `FormatHelpers`.
- **Verify**: golden frames — diff-style unit tests first, then frame comparisons against C# output.

### Phase 3 — App spine (done: 3a, 3b, 3c)

- **3a (done)**: App state machine (navigation, selection, three panes, layout),
  `DirectoryContents`, status bar, action palette (portable entries only),
  renderer fixtures.
- **3b (done)**: `DialogBox`, `TextInput`, Help overlay, Confirm/TextInput/
  GoToPath dialogs, action-palette stack machinery, search bar; C# render
  cores extracted into `src/Wade/UI/ModalDialogs.cs` for fixture parity.
- **3c (done)**: config dialog (`ConfigDialogState`, full 27-item port),
  config persistence (`WadeConfig.Save` port, `--config-file=` honored),
  `BookmarkStore` + bookmarks dialog, `PathCompletion` + GoToPath ghost
  completion, paste events, mouse input (scroll, pane click navigation),
  right-click context menu; C# render cores extracted into `ModalDialogs.cs`
  (`ConfigDialog`, `BookmarksDialog`); renderer fixtures gained a
  `platform windows` gate for platform-dependent scenarios.

### Phase 4 — Git + async loaders

- `GitStatusLoader`, `GitUtils`, `GitActionRunner`, `InlineDirSizeLoader`, `DirectorySizeLoader`,
  `FileSystemWatcherManager`, `FileOperationRunner` — the thread/event-loader pattern becomes
  channels or an equivalent idiomatic mechanism; timing/throttle behavior (200ms/100ms/300ms)
  cloned exactly.
- **Verify**: golden frames for status column coloring/ahead-behind display; unit tests for the
  porcelain parser and aggregate-status logic.

### Phase 5 — Search + file finder

- Port `Wade.Search` (FuzzyScorer, SearchIndex, SearchQuery, ActiveQuery) and the Ctrl+F flow
  (BFS walk, caps, throttles, scoring).
- **Verify**: the C# FuzzyScorer/SearchQuery tests port nearly 1:1 here — this module is pure;
  port them as behavioral parity evidence.

### Phase 6 — Syntax highlighting (agent fan-out)

- SyntaxHighlighter/SyntaxTheme plus all 21 language modules. Establish the pattern by hand on
  2-3 languages, then fan out agents per language using the established module as template.
- **Verify**: C# per-language tests become golden-token fixtures shared conceptually across both
  implementations (same input -> same spans).

### Phase 7 — Preview + metadata core

- Provider registry (`PreviewProviderRegistry`, `MetadataProviderRegistry`), `PreviewLoader`,
  `MetadataRenderer`, text/diff/hex/zip/tar providers, LnkParser, markdown preview
  (pulldown-cmark rework of `MarkdigRenderer`, same rendered output contract).
- **Verify**: registry ordering tests; per-provider fixtures; golden frames of preview pane.

### Phase 8 — Imaging + Sixel

- `image` crate decode/scale, port `SixelEncoder` (median cut), capability detection
  (WT_SESSION/DA1), cell pixel dimensions, PDF convert-to-image pipeline, `ImageMetadataProvider`
  with EXIF.
- **Verify**: encoder golden output vs C# encoder on fixture images; manual sixel verification in
  Windows Terminal.

### Phase 9 — Long tail (agent fan-out)

- Each item isolated behind the registry; parallelizable: reparse point detection (junction,
  app-exec-link), drive type detection, cloud placeholders, clipboard, Shell32, MsiInterop,
  Office/NuGet OPC metadata, exe metadata (goblin/pelite), media providers (ffprobe/mediainfo),
  Unix input source (`cfmakeraw`/`poll` via libc), Unix/macOS clipboard.
- **Verify**: per-item unit tests + fixture files; the phase is done when `TODO.md`'s C# feature
  inventory maps 1:1 to Rust features.

### Phase 10 — Parity + cutover

- Full golden-frame sweep across both binaries; `KNOWN_DEVIATIONS.md` empty or accepted.
- Switch `install-local.ps1`/`install-remote.ps1` to the Rust binary; daily-drive for 2 weeks.
- **Cutover**: delete `src/Wade*`, remove dual-build CI, update README/CLAUDE.md/CHANGELOG.

## Retirement trigger

C# is deleted when: (1) full test suite green in Rust, (2) 2 weeks of daily use, (3) no open items
in `KNOWN_DEVIATIONS.md`.
