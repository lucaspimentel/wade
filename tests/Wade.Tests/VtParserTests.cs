using System.Text;
using Wade.Terminal;

namespace Wade.Tests;

public class VtParserTests
{
    private static List<InputEvent> Parse(params byte[] data) => VtParser.Parse(data);

    private static List<InputEvent> Parse(string s) => VtParser.Parse(Encoding.ASCII.GetBytes(s));

    // ── Plain ASCII ─────────────────────────────────────────────────────────────

    [Theory]
    [InlineData('a', ConsoleKey.A)]
    [InlineData('z', ConsoleKey.Z)]
    [InlineData('A', ConsoleKey.A)]
    [InlineData('Z', ConsoleKey.Z)]
    [InlineData('0', ConsoleKey.D0)]
    [InlineData('9', ConsoleKey.D9)]
    [InlineData(' ', ConsoleKey.Spacebar)]
    public void Parse_PrintableAscii_ReturnsKeyEvent(char c, ConsoleKey expectedKey)
    {
        var events = Parse((byte)c);
        var key = Assert.Single(events);
        var keyEvent = Assert.IsType<KeyEvent>(key);
        Assert.Equal(expectedKey, keyEvent.Key);
        Assert.Equal(c, keyEvent.KeyChar);
    }

    // ── CSI arrow sequences ─────────────────────────────────────────────────────

    [Theory]
    [InlineData("A", ConsoleKey.UpArrow)]
    [InlineData("B", ConsoleKey.DownArrow)]
    [InlineData("C", ConsoleKey.RightArrow)]
    [InlineData("D", ConsoleKey.LeftArrow)]
    [InlineData("H", ConsoleKey.Home)]
    [InlineData("F", ConsoleKey.End)]
    public void Parse_CsiSequence_ReturnsExpectedKey(string suffix, ConsoleKey expectedKey)
    {
        var data = new byte[] { 0x1B, (byte)'[' }.Concat(Encoding.ASCII.GetBytes(suffix)).ToArray();
        var events = Parse(data);
        var key = Assert.Single(events);
        var keyEvent = Assert.IsType<KeyEvent>(key);
        Assert.Equal(expectedKey, keyEvent.Key);
    }

    // ── CSI tilde sequences ─────────────────────────────────────────────────────

    [Theory]
    [InlineData("5~", ConsoleKey.PageUp)]
    [InlineData("6~", ConsoleKey.PageDown)]
    [InlineData("1~", ConsoleKey.Home)]
    [InlineData("4~", ConsoleKey.End)]
    [InlineData("2~", ConsoleKey.Insert)]
    [InlineData("3~", ConsoleKey.Delete)]
    public void Parse_CsiTildeSequence_ReturnsExpectedKey(string params_, ConsoleKey expectedKey)
    {
        var data = new byte[] { 0x1B, (byte)'[' }.Concat(Encoding.ASCII.GetBytes(params_)).ToArray();
        var events = Parse(data);
        var key = Assert.Single(events);
        var keyEvent = Assert.IsType<KeyEvent>(key);
        Assert.Equal(expectedKey, keyEvent.Key);
    }

    // ── Standalone ESC ──────────────────────────────────────────────────────────

    [Fact]
    public void Parse_StandaloneEsc_ReturnsEscapeKey()
    {
        var events = Parse(0x1B);
        var key = Assert.Single(events);
        var keyEvent = Assert.IsType<KeyEvent>(key);
        Assert.Equal(ConsoleKey.Escape, keyEvent.Key);
    }

    // ── Control characters ──────────────────────────────────────────────────────

    [Theory]
    [InlineData(0x01, ConsoleKey.A)]  // Ctrl+A
    [InlineData(0x03, ConsoleKey.C)]  // Ctrl+C
    [InlineData(0x04, ConsoleKey.D)]  // Ctrl+D
    [InlineData(0x1A, ConsoleKey.Z)]  // Ctrl+Z
    public void Parse_ControlChar_ReturnsCtrlKeyEvent(byte b, ConsoleKey expectedKey)
    {
        var events = Parse(b);
        var key = Assert.Single(events);
        var keyEvent = Assert.IsType<KeyEvent>(key);
        Assert.Equal(expectedKey, keyEvent.Key);
        Assert.True(keyEvent.Control);
    }

    // ── Special keys ────────────────────────────────────────────────────────────

    [Fact]
    public void Parse_Enter_ReturnsEnterKey()
    {
        var events = Parse(0x0D);
        var key = Assert.Single(events);
        var keyEvent = Assert.IsType<KeyEvent>(key);
        Assert.Equal(ConsoleKey.Enter, keyEvent.Key);
    }

