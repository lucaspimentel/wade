using Wade.Terminal;

namespace Wade.Tests;

public class InputReaderTests
{
    // ── Arrow keys ──────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(ConsoleKey.UpArrow, (int)AppAction.NavigateUp)]
    [InlineData(ConsoleKey.DownArrow, (int)AppAction.NavigateDown)]
    [InlineData(ConsoleKey.RightArrow, (int)AppAction.Open)]
    [InlineData(ConsoleKey.LeftArrow, (int)AppAction.Back)]
    public void MapKey_ArrowKeys_ReturnsExpectedAction(ConsoleKey key, int expected)
    {
        var evt = new KeyEvent(key, '\0', false, false, false);
        Assert.Equal((AppAction)expected, InputReader.MapKey(evt));
    }

    // ── Vim keys ────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(ConsoleKey.K, (int)AppAction.NavigateUp)]
    [InlineData(ConsoleKey.J, (int)AppAction.NavigateDown)]
    [InlineData(ConsoleKey.L, (int)AppAction.Open)]
    [InlineData(ConsoleKey.H, (int)AppAction.Back)]
    public void MapKey_VimKeys_ReturnsExpectedAction(ConsoleKey key, int expected)
    {
        var evt = new KeyEvent(key, '\0', false, false, false);
        Assert.Equal((AppAction)expected, InputReader.MapKey(evt));
    }

    // ── Navigation keys ─────────────────────────────────────────────────────────

    [Theory]
    [InlineData(ConsoleKey.Enter, (int)AppAction.Open)]
    [InlineData(ConsoleKey.Backspace, (int)AppAction.Back)]
    [InlineData(ConsoleKey.Escape, (int)AppAction.Quit)]
    [InlineData(ConsoleKey.Q, (int)AppAction.Quit)]
    [InlineData(ConsoleKey.PageUp, (int)AppAction.PageUp)]
    [InlineData(ConsoleKey.PageDown, (int)AppAction.PageDown)]
    [InlineData(ConsoleKey.Home, (int)AppAction.Home)]
    [InlineData(ConsoleKey.End, (int)AppAction.End)]
    public void MapKey_NavigationKeys_ReturnsExpectedAction(ConsoleKey key, int expected)
    {
        var evt = new KeyEvent(key, '\0', false, false, false);
        Assert.Equal((AppAction)expected, InputReader.MapKey(evt));
    }

    // ── Special chars ───────────────────────────────────────────────────────────

    [Fact]
    public void MapKey_QuestionMark_ReturnsShowHelp()
    {
        var evt = new KeyEvent((ConsoleKey)0, '?', false, false, false);
        Assert.Equal(AppAction.ShowHelp, InputReader.MapKey(evt));
    }

    [Fact]
    public void MapKey_Slash_ReturnsSearch()
    {
        var evt = new KeyEvent((ConsoleKey)0, '/', false, false, false);
        Assert.Equal(AppAction.Search, InputReader.MapKey(evt));
    }

    // ── Unrecognized keys ───────────────────────────────────────────────────────

    [Theory]
    [InlineData(ConsoleKey.A, '\0')]
    [InlineData(ConsoleKey.F1, '\0')]
    public void MapKey_UnrecognizedKey_ReturnsNone(ConsoleKey key, char keyChar)
    {
        var evt = new KeyEvent(key, keyChar, false, false, false);
        Assert.Equal(AppAction.None, InputReader.MapKey(evt));
    }

    // ── Toggle mark ──────────────────────────────────────────────────────────────

    [Fact]
    public void MapKey_Space_ReturnsToggleMark()
    {
        var evt = new KeyEvent(ConsoleKey.Spacebar, ' ', false, false, false);
        Assert.Equal(AppAction.ToggleMark, InputReader.MapKey(evt));
    }

    // ── Modifier combinations ───────────────────────────────────────────────────

    [Fact]
    public void MapKey_CtrlC_ReturnsNone()
    {
        var evt = new KeyEvent(ConsoleKey.C, '\x03', false, false, true);
        Assert.Equal(AppAction.None, InputReader.MapKey(evt));
    }
}
