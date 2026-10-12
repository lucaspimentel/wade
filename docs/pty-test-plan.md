# PTY test scenarios plan

About 20 new scenarios for the PTY smoke tests, in three batches, starting
with input decoding, where three likely bugs are already visible in the code.

**Status:** Batch 1 is done. It confirmed the three input bugs (A1, A3, A4)
and found a fourth: text boxes drew wide characters one column wide. All
four are fixed. A3's mouse-burst case moves to C10. B9 is in Batch 3.

Batch 2 is done. D13 found the expected gap (signals left the terminal
raw), now fixed with handlers. The other scenarios passed without code
changes. C10 runs on Windows too: ConPTY passes SGR mouse input through.

## Context

`src/wade-rs/tests/pty_smoke.rs` runs the built `wade` binary in a
pseudo-terminal (`portable-pty`: a Unix PTY, or ConPTY on Windows). It writes
key bytes and reads the screen through `vt100`. It covers what the in-process
App tests skip: terminal setup and restore, raw input decoding, resize, the
main loop and exit. The five current tests run in under a second on Linux,
macOS and Windows CI.

The spike found two bugs, both fixed: the main loop dropped all but the last
queued key, and macOS hung at startup because `poll()` does not support
`/dev/tty`.

## Harness additions

These helpers come first; the scenarios below build on them.

| Helper | What it does | Used by |
| --- | --- | --- |
| `send_split(&[(&str, delay_ms)])` | Writes bytes in parts with pauses between them | A2, A3 |
| `send_paste(text)` | Wraps text in `ESC[200~` … `ESC[201~` | B8 |
| `click(row, col)`, `wheel(row, col, up/down)` | Build SGR mouse sequences | C10 |
| Query responder | Answers the startup capability queries as no terminal, kitty, Sixel or iTerm2 would | G19 |
| `raw_contains(bytes)`, `raw_since(mark)` | Search the recorded output | D11, D12, G19, G20 |
| `termios()` (Unix only) | Reads the terminal mode through `MasterPty::get_termios()`; adds `nix` as a dev-dependency at the version `portable-pty` already uses | D11 |
| `alive()` | wade is still running after a check | D14 |

## Scenarios

Twenty scenarios in seven groups. Three in group A are expected to find bugs,
read from the code in `src/input/vt_parser.rs` and `src/input/unix.rs`; C10
on Windows, D13 and G20 have uncertain outcomes.

### A. Input decoding (Unix)

- **A1. Alt+letter.** Alt+j arrives as `ESC j`. The parser reads a separate
  Esc, then `j`, so Alt+j moves the selection and the Esc clears marks.
  *Likely bug:* contradicts the modifier rule.
- **A2. Lone Esc vs an Esc sequence.** `ESC`, a pause over 50 ms, then `[B`
  is Esc plus the characters `[` and `B`; under 50 ms it is Down. Esc alone
  shows "Press q to quit".
- **A3. Input on a read boundary.** wade reads 64 bytes at a time and drops
  an escape sequence or UTF-8 character cut by the boundary (`parse_csi` and
  the UTF-8 branch). Type over 64 bytes of `é` into the filter without
  bracketed paste; send a burst of mouse wheel events. *Likely bug.*
- **A4. Characters beyond U+FFFF.** `decode_utf8_char` returns 16 bits, so an
  emoji typed into the filter or rename box is corrupted. *Likely bug.*
- **A5. Ctrl letters reach the app.** Ctrl+C clears the filter instead of
  killing wade; Ctrl+Z does not suspend it.

### B. Text entry (all OSes unless noted)

- **B6. Filter box.** `/`, type a name, the list narrows; Enter applies; Esc
  clears.
- **B7. Rename.** `r`, Backspace over the name, type a new one, Enter: renamed
  on disk. Covers the 0x7F and 0x08 Backspace bytes on Unix and ConPTY's
  translation on Windows.
- **B8. Bracketed paste (Unix).** Pasting into the filter inserts the text; a
  pasted newline does not act as Enter; a paste over 64 bytes arrives whole.
- **B9. Delete with confirmation.** `D` then `y` deletes permanently. On
  Linux, `d` with `XDG_DATA_HOME` in the test folder moves the file to that
  Trash and writes its `.trashinfo`. macOS Trash goes through Finder and stays
  manual.

### C. Mouse

