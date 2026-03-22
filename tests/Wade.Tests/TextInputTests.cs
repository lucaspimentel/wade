using System.Text;
using System.Text.RegularExpressions;
using Wade.Terminal;
using Wade.UI;

namespace Wade.Tests;

public class TextInputTests
{
    // ── Rendering ───────────────────────────────────────────────────────────

    private static readonly CellStyle TestStyle = new(new Color(200, 200, 200), null);
    // ── Editing operations ──────────────────────────────────────────────────

    [Fact]
    public void Constructor_Default_EmptyValueAndZeroCursor()
    {
        var input = new TextInput();
        Assert.Equal("", input.Value);
        Assert.Equal(0, input.CursorPosition);
    }

    [Fact]
    public void Constructor_WithInitialValue_CursorAtEnd()
    {
        var input = new TextInput("hello");
        Assert.Equal("hello", input.Value);
        Assert.Equal(5, input.CursorPosition);
    }

    [Fact]
    public void InsertChar_AppendsAtCursorAndAdvances()
    {
        var input = new TextInput();
        input.InsertChar('a');
        input.InsertChar('b');
        Assert.Equal("ab", input.Value);
        Assert.Equal(2, input.CursorPosition);
    }

    [Fact]
    public void InsertChar_InMiddle_InsertsCorrectly()
    {
        var input = new TextInput("ac");
        input.MoveCursorLeft(); // cursor at 1
        input.InsertChar('b');
        Assert.Equal("abc", input.Value);
        Assert.Equal(2, input.CursorPosition);
    }

    [Theory]
    [InlineData("abc", 3, "ab", 2)] // delete last char
    [InlineData("abc", 1, "bc", 0)] // delete first char (cursor at 1)
    public void DeleteBackward_RemovesCharBeforeCursor(string initial, int cursorPos, string expectedValue, int expectedCursor)
    {
        var input = new TextInput(initial);
        // Move cursor to specified position
        input.MoveCursorHome();
        for (int i = 0; i < cursorPos; i++)
        {
            input.MoveCursorRight();
        }

        input.DeleteBackward();
        Assert.Equal(expectedValue, input.Value);
        Assert.Equal(expectedCursor, input.CursorPosition);
    }

    [Fact]
    public void DeleteBackward_AtPositionZero_IsNoOp()
    {
        var input = new TextInput("abc");
        input.MoveCursorHome();
        input.DeleteBackward();
        Assert.Equal("abc", input.Value);
        Assert.Equal(0, input.CursorPosition);
    }

    [Fact]
    public void DeleteForward_RemovesCharAtCursor()
    {
        var input = new TextInput("abc");
        input.MoveCursorHome();
        input.DeleteForward();
        Assert.Equal("bc", input.Value);
        Assert.Equal(0, input.CursorPosition);
    }

    [Fact]
    public void DeleteForward_AtEnd_IsNoOp()
    {
        var input = new TextInput("abc");
        input.DeleteForward();
        Assert.Equal("abc", input.Value);
        Assert.Equal(3, input.CursorPosition);
    }

    [Theory]
    [InlineData(0, 0)] // already at start, clamped
    [InlineData(3, 2)] // normal move
    public void MoveCursorLeft_ClampsAtZero(int startPos, int expectedPos)
    {
        var input = new TextInput("abc");
        input.MoveCursorHome();
        for (int i = 0; i < startPos; i++)
        {
            input.MoveCursorRight();
        }

        input.MoveCursorLeft();
        Assert.Equal(expectedPos, input.CursorPosition);
    }

    [Theory]
    [InlineData(3, 3)] // already at end, clamped
    [InlineData(0, 1)] // normal move
    public void MoveCursorRight_ClampsAtLength(int startPos, int expectedPos)
    {
        var input = new TextInput("abc");
        input.MoveCursorHome();
        for (int i = 0; i < startPos; i++)
        {
            input.MoveCursorRight();
        }

        input.MoveCursorRight();
        Assert.Equal(expectedPos, input.CursorPosition);
    }

    [Fact]
    public void MoveCursorHome_JumpsToZero()
    {
        var input = new TextInput("hello");
        input.MoveCursorHome();
        Assert.Equal(0, input.CursorPosition);
    }

    [Fact]
    public void MoveCursorEnd_JumpsToLength()
    {
        var input = new TextInput("hello");
        input.MoveCursorHome();
        input.MoveCursorEnd();
        Assert.Equal(5, input.CursorPosition);
    }

    [Fact]
    public void Clear_ResetsValueAndCursor()
    {
        var input = new TextInput("hello");
        input.Clear();
        Assert.Equal("", input.Value);
        Assert.Equal(0, input.CursorPosition);
    }

    private static string FlushRaw(ScreenBuffer buf)
    {
        var sb = new StringBuilder();
        buf.Flush(sb);
        return sb.ToString();
    }

    private static string StripAnsi(string s) =>
        Regex.Replace(s, @"\x1b\[[^a-zA-Z]*[a-zA-Z]", "");

    [Fact]
    public void Render_WritesTextAtPosition()
    {
        var input = new TextInput("abc");
        var buf = new ScreenBuffer(20, 3);
        input.Render(buf, 1, 2, 10, TestStyle);

        string output = StripAnsi(FlushRaw(buf));
        Assert.Contains("abc", output);
    }

    [Fact]
    public void Render_CursorPositionHasInverseStyle()
    {
        var input = new TextInput("ab");
        input.MoveCursorHome(); // cursor at 0, on 'a'
        var buf = new ScreenBuffer(20, 3);
        input.Render(buf, 0, 0, 10, TestStyle);

        string raw = FlushRaw(buf);
        // \x1b[7m is the SGR inverse sequence
        Assert.Contains("\x1b[7m", raw);
    }

    [Fact]
    public void Render_CursorAtEnd_RendersInverseSpace()
    {
        var input = new TextInput("ab"); // cursor at 2 (end)
        var buf = new ScreenBuffer(20, 3);
        input.Render(buf, 0, 0, 10, TestStyle);

        string raw = FlushRaw(buf);
        // Should have inverse style for the cursor-at-end space
        Assert.Contains("\x1b[7m", raw);
    }

    [Fact]
    public void Render_TextLongerThanMaxWidth_ShowsWindowAroundCursor()
    {
        var input = new TextInput("abcdefghij"); // 10 chars, cursor at end
        var buf = new ScreenBuffer(20, 3);
        input.Render(buf, 0, 0, 5, TestStyle);

        string output = StripAnsi(FlushRaw(buf));
        // With cursor at end (pos 10), maxWidth 5, visible window is [6..10]
        Assert.Contains("ghij", output);
        Assert.DoesNotContain("abcde", output);
    }

    [Fact]
    public void Render_ScrollsWhenCursorMovesLeft()
    {
        var input = new TextInput("abcdefghij"); // cursor at 10
        var buf = new ScreenBuffer(20, 3);
        // First render to establish scroll offset
        input.Render(buf, 0, 0, 5, TestStyle);

        // Move cursor to beginning
        input.MoveCursorHome();
        var buf2 = new ScreenBuffer(20, 3);
        input.Render(buf2, 0, 0, 5, TestStyle);

        string output = StripAnsi(FlushRaw(buf2));
        Assert.Contains("abcd", output);
    }
}
