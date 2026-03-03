# Raw Input Pipeline

## Context

The wade main loop blocks on `Console.ReadKey` (`InputReader.cs:22`), which freezes the entire app until a keypress arrives. This prevents resize detection between keypresses, blocks future async preview loading, and makes mouse support impossible. We need to replace it with a background input reader that posts events to a queue, with a platform abstraction layer for OS-specific input APIs.

## Scope

- Keyboard + resize events only (mouse event type defined but not wired up)
- Cross-platform: Win32 `ReadConsoleInput` on Windows, raw VT byte parsing on Unix
- `ENABLE_VIRTUAL_TERMINAL_INPUT` remains **disabled**

## Architecture

```
┌─────────────────────┐     ┌──────────────────────────┐
│  WindowsInputSource │     │    UnixInputSource        │
│  (ReadConsoleInput)  │     │  (raw stdin + VT parser)  │
└────────┬────────────┘     └────────────┬─────────────┘
         │ IInputSource.ReadNext()       │
         └──────────┬────────────────────┘
                    ▼
         ┌─────────────────────┐
         │   InputPipeline     │
         │ (background Thread) │
         │ BlockingCollection  │
         └────────┬────────────┘
                  ▼
         ┌─────────────────────┐
         │   App.Run() loop    │
         │ Take → dispatch →   │
         │ render              │
         └─────────────────────┘
```

## New Files

### 1. `src/Wade/Terminal/InputEvent.cs` — Event types

```csharp
abstract record InputEvent
sealed record KeyEvent(ConsoleKey Key, char KeyChar, bool Shift, bool Alt, bool Control) : InputEvent
sealed record MouseEvent(MouseButton Button, int Row, int Col, bool IsRelease) : InputEvent
sealed record ResizeEvent(int Width, int Height) : InputEvent
enum MouseButton { Left, Middle, Right, ScrollUp, ScrollDown, None }
```

`MouseEvent` defined now but unused until mouse support is wired up.

### 2. `src/Wade/Terminal/IInputSource.cs` — Platform abstraction

```csharp
interface IInputSource : IDisposable
    InputEvent? ReadNext(CancellationToken ct)  // blocking, one event at a time
```

### 3. `src/Wade/Terminal/WindowsInputSource.cs` — Win32 implementation

- P/Invoke: `ReadConsoleInput`, `WaitForSingleObject`, `GetNumberOfConsoleInputEvents`
- P/Invoke structs: `INPUT_RECORD`, `KEY_EVENT_RECORD`, `WINDOW_BUFFER_SIZE_RECORD` (mouse struct defined but unused for now)
- Cancellation: poll with `WaitForSingleObject(stdinHandle, 100ms)` in a loop, check `CancellationToken` between waits
- Maps `KEY_EVENT_RECORD` (keyDown only) → `KeyEvent`, `WINDOW_BUFFER_SIZE_RECORD` → `ResizeEvent`
- Skips `MOUSE_EVENT`, `MENU_EVENT`, `FOCUS_EVENT` records

### 4. `src/Wade/Terminal/UnixInputSource.cs` — VT sequence parser

- Reads raw bytes from `Console.OpenStandardInput()`
- State machine parser:
  - Plain ASCII (0x20-0x7E excl 0x1B) → `KeyEvent`
  - ESC (0x1B) → wait ~50ms for more bytes; standalone = Escape key, otherwise parse sequence
  - CSI (`ESC [`) → parse params + final byte → map to `KeyEvent` (arrows, Home/End, PgUp/PgDn, Delete, F-keys)
  - Control chars (0x01-0x1A) → Ctrl+letter `KeyEvent`
  - UTF-8 multibyte → decode to char for `KeyEvent.KeyChar`
- Resize: `PosixSignalRegistration.Create(PosixSignal.SIGWINCH, ...)` → post `ResizeEvent`

### 5. `src/Wade/Terminal/InputPipeline.cs` — Orchestrator

- Owns a `BlockingCollection<InputEvent>` (capacity 64) and a dedicated background `Thread`
- Background thread loops: `IInputSource.ReadNext()` → `_queue.Add()`
- Exposes `Take(CancellationToken)` and `TryTake(out InputEvent)` for main loop consumption
- Factory: `static IInputSource CreatePlatformSource()` — returns `WindowsInputSource` or `UnixInputSource`

## Modified Files

### 6. `src/Wade/Terminal/TerminalSetup.cs`

