using System.Buffers;
using System.Text;

namespace Wade.Terminal;

internal sealed class ScreenBuffer
{
    private static readonly Stream StdOut = Console.OpenStandardOutput();

    private Cell[] _front;
    private Cell[] _back;
    private int _width;
    private int _height;

    // Dirty-row bitfield: 1 bit per row, packed into ulong[]
    private ulong[] _dirtyRows;

    private char[] _writeBuffer = new char[4096];

    public int Width => _width;
    public int Height => _height;

    public ScreenBuffer(int width, int height)
    {
        _width = width;
        _height = height;
        _front = new Cell[width * height];
        _back = new Cell[width * height];
        _dirtyRows = new ulong[(height + 63) / 64];
        Array.Fill(_front, Cell.Empty);
        Array.Fill(_back, Cell.Empty);
    }

    public void Resize(int width, int height)
    {
        _width = width;
        _height = height;
        _front = new Cell[width * height];
        _back = new Cell[width * height];
        _dirtyRows = new ulong[(height + 63) / 64];
        Array.Fill(_front, Cell.Empty);
        Array.Fill(_back, Cell.Empty);
    }

    public void Clear()
    {
        Array.Fill(_back, Cell.Empty);
        // Don't clear dirty bits — rows that changed from previous frame must still be flushed
    }

    public void Put(int row, int col, Rune rune, CellStyle style)
    {
        if (row < 0 || row >= _height || col < 0 || col >= _width)
        {
            return;
        }

        int idx = row * _width + col;
        _back[idx] = new Cell(rune, style);

        // Wide characters occupy 2 terminal columns; store a continuation marker in the next cell
        if (RuneWidth.GetWidth(rune) == 2 && col + 1 < _width)
        {
            _back[idx + 1] = Cell.WideContinuation;
        }

        _dirtyRows[row >> 6] |= 1UL << (row & 63);
    }

    public void Put(int row, int col, char ch, CellStyle style) =>
        Put(row, col, new Rune(ch), style);

    public void FillRow(int row, int startCol, int count, char ch, CellStyle style)
    {
        if (row < 0 || row >= _height)
        {
            return;
        }

        int clampedStart = Math.Max(startCol, 0);
        int clampedEnd = Math.Min(startCol + count, _width);
        if (clampedStart >= clampedEnd)
        {
            return;
        }

        var cell = new Cell(new Rune(ch), style);
        _back.AsSpan(row * _width + clampedStart, clampedEnd - clampedStart).Fill(cell);
        _dirtyRows[row >> 6] |= 1UL << (row & 63);
    }

    public void WriteString(int row, int col, string text, CellStyle style, int maxWidth = int.MaxValue)
    {
        if (row < 0 || row >= _height)
        {
            return;
        }

        int clampedStart = Math.Max(col, 0);
        int clampedEnd = (int)Math.Min((long)col + maxWidth, _width);
        if (clampedStart >= clampedEnd)
        {
            return;
        }

        int rowOffset = row * _width;
        int c = col;
        foreach (var rune in text.EnumerateRunes())
        {
            int w = RuneWidth.GetWidth(rune);
            if (c + w > clampedEnd)
            {
                break;
            }

            if (c >= 0)
            {
                _back[rowOffset + c] = new Cell(rune, style);
                if (w == 2 && c + 1 < _width)
                {
                    _back[rowOffset + c + 1] = Cell.WideContinuation;
                }
            }

            c += w;
        }

        _dirtyRows[row >> 6] |= 1UL << (row & 63);
    }

    public void WriteString(int row, int col, ReadOnlySpan<char> text, CellStyle style, int maxWidth = int.MaxValue)
    {
        if (row < 0 || row >= _height)
        {
            return;
        }

        int clampedStart = Math.Max(col, 0);
        int clampedEnd = (int)Math.Min((long)col + maxWidth, _width);
        if (clampedStart >= clampedEnd)
        {
            return;
        }

        int rowOffset = row * _width;
        int c = col;
        foreach (var rune in text.EnumerateRunes())
        {
            int w = RuneWidth.GetWidth(rune);
            if (c + w > clampedEnd)
            {
                break;
            }

            if (c >= 0)
            {
                _back[rowOffset + c] = new Cell(rune, style);
                if (w == 2 && c + 1 < _width)
                {
                    _back[rowOffset + c + 1] = Cell.WideContinuation;
                }
            }

            c += w;
        }

        _dirtyRows[row >> 6] |= 1UL << (row & 63);
    }

