# TODO

## Bugs (open)

(none)

---

## Refactoring

(none)

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

### Drive type detection

Detect whether a drive is SSD, HDD, or network. Some features (like directory size in the file browser) should behave differently based on drive speed.

- On Windows, use WMI or `DeviceIoControl` / `IOCTL_STORAGE_QUERY_PROPERTY` to distinguish SSD vs HDD; `DriveInfo.DriveType == Network` for network drives
- `DriveInfo` is already used in `DirectoryContents.GetDriveEntries()` and `PropertiesOverlay`
- If feasible, add per-drive-type settings for showing directory size inline in the file list:
  - Show directory size for SSD (default true)
  - Show directory size for HDD (default false)
  - Show directory size for network drives (default false)
- Must be NativeAOT-compatible (no reflection-heavy WMI wrappers)

### Format-specific metadata providers

#### Backlog

- **Font files** (`.ttf`, `.otf`, `.woff2`) — font family, style, weight, glyph count. Parse OpenType/TrueType `name` and `head` tables.
- **OpenDocument** (`.odt`, `.ods`, `.odp`) — title, author, dates, page/sheet count. Extract from `meta.xml` inside the ODF zip archive (similar to Office OOXML approach).
- **EPUB** (`.epub`) — title, author, publisher, language, identifier. Extract from `content.opf` metadata inside the zip archive.

### Zip — other archive formats

Support additional archive formats in the preview pane (`.tar`, `.gz`, `.tar.gz`). Zip preview is already implemented via `System.IO.Compression.ZipFile`.

---

## Backlog

### Replace hand-coded P/Invoke with Microsoft.Windows.CsWin32

Consider using the [CsWin32](https://github.com/microsoft/CsWin32) source generator to auto-generate P/Invoke signatures instead of hand-coding `[DllImport]` declarations. Files with manual P/Invoke:
- `src/Wade/FileSystem/SystemClipboard.cs` — user32 (clipboard), kernel32 (GlobalAlloc/Lock/Free), shell32 (DragQueryFileW)
- `src/Wade/FileSystem/Shell32.cs` — shell32 (SHFileOperation)
- `src/Wade/Terminal/TerminalSetup.cs` — kernel32 (GetStdHandle, Get/SetConsoleMode)
- `src/Wade/Terminal/WindowsInputSource.cs` — kernel32 (ReadConsoleInput, WaitForSingleObject)
- `src/Wade/Terminal/LibC.cs` — libc (open, read, poll, tcgetattr, tcsetattr, cfmakeraw) — Unix only, CsWin32 won't cover these

### Investigate oversized image preview rendering

Look into cases where images are too large to fit in the preview pane. `ImagePreview.Load()` already scales down to fit pane pixel dimensions and avoids upscaling (`scale = 1.0` cap), but verify that the sixel output doesn't exceed pane bounds after encoding — especially in expanded preview mode or with unusual cell pixel sizes.
