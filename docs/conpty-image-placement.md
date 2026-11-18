# ConPTY drops a cursor move while the cursor is hidden

ConPTY re-serializes what the child writes, and it holds back a cursor move it
has not flushed until it believes a visible cursor wants it. A program that
hides the cursor and then asks the terminal to place something at the cursor (an
iTerm2 or Sixel image, sent as `CSI <row>;<col>H` followed by the payload) gets
the payload drawn wherever the last paint ended, with its height clamped to the
one row the cursor sits on, and no cursor action in between.

wade hit this on 2026-11-18, first in WezTerm on Windows with the Linux build
inside a WSL pane, where no preview image appeared at all while the same files
rendered correctly in Windows Terminal. The WSL layer is incidental: the drop
happens in ConPTY, so the Windows build is affected the same way. wade's own
bytes were provably correct, with `ESC[<row>;<col>H` immediately before the
payload and a payload byte-identical to the source PNG.

## Evidence

WezTerm's log (`%USERPROFILE%\.local\share\wezterm\wezterm-gui.exe-log-<pid>.txt`)
reports the frame's actions arriving as `BSU`,
`OperatingSystemCommand(ITermProprietary(File(...)))`, `ESU` with **no cursor
action at all**, and the placement as `image is 20x1 cells (cell is 13x29)` at
`setting cells for y=35 x=[124..144]`, then `[145..150]` and `[142..147]`: the
last cell the previous frame painted, its height clamped to one row because the
cursor was on the last row. Four of four attaches in the logged run were on row
35, the status bar, which is the lowest changed row in every frame; only the
column moved.

Nothing about the child's write shape changed it. A payload that waits for a
frame that paints cells, a payload delayed until after the frame, a payload in
its own synchronized update, a payload with no bracket at all, and a payload
whose cursor move ends its own write: all of them landed on row 35.

## The variant that isolates it

A script that sends the same payload to row 10 column 40 once per terminal trait
found the one that matters. Run inside a WezTerm pane on Windows, it clears,
labels itself, paints 30 numbered lines, sends the payload, and holds the screen
for five seconds:

| Variant | Traits | Result |
| --- | --- | --- |
| A | baseline, cursor visible | correct: the image sits in line 10 |
| B | cursor hidden (`CSI ?25l`) | wrong: bottom-right corner |
| C | hidden, shown around the move | correct, cursor hidden again afterwards |
| D | alternate screen | correct |
| E | the cells write in `CSI ?2026h` / `CSI ?2026l`, move in its own write | correct |
| F | every wade trait at once: hidden cursor, bracket, move in its own write, alternate screen, a kilobytes-large cells write, raw mode | wrong: bottom-right corner |

B and F are the only two variants that hide the cursor, and they are the only
two that misplace the image. Showing the cursor around the move (C) fixes it
while leaving the cursor hidden for the rest of the screen.

## The wade-side fix

Commit `cd00ed0`, refactored into `frame::write_frame` by `4cb9283`: a
cursor-placed payload is split from its leading move and written as four writes
after the frame, `CSI ?25h`, the move, the payload, `CSI ?25l`. A payload that
carries its move inside a wrapper (tmux) still rides the frame, and kitty images
and exit-time deletes are untouched, since those do not depend on the cursor.

## What an upstream report would say

ConPTY holds a cursor move it has not yet flushed and emits it only when it
believes a visible cursor wants it, so a pass-through payload that the child asks
the terminal to place at the cursor lands at the last painted cell instead. A
child may legitimately hide the cursor (wade keeps it out of its own frame) and
still expect the move in front of the payload to be honored. Showing the cursor
around the payload is a workaround on the child side for behavior that looks
like a ConPTY defect.

Record which console host is in play before filing: no WezTerm directory with a
bundled `conpty.dll` or `OpenConsole.exe` was found under `C:\Program Files` or
`%LOCALAPPDATA%\Programs`, so it may be using the inbox `conhost.exe`
10.0.26100.1. An old bundled `OpenConsole.exe` would make this a WezTerm report
rather than a `microsoft/terminal` one.

## The script

