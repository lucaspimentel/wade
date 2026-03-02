using Wade.Terminal;

namespace Wade.UI;

internal static class HelpOverlay
{
    private static readonly Color BorderColor = new(100, 100, 120);
    private static readonly Color TitleColor = new(80, 160, 255);
    private static readonly Color KeyColor = new(220, 220, 100);
    private static readonly Color DescColor = new(200, 200, 200);
    private static readonly Color DimColor = new(120, 120, 140);
    private static readonly Color BgColor = new(20, 20, 35);

    private static readonly (string Key, string Description)[] Bindings =
    [
        ("Up / k",              "Move selection up"),
        ("Down / j",            "Move selection down"),
        ("Right / l / Enter",   "Open directory"),
        ("Left / h / Backspace","Go to parent directory"),
        ("Page Up / Page Down", "Scroll by page"),
        ("Home / End",          "Jump to first / last item"),
        ("?",                   "Show help"),
        ("q / Escape",          "Quit"),
    ];

    public static void Render(ScreenBuffer buffer, int screenWidth, int screenHeight)
    {
        // Box dimensions
        const int innerWidth = 46;  // content width (key col + sep + desc col)
        const int boxWidth = innerWidth + 4; // 2 border + 2 padding
        // rows: top border + title + separator + one per binding + blank + footer + bottom border
        int boxHeight = 3 + Bindings.Length + 2 + 1;

        int left = (screenWidth - boxWidth) / 2;
        int top = (screenHeight - boxHeight) / 2;

        var borderStyle = new CellStyle(BorderColor, BgColor, Dim: true);
        var titleStyle = new CellStyle(TitleColor, BgColor, Bold: true);
        var keyStyle = new CellStyle(KeyColor, BgColor);
        var descStyle = new CellStyle(DescColor, BgColor);
        var dimStyle = new CellStyle(DimColor, BgColor);
        var bgStyle = new CellStyle(DescColor, BgColor);

        // Draw top border: ┌──...──┐
        buffer.Put(top, left, '┌', borderStyle);
        for (int c = 1; c < boxWidth - 1; c++)
            buffer.Put(top, left + c, '─', borderStyle);
        buffer.Put(top, left + boxWidth - 1, '┐', borderStyle);

        int row = top + 1;

        // Title row
        FillRow(buffer, row, left, boxWidth, BgColor);
        buffer.Put(row, left, '│', borderStyle);
        buffer.Put(row, left + boxWidth - 1, '│', borderStyle);
        string title = "Help";
        int titleCol = left + (boxWidth - title.Length) / 2;
        buffer.WriteString(row, titleCol, title, titleStyle);
        row++;

        // Separator: ├──...──┤
        buffer.Put(row, left, '├', borderStyle);
        for (int c = 1; c < boxWidth - 1; c++)
            buffer.Put(row, left + c, '─', borderStyle);
        buffer.Put(row, left + boxWidth - 1, '┤', borderStyle);
        row++;

        // Keybinding rows
        foreach (var (key, desc) in Bindings)
        {
            FillRow(buffer, row, left, boxWidth, BgColor);
            buffer.Put(row, left, '│', borderStyle);
            buffer.Put(row, left + boxWidth - 1, '│', borderStyle);
            buffer.WriteString(row, left + 2, key, keyStyle, 22);
            buffer.WriteString(row, left + 2 + 22 + 2, desc, descStyle, innerWidth - 22 - 2);
            row++;
        }

        // Blank row
        FillRow(buffer, row, left, boxWidth, BgColor);
        buffer.Put(row, left, '│', borderStyle);
        buffer.Put(row, left + boxWidth - 1, '│', borderStyle);
        row++;

        // Footer row
        FillRow(buffer, row, left, boxWidth, BgColor);
        buffer.Put(row, left, '│', borderStyle);
        buffer.Put(row, left + boxWidth - 1, '│', borderStyle);
        string footer = "Press any key to close";
        int footerCol = left + (boxWidth - footer.Length) / 2;
        buffer.WriteString(row, footerCol, footer, dimStyle);
        row++;

        // Bottom border: └──...──┘
        buffer.Put(row, left, '└', borderStyle);
        for (int c = 1; c < boxWidth - 1; c++)
            buffer.Put(row, left + c, '─', borderStyle);
        buffer.Put(row, left + boxWidth - 1, '┘', borderStyle);
    }

    private static void FillRow(ScreenBuffer buffer, int row, int left, int width, Color bg)
    {
        var style = new CellStyle(null, bg);
        for (int c = 0; c < width; c++)
            buffer.Put(row, left + c, ' ', style);
    }
}
