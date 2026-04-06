# wade

[![CI](https://github.com/lucaspimentel/wade/actions/workflows/ci.yml/badge.svg)](https://github.com/lucaspimentel/wade/actions/workflows/ci.yml) [![Release](https://github.com/lucaspimentel/wade/actions/workflows/release.yml/badge.svg)](https://github.com/lucaspimentel/wade/actions/workflows/release.yml)

A terminal file browser with Miller columns, written in C#.

Pre-built binaries support Windows and Linux. macOS is supported when built from source.

Inspired by [yazi](https://github.com/sxyazi/yazi), [broot](https://github.com/Canop/broot), and [ranger](https://github.com/ranger/ranger).

![wade screenshot](docs/screenshot.png)

## Features

### Layout & Navigation

- **Miller columns** — three-pane layout (parent / current / preview), each pane independently hideable
- **Column headers** — labeled header row with separator line in all panes; adapts to visible columns and drive view
- **Detail columns** — file size and modification date with responsive column tiers; async directory sizes (gated per drive type)
- **Drive navigation** — browse drives on Windows with file system, free/total space, and percent-full bar columns; SSD/HDD detection
- **File-type icons** — Nerd Fonts v3 glyphs per extension
- **Sort order** — by name, date, size, or extension (`s` to cycle, `S` to reverse)
- **Filter** — `/` to narrow entries in real-time
- **Go-to-path** — `Ctrl+G` with Tab autocomplete
- **File finder** — `Ctrl+F` recursive breadth-first search with fuzzy matching and highlighted results
- **Bookmarks** — `b` to open, `B` to toggle; persists in MRU order
- **Hidden files** — `.` to toggle; separate system files toggle on Windows

### Preview & Metadata

- **File preview** — text, hex, git diff, archive contents, images (Sixel), PDF (via `pdftopng`), MSI file listing, and Markdown (built-in Markdig renderer); multiple modes per file switchable via `p`
- **Expanded preview** — Right/Enter expands to full width; scrollable
- **Image preview** — Sixel graphics (Windows Terminal 1.22+, kitty, WezTerm, etc.)
- **File metadata** — structured metadata above preview and in properties overlay (`i`); format-specific providers for images (EXIF), executables (PE/.NET), Office docs, NuGet packages, archives, PDF, media files, and MSI installers
- **File properties** — `i` for scrollable overlay with timestamps, attributes, git status, and extracted metadata

### Git Integration

- **Git status** — color-coded files/dirs (modified, staged, untracked, conflict); status icons; branch name with ahead/behind counts in status bar
- **Git actions** — action palette (`Ctrl+P`) offers diff preview, stage/unstage, commit, push, pull, fetch

### File Operations

- **File actions** — open, rename, delete (with confirmation), copy/cut/paste with Windows clipboard interop; multi-select supported
- **Copy path** — `y` for absolute path, `Y` for git-relative path
- **Symlinks** — visual distinction with target display; `Ctrl+L` to create
- **Junction points** — detected and distinguished from symlinks with dedicated icon and "Junction" label (Windows)
- **Cloud files** — auto-detected OneDrive/Dropbox placeholders shown with cloud icon (Windows)

### Interface

- **Mouse support** — click, scroll, right-click context menu in all panes
- **Action palette** — `Ctrl+P` / `Ctrl+K` searchable command list with submenus
- **In-app config** — `,` opens hierarchical settings dialog
- **Status bar** — path, git branch, item count, file type, encoding, line endings, size, sort mode
- **Shell integration** — `Ctrl+T` opens terminal; `wd` wrapper for cd-on-exit (bash, zsh, fish, PowerShell)
- **Auto-refresh** — `FileSystemWatcher` with 300ms debounce
- **Terminal title** — shows current directory; clears on exit
- **Minimal rendering** — raw VT/ANSI, double-buffered with dirty-row tracking

## Requirements

- A terminal with VT/ANSI support (Windows Terminal, ConPTY-based terminals, or Unix/WSL terminals)

## Installation

### Scoop (Windows)

```powershell
scoop bucket add lucaspimentel https://github.com/lucaspimentel/scoop-bucket
scoop install wade
```

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
show_hidden_files = false
show_system_files = false       # Windows only; requires show_hidden_files
sort_mode = name                # name, modified, size, extension
sort_ascending = true
confirm_delete_enabled = true
parent_pane_enabled = true
preview_pane_enabled = true
size_column_enabled = true
date_column_enabled = true
column_headers_enabled = true
copy_symlinks_as_links_enabled = true
terminal_title_enabled = true
git_status_enabled = true
file_metadata_enabled = true
archive_metadata_enabled = true
dir_size_ssd_enabled = true
dir_size_hdd_enabled = false
dir_size_network_enabled = false
file_previews_enabled = true
image_previews_enabled = true
zip_preview_enabled = true
pdf_preview_enabled = true
pdf_metadata_enabled = true
markdown_preview_enabled = true
ffprobe_enabled = true
mediainfo_enabled = true
```

### CLI flags

```bash
wade --version                         # print version and exit
wade --show-config                     # print current config as JSON and exit
wade --config-file=/path/to/config     # use a custom config file
wade --help                            # print usage info and exit
wade -h                                # same as --help
```

## Shell integration (cd on exit)

Wade can change your shell's working directory when you quit with `q`. Source the appropriate wrapper function in your shell config:

**Bash / Zsh** — add to `~/.bashrc` or `~/.zshrc`:
```bash
source /path/to/wade/shell/wd.sh
```

**Fish** — copy to `~/.config/fish/functions/` or source in `config.fish`:
```fish
source /path/to/wade/shell/wd.fish
```

**PowerShell** — add to your `$PROFILE`:
```powershell
. /path/to/wade/shell/wd.ps1
```

Then use `wd` instead of `wade` to browse. Press `q` to quit and cd to the last directory, or `Q` to quit without changing directory.

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
| Right Click | Context menu |
| Scroll | Navigate up/down |
| Ctrl+R / F5 | Refresh |
| Ctrl+T | Open terminal here |
| . | Toggle hidden files |
| s | Cycle sort (name / time / size / ext) |
| S | Reverse sort direction |
| Ctrl+G | Go to path (Esc clears input, Up goes up a directory) |
| b | Open bookmarks dialog |
| B | Toggle current directory as bookmark |
| / | Filter |
| Esc (in filter) | Clear filter |
| Space | Toggle mark (multi-select) |
| o | Open with default app |
| F2 | Rename |
| Del | Delete (Recycle Bin on Windows) |
| Shift+Del | Delete permanently |
| c | Copy |
| x | Cut |
| v | Paste |
| y | Copy absolute path to clipboard |
| Y | Copy git-relative path to clipboard |
| n | Create new file |
| Shift+N | Create new directory |
| Ctrl+L | Create symlink to selected item |
| p | Change preview (when multiple preview modes available) |
| i | Properties |
| , | Configuration |
| Ctrl+F | Search / Find file |
| Ctrl+P / Ctrl+K | Action palette |
| ? | Show help |
| q / Escape | Quit |
| Q | Quit without cd |

## Building

```bash
dotnet build Wade.slnx
dotnet test Wade.slnx
```

## License

This project is licensed under the [MIT License](LICENSE).

## Development

This project was developed with [Claude Code](https://claude.ai/code) 🤖