- Add `ENABLE_WINDOW_INPUT` (0x0008) to Windows input mode so `ReadConsoleInput` delivers resize events
- Expose `StdinHandle` property for `WindowsInputSource` to use (or let it call `GetStdHandle` itself)

### 7. `src/Wade/Terminal/InputReader.cs`

- Keep `AppAction` enum unchanged
- Replace `Read()` method with `MapKey(KeyEvent) → AppAction` (same switch body, different input type)

### 8. `src/Wade/Terminal/AnsiCodes.cs`

- Add mouse reporting constants (for future use):
  - `EnableMouseReporting`, `DisableMouseReporting`
  - `EnableSgrMouseMode`, `DisableSgrMouseMode`

### 9. `src/Wade/App.cs`

Replace the main loop:

```
Old: render → Console.ReadKey (blocks) → dispatch → clamp
New: render → pipeline.Take() (blocks on queue) → drain remaining → dispatch → clamp
```

- Remove inline resize polling (`Console.WindowWidth`/`Console.WindowHeight` checks) — resize comes as `ResizeEvent`
- Create `InputPipeline` in `Run()` alongside `TerminalSetup`
- Event dispatch: `ResizeEvent` → resize buffer/layout, `KeyEvent` → `InputReader.MapKey()` → existing switch

## Tests

New test file: `tests/Wade.Tests/InputReaderTests.cs`

Test `InputReader.MapKey(KeyEvent)` — the pure key-to-action mapping function. Uses `[Theory]`/`[InlineData]` following project conventions.

- Arrow keys / vim keys → NavigateUp/Down/Open/Back
- Enter/Backspace → Open/Back
- Escape/Q → Quit
- PageUp/PageDown/Home/End → corresponding actions
- `?` char → ShowHelp
- Unrecognized key → None
- Modifier combinations (Ctrl+C, etc.) → None (no mapping yet)

New test file: `tests/Wade.Tests/InputPipelineTests.cs`

Test `InputPipeline` with a hand-written `FakeInputSource : IInputSource` that yields scripted events.

- Single event: `Take()` returns the event posted by the source
- Multiple events: events arrive in order
- Drain: `TryTake` returns `false` when queue is empty
- Disposal: pipeline shuts down cleanly when disposed

New test file: `tests/Wade.Tests/VtParserTests.cs`

Test the Unix VT sequence parser in isolation (extract the parser as a static/testable method that takes a byte span and returns parsed events).

- Plain ASCII chars → correct `KeyEvent` (char + ConsoleKey)
- CSI sequences: `ESC [ A` → UpArrow, `ESC [ B` → DownArrow, `ESC [ C` → RightArrow, `ESC [ D` → LeftArrow
- CSI with params: `ESC [ 5 ~` → PageUp, `ESC [ 6 ~` → PageDown, `ESC [ H` → Home, `ESC [ F` → End
- Standalone ESC byte → Escape key
- Control chars: 0x01 → Ctrl+A, 0x03 → Ctrl+C, etc.
- Enter (0x0D) → Enter key
- Tab (0x09) → Tab key
- Backspace (0x7F) → Backspace key
- UTF-8 multibyte → correct decoded char

## Implementation Order

1. `InputEvent.cs` — pure types, no deps
2. `IInputSource.cs` — interface only
3. `InputPipeline.cs` — depends on 1, 2
4. `WindowsInputSource.cs` — depends on 1, 2
5. `UnixInputSource.cs` (with testable VT parser) — depends on 1, 2
6. `TerminalSetup.cs` — add `ENABLE_WINDOW_INPUT`
7. `AnsiCodes.cs` — add mouse constants
8. `InputReader.cs` — `Read()` → `MapKey(KeyEvent)`
9. `App.cs` — new main loop
10. Tests: `InputReaderTests.cs`, `InputPipelineTests.cs`, `VtParserTests.cs`

Steps 4 and 5 are independent and can be done in parallel.

## Verification

1. `dotnet build Wade.slnx` — must compile clean
2. `dotnet test Wade.slnx` — all tests pass (existing + new)
3. `dotnet run --project src/Wade` on Windows — navigate dirs, arrow keys, PgUp/PgDn, Home/End, ?, Esc all work as before
4. Resize the terminal window — should update layout immediately (no keypress required, unlike current behavior)
5. Verify NativeAOT: `dotnet publish src/Wade -c Release -r win-x64` succeeds
