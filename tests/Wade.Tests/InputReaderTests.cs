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

    // ── Sort keys ─────────────────────────────────────────────────────────────

    [Fact]
    public void MapKey_S_ReturnsCycleSortMode()
    {
        var evt = new KeyEvent(ConsoleKey.S, 's', false, false, false);
        Assert.Equal(AppAction.CycleSortMode, InputReader.MapKey(evt));
    }

    [Fact]
    public void MapKey_ShiftS_ReturnsToggleSortDirection()
    {
        var evt = new KeyEvent(ConsoleKey.S, 'S', true, false, false);
        Assert.Equal(AppAction.ToggleSortDirection, InputReader.MapKey(evt));
    }

    // ── Go-to-path ────────────────────────────────────────────────────────────

    [Fact]
    public void MapKey_G_ReturnsGoToPath()
    {
        var evt = new KeyEvent(ConsoleKey.G, 'g', false, false, false);
        Assert.Equal(AppAction.GoToPath, InputReader.MapKey(evt));
    }

    // ── File actions ─────────────────────────────────────────────────────────

    [Fact]
    public void MapKey_O_ReturnsOpenExternal()
    {
        var evt = new KeyEvent(ConsoleKey.O, 'o', false, false, false);
        Assert.Equal(AppAction.OpenExternal, InputReader.MapKey(evt));
    }

    [Fact]
    public void MapKey_F2_ReturnsRename()
    {
        var evt = new KeyEvent(ConsoleKey.F2, '\0', false, false, false);
        Assert.Equal(AppAction.Rename, InputReader.MapKey(evt));
    }

    [Fact]
    public void MapKey_Delete_ReturnsDelete()
    {
        var evt = new KeyEvent(ConsoleKey.Delete, '\0', false, false, false);
        Assert.Equal(AppAction.Delete, InputReader.MapKey(evt));
    }

    [Fact]
    public void MapKey_CtrlC_ReturnsCopy()
    {
        var evt = new KeyEvent(ConsoleKey.C, '\x03', false, false, true);
        Assert.Equal(AppAction.Copy, InputReader.MapKey(evt));
    }

    [Fact]
    public void MapKey_CtrlX_ReturnsCut()
    {
        var evt = new KeyEvent(ConsoleKey.X, '\x18', false, false, true);
        Assert.Equal(AppAction.Cut, InputReader.MapKey(evt));
    }

    [Fact]
    public void MapKey_C_ReturnsCopy()
    {
        var evt = new KeyEvent(ConsoleKey.C, 'c', false, false, false);
        Assert.Equal(AppAction.Copy, InputReader.MapKey(evt));
    }

    [Fact]
    public void MapKey_X_ReturnsCut()
    {
        var evt = new KeyEvent(ConsoleKey.X, 'x', false, false, false);
        Assert.Equal(AppAction.Cut, InputReader.MapKey(evt));
    }

    [Fact]
    public void MapKey_P_ReturnsPaste()
    {
        var evt = new KeyEvent(ConsoleKey.P, 'p', false, false, false);
        Assert.Equal(AppAction.Paste, InputReader.MapKey(evt));
    }

    [Fact]
    public void MapKey_V_ReturnsPaste()
    {
        var evt = new KeyEvent(ConsoleKey.V, 'v', false, false, false);
        Assert.Equal(AppAction.Paste, InputReader.MapKey(evt));
    }

    // ── Open terminal ───────────────────────────────────────────────────────

    [Fact]
    public void MapKey_CtrlT_ReturnsOpenTerminal()
    {
        var evt = new KeyEvent(ConsoleKey.T, '\x14', false, false, true);
        Assert.Equal(AppAction.OpenTerminal, InputReader.MapKey(evt));
    }

    // ── Action palette ──────────────────────────────────────────────────────

    [Fact]
    public void MapKey_CtrlP_ReturnsShowActionPalette()
    {
        var evt = new KeyEvent(ConsoleKey.P, '\x10', false, false, true);
        Assert.Equal(AppAction.ShowActionPalette, InputReader.MapKey(evt));
    }

    // ── Quit variants ─────────────────────────────────────────────────────

    [Fact]
    public void MapKey_LowercaseQ_ReturnsQuit()
    {
        var evt = new KeyEvent(ConsoleKey.Q, 'q', false, false, false);
        Assert.Equal(AppAction.Quit, InputReader.MapKey(evt));
    }

    [Fact]
    public void MapKey_ShiftQ_ReturnsQuitNoCd()
    {
        var evt = new KeyEvent(ConsoleKey.Q, 'Q', true, false, false);
        Assert.Equal(AppAction.QuitNoCd, InputReader.MapKey(evt));
    }
}