Save it as `wade-conpty-variants.py` and run it in the target pane with any PNG:
`python3 wade-conpty-variants.py logo.png`. The original hard-coded a local logo
path; nothing else changed.

```python
#!/usr/bin/env python3
"""Show where a WezTerm (ConPTY) pane places an iTerm2 image, one variant per screen.

Run inside the target pane:  python3 wade-conpty-variants.py logo.png

Each screen clears, labels itself, paints 30 numbered lines, then sends the same
image payload a terminal should place at row 10, column 40. Watch each screen for
five seconds and note which labels put the logo at the bottom of the pane instead
of beside "line 10".

Whatever happens, the cursor is shown and the alternate screen is left on the way
out, so no variant can strand the pane with an invisible cursor.
"""
import base64
import os
import sys
import termios
import time
import tty

LOGO = sys.argv[1] if len(sys.argv) > 1 else "logo.png"
BSU = b"\x1b[?2026h"
ESU = b"\x1b[?2026l"
HIDE = b"\x1b[?25l"
SHOW = b"\x1b[?25h"
ALT_ON = b"\x1b[?1049h"
ALT_OFF = b"\x1b[?1049l"
AT = b"\x1b[10;40H"


def write_all(data):
    """One write per call, looping like write(2) makes necessary on a pty."""
    while data:
        data = data[os.write(1, data):]


def payload_bytes():
    with open(LOGO, "rb") as handle:
        png = handle.read()
    b64 = base64.b64encode(png).decode()
    return (
        "\x1b]1337;File=inline=1;size=%d;width=10;height=10;"
        "preserveAspectRatio=1;doNotMoveCursor=1:%s\x1b\\" % (len(png), b64)
    ).encode()


def screen(label, wide):
    out = bytearray(b"\x1b[2J\x1b[3J\x1b[H\x1b[1;1H")
    out += label.encode()
    for row in range(2, 32):
        out += ("\x1b[%d;1H%s" % (row, ("line %02d" % row).ljust(140 if wide else 8))).encode()
    return bytes(out)


def run(label, payload, hide=False, show_around_move=False, bracket=False,
        move_apart=False, alt=False, wide=False, raw=False, hold=5.0):
    tty_fd = os.open("/dev/tty", os.O_RDWR)
    saved = termios.tcgetattr(tty_fd) if raw else None
    try:
        if raw:
            tty.setraw(tty_fd)
        if alt:
            write_all(ALT_ON)
        if hide:
            write_all(HIDE)

        frame = screen(label, wide)
        if bracket:
            frame = BSU + frame + ESU

        if show_around_move:
            write_all(frame)
            write_all(SHOW)
            write_all(AT)
            write_all(payload)
            write_all(HIDE)
        else:
            if move_apart:
                write_all(frame)
                write_all(AT)
            else:
                write_all(frame + AT)
            write_all(payload)
            if hide:
                write_all(HIDE)

        time.sleep(hold)
    finally:
        # Leave the pane as it was found, whatever happened above
        write_all(SHOW)
        if alt:
            write_all(ALT_OFF)
        if saved is not None:
            try:
                termios.tcsetattr(tty_fd, termios.TCSANOW, saved)
            except termios.error:
                pass
        os.close(tty_fd)


def main():
    if not os.path.exists(LOGO):
        sys.stderr.write("missing %s\n" % LOGO)
        return 1

    payload = payload_bytes()
    variants = [
        ("A baseline, cursor visible", {}),
        ("B cursor hidden", {"hide": True}),
        ("C hidden, shown around the move", {"hide": True, "show_around_move": True}),
        ("D alternate screen", {"alt": True}),
        ("E cells write bracketed", {"bracket": True, "move_apart": True}),
        ("F every wade trait", {"hide": True, "bracket": True, "move_apart": True, "alt": True,
                                "wide": True, "raw": True}),
    ]
    try:
        for label, kwargs in variants:
            run(label, payload, **kwargs)
    finally:
        write_all(SHOW)
        write_all(ALT_OFF)
    return 0


if __name__ == "__main__":
    sys.exit(main())
```
