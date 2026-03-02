# wade

A terminal file browser with Miller columns, written in .NET 10.

Inspired by [yazi](https://github.com/sxyazi/yazi), [broot](https://github.com/Canop/broot), and [ranger](https://github.com/ranger/ranger).

## Features

- **Miller columns** — three-pane layout: parent / current directory / preview
- **File preview** — displays first 100 lines of text files; shows file type description for binary files (via `file` command when available)
- **Directory preview** — shows contents of the selected directory
- **Drive navigation** — browse across drives on Windows (Backspace from a drive root)
- **Status bar** — current path, item count, file size
- **Minimal rendering** — raw VT/ANSI escape sequences, double-buffered with diff-based flush

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

## Keybindings

| Key | Action |
|---|---|
| Up / k | Move selection up |
| Down / j | Move selection down |
| Right / Enter | Open directory |
| Left / Backspace | Go to parent directory |
| Page Up / Page Down | Scroll by page |
| Home / End | Jump to first / last item |
| q / Escape | Quit |

## Building

```bash
dotnet build Wade.slnx
dotnet test Wade.slnx
```
