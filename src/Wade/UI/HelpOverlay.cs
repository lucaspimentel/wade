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
        ("/",                   "Search / filter"),
        ("Esc (in search)",     "Clear filter"),
        ("?",                   "Show help"),
        ("q / Escape",          "Quit"),
    ];

    public static void Render(ScreenBuffer buffer, int screenWidth, int screenHeight)
    {
        const int ContentWidth = 46;
        int contentHeight = Bindings.Length;

        var content = DialogBox.Render(
            buffer, screenWidth, screenHeight,
            ContentWidth, contentHeight,
            title: "Help",
            footer: "Press any key to close");

        var keyStyle = new CellStyle(KeyColor, DialogBox.BgColor);
        var descStyle = new CellStyle(DescColor, DialogBox.BgColor);

        for (int i = 0; i < Bindings.Length; i++)
        {
            var (key, desc) = Bindings[i];
            int row = content.Top + i;
            buffer.WriteString(row, content.Left, key, keyStyle, 22);
            buffer.WriteString(row, content.Left + 24, desc, descStyle, ContentWidth - 24);
        }
    }
}
