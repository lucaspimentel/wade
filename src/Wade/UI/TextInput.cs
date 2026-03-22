using System.Text;
using Wade.Terminal;

namespace Wade.UI;

internal sealed class TextInput
{
    private readonly StringBuilder _buffer;

    public TextInput(string initialValue = "")
    {
        _buffer = new StringBuilder(initialValue);
        CursorPosition = initialValue.Length;
    }

    public string Value => _buffer.ToString();

    public int CursorPosition { get; private set; }

    public int ScrollOffset { get; private set; }

    public void InsertChar(char ch)
    {
        _buffer.Insert(CursorPosition, ch);
        CursorPosition++;
    }

    public void DeleteBackward()
    {
        if (CursorPosition <= 0)
        {
            return;
        }

        CursorPosition--;
        _buffer.Remove(CursorPosition, 1);
    }

    public void DeleteForward()
    {
        if (CursorPosition >= _buffer.Length)
        {
            return;
        }

        _buffer.Remove(CursorPosition, 1);
    }

    public void MoveCursorLeft()
    {
        if (CursorPosition > 0)
        {
            CursorPosition--;
        }
    }

    public void MoveCursorRight()
    {
        if (CursorPosition < _buffer.Length)
        {
            CursorPosition++;
        }
    }

    public void MoveCursorHome() => CursorPosition = 0;

    public void MoveCursorEnd() => CursorPosition = _buffer.Length;

    public void Clear()
    {
        _buffer.Clear();
        CursorPosition = 0;
        ScrollOffset = 0;
    }

    public void Render(ScreenBuffer buffer, int row, int col, int maxWidth, CellStyle style)
    {
        if (maxWidth <= 0)
        {
            return;
        }

        // Adjust scroll offset to keep cursor visible
        if (CursorPosition < ScrollOffset)
        {
            ScrollOffset = CursorPosition;
        }
        else if (CursorPosition >= ScrollOffset + maxWidth)
        {
            ScrollOffset = CursorPosition - maxWidth + 1;
        }

        CellStyle cursorStyle = style with { Inverse = true };
        int visibleEnd = Math.Min(ScrollOffset + maxWidth, _buffer.Length);

        // Render visible text
        int c = col;
        for (int i = ScrollOffset; i < visibleEnd; i++)
        {
            CellStyle cellStyle = i == CursorPosition ? cursorStyle : style;
            buffer.Put(row, c, _buffer[i], cellStyle);
            c++;
        }

        // Cursor at end of text — render inverse space
        if (CursorPosition >= _buffer.Length && CursorPosition >= ScrollOffset && CursorPosition - ScrollOffset < maxWidth)
        {
            buffer.Put(row, col + CursorPosition - ScrollOffset, ' ', cursorStyle);
            c = Math.Max(c, col + CursorPosition - ScrollOffset + 1);
        }

        // Fill remaining width with spaces to clear stale content
        int remaining = maxWidth - (c - col);
        if (remaining > 0)
        {
            buffer.FillRow(row, c, remaining, ' ', style);
        }
    }
}
