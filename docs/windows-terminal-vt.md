# Windows Terminal and ConPTY: VT query behavior

Findings from the query-only VT input spike (October 2026). Sources: the
`microsoft/terminal` source at tags `v1.21.10351.0`, `v1.22.12111.0`,
`v1.23.20211.0` and `main` (e90f9a5), plus two Windows CI runs of a PTY test.

## Who answers queries

| | WT 1.21 and older (and the console built into Windows, including on CI's `windows-latest`) | WT 1.22+ |
|---|---|---|
| Inside ConPTY | The console host answers what it implements and forwards the rest | The console host never answers: `ConhostInternalGetSet::ReturnResponse` (`src/host/outputStream.cpp`) returns early in VT IO mode ("ConPTY should not respond to requests. That's the job of the terminal."); every query reaches the terminal |
| DA1 (`CSI c`) | Answered locally, no Sixel: `?61;6;7;21;22;23;24;28;32;42c` (seen on CI) | Windows Terminal: `?61;4;6;7;14;21;22;23;24;28;32;42c` (`4` = Sixel; `;52` added with clipboard write), `AdaptDispatch::DeviceAttributes` |
| Cell size (`CSI 16t`) | Not implemented; forwarded to the terminal (seen on CI) | Always `6;20;10t` |
| Text area in pixels (`CSI 14t`) | Not checked | Text area in characters x the virtual cell size (`main`) |
| XTVERSION (`CSI >0q`) | Forwarded (seen on CI), not answered by WT | Not answered (no handler on `main`) |

## The cell size is virtual

`ReportCharacterCellSize` returns `SixelParser::CellSizeForLevel()`: 10x20
(VT240/VT340, the default) or 9x20 (conformance level 1, VT125). It is not the
font's pixel size. Windows Terminal lays Sixel images out on this virtual grid
and scales them to the real cell, so 10x20 is the right size for image layout
whatever the font. The source says why: Sixel apps need "the virtual pixel
size, as used in the Sixel graphics emulation, and not the physical pixel size
(which should be of no concern to applications)".

wade therefore uses 10x20 under `WT_SESSION` without asking. Before, it used
8x16, so images came out at 80% of the intended size and full-screen centering
was off by the same factor.

## Reading replies on Windows

- With `ENABLE_VIRTUAL_TERMINAL_INPUT` off (wade's mode), replies arrive as
  ordinary key records: `ESC`, `[`, `6`, ... would be read as key presses.
- With it on, `ReadConsoleInputW` returns them as key-down records with the
  characters in `uChar.UnicodeChar`. The spike switched VT input on for the
  queries only, then back, and this worked through ConPTY on CI.
- Reply order: WT 1.22+ answers in the order asked. With the old console host,
  the locally answered DA1 comes back first and forwarded replies later, so
  DA1 can't mark the end of the replies there. If queries are ever added, send
  the one whose reply ends the read last, or a reply can arrive after VT input
  is off and be read as keys.

## Other notes

- Windows Terminal handles kitty keyboard protocol flags (`CSI ? u` and
  friends), with special handling when the console host runs as ConPTY
  (`AdaptDispatch`, `_api.IsConPTY()`). Relevant only to a full VT input switch.
- Full VT input would lose detail that `ReadConsoleInput` gives (key-up,
  some modifier combinations); win32-input-mode (`CSI ?9001h`, spec
  `doc/specs/#4999 - Improved keyboard handling in Conpty.md`) encodes whole
  key records as `CSI Vk;Sc;Uc;Kd;Cs;Rc _` to keep it.
- The console host still needs the `ReadConsoleInput` path, so VT input
  would add a second Windows input path, not replace the first.
- ConPTY always writes `CSI ?9001h` to the terminal at startup, so Windows
  Terminal already sends keys as win32-input-mode sequences and ConPTY turns
  them back into key records: `ReadConsoleInput` gets full key detail as is.
  A full VT input switch would only add bracketed paste, so it is not planned
  (TODO.md, "Windows Terminal VT input mode").
