using System.Globalization;
using System.Text;
using Wade.FileSystem;
using Wade.Terminal;
using Wade.UI;

namespace Wade.Tests;

/// <summary>
/// Executes renderer golden-frame scenario files (.scn format) from
/// tests/golden/renderer/ and captures ScreenBuffer flush output. The same
/// format is consumed by src/wade-rs/tests/renderer_frames.rs.
/// </summary>
internal static class RendererFixtureRunner
{
    public static string Run(string scenarioPath)
    {
        int width = 80, height = 25;
        var buffer = default(ScreenBuffer)!;
        var layout = new Layout();
        var lists = new Dictionary<string, List<FileSystemEntry>>(StringComparer.Ordinal);
        List<FileSystemEntry>? currentList = null;
        string paletteTitle = "";
        int paletteDepth = 1;
        int paletteSelected = 0;
        List<ActionMenuItem>? currentPaletteItems = null;
        ConfigDialogState? configState = null;
        BookmarkStore? bookmarkStore = null;
        TextInput? bookmarkInput = null;
        int bookmarkSelected = 0;
        int bookmarkScroll = 0;
        List<ActionMenuItem>? ctxItems = null;
        int ctxAnchorRow = 0, ctxAnchorCol = 0;
        Notification? notification = null;
        var flushes = new List<string>();
        var sb = new StringBuilder();

        foreach (string rawLine in File.ReadAllLines(scenarioPath))
        {
            string line = rawLine.Trim();
            if (line.Length == 0 || line.StartsWith('#'))
            {
                continue;
            }

            if (line.StartsWith("platform "))
            {
                // platform windows: skip the whole scenario on other OSes
                // (scenarios that render platform-dependent content)
                string[] ptokens = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (ptokens[1] == "windows" && !OperatingSystem.IsWindows())
                {
                    return "PLATFORM-SKIPPED";
                }

                continue;
            }

            string[] tokens = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            switch (tokens[0])
            {
                case "size":
                    width = ParseInt(tokens[1]);
                    height = ParseInt(tokens[2]);
                    buffer = new ScreenBuffer(width, height);
                    break;
                case "list":
                    currentList = [];
                    lists[tokens[1]] = currentList;
                    break;
                case "dir":
                    currentList!.Add(MakeEntry(tokens[1], isDirectory: true, size: 0, tokens.Length > 2 ? ParseDate(tokens[2]) : default));
                    break;
                case "file":
                    currentList!.Add(MakeEntry(tokens[1], isDirectory: false, size: ParseLong(tokens[2]), tokens.Length > 3 ? ParseDate(tokens[3]) : default));
                    break;
                case "drive":
                    currentList!.Add(MakeDriveEntry(tokens[1], tokens[2], Unquote(tokens[3]), ParseLong(tokens[4]), ParseLong(tokens[5])));
                    break;
                case "endlist":
                    currentList = null;
                    break;
                case "layout":
                    layout.Calculate(width, height,
                        previewPaneEnabled: tokens[1] is "3pane" or "preview",
                        parentPaneEnabled: tokens[1] is "3pane" or "parent");
                    break;
                case "headers":
                {
                    var flags = tokens.Skip(2).ToHashSet(StringComparer.Ordinal);
                    PaneRenderer.RenderColumnHeaders(
                        buffer, PaneRect(layout, tokens[1]),
                        showIcons: flags.Contains("icons"),
                        showSize: flags.Contains("size"),
                        showDate: flags.Contains("date"),
                        isDriveView: flags.Contains("drive"),
                        hasStatusCol: flags.Contains("status"));
                    break;
                }
                case "filelist":
                {
                    string pane = tokens[1];
                    string listName = tokens[2];
                    int selected = ParseInt(tokens[3]);
                    int scroll = ParseInt(tokens[4]);
                    bool active = tokens[5] == "active";
                    var flags = tokens.Skip(6).ToHashSet(StringComparer.Ordinal);
                    PaneRenderer.RenderFileList(
                        buffer, PaneRect(layout, pane), lists[listName], selected, scroll, active,
                        showIcons: flags.Contains("icons"),
                        showSize: flags.Contains("size"),
                        showDate: flags.Contains("date"),
                        markedPaths: [],
                        gitStatuses: null,
                        dirSizes: null,
                        isDriveView: flags.Contains("drive"));
                    break;
                }
                case "message":
                {
                    string text = Unquote(line.Split(' ', 3)[2]);
                    PaneRenderer.RenderMessage(buffer, PaneRect(layout, tokens[1]), text);
                    break;
                }
                case "notify":
                {
                    string text = Unquote(line.Split(' ', 3)[2]);
                    notification = new Notification(
                        text,
                        tokens[1] switch
                        {
                            "success" => NotificationKind.Success,
                            "error" => NotificationKind.Error,
                            _ => NotificationKind.Info,
                        },
                        Environment.TickCount64);
                    break;
                }
                case "statusbar":
                {
                    // statusbar PATH SELECTED ITEM_COUNT [list LISTNAME]
                    string path = Unquote(tokens[1]);
                    int selected = ParseInt(tokens[2]);
                    int itemCount = ParseInt(tokens[3]);
                    FileSystemEntry? selectedEntry = tokens.Length > 5 && tokens[4] == "list"
                        ? lists[tokens[5]].ElementAtOrDefault(selected)
                        : null;
                    StatusBar.Render(
                        buffer, layout.StatusBar, path, itemCount, selected,
                        selectedEntry,
                        fileTypeLabel: null, encoding: null, lineEnding: null,
                        notification: notification,
                        markedCount: 0,
                        SortMode.Name, true, 0, false, null, null);
                    notification = null;
                    break;
                }
                case "help":
                    HelpOverlay.Render(buffer, width, height);
                    break;
                case "confirmdlg":
                {
                    // confirmdlg "TITLE" "MESSAGE"
                    string title = Unquote(tokens[1]);
                    string message = Unquote(line.Split(' ', 3)[2]);
                    ConfirmDialog.Render(buffer, width, height, title == "-" ? null : title, message);
                    break;
                }
                case "textinputdlg":
                {
                    // textinputdlg "TITLE" "VALUE"
                    string title = Unquote(tokens[1]);
                    string value = Unquote(line.Split(' ', 3)[2]);
                    TextInputDialog.Render(buffer, width, height, title == "-" ? null : title, new TextInput(value));
                    break;
                }
                case "gotopathdlg":
                {
                    // gotopathdlg "VALUE" ["SUGGESTION"]
                    string rest = line["gotopathdlg ".Length..];
                    string value = Unquote(FirstQuotedSpan(rest));
                    string? suggestion = null;
                    int afterFirst = rest.IndexOf('"', 1);
                    if (afterFirst >= 0 && rest.IndexOf('"', afterFirst + 1) >= 0)
                    {
                        int secondStart = rest.IndexOf('"', afterFirst + 1);
                        suggestion = rest[(secondStart + 1)..rest.IndexOf('"', secondStart + 1)];
                    }

                    GoToPathDialog.Render(buffer, width, height, new TextInput(value), suggestion);
                    break;
                }
                case "palette":
                {
                    // palette "TITLE" DEPTH SELECTED
                    string rest = line["palette ".Length..];
                    paletteTitle = Unquote(FirstQuotedSpan(rest));
                    string after = rest[(rest.IndexOf('"', 1) + 1)..];
                    string[] nums = after.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                    paletteDepth = ParseInt(nums[0]);
                    paletteSelected = ParseInt(nums[1]);
                    currentPaletteItems = [];
                    break;
                }
                case "pitem":
                {
                    // pitem "Label" "Shortcut" [sub]
                    string rest = line["pitem ".Length..];
                    int firstEnd = rest.IndexOf('"', 1);
                    int secondStart = rest.IndexOf('"', firstEnd + 1);
                    int secondEnd = rest.IndexOf('"', secondStart + 1);
                    string label = rest[1..firstEnd];
                    string shortcut = rest[(secondStart + 1)..secondEnd];
                    currentPaletteItems!.Add(new ActionMenuItem { Label = label, Shortcut = shortcut, Action = AppAction.None });
                    break;
                }
                case "endpalette":
                {
                    var level = new ActionMenuLevel(paletteTitle, [.. currentPaletteItems!]);
                    level.SelectedIndex = paletteSelected;
                    ActionPaletteDialog.Render(buffer, width, height, level, paletteDepth);
                    currentPaletteItems = null;
                    break;
                }
                case "searchbar":
                {
                    // searchbar ACTIVE "FILTER"
                    bool active = tokens[1] == "active";
                    string filter = Unquote(line.Split(' ', 3)[2]);
                    SearchBar.Render(buffer, layout.CenterPane, active, filter, active ? new TextInput(filter) : null);
                    break;
                }
                case "textinput":
                {
                    // textinput ROW COL WIDTH "VALUE"
                    int row = ParseInt(tokens[1]);
                    int col = ParseInt(tokens[2]);
                    int w = ParseInt(tokens[3]);
                    string value = Unquote(line.Split(' ', 5)[4]);
                    new TextInput(value).Render(buffer, row, col, w, new CellStyle(new Terminal.Color(200, 200, 200), null));
                    break;
                }
                case "flush":
                    buffer.Serialize(sb);
                    flushes.Add(sb.ToString());
                    break;
                case "configdlg":
                    configState = ConfigDialogState.FromConfig(new WadeConfig());
                    break;
                case "csel":
                    configState!.SelectedIndex = ParseInt(tokens[1]);
                    break;
                case "ctoggle":
                    configState!.ToggleSelected();
                    break;
                case "ccycle":
                    if (tokens[1] == "next")
                    {
                        configState!.CycleNextSelected();
                    }
                    else
                    {
                        configState!.CyclePrevSelected();
                    }

                    break;
                case "cnav":
                    if (tokens[1] == "up")
                    {
                        configState!.MoveUp();
                    }
                    else if (tokens[1] == "down")
                    {
                        configState!.MoveDown();
                    }
                    else if (tokens[1] == "left")
                    {
                        configState!.CyclePrevSelected();
                    }
                    else
                    {
                        configState!.CycleNextSelected();
                    }

                    break;
                case "configrender":
                    ConfigDialog.Render(buffer, width, height, configState!);
                    break;
                case "bmark":
                    bookmarkStore ??= new BookmarkStore(Path.Combine(Path.GetTempPath(), $"wade-fixture-{Guid.NewGuid():N}"));
                    bookmarkStore.Add(Unquote(line.Split(' ', 2)[1]));
                    break;
                case "bfilter":
                    bookmarkInput = new TextInput();
                    bookmarkInput.InsertString(Unquote(line.Split(' ', 2)[1]));
                    break;
                case "bsel":
                    bookmarkSelected = ParseInt(tokens[1]);
                    break;
                case "bscroll":
                    bookmarkScroll = ParseInt(tokens[1]);
                    break;
                case "bookmarkrender":
                {
                    List<string> filtered = [];
                    string filter = bookmarkInput?.Value ?? "";
                    foreach (string bookmark in bookmarkStore!.Bookmarks)
                    {
                        if (string.IsNullOrEmpty(filter) || bookmark.Contains(filter, StringComparison.OrdinalIgnoreCase))
                        {
                            filtered.Add(bookmark);
                        }
                    }

                    BookmarksDialog.Render(buffer, width, height, filtered, bookmarkSelected, bookmarkScroll, bookmarkInput);
                    break;
                }
                case "ctxmenu":
                {
                    // ctxmenu ANCHORROW ANCHORCOL
                    ctxAnchorRow = ParseInt(tokens[1]);
                    ctxAnchorCol = ParseInt(tokens[2]);
                    ctxItems = [];
                    break;
                }
                case "citem":
                {
                    // citem "Label" "Shortcut"
                    string rest = line["citem ".Length..];
                    int firstEnd = rest.IndexOf('"', 1);
                    int secondStart = rest.IndexOf('"', firstEnd + 1);
                    int secondEnd = rest.IndexOf('"', secondStart + 1);
                    string label = rest[1..firstEnd];
                    string shortcut = rest[(secondStart + 1)..secondEnd];
                    ctxItems!.Add(new ActionMenuItem { Label = label, Shortcut = shortcut, Action = AppAction.None });
                    break;
                }
                case "endctxmenu":
                {
                    var state = new ContextMenuState([.. ctxItems!], ctxAnchorRow, ctxAnchorCol);
                    ContextMenuRenderer.Render(buffer, width, height, state);
                    ctxItems = null;
                    break;
                }
                default:
                    throw new InvalidDataException($"Unknown op '{tokens[0]}' in {scenarioPath}");
            }
        }

        if (buffer is null)
        {
            throw new InvalidDataException($"Scenario {scenarioPath} has no size op");
        }

        var golden = new StringBuilder();
        for (int i = 0; i < flushes.Count; i++)
        {
            golden.Append("=== flush ").Append(i).Append(" ===").Append('\n');
            golden.Append(flushes[i]).Append('\n');
        }

        return golden.ToString();
    }

