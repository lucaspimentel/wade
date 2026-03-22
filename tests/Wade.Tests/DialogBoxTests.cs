using System.Text;
using System.Text.RegularExpressions;
using Wade.Terminal;
using Wade.UI;

namespace Wade.Tests;

public class DialogBoxTests
{
    private static string StripAnsi(string s) =>
        Regex.Replace(s, @"\x1b\[[^a-zA-Z]*[a-zA-Z]", "");

    private static string Flush(ScreenBuffer buf)
    {
        var sb = new StringBuilder();
        buf.Flush(sb);
        return StripAnsi(sb.ToString());
    }

    [Fact]
    public void Render_ReturnsContentRect_WithCorrectDimensions()
    {
        var buf = new ScreenBuffer(80, 24);

        Rect content = DialogBox.Render(buf, 80, 24, 20, 5, title: "Test", footer: "Footer");

        Assert.Equal(20, content.Width);
        Assert.Equal(5, content.Height);
    }

    [Fact]
    public void Render_CentersBox_Horizontally()
    {
        var buf = new ScreenBuffer(80, 24);

        // box width = contentWidth + 4 = 24; content.Left = (80 - 24)/2 + 2 = 30
        Rect content = DialogBox.Render(buf, 80, 24, 20, 5, title: "Test", footer: "Footer");

        Assert.Equal(30, content.Left);
    }

    [Fact]
    public void Render_DrawsBorderCharacters()
    {
        var buf = new ScreenBuffer(60, 20);

        DialogBox.Render(buf, 60, 20, 10, 3, title: "T", footer: "F");

        string output = Flush(buf);
        Assert.Contains("┌", output);
        Assert.Contains("┐", output);
        Assert.Contains("└", output);
        Assert.Contains("┘", output);
        Assert.Contains("├", output);
        Assert.Contains("┤", output);
        Assert.Contains("│", output);
    }

    [Fact]
    public void Render_DrawsTitleText()
    {
        var buf = new ScreenBuffer(60, 20);

        DialogBox.Render(buf, 60, 20, 20, 3, title: "My Title");

        string output = Flush(buf);
        Assert.Contains("My Title", output);
    }

    [Fact]
    public void Render_DrawsFooterText()
    {
        var buf = new ScreenBuffer(60, 20);

        DialogBox.Render(buf, 60, 20, 20, 3, footer: "Press ESC");

        string output = Flush(buf);
        Assert.Contains("Press ESC", output);
    }

    [Fact]
    public void Render_NoTitleOrFooter_StillDrawsBorders()
    {
        var buf = new ScreenBuffer(60, 20);

        Rect content = DialogBox.Render(buf, 60, 20, 10, 3);

        string output = Flush(buf);
        Assert.Contains("┌", output);
        Assert.Contains("┘", output);
        Assert.Equal(10, content.Width);
        Assert.Equal(3, content.Height);
    }

    [Theory]
    [InlineData(true, true)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(false, false)]
    public void Render_ContentRect_AllowsCallerToWriteInside(bool hasTitle, bool hasFooter)
    {
        var buf = new ScreenBuffer(80, 24);
        string? title = hasTitle ? "Title" : null;
        string? footer = hasFooter ? "Footer" : null;

        Rect content = DialogBox.Render(buf, 80, 24, 30, 5, title: title, footer: footer);

        // Write into the content area
        var style = new CellStyle(new Color(255, 255, 255), DialogBox.BgColor);
        buf.WriteString(content.Top, content.Left, "Hello World", style);

        string output = Flush(buf);
        Assert.Contains("Hello World", output);
    }

    [Fact]
    public void HelpOverlay_Render_ContainsKeybindings()
    {
        var buf = new ScreenBuffer(120, 40);

        HelpOverlay.Render(buf, 120, 40);

        string output = Flush(buf);
        Assert.Contains("Help", output);
        Assert.Contains("Press any key to close", output);
        Assert.Contains("Ctrl+P", output);
        Assert.Contains("Open action list", output);
        Assert.Contains("Navigation", output);
        Assert.Contains("Move selection", output);
    }
}
