using Wade.Terminal;

namespace Wade.UI;

internal static class HelpOverlay
{
    private static readonly Color KeyColor = new(220, 220, 100);
    private static readonly Color DescColor = new(200, 200, 200);

    private static readonly (string Key, string Description)[] Bindings =
    [
        ("Up / k",              "Move selection up"),
        ("Down / j",            "Move selection down"),
        ("Right / l / Enter",   "Open directory / expand preview"),
        ("Left / h / Backspace","Go back / collapse preview"),
        ("Page Up / Page Down", "Scroll by page"),
        ("Home / End",          "Jump to first / last item"),
        ("Left Click",          "Select / Open"),
        ("Scroll",              "Navigate up/down"),
        ("Ctrl+R",              "Refresh"),
        ("Space",               "Toggle mark (multi-select)"),
        (".",                   "Toggle hidden files"),
        ("s",                   "Cycle sort mode"),
        ("S",                   "Reverse sort direction"),
        ("g",                   "Go to path"),
        ("/",                   "Search / filter"),
        ("Esc (in search)",     "Clear filter"),
        ("o",                   "Open with default app"),
        ("Ctrl+T",              "Open terminal here"),
        ("F2",                  "Rename"),
        ("Del",                 "Delete (Recycle Bin on Windows)"),
        ("Shift+Del",           "Delete permanently"),
        ("c / Ctrl+C",          "Copy"),
        ("x / Ctrl+X",          "Cut"),
        ("p / v",               "Paste"),
        ("Shift+N",             "Create new file"),
        ("F7",                  "Create new directory"),
        (",",                   "Configuration"),
        ("?",                   "Show help"),
        ("q / Escape",          "Quit"),
        ("Q",                   "Quit without cd"),
    ];

    public static void Render(ScreenBuffer buffer, int screenWidth, int screenHeight)
    {
        const int ColumnWidth = 46;
        const int ColumnGap = 4;
        const int ContentWidth = ColumnWidth * 2 + ColumnGap;
        int contentHeight = (Bindings.Length + 1) / 2;

        var content = DialogBox.Render(
            buffer, screenWidth, screenHeight,
            ContentWidth, contentHeight,
            title: "Help",
            footer: "Press any key to close");

        var keyStyle = new CellStyle(KeyColor, DialogBox.BgColor);
        var descStyle = new CellStyle(DescColor, DialogBox.BgColor);

        for (int row = 0; row < contentHeight; row++)
        {
            // Left column
            var (key, desc) = Bindings[row];
            int y = content.Top + row;
            buffer.WriteString(y, content.Left, key, keyStyle, 22);
            buffer.WriteString(y, content.Left + 24, desc, descStyle, ColumnWidth - 24);

            // Right column
            int rightIndex = row + contentHeight;

            if (rightIndex < Bindings.Length)
            {
                var (key2, desc2) = Bindings[rightIndex];
                int rightCol = content.Left + ColumnWidth + ColumnGap;
                buffer.WriteString(y, rightCol, key2, keyStyle, 22);
                buffer.WriteString(y, rightCol + 24, desc2, descStyle, ColumnWidth - 24);
            }
        }
    }
}