    [Fact]
    public void Parse_Tab_ReturnsTabKey()
    {
        var events = Parse(0x09);
        var key = Assert.Single(events);
        var keyEvent = Assert.IsType<KeyEvent>(key);
        Assert.Equal(ConsoleKey.Tab, keyEvent.Key);
    }

    [Fact]
    public void Parse_Backspace_ReturnsBackspaceKey()
    {
        var events = Parse(0x7F);
        var key = Assert.Single(events);
        var keyEvent = Assert.IsType<KeyEvent>(key);
        Assert.Equal(ConsoleKey.Backspace, keyEvent.Key);
    }

    // ── SGR mouse sequences ──────────────────────────────────────────────────────

    [Fact]
    public void Parse_SgrMousePress_ReturnsMouseEvent()
    {
        // ESC [ < 0 ; 10 ; 5 M → left press at row 4, col 9 (1-based → 0-based)
        var events = Parse("\x1b[<0;10;5M");
        var evt = Assert.Single(events);
        var mouse = Assert.IsType<MouseEvent>(evt);
        Assert.Equal(MouseButton.Left, mouse.Button);
        Assert.Equal(4, mouse.Row);
        Assert.Equal(9, mouse.Col);
        Assert.False(mouse.IsRelease);
    }

    [Fact]
    public void Parse_SgrMouseRelease_ReturnsMouseEvent()
    {
        // ESC [ < 0 ; 10 ; 5 m → left release at row 4, col 9
        var events = Parse("\x1b[<0;10;5m");
        var evt = Assert.Single(events);
        var mouse = Assert.IsType<MouseEvent>(evt);
        Assert.Equal(MouseButton.Left, mouse.Button);
        Assert.Equal(4, mouse.Row);
        Assert.Equal(9, mouse.Col);
        Assert.True(mouse.IsRelease);
    }

    [Fact]
    public void Parse_SgrScrollUp_ReturnsMouseEvent()
    {
        var events = Parse("\x1b[<64;10;5M");
        var evt = Assert.Single(events);
        var mouse = Assert.IsType<MouseEvent>(evt);
        Assert.Equal(MouseButton.ScrollUp, mouse.Button);
        Assert.Equal(4, mouse.Row);
        Assert.Equal(9, mouse.Col);
        Assert.False(mouse.IsRelease);
    }

    [Fact]
    public void Parse_SgrScrollDown_ReturnsMouseEvent()
    {
        var events = Parse("\x1b[<65;10;5M");
        var evt = Assert.Single(events);
        var mouse = Assert.IsType<MouseEvent>(evt);
        Assert.Equal(MouseButton.ScrollDown, mouse.Button);
        Assert.Equal(4, mouse.Row);
        Assert.Equal(9, mouse.Col);
        Assert.False(mouse.IsRelease);
    }

    [Fact]
    public void Parse_SgrRightClick_ReturnsMouseEvent()
    {
        var events = Parse("\x1b[<2;10;5M");
        var evt = Assert.Single(events);
        var mouse = Assert.IsType<MouseEvent>(evt);
        Assert.Equal(MouseButton.Right, mouse.Button);
        Assert.Equal(4, mouse.Row);
        Assert.Equal(9, mouse.Col);
        Assert.False(mouse.IsRelease);
    }

    // ── UTF-8 multibyte ─────────────────────────────────────────────────────────

    [Fact]
    public void Parse_Utf8TwoByte_ReturnsDecodedChar()
    {
        // é = U+00E9 = 0xC3 0xA9 in UTF-8
        var events = Parse(0xC3, 0xA9);
        var key = Assert.Single(events);
        var keyEvent = Assert.IsType<KeyEvent>(key);
        Assert.Equal('é', keyEvent.KeyChar);
    }

    [Fact]
    public void Parse_Utf8ThreeByte_ReturnsDecodedChar()
    {
        // € = U+20AC = 0xE2 0x82 0xAC in UTF-8
        var events = Parse(0xE2, 0x82, 0xAC);
        var key = Assert.Single(events);
        var keyEvent = Assert.IsType<KeyEvent>(key);
        Assert.Equal('€', keyEvent.KeyChar);
    }

    // ── SS3 F-keys ──────────────────────────────────────────────────────────────

    [Theory]
    [InlineData('P', ConsoleKey.F1)]
    [InlineData('Q', ConsoleKey.F2)]
    [InlineData('R', ConsoleKey.F3)]
    [InlineData('S', ConsoleKey.F4)]
    public void Parse_Ss3FKey_ReturnsExpectedKey(char finalChar, ConsoleKey expectedKey)
    {
        var events = Parse(0x1B, (byte)'O', (byte)finalChar);
        var key = Assert.Single(events);
        var keyEvent = Assert.IsType<KeyEvent>(key);
        Assert.Equal(expectedKey, keyEvent.Key);
    }
}
