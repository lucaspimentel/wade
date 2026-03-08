using Wade.Terminal;

namespace Wade.UI;

/// <summary>
/// Reusable dialog box chrome: border, background fill, title, footer, centering.
/// Callers render their own content into the returned content <see cref="Rect"/>.
/// </summary>
internal static class DialogBox
{
    internal static readonly Color BorderColor = new(100, 100, 120);
    internal static readonly Color TitleColor = new(80, 160, 255);
    internal static readonly Color FooterColor = new(120, 120, 140);
    internal static readonly Color BgColor = new(20, 20, 35);

    /// <summary>
    /// Renders box chrome centered on screen and returns the content area rect.
    /// </summary>
    /// <param name="buffer">Screen buffer to draw into.</param>
    /// <param name="screenWidth">Terminal width in columns.</param>
    /// <param name="screenHeight">Terminal height in rows.</param>
    /// <param name="contentWidth">Desired width of the content area (inside borders/padding).</param>
    /// <param name="contentHeight">Number of content rows.</param>
    /// <param name="title">Optional title displayed centered below the top border.</param>
    /// <param name="footer">Optional footer displayed centered above the bottom border.</param>
    /// <returns>A <see cref="Rect"/> describing the content area inside the box.</returns>
    public static Rect Render(
        ScreenBuffer buffer,
        int screenWidth,
        int screenHeight,
        int contentWidth,
        int contentHeight,
        string? title = null,
        string? footer = null)
    {
        // Box dimensions: content + 2 border chars + 2 padding chars
        int boxWidth = contentWidth + 4;

        // Height: top border + (title + separator if title) + content rows + (blank + footer if footer) + bottom border
        int boxHeight = 1 + contentHeight + 1; // top border + content + bottom border
        if (title is not null)
        {
            boxHeight += 2;  // title row + separator
        }

        if (footer is not null)
        {
            boxHeight += 2; // blank row + footer row
        }

        int left = (screenWidth - boxWidth) / 2;
        int top = (screenHeight - boxHeight) / 2;

        var borderStyle = new CellStyle(BorderColor, BgColor, Dim: true);
        var titleStyle = new CellStyle(TitleColor, BgColor, Bold: true);
        var footerStyle = new CellStyle(FooterColor, BgColor);

        int row = top;

        // Top border: ┌──...──┐
        DrawHorizontalBorder(buffer, row, left, boxWidth, '┌', '─', '┐', borderStyle);
        row++;

        // Title row + separator
        if (title is not null)
        {
            FillRow(buffer, row, left, boxWidth);
            buffer.Put(row, left, '│', borderStyle);
            buffer.Put(row, left + boxWidth - 1, '│', borderStyle);
            int titleCol = left + (boxWidth - title.Length) / 2;
            buffer.WriteString(row, titleCol, title, titleStyle);
            row++;

            // Separator: ├──...──┤
            DrawHorizontalBorder(buffer, row, left, boxWidth, '├', '─', '┤', borderStyle);
            row++;
        }

        // Content area starts here
        int contentTop = row;
        int contentLeft = left + 2; // border + 1 padding

        for (int i = 0; i < contentHeight; i++)
        {
            FillRow(buffer, row, left, boxWidth);
            buffer.Put(row, left, '│', borderStyle);
            buffer.Put(row, left + boxWidth - 1, '│', borderStyle);
            row++;
        }

        // Footer
        if (footer is not null)
        {
            // Blank row
            FillRow(buffer, row, left, boxWidth);
            buffer.Put(row, left, '│', borderStyle);
            buffer.Put(row, left + boxWidth - 1, '│', borderStyle);
            row++;

            // Footer row
            FillRow(buffer, row, left, boxWidth);
            buffer.Put(row, left, '│', borderStyle);
            buffer.Put(row, left + boxWidth - 1, '│', borderStyle);
            int footerCol = left + (boxWidth - footer.Length) / 2;
            buffer.WriteString(row, footerCol, footer, footerStyle);
            row++;
        }

        // Bottom border: └──...──┘
        DrawHorizontalBorder(buffer, row, left, boxWidth, '└', '─', '┘', borderStyle);

        return new Rect(contentLeft, contentTop, contentWidth, contentHeight);
    }

    private static void DrawHorizontalBorder(
        ScreenBuffer buffer, int row, int left, int width,
        char leftCap, char fill, char rightCap, CellStyle style)
    {
        buffer.Put(row, left, leftCap, style);
        for (int c = 1; c < width - 1; c++)
        {
            buffer.Put(row, left + c, fill, style);
        }

        buffer.Put(row, left + width - 1, rightCap, style);
    }

    private static void FillRow(ScreenBuffer buffer, int row, int left, int width)
    {
        var style = new CellStyle(null, BgColor);
        for (int c = 0; c < width; c++)
        {
            buffer.Put(row, left + c, ' ', style);
        }
    }
}
