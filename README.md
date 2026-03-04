# wade

A terminal file browser with Miller columns, written in .NET 10.

Inspired by [yazi](https://github.com/sxyazi/yazi), [broot](https://github.com/Canop/broot), and [ranger](https://github.com/ranger/ranger).

## Features

- **Miller columns** — three-pane layout: parent / current directory / preview
- **File-type icons** — Nerd Fonts v3 glyphs per file extension (enabled by default; requires a Nerd Font)
- **Syntax highlighting** — per-token coloring for 15 languages (C#, JS/TS, Python, Go, Rust, Java, Shell, PowerShell, CSS, JSON, TOML, YAML, XML/HTML, Markdown) using a hand-rolled tokenizer; VS Code Dark+ inspired palette
- **File preview** — displays first 100 lines of text files; shows `[binary file]` for binary files
- **Image preview** — renders image thumbnails in the preview pane using Sixel graphics (requires a Sixel-capable terminal such as Windows Terminal v1.22+; disabled by default, enable with `--image-previews-enabled`)
- **Directory preview** — shows contents of the selected directory
- **Drive navigation** — browse across drives on Windows (Backspace from a drive root)
- **Status bar** — current path, item count, file type label (language name, "Text", or "Binary"), encoding (UTF-8, UTF-8 BOM, UTF-16 LE/BE), line endings (CRLF, LF, CR, Mixed), and file size
- **Mouse support** — click to select entries in any pane, scroll wheel to navigate; left/right pane clicks navigate directories
- **Minimal rendering** — raw VT/ANSI escape sequences, double-buffered with dirty-row tracking, cell diff, and style diffing

## Requirements

- A terminal with VT/ANSI support (Windows Terminal, ConPTY-based terminals, or any Unix terminal)

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

Settings are resolved in increasing priority: config file → environment variables → CLI flags.

### Config file

`~/.config/wade/config.toml`

```toml
show_icons_enabled = true
image_previews_enabled = false
```

### Environment variables

| Variable | Values |
|---|---|
| `WADE_SHOW_ICONS_ENABLED` | `true` / `false` |
| `WADE_IMAGE_PREVIEWS_ENABLED` | `true` / `false` |

### CLI flags

```bash
wade --show-icons-enabled             # enable icons
wade --no-show-icons-enabled          # disable icons
wade --show-icons-enabled=true        # same as above, key=value form
wade --image-previews-enabled         # enable image previews
wade --no-image-previews-enabled      # disable image previews
wade --image-previews-enabled=false   # same as above, key=value form
wade --show-config                    # print current config as JSON and exit
wade --config-file=/path/to/config    # use a custom config file
```

## Keybindings

| Key | Action |
|---|---|
| Up / k | Move selection up |
| Down / j | Move selection down |
| Right / l / Enter | Open directory |
| Left / h / Backspace | Go to parent directory |
| Page Up / Page Down | Scroll by page |
| Home / End | Jump to first / last item |
| Left Click | Select / Open |
| Scroll | Navigate up/down |
| Ctrl+R | Refresh |
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
