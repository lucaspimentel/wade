# TODO

## Bugs (open)

(none)

---

## Refactoring

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

### Preview provider system

Refactor the preview pane from a hardcoded fallback chain into a generic provider-based system. Any file type can have multiple preview providers (text, image, or both). Users can switch between providers via the action palette.

See plan: `.claude/plans/buzzing-gathering-rain.md`

#### Task 1: Define IPreviewProvider interface and PreviewResult

Create `src/Wade/Preview/IPreviewProvider.cs` with `IPreviewProvider` interface, `PreviewContext` record, and `PreviewResult` record. A `PreviewResult` can carry text lines, Sixel image data, or both. No dependencies on other tasks.

#### Task 2: Implement preview providers

Create `src/Wade/Preview/PreviewProviders.cs` with provider implementations that wrap existing static methods: `ImagePreviewProvider`, `PdfPreviewProvider`, `GlowMarkdownPreviewProvider`, `ZipContentsPreviewProvider`, `TextPreviewProvider`, `HexPreviewProvider`, `DiffPreviewProvider`. Each is a thin wrapper around existing code (`ImagePreview.Load()`, `ZipPreview.GetPreviewLines()`, etc.).

Depends on: Task 1

#### Task 3: Create PreviewProviderRegistry

Create `src/Wade/Preview/PreviewProviderRegistry.cs` with static ordered list of all providers and `GetApplicableProviders(path, context)` method. Providers ordered by specificity (most specific first).

Depends on: Task 2

#### Task 4: Refactor PreviewLoader to use providers

Replace the hardcoded fallback chain in `PreviewLoader.LoadPreview()` with provider dispatch. `BeginLoad()` accepts a provider (or index). Remove `BeginLoadDiff()` and `BeginLoadHex()` — these become provider calls.

Depends on: Task 3

#### Task 5: Replace toggle state with provider index in App.cs

Replace `_diffPreviewActive` / `_hexPreviewActive` booleans with `_activeProviderIndex` (int) and `_applicableProviders` (list). Update `ClearPreviewCache()` and file selection change logic. Wire up `HandleSelectPreviewProvider(int index)`. Update `AppAction` enum (remove `ToggleDiffPreview`/`ToggleHexPreview`, add `SelectPreviewProvider`).

Depends on: Task 4

#### Task 6: Action palette submenu system

Add generic submenu support to the action palette. Refactor from flat item list to a stack-based `ActionMenuLevel` model. Items can either dispatch an action or open a submenu. Enter on submenu → push level; Escape → pop level (or close if at root). Show title/breadcrumb for current level.

Depends on: nothing (can be done in parallel with Tasks 1-5, but must be integrated with Task 7)

#### Task 7: "Change preview" submenu

Add "Change preview" entry to action palette that opens a submenu listing applicable preview providers with the active one highlighted. Selecting a provider dispatches `SelectPreviewProvider`.

Depends on: Task 5, Task 6

#### Task 8: Combined text+image preview rendering

Update rendering code to handle `PreviewResult` with both text and image. Text renders in top portion of pane, image fills remaining space below. Update Sixel cursor positioning for the offset.

Depends on: Task 5

#### Future: Format-specific preview providers

Add primary preview providers for specific formats (e.g., NuGet metadata for `.nupkg`, document properties for `.docx`). Each is an `IPreviewProvider` implementation registered in the provider list. They automatically become the default for their file types and the existing zip/text previews become secondary options.

Depends on: Tasks 1-7 complete

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
