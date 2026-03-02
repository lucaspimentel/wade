using System.Text;

namespace Wade.Terminal;

internal sealed class ScreenBuffer
{
    private Cell[] _front;
    private Cell[] _back;
    private int _width;
    private int _height;

    public int Width => _width;
    public int Height => _height;

    public ScreenBuffer(int width, int height)
    {
        _width = width;
        _height = height;
        _front = new Cell[width * height];
        _back = new Cell[width * height];
        Array.Fill(_front, Cell.Empty);
        Array.Fill(_back, Cell.Empty);
    }

    public void Resize(int width, int height)
    {
        _width = width;
        _height = height;
        _front = new Cell[width * height];
        _back = new Cell[width * height];
        Array.Fill(_front, Cell.Empty);
        Array.Fill(_back, Cell.Empty);
    }

    public void Clear()
    {
        Array.Fill(_back, Cell.Empty);
    }

    public void Put(int row, int col, Rune rune, CellStyle style)
    {
        if (row < 0 || row >= _height || col < 0 || col >= _width) return;
        _back[row * _width + col] = new Cell(rune, style);
    }

    public void Put(int row, int col, char ch, CellStyle style) =>
        Put(row, col, new Rune(ch), style);

    public void WriteString(int row, int col, string text, CellStyle style, int maxWidth = int.MaxValue)
    {
        int c = col;
        foreach (var rune in text.EnumerateRunes())
        {
            if (c >= col + maxWidth || c >= _width) break;
            Put(row, c, rune, style);
            c++;
        }
    }

    public void Flush(StringBuilder sb)
    {
        sb.Clear();
        CellStyle? currentStyle = null;

        for (int row = 0; row < _height; row++)
        {
            for (int col = 0; col < _width; col++)
            {
                int idx = row * _width + col;
                ref var front = ref _front[idx];
                ref var back = ref _back[idx];

                if (front == back) continue;

                front = back;
                sb.Append(AnsiCodes.MoveCursor(row, col));

                if (currentStyle != back.Style)
                {
                    currentStyle = back.Style;
                    AppendStyle(sb, currentStyle.Value);
                }

                sb.Append(back.Char.ToString());
            }
        }

        if (sb.Length > 0)
        {
            sb.Append(AnsiCodes.ResetAttributes);
            Console.Write(sb);
        }
    }

    public void ForceFullRedraw()
    {
        Array.Fill(_front, Cell.Dirty);
    }

    private static void AppendStyle(StringBuilder sb, CellStyle style)
    {
        sb.Append(AnsiCodes.ResetAttributes);

        if (style.Fg is { } fg)
            sb.Append(AnsiCodes.SetFg(fg.R, fg.G, fg.B));

        if (style.Bg is { } bg)
            sb.Append(AnsiCodes.SetBg(bg.R, bg.G, bg.B));

        if (style.Bold)
            sb.Append("\x1b[1m");

        if (style.Dim)
            sb.Append("\x1b[2m");
    }
}

internal readonly record struct Color(byte R, byte G, byte B);

internal readonly record struct CellStyle(Color? Fg, Color? Bg, bool Bold = false, bool Dim = false)
{
    public static readonly CellStyle Default = new(null, null);
}

internal readonly record struct Cell(Rune Char, CellStyle Style)
{
    public static readonly Cell Empty = new(new Rune(' '), CellStyle.Default);
    // Sentinel value that never matches any real cell, used to force a full redraw
    public static readonly Cell Dirty = new(new Rune('\0'), new CellStyle(new Color(255, 255, 255), new Color(255, 255, 255)));
}
