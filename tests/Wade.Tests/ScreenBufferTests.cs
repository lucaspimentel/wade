using System.Text;
using System.Text.RegularExpressions;
using Wade.Terminal;

namespace Wade.Tests;

public class ScreenBufferTests
{
    private static readonly CellStyle DefaultStyle = CellStyle.Default;

    // Strip ANSI/VT escape sequences so tests can assert on plain text content
    private static string StripAnsi(string s) =>
        Regex.Replace(s, @"\x1b\[[^a-zA-Z]*[a-zA-Z]", "");

    private static string Flush(ScreenBuffer buf)
    {
        var sb = new StringBuilder();
        buf.Flush(sb);
        return StripAnsi(sb.ToString());
    }

    // ── Put (char overload) ───────────────────────────────────────────────────

    [Fact]
    public void Put_Char_StoresInBack()
    {
        var buf = new ScreenBuffer(10, 5);
        buf.Put(0, 0, 'A', DefaultStyle);
        Assert.Contains("A", Flush(buf));
    }

    [Theory]
    [InlineData(-1, 0)]
    [InlineData(0, -1)]
    [InlineData(5, 0)]   // row == height
    [InlineData(0, 10)]  // col == width
    public void Put_OutOfBounds_DoesNotThrow(int row, int col)
    {
        var buf = new ScreenBuffer(10, 5);
        buf.Put(row, col, 'X', DefaultStyle); // no exception
    }

    // ── Put (Rune overload) ───────────────────────────────────────────────────

    [Fact]
    public void Put_Rune_BMP_StoresAndFlushes()
    {
        var buf = new ScreenBuffer(10, 5);
        buf.Put(0, 0, new Rune('Z'), DefaultStyle);
        Assert.Contains("Z", Flush(buf));
    }

    [Fact]
    public void Put_Rune_SupplementaryPlane_StoresAndFlushes()
    {
        // U+1F600 GRINNING FACE — supplementary plane, requires surrogate pair
        var rune = new Rune(0x1F600);
        var buf = new ScreenBuffer(10, 5);
        buf.Put(0, 0, rune, DefaultStyle);
        Assert.Contains(rune.ToString(), Flush(buf));
    }

    // ── WriteString ───────────────────────────────────────────────────────────

    [Fact]
    public void WriteString_ASCII_FillsCells()
    {
        var buf = new ScreenBuffer(20, 5);
        buf.WriteString(0, 0, "Hello", DefaultStyle);
        Assert.Contains("Hello", Flush(buf));
    }

    [Fact]
    public void WriteString_RespectsMaxWidth()
    {
        var buf = new ScreenBuffer(20, 5);
        buf.WriteString(0, 0, "Hello World", DefaultStyle, maxWidth: 5);
        var output = Flush(buf);
        Assert.Contains("Hello", output);
        Assert.DoesNotContain(" World", output);
    }

    [Fact]
    public void WriteString_WithSupplementaryPlane_CountsAsOneColumn()
    {
        // U+F0001 is in the supplementary plane (Nerd Fonts v3 MDI range).
        // It occupies one buffer cell, not two.
        var icon = new Rune(0xF0001);
        var text = icon.ToString() + " foo";
        var buf = new ScreenBuffer(20, 5);
        buf.WriteString(0, 0, text, DefaultStyle);
        var output = Flush(buf);
        Assert.Contains(icon.ToString(), output);
        Assert.Contains("foo", output);
    }

    [Fact]
    public void WriteString_ClipsAtBufferWidth()
    {
        var buf = new ScreenBuffer(5, 5);
        buf.WriteString(0, 0, "ABCDEFGHIJ", DefaultStyle);
        var output = Flush(buf);
        Assert.Contains("ABCDE", output);
        Assert.DoesNotContain("F", output);
    }

    // ── Flush / dirty tracking ─────────────────────────────────────────────────

    [Fact]
    public void Flush_UnchangedCells_ProducesNoOutput()
    {
        var buf = new ScreenBuffer(10, 5);
        Flush(buf); // first flush: Empty == Empty everywhere, nothing to emit
        // Second flush: front is now synced with back, no changes
        var sb = new StringBuilder();
        buf.Flush(sb);
        Assert.Equal(0, sb.Length);
    }

