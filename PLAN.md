# wade — MVP Implementation Plan

## Context

Build "wade", a command-line TUI file browser written in .NET 10, targeting Windows Terminal first. Inspired by yazi, broot, ranger. The repo is at `D:/source/lucaspimentel/wade` (https://github.com/lucaspimentel/wade, private).

NativeAOT will be enabled later, so we avoid reflection-heavy patterns from the start.

## Design Decisions

- **Layout**: Miller columns (3-pane: parent / current / preview)
- **Rendering**: Raw VT/ANSI escape sequences (no Terminal.Gui, no Spectre.Console for UI)
- **Keybindings**: Arrow keys + standard keys (Enter, Backspace, Escape, Tab)
- **Platform**: Windows-first, Linux/macOS secondary
- **Framework**: .NET 10, console app, NativeAOT-ready patterns

## MVP Feature Scope

**In scope:**
- Directory listing with columns (name, size, date)
- Miller columns navigation (parent / current / preview)
- Arrow key navigation (up/down to select, right/Enter to open, left/Backspace to go up)
- Text file preview in the right pane (plain text, no syntax highlighting yet)
- Directory preview (list of contents) in the right pane
- Terminal resize handling
- Sorting (name, by default)
- Basic status bar (current path, item count)

**Deferred:**
- File operations (copy, move, delete, rename)
- Search/filter
- Bookmarks
- Syntax highlighting in preview
- Plugin system
- Mouse support
- Config file / custom keybindings
- NativeAOT publishing (project will be structured for it)

## Project Structure

```
D:/source/lucaspimentel/wade/
├── wade.sln
├── src/
│   └── Wade/
│       ├── Wade.csproj
│       ├── Program.cs                  # Entry point, main loop
│       ├── App.cs                      # Application state + main loop orchestration
│       ├── Terminal/
│       │   ├── AnsiCodes.cs            # VT escape sequence constants/helpers
│       │   ├── ScreenBuffer.cs         # Double-buffered screen (char + style grid)
│       │   ├── InputReader.cs          # Console.ReadKey loop, key event translation
│       │   └── TerminalSetup.cs        # Enable VT processing, alternate screen, raw mode
│       ├── UI/
│       │   ├── Layout.cs              # 3-pane layout calculation (column widths based on terminal size)
│       │   ├── PaneRenderer.cs        # Renders a single pane (file list or preview)
│       │   └── StatusBar.cs           # Bottom status bar
│       └── FileSystem/
│           ├── DirectoryContents.cs   # Load + cache directory entries
│           └── FilePreview.cs         # Read first N lines of a text file for preview
├── tests/
│   └── Wade.Tests/
│       └── Wade.Tests.csproj
├── .gitignore
└── CLAUDE.md
```

## Architecture

### Main Loop (`App.cs`)

```
Initialize terminal (alternate screen, raw mode, hide cursor)
Load initial directory (CWD)
Loop:
  1. Calculate layout (pane widths from terminal size)
  2. Render all panes to screen buffer
  3. Flush buffer to terminal (diff-based to minimize writes)
  4. Wait for input (blocking Console.ReadKey)
  5. Handle input → update state
  6. If quit requested, break
Restore terminal (main screen, show cursor)
```

### Key Components

**ScreenBuffer** — 2D grid of cells (char + foreground + background). Double-buffered: write to back buffer, then diff against front buffer to emit only changed cells. This minimizes VT output and avoids flicker.

**AnsiCodes** — Static helper for cursor movement, colors (24-bit SGR), clear line, alternate screen buffer, etc.

**TerminalSetup** — On Windows: P/Invoke `SetConsoleMode` to enable `ENABLE_VIRTUAL_TERMINAL_PROCESSING` + `ENABLE_VIRTUAL_TERMINAL_INPUT`. Set `Console.OutputEncoding = UTF8`. Enter alternate screen buffer. On exit: restore original mode, leave alternate screen.

**InputReader** — Wraps `Console.ReadKey(intercept: true)`. Maps `ConsoleKeyInfo` to application actions (NavigateUp, NavigateDown, Open, Back, Quit).

**Layout** — Given terminal width/height, computes 3 column rectangles. Default split: 20% / 40% / 40% with 1-char borders between panes.

**DirectoryContents** — Enumerates a directory using `Directory.EnumerateFileSystemEntries()`. Returns sorted list of entries with name, size, type, last modified. Caches results per path.

**FilePreview** — Reads first ~100 lines of a file. Detects binary files (null bytes in first 512 bytes) and shows "[binary file]" instead.

## Build Order

### Step 1: Project scaffolding
- Create `wade.sln`, `Wade.csproj` (.NET 10 console app), `.gitignore`
- `CLAUDE.md` with project-specific instructions
- `Program.cs` with minimal entry point

### Step 2: Terminal setup + VT basics
- `TerminalSetup.cs` — enable VT mode, alternate screen, cleanup on exit
- `AnsiCodes.cs` — cursor movement, colors, clear
- Verify: app enters alternate screen, clears it, exits cleanly on 'q'

### Step 3: Screen buffer
- `ScreenBuffer.cs` — cell grid, write text at position, diff-flush
- Verify: can render text at arbitrary positions, only changed cells are re-emitted

### Step 4: Layout + single pane rendering
- `Layout.cs` — compute 3 pane rectangles
- `PaneRenderer.cs` — render a file list into a pane region
- `DirectoryContents.cs` — enumerate and sort directory
- Verify: current directory renders in the center pane

### Step 5: Navigation + miller columns
- `InputReader.cs` — key input loop
- `App.cs` — state management (current path, selected index, parent path)
- Wire up: arrow keys move selection, Enter/Right opens, Left/Backspace goes up
- Parent directory shows in left pane, preview in right pane
- Verify: can navigate the full file system

### Step 6: File preview + status bar
- `FilePreview.cs` — text preview with binary detection
- `StatusBar.cs` — current path, item count, selected item info
- Verify: selecting a text file shows preview, binary files show placeholder

## Verification

1. `dotnet build` succeeds with no warnings
2. `dotnet run` from the `src/Wade` directory:
   - Enters alternate screen buffer
   - Shows 3 panes: parent dir / current dir / preview
   - Arrow keys navigate, Enter opens directories, Backspace goes up
   - Selecting a text file shows its contents in the preview pane
   - `q` or `Escape` exits cleanly back to the normal terminal
   - Terminal resize redraws correctly
3. `dotnet test` passes (basic unit tests for DirectoryContents, FilePreview, Layout)
