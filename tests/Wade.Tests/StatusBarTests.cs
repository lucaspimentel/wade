using System.Text;
using System.Text.RegularExpressions;
using Wade.FileSystem;
using Wade.Terminal;
using Wade.UI;

namespace Wade.Tests;

public class StatusBarTests
{
    private static string StripAnsi(string s) =>
        Regex.Replace(s, @"\x1b\[[^a-zA-Z]*[a-zA-Z]", "");

    private static string Flush(ScreenBuffer buf)
    {
        var sb = new StringBuilder();
        buf.Flush(sb);
        return StripAnsi(sb.ToString());
    }

    private static Rect StatusBarRect(int width) => new(0, 0, width, 1);

    [Fact]
    public void Render_WithNotification_ShowsMessage()
    {
        var buf = new ScreenBuffer(80, 1);
        var notification = new Notification("File copied!", NotificationKind.Success, 0);

        StatusBar.Render(buf, StatusBarRect(80), "/home", 5, 0, null, notification: notification);

        string output = Flush(buf);
        Assert.Contains("File copied!", output);
    }

    [Fact]
    public void Render_WithNotification_ShowsBothNotificationAndMetadata()
    {
        var buf = new ScreenBuffer(80, 1);
        var notification = new Notification("Done", NotificationKind.Info, 0);
        var entry = new FileSystemEntry("test.txt", "/home/test.txt", false, 1234, DateTime.Now, LinkTarget: null, IsDrive: false);

        StatusBar.Render(buf, StatusBarRect(80), "/home", 5, 2, entry, notification: notification);

        string output = Flush(buf);
        Assert.Contains("Done", output);
        Assert.Contains("3/5", output);
    }

    [Fact]
    public void Render_NarrowTerminal_TruncatesNotificationKeepsMetadata()
    {
        // Very narrow: path takes half, metadata takes most of the rest — notification should truncate or be skipped
        int width = 30;
        var buf = new ScreenBuffer(width, 1);
        var notification = new Notification("This is a long notification", NotificationKind.Success, 0);

        StatusBar.Render(buf, StatusBarRect(width), "/home", 5, 2, null, notification: notification);

        string output = Flush(buf);
        // Metadata (position) should always be present
        Assert.Contains("3/5", output);
        // The full notification should not appear (truncated or skipped)
        Assert.DoesNotContain("This is a long notification", output);
    }

    [Fact]
    public void Render_WithoutNotification_ShowsMetadata()
    {
        var buf = new ScreenBuffer(80, 1);

        StatusBar.Render(buf, StatusBarRect(80), "/home", 5, 2, null);

        string output = Flush(buf);
        Assert.Contains("3/5", output);
    }

    [Fact]
    public void Render_WithNotification_StillShowsPath()
    {
        var buf = new ScreenBuffer(80, 1);
        var notification = new Notification("Saved!", NotificationKind.Success, 0);

        StatusBar.Render(buf, StatusBarRect(80), "/home/user", 5, 0, null, notification: notification);

        string output = Flush(buf);
        Assert.Contains("/home/user", output);
        Assert.Contains("Saved!", output);
    }

    [Theory]
    [InlineData((int)SortMode.Name, true, "name\u2191")]
    [InlineData((int)SortMode.Modified, true, "time\u2191")]
    [InlineData((int)SortMode.Size, false, "size\u2193")]
    [InlineData((int)SortMode.Extension, false, "ext\u2193")]
    public void Render_ShowsSortIndicator(int sortModeInt, bool ascending, string expected)
    {
        var sortMode = (SortMode)sortModeInt;
        var buf = new ScreenBuffer(80, 1);

        StatusBar.Render(buf, StatusBarRect(80), "/home", 5, 0, null, sortMode: sortMode, sortAscending: ascending);

        string output = Flush(buf);
        Assert.Contains(expected, output);
    }

    [Fact]
    public void Render_NotificationTruncatedToFit()
    {
        int width = 40;
        var buf = new ScreenBuffer(width, 1);
        string longMessage = new('X', 200);
        var notification = new Notification(longMessage, NotificationKind.Error, 0);

        StatusBar.Render(buf, StatusBarRect(width), "/home", 5, 0, null, notification: notification);

        string output = Flush(buf);
        // The notification should not exceed the buffer width
        // Each line of output should fit within the status bar
        Assert.DoesNotContain(longMessage, output);
        Assert.Contains("X", output); // truncated but still present
    }
}