    public void Flush(StringBuilder sb)
    {
        sb.Clear();
        CellStyle currentStyle = default;
        bool hasStyle = false;
        int lastRow = -1, lastCol = -1;
        Span<char> charBuf = stackalloc char[2];

        for (int row = 0; row < _height; row++)
        {
            // Skip clean rows
            if ((_dirtyRows[row >> 6] & (1UL << (row & 63))) == 0)
            {
                continue;
            }

            for (int col = 0; col < _width; col++)
            {
                int idx = row * _width + col;
                ref var front = ref _front[idx];
                ref var back = ref _back[idx];

                if (front == back)
                {
                    continue;
                }

                front = back;

                // Skip continuation cells — the wide character at col-1 already covers this column
                if (back.IsWideContinuation)
                {
                    continue;
                }

                // Only emit cursor move if not already positioned here
                if (row != lastRow || col != lastCol)
                {
                    AnsiCodes.AppendMoveCursor(sb, row, col);
                }

                if (!hasStyle || currentStyle != back.Style)
                {
                    AppendStyleDiff(sb, hasStyle ? currentStyle : default, back.Style, hasStyle);
                    currentStyle = back.Style;
                    hasStyle = true;
                }

                int charLen = back.Char.EncodeToUtf16(charBuf);
                sb.Append(charBuf[..charLen]);

                int charWidth = RuneWidth.GetWidth(back.Char);
                lastRow = row;
                lastCol = col + charWidth;
            }
        }

        // Clear dirty bits
        Array.Clear(_dirtyRows);

        if (sb.Length > 0)
        {
            sb.Append(AnsiCodes.ResetAttributes);

            // Write via stdout stream to avoid ToString() allocation
            int totalChars = sb.Length;
            if (_writeBuffer.Length < totalChars)
            {
                _writeBuffer = new char[totalChars * 2];
            }

            sb.CopyTo(0, _writeBuffer, 0, totalChars);

            byte[] encoded = ArrayPool<byte>.Shared.Rent(Encoding.UTF8.GetMaxByteCount(totalChars));
            try
            {
                int byteCount = Encoding.UTF8.GetBytes(_writeBuffer, 0, totalChars, encoded, 0);
                StdOut.Write(encoded, 0, byteCount);
                StdOut.Flush();
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(encoded);
            }
        }
    }

    public void WriteRaw(string data)
    {
        int totalChars = data.Length;
        byte[] encoded = ArrayPool<byte>.Shared.Rent(Encoding.UTF8.GetMaxByteCount(totalChars));
        try
        {
            int byteCount = Encoding.UTF8.GetBytes(data, 0, totalChars, encoded, 0);
            StdOut.Write(encoded, 0, byteCount);
            StdOut.Flush();
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(encoded);
        }
    }

    public void ForceFullRedraw()
    {
        Array.Fill(_front, Cell.Dirty);
        // Mark all rows dirty
        Array.Fill(_dirtyRows, ulong.MaxValue);
    }

    private static void AppendStyleDiff(StringBuilder sb, CellStyle oldStyle, CellStyle newStyle, bool hadStyle)
    {
        // If Bold, Dim, or Inverse was on and is now off, we must reset then reapply
        bool needsReset = !hadStyle
            || (oldStyle.Bold && !newStyle.Bold)
            || (oldStyle.Dim && !newStyle.Dim)
            || (oldStyle.Inverse && !newStyle.Inverse)
            || oldStyle.Bg != newStyle.Bg;

        if (needsReset)
        {
            sb.Append(AnsiCodes.ResetAttributes);
            if (newStyle.Fg is { } fg)
            {
                AnsiCodes.AppendSetFg(sb, fg.R, fg.G, fg.B);
            }

            if (newStyle.Bg is { } bg)
            {
                AnsiCodes.AppendSetBg(sb, bg.R, bg.G, bg.B);
            }

            if (newStyle.Bold)
            {
                sb.Append("\x1b[1m");
            }

            if (newStyle.Dim)
            {
                sb.Append("\x1b[2m");
            }

            if (newStyle.Inverse)
            {
                sb.Append("\x1b[7m");
            }

            return;
        }

        // Only emit what changed
        if (oldStyle.Fg != newStyle.Fg)
        {
            if (newStyle.Fg is { } fg)
            {
                AnsiCodes.AppendSetFg(sb, fg.R, fg.G, fg.B);
            }
            else
            {
                sb.Append("\x1b[39m"); // default fg
            }
        }

        if (!oldStyle.Bold && newStyle.Bold)
        {
            sb.Append("\x1b[1m");
        }

        if (!oldStyle.Dim && newStyle.Dim)
        {
            sb.Append("\x1b[2m");
        }

        if (!oldStyle.Inverse && newStyle.Inverse)
        {
            sb.Append("\x1b[7m");
        }
    }
}

internal readonly record struct Color(byte R, byte G, byte B);

internal readonly record struct CellStyle(Color? Fg, Color? Bg, bool Bold = false, bool Dim = false, bool Inverse = false)
{
    public static readonly CellStyle Default = new(null, null);
}

internal readonly record struct Cell(Rune Char, CellStyle Style)
{
    public static readonly Cell Empty = new(new Rune(' '), CellStyle.Default);
    // Sentinel value that never matches any real cell, used to force a full redraw
    public static readonly Cell Dirty = new(new Rune('\0'), new CellStyle(new Color(255, 255, 255), new Color(255, 255, 255)));
    // Placeholder for the second column of a wide character
    public static readonly Cell WideContinuation = new(new Rune('\0'), CellStyle.Default);

    public bool IsWideContinuation => this == WideContinuation;
}
