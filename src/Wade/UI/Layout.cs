namespace Wade.UI;

internal readonly record struct Rect(int Left, int Top, int Width, int Height)
{
    public int Right => Left + Width;
    public int Bottom => Top + Height;

    public (int Row, int Col) CenterContent(int contentWidthCells, int contentHeightCells)
    {
        int col = Left + Math.Max(0, (Width - contentWidthCells) / 2);
        int row = Top + Math.Max(0, (Height - contentHeightCells) / 2);
        return (row, col);
    }
}

internal sealed class Layout
{
    public Rect LeftPane { get; private set; }
    public Rect CenterPane { get; private set; }
    public Rect RightPane { get; private set; }
    public Rect ExpandedPane { get; private set; }
    public Rect StatusBar { get; private set; }

    public void Calculate(int terminalWidth, int terminalHeight, bool previewPaneEnabled = true)
    {
        // Reserve 1 row for status bar at the bottom
        int contentHeight = terminalHeight - 1;
        if (contentHeight < 1)
        {
            contentHeight = 1;
        }

        if (previewPaneEnabled)
        {
            // Split: 20% / 40% / 40% with 1-char border (vertical line) between panes
            int usableWidth = terminalWidth - 2; // 2 border columns
            int leftWidth = Math.Max(1, usableWidth * 20 / 100);
            int centerWidth = Math.Max(1, usableWidth * 40 / 100);
            int rightWidth = Math.Max(1, usableWidth - leftWidth - centerWidth);

            LeftPane = new Rect(0, 0, leftWidth, contentHeight);
            CenterPane = new Rect(leftWidth + 1, 0, centerWidth, contentHeight);
            RightPane = new Rect(leftWidth + 1 + centerWidth + 1, 0, rightWidth, contentHeight);
        }
        else
        {
            // No preview pane: 25% / 75% with 1 border column
            int usableWidth = terminalWidth - 1; // 1 border column
            int leftWidth = Math.Max(1, usableWidth * 25 / 100);
            int centerWidth = Math.Max(1, usableWidth - leftWidth);

            LeftPane = new Rect(0, 0, leftWidth, contentHeight);
            CenterPane = new Rect(leftWidth + 1, 0, centerWidth, contentHeight);
            RightPane = new Rect(0, 0, 0, 0);
        }

        ExpandedPane = new Rect(0, 0, terminalWidth, contentHeight);
        StatusBar = new Rect(0, terminalHeight - 1, terminalWidth, 1);
    }
}
