using Wade.Terminal;

namespace Wade.UI;

internal static class ProgressOverlay
{
    private static readonly CellStyle MessageStyle = new(new Color(200, 200, 200), DialogBox.BgColor);

    public static void Render(
        ScreenBuffer buffer,
        int screenWidth,
        int screenHeight,
        string operationLabel)
    {
        string message = $"{operationLabel}...";
        int contentWidth = Math.Max(message.Length, 20);

        Rect content = DialogBox.Render(
            buffer, screenWidth, screenHeight,
            contentWidth, 1,
            footer: "Esc to cancel");

        buffer.WriteString(content.Top, content.Left, message, MessageStyle, contentWidth);
    }
}
