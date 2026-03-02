using Wade.FileSystem;
using Wade.Terminal;

namespace Wade.UI;

internal static class StatusBar
{
    private static readonly Color StatusFg = new(180, 180, 180);
    private static readonly Color StatusBg = new(30, 30, 50);
    private static readonly Color PathFg = new(80, 160, 255);

    public static void Render(
        ScreenBuffer buffer,
        Rect rect,
        string currentPath,
        int itemCount,
        int selectedIndex,
        FileSystemEntry? selectedEntry)
    {
        // Fill background
        var bgStyle = new CellStyle(StatusFg, StatusBg);
        for (int col = 0; col < rect.Width; col++)
            buffer.Put(rect.Top, rect.Left + col, ' ', bgStyle);

        // Left side: current path
        var pathStyle = new CellStyle(PathFg, StatusBg, Bold: true);
        buffer.WriteString(rect.Top, rect.Left + 1, currentPath, pathStyle, rect.Width / 2);

        // Right side: item count + selected info
        string right = itemCount > 0
            ? $"{selectedIndex + 1}/{itemCount}"
            : "empty";

        if (selectedEntry is not null)
        {
            string size = selectedEntry.IsDirectory
                ? "dir"
                : FormatSize(selectedEntry.Size);
            right = $"{size}  {right}";
        }

        int rightCol = rect.Width - right.Length - 1;
        if (rightCol > 0)
            buffer.WriteString(rect.Top, rect.Left + rightCol, right, bgStyle, right.Length);
    }

    private static string FormatSize(long bytes) => bytes switch
    {
        < 1024 => $"{bytes} B",
        < 1024 * 1024 => $"{bytes / 1024.0:F1} KB",
        < 1024 * 1024 * 1024 => $"{bytes / (1024.0 * 1024.0):F1} MB",
        _ => $"{bytes / (1024.0 * 1024.0 * 1024.0):F1} GB",
    };
}
