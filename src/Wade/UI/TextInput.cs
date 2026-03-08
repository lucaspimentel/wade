using System.Text;
using Wade.Terminal;

namespace Wade.UI;

internal sealed class TextInput
{
    private readonly StringBuilder _buffer;
    private int _cursorPos;
    private int _scrollOffset;

    public TextInput(string initialValue = "")
    {
        _buffer = new StringBuilder(initialValue);
        _cursorPos = initialValue.Length;
    }

    public string Value => _buffer.ToString();

    public int CursorPosition => _cursorPos;

    public int ScrollOffset => _scrollOffset;

    public void InsertChar(char ch)
    {
        _buffer.Insert(_cursorPos, ch);
        _cursorPos++;
    }

    public void DeleteBackward()
    {
        if (_cursorPos <= 0)
        {
            return;
        }

        _cursorPos--;
        _buffer.Remove(_cursorPos, 1);
    }

    public void DeleteForward()
    {
        if (_cursorPos >= _buffer.Length)
        {
            return;
        }

        _buffer.Remove(_cursorPos, 1);
    }

    public void MoveCursorLeft()
    {
        if (_cursorPos > 0)
        {
            _cursorPos--;
        }
    }

    public void MoveCursorRight()
    {
        if (_cursorPos < _buffer.Length)
        {
            _cursorPos++;
        }
    }

    public void MoveCursorHome() => _cursorPos = 0;

    public void MoveCursorEnd() => _cursorPos = _buffer.Length;

    public void Clear()
    {
        _buffer.Clear();
        _cursorPos = 0;
        _scrollOffset = 0;
    }

    public void Render(ScreenBuffer buffer, int row, int col, int maxWidth, CellStyle style)
    {
        if (maxWidth <= 0)
        {
            return;
        }

        // Adjust scroll offset to keep cursor visible
        if (_cursorPos < _scrollOffset)
        {
            _scrollOffset = _cursorPos;
        }
        else if (_cursorPos >= _scrollOffset + maxWidth)
        {
            _scrollOffset = _cursorPos - maxWidth + 1;
        }

        var cursorStyle = style with { Inverse = true };
        int visibleEnd = Math.Min(_scrollOffset + maxWidth, _buffer.Length);

        // Render visible text
        int c = col;
        for (int i = _scrollOffset; i < visibleEnd; i++)
        {
            CellStyle cellStyle = (i == _cursorPos) ? cursorStyle : style;
            buffer.Put(row, c, _buffer[i], cellStyle);
            c++;
        }

        // Cursor at end of text — render inverse space
        if (_cursorPos >= _buffer.Length && _cursorPos >= _scrollOffset && (_cursorPos - _scrollOffset) < maxWidth)
        {
            buffer.Put(row, col + _cursorPos - _scrollOffset, ' ', cursorStyle);
            c = Math.Max(c, col + _cursorPos - _scrollOffset + 1);
        }

        // Fill remaining width with spaces to clear stale content
        int remaining = maxWidth - (c - col);
        if (remaining > 0)
        {
            buffer.FillRow(row, c, remaining, ' ', style);
        }
    }
}