    private static Rect PaneRect(Layout layout, string pane) => pane switch
    {
        "left" => layout.LeftPane,
        "right" => layout.RightPane,
        _ => layout.CenterPane,
    };

    private static FileSystemEntry MakeEntry(string name, bool isDirectory, long size, DateTime modified) => new(
        name, @"C:\fixture\" + name, IsDirectory: isDirectory, Size: size, LastModified: modified,
        LinkTarget: null, IsBrokenSymlink: false, IsDrive: false);

    private static FileSystemEntry MakeDriveEntry(string name, string format, string label, long free, long total) => new(
        name, name + @"\", IsDirectory: true, 0, default,
        LinkTarget: null, IsBrokenSymlink: false, IsDrive: true,
        DriveFormat: format, DriveLabel: label, DriveFreeSpace: free, DriveTotalSize: total);

    private static string Unquote(string s) => s.Trim('"');

    private static string FirstQuotedSpan(string s)
    {
        int start = s.IndexOf('"');
        int end = s.IndexOf('"', start + 1);
        return s[(start + 1)..end];
    }

    private static int ParseInt(string token) => int.Parse(token, CultureInfo.InvariantCulture);

    private static long ParseLong(string token) => long.Parse(token, CultureInfo.InvariantCulture);

    private static DateTime ParseDate(string token) =>
        DateTime.ParseExact(token, "yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture);
}
