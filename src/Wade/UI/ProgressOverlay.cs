using Wade.Terminal;

namespace Wade.UI;

internal static class ProgressOverlay
{
    private static readonly CellStyle LabelStyle = new(new Color(200, 200, 200), DialogBox.BgColor);
    private static readonly CellStyle BarFilledStyle = new(new Color(80, 160, 255), new Color(80, 160, 255));
    private static readonly CellStyle BarEmptyStyle = new(new Color(60, 60, 80), DialogBox.BgColor);

    public static void Render(
        ScreenBuffer buffer,
        int screenWidth,
        int screenHeight,
        string operationLabel,
        int filesProcessed,
        int totalFiles,
        string currentFile)
    {
        int contentWidth = Math.Min(50, screenWidth - 8);

        if (contentWidth < 20)
        {
            return;
        }

        Rect content = DialogBox.Render(
            buffer, screenWidth, screenHeight,
            contentWidth, 4,
            title: operationLabel,
            footer: "Esc to cancel");

        // Line 1: "N of M files"
        string progress = totalFiles > 0
            ? $"{filesProcessed} of {totalFiles} items"
            : $"{filesProcessed} items";

        buffer.WriteString(content.Top, content.Left, progress, LabelStyle, contentWidth);

        // Line 2: progress bar
        int barWidth = contentWidth;

        if (barWidth > 0 && totalFiles > 0)
        {
            int filled = totalFiles > 0
                ? (int)((long)filesProcessed * barWidth / totalFiles)
                : 0;

            filled = Math.Clamp(filled, 0, barWidth);

            for (int i = 0; i < barWidth; i++)
            {
                CellStyle style = i < filled ? BarFilledStyle : BarEmptyStyle;
                buffer.Put(content.Top + 1, content.Left + i, '\u2588', style);
            }
        }

        // Line 3: blank

        // Line 4: current file (truncated)
        if (currentFile.Length > contentWidth)
        {
            currentFile = "\u2026" + currentFile[(currentFile.Length - contentWidth + 1)..];
        }

        var fileStyle = new CellStyle(new Color(140, 140, 160), DialogBox.BgColor, Dim: true);
        buffer.WriteString(content.Top + 3, content.Left, currentFile, fileStyle, contentWidth);
    }
}
