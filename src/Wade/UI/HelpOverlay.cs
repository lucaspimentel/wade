using Wade.Terminal;

namespace Wade.UI;

internal static class HelpOverlay
{
    private static readonly Color KeyColor = new(220, 220, 100);
    private static readonly Color DescColor = new(200, 200, 200);
    private static readonly Color SectionColor = new(180, 180, 220);

    public static void Render(ScreenBuffer buffer, int screenWidth, int screenHeight)
    {
        const int ContentWidth = 46;
        const int contentHeight = 10;

        var content = DialogBox.Render(
            buffer, screenWidth, screenHeight,
            ContentWidth, contentHeight,
            title: "Help",
            footer: "Press any key to close");

        var keyStyle = new CellStyle(KeyColor, DialogBox.BgColor);
        var descStyle = new CellStyle(DescColor, DialogBox.BgColor);
        var sectionStyle = new CellStyle(SectionColor, DialogBox.BgColor);

        int y = content.Top;
        int left = content.Left;

        buffer.WriteString(y++, left, "Ctrl+P", keyStyle, 8);
        buffer.WriteString(y - 1, left + 8, "Open action list (all hotkeys)", descStyle, ContentWidth - 8);

        y++; // blank line

        buffer.WriteString(y++, left, "Navigation", sectionStyle, ContentWidth);

        buffer.WriteString(y, left, "↑↓  or  j/k", keyStyle, 16);
        buffer.WriteString(y++, left + 16, "Move selection", descStyle, ContentWidth - 16);

        buffer.WriteString(y, left, "←→  or  h/l", keyStyle, 16);
        buffer.WriteString(y++, left + 16, "Open / go back", descStyle, ContentWidth - 16);

        buffer.WriteString(y, left, "PgUp/PgDn", keyStyle, 16);
        buffer.WriteString(y++, left + 16, "Scroll by page", descStyle, ContentWidth - 16);

        buffer.WriteString(y, left, "Click / Scroll", keyStyle, 16);
        buffer.WriteString(y++, left + 16, "Mouse navigation", descStyle, ContentWidth - 16);

        y++; // blank line

        buffer.WriteString(y, left, "/", keyStyle, 16);
        buffer.WriteString(y++, left + 16, "Filter", descStyle, ContentWidth - 16);

        buffer.WriteString(y, left, "Ctrl+F", keyStyle, 16);
        buffer.WriteString(y++, left + 16, "Find files", descStyle, ContentWidth - 16);

        buffer.WriteString(y, left, "Esc", keyStyle, 16);
        buffer.WriteString(y, left + 16, "Clear filter / cancel", descStyle, ContentWidth - 16);
    }
}
