# wade

A terminal file browser with Miller columns, written in .NET 10.

Inspired by [yazi](https://github.com/sxyazi/yazi), [broot](https://github.com/Canop/broot), and [ranger](https://github.com/ranger/ranger).

## Features

- **Miller columns** — three-pane layout: parent / current directory / preview
- **File-type icons** — Nerd Fonts v3 glyphs per file extension (enabled by default; requires a Nerd Font)
- **Syntax highlighting** — per-token coloring for 15 languages (C#, JS/TS, Python, Go, Rust, Java, Shell, PowerShell, CSS, JSON, TOML, YAML, XML/HTML, Markdown) using a hand-rolled tokenizer; VS Code Dark+ inspired palette
- **File preview** — displays first 100 lines of text files; shows `[binary file]` for binary files
- **Image preview** — renders image thumbnails in the preview pane using Sixel graphics (auto-detects Sixel support; works with Windows Terminal v1.22+, kitty, WezTerm, and other Sixel-capable terminals; enabled by default; disable with `image_previews_enabled = false` in config)
- **Expanded preview** — press Right/Enter on a file to expand the preview to full terminal width; scroll with Up/Down/J/K/PageUp/PageDown/Home/End/mouse wheel; press Left/Escape to collapse back to 3-pane view
- **Directory preview** — shows contents of the selected directory
- **Drive navigation** — browse across drives on Windows (Backspace from a drive root)
- **Detail columns** — file size and modification date in the center pane; columns adapt responsively as the terminal narrows (full date → date only → short date → size only → name only)
- **Status bar** — current path, item count, file type label (language name, "Text", or "Binary"), encoding (UTF-8, UTF-8 BOM, UTF-16 LE/BE), line endings (CRLF, LF, CR, Mixed), file size, and sort indicator
- **Hidden files toggle** — dotfiles and system-hidden files are hidden by default; press `.` to toggle visibility at runtime, or set `show_hidden_files = true` in config
- **Sort order** — sort by name (default), modification time, size, or extension; press `s` to cycle modes, `S` to reverse direction; directories always listed first; configurable via `sort_mode` and `sort_ascending` in config; current sort mode shown in status bar
- **Go-to-path bar** — press `g` to type an arbitrary path and jump to it; Tab auto-completes from the filesystem; navigates to directories or selects files
- **Search / filter** — press `/` to type a query that narrows visible entries in real-time; Enter persists the filter, Escape clears it; filter auto-clears on directory change
- **Multi-select** — press Space to mark/unmark entries for future bulk operations; marks are path-based and survive scrolling and filtering; marked entries highlighted with a distinct background; mark count shown in status bar
- **File actions** — open files with default app, rename, delete, copy/cut/paste with internal clipboard; multi-select supported for delete/copy/cut
- **Mouse support** — click to select entries in any pane, scroll wheel to navigate; left/right pane clicks navigate directories
- **In-app configuration** — press `,` to open a config dialog; toggle booleans, cycle sort modes, and save changes directly to the config file
- **Shell integration** — press `Ctrl+T` to open a new terminal in the current directory; use shell wrapper functions (`wd`) to cd to the final directory on exit (bash, zsh, fish, PowerShell wrappers provided)
- **Minimal rendering** — raw VT/ANSI escape sequences, double-buffered with dirty-row tracking, cell diff, and style diffing

## Requirements

- A terminal with VT/ANSI support (Windows Terminal, ConPTY-based terminals, or Unix/WSL terminals)

## Installation

### From GitHub releases (no build tools needed)

```powershell
./install-remote.ps1
```

Or as a one-liner:

```powershell
irm https://raw.githubusercontent.com/lucaspimentel/wade/main/install-remote.ps1 | iex
```

### From source

Requires [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0).

```powershell
./install-local.ps1
```

Both scripts install to `~/.local/bin/wade`. Ensure that directory is in your `PATH`.

## Usage

```bash
wade              # open in current directory
wade C:\Users     # open in a specific directory
```

## Configuration

### Config file

`~/.config/wade/config.toml`

```toml
show_icons_enabled = true
image_previews_enabled = true
show_hidden_files = false
sort_mode = name                # name, modified, size, extension
sort_ascending = true
```

### CLI flags

```bash
wade --show-config                    # print current config as JSON and exit
wade --config-file=/path/to/config    # use a custom config file
wade --cwd-file=/tmp/cwd              # write final directory to file on exit (for shell integration)
wade --help                           # print usage info and exit
wade -h                               # same as --help
```

## Keybindings

| Key | Action |
|---|---|
| Up / k | Move selection up |
| Down / j | Move selection down |
| Right / l / Enter | Open directory / expand preview |
| Left / h / Backspace | Go back / collapse preview |
| Page Up / Page Down | Scroll by page |
| Home / End | Jump to first / last item |
| Left Click | Select / Open |
| Scroll | Navigate up/down |
| Ctrl+R | Refresh |
| Ctrl+T | Open terminal here |
| . | Toggle hidden files |
| s | Cycle sort (name / time / size / ext) |
| S | Reverse sort direction |
| g | Go to path (Esc clears input, Up goes up a directory) |
| / | Search / filter |
| Esc (in search) | Clear filter |
| Space | Toggle mark (multi-select) |
| o | Open with default app |
| F2 | Rename |
| Del | Delete (Recycle Bin on Windows) |
| Shift+Del | Delete permanently |
| c / Ctrl+C | Copy |
| x / Ctrl+X | Cut |
| p / v | Paste |
| Shift+N | Create new file |
| F7 | Create new directory |
| , | Configuration |
| ? | Show help |
| q / Escape | Quit |

## Building

```bash
dotnet build Wade.slnx
dotnet test Wade.slnx
```

## License

This project is licensed under the [MIT License](LICENSE).

## Development

This project was developed with [Claude Code](https://claude.ai/code) 🤖