- **C10. Click and wheel.** A click selects a row and the preview follows; the
  wheel scrolls a long folder. Unix uses SGR mouse sequences. Whether ConPTY
  turns SGR mouse input into the mouse events wade reads is unknown; the first
  CI run decides whether this is Unix-only.

### D. Terminal state and lifecycle

- **D11. Raw mode and exit sequence (Unix).** After `q` the terminal mode
  matches the start (canonical mode, echo). The output ends with: leave the
  alternate screen, restore the title, show the cursor, mouse and bracketed
  paste off.
- **D12. Window title.** Set to the current folder while running; the saved
  title is restored on exit; never set with `terminal_title_enabled = false`.
- **D13. Termination by signal (Unix).** SIGTERM or SIGHUP while running. wade
  installs no signal handlers and release builds abort on panic, so the
  terminal likely stays raw in the alternate screen. *Likely gap.*
- **D14. Tiny and extreme sizes.** Resize to 20×5, 1×1, then back: wade stays
  alive, redraws and exits 0 on `q`. Covers ConPTY's resize path.

### E. Rendering

- **E15. Wide characters.** Files `日本語.txt` and `😀.md`: the pane border
  stays in one column on every row, so wade's width table agrees with
  `vt100`'s. On Windows this also checks ConPTY's re-rendering.
- **E16. Long names, small window.** Names end with `…` and never wrap into
  the next pane.

### F. Background work

- **F17. External file change.** The test creates, then deletes, a file in the
  listed folder; the list updates within the timeout (notify;
  ReadDirectoryChanges on Windows).
- **F18. Git status.** `git init` in the fixture and change a file; the status
  marker appears. Skipped without `git`.

### G. Capability detection and images

- **G19. Unix.** With kitty, Sixel or iTerm2 query answers, a small PNG sends
  that protocol's image data; with no answers, no image data and a text
  fallback.
- **G20. Windows.** `WT_SESSION=1` selects Sixel; Sixel image data is sent. If
  ConPTY doesn't pass Sixel through, the test only checks that wade doesn't
  hang.

## Order and conventions

1. **Batch 1:** the harness additions, A1–A5 and B6–B8. Each bug found gets
   its own fix commit, with the test written first.
2. **Batch 2:** D11–D14, C10 and E15.
3. **Batch 3:** F17–F18, G19–G20, E16 and B9.

- Unix-only tests use `#[cfg(unix)]`. A test ConPTY can't support gets a
  comment saying why, never a silent skip.
- Each test checks a visible result or the bytes wade sends, never timing
  alone.
- Every new test is checked by breaking the code it covers and confirming the
  test fails.
- Run time: the five current tests take under 1 s; about 20 more should add
  3–5 s per OS, mostly Esc pauses and the file-change wait.

Docs to update as tests land:

- `TODO.md`: the "Still untested" item and the manual Windows and macOS lists
  lose each item that becomes automated.
- `src/wade-rs/KNOWN_DEVIATIONS.md`: an entry for each behavior change from a
  bug fix, such as how Alt+letter is read.

## Decisions

- **Bugs found in Batch 1 are fixed in the same pass:** a failing test
  first, then the fix in its own commit.
- **Alt+letter on Unix:** `ESC` followed by a printable character in the same
  read (or within the 50 ms Esc wait) is one key event with `alt = true`, as
  Windows reports it. Under the modifier rule, Alt+j then does nothing.
- **Characters beyond U+FFFF:** `KeyEvent.key_char` widens from `u16` to
  `char`, one event per character on every OS. Windows already turns a typed
  surrogate pair into a paste (frozen fixture
  `tests/golden/input/004-single-char-and-surrogate`), so its output is
  unchanged.
- **Input split across reads:** the Unix reader keeps an incomplete trailing
  escape sequence or UTF-8 character and joins it with the next read.
- **SIGTERM and SIGHUP (D13):** handlers set a flag; the main loop restores
  the terminal (raw mode, alternate screen, title, cursor, mouse, bracketed
  paste) and exits with 128 + the signal number.
- **Mouse on Windows (C10):** if ConPTY drops SGR mouse input, the test stays
  Unix-only with a comment, and Windows mouse stays in the manual checklist.
- **Scope of this pass:** Batch 1 only (harness helpers, A1–A5, B6–B8 and the
  fixes they need), then push, CI and report before Batch 2.