    [Fact]
    public void Flush_AfterClear_OnlyDirtyCellsOutput()
    {
        var buf = new ScreenBuffer(10, 5);
        buf.Put(0, 0, 'X', DefaultStyle);
        Flush(buf); // sync front/back
        buf.Clear();
        buf.Put(0, 1, 'Y', DefaultStyle);
        Assert.Contains("Y", Flush(buf));
    }

    [Fact]
    public void ForceFullRedraw_CausesAllCellsToBeRedrawn()
    {
        var buf = new ScreenBuffer(5, 2);
        buf.WriteString(0, 0, "ABCDE", DefaultStyle);
        Flush(buf); // sync front/back
        buf.ForceFullRedraw();
        buf.WriteString(0, 0, "ABCDE", DefaultStyle);
        Assert.Contains("ABCDE", Flush(buf));
    }

    // ── Resize ────────────────────────────────────────────────────────────────

    [Fact]
    public void Resize_NewDimensionsAreReflected()
    {
        var buf = new ScreenBuffer(10, 5);
        buf.Resize(20, 10);
        Assert.Equal(20, buf.Width);
        Assert.Equal(10, buf.Height);
    }

    [Fact]
    public void Resize_ClearsBuffer()
    {
        var buf = new ScreenBuffer(10, 5);
        buf.Put(0, 0, 'X', DefaultStyle);
        buf.Resize(10, 5);
        // After resize, front and back are both Empty → no dirty cells
        var sb = new StringBuilder();
        buf.Flush(sb);
        Assert.Equal(0, sb.Length);
    }

    [Fact]
    public void Resize_ThenWrite_FlushesNewContent()
    {
        var buf = new ScreenBuffer(10, 5);
        buf.WriteString(0, 0, "OLD", DefaultStyle);
        Flush(buf); // sync front/back

        buf.Resize(20, 10);
        buf.WriteString(0, 0, "NEW", DefaultStyle);

        // After resize, front is empty so "NEW" must appear in flush output
        Assert.Contains("NEW", Flush(buf));
    }

    // ── Clear marks rows dirty ─────────────────────────────────────────────

    [Fact]
    public void Clear_MarksRowsDirty_StaleContentFlushed()
    {
        // Simulate the glow scroll bug: content at row 1, then scroll so row 1
        // becomes empty, then scroll back so row 1 has the same content again.
        // If Clear() doesn't mark rows dirty, the "empty" frame never updates
        // the front buffer, so the third frame sees front==back and emits nothing.
        var buf = new ScreenBuffer(20, 3);

        // Frame 1: write "HELLO" at row 1
        buf.Clear();
        buf.WriteString(1, 0, "HELLO", DefaultStyle);
        Flush(buf); // front[row 1] = "HELLO"

        // Frame 2: row 1 is empty (spacer line after scroll)
        buf.Clear();
        buf.WriteString(0, 0, "TOP", DefaultStyle);
        // row 1 intentionally left empty
        buf.WriteString(2, 0, "BOT", DefaultStyle);
        Flush(buf); // should clear front[row 1] to Cell.Empty

        // Frame 3: write "HELLO" at row 1 again (scroll back)
        buf.Clear();
        buf.WriteString(1, 0, "HELLO", DefaultStyle);
        var output = Flush(buf);

        // If Clear() properly marked rows dirty in frame 2, front[row 1] was
        // cleared to Cell.Empty, so frame 3 must re-emit "HELLO".
        // If Clear() didn't mark rows dirty, front[row 1] still has stale
        // "HELLO" from frame 1, and frame 3 sees no difference → no output.
        Assert.Contains("HELLO", output);
    }

    // ── Underline style ──────────────────────────────────────────────────────

    [Fact]
    public void Flush_UnderlineStyle_EmitsSgr4()
    {
        var buf = new ScreenBuffer(10, 1);
        var style = new CellStyle(null, null, Underline: true);
        buf.Put(0, 0, 'U', style);

        var sb = new StringBuilder();
        buf.Flush(sb);
        var raw = sb.ToString();
        Assert.Contains("\x1b[4m", raw);
    }
}
