using Wade.UI;

namespace Wade.Tests;

public class NotificationTests
{
    [Theory]
    [InlineData(0, false)]
    [InlineData(2000, false)]
    [InlineData(3999, false)]
    [InlineData(4000, true)]
    [InlineData(5000, true)]
    public void IsExpired_ReturnsExpectedResult(int elapsed, bool expectedExpired)
    {
        long baseTimestamp = 100_000;
        var notification = new Notification("test", NotificationKind.Info, baseTimestamp);

        bool expired = notification.IsExpired(baseTimestamp + elapsed);

        Assert.Equal(expectedExpired, expired);
    }

    [Theory]
    [InlineData(999, 1000, false)]
    [InlineData(1000, 1000, true)]
    [InlineData(1001, 1000, true)]
    public void IsExpired_CustomDuration_ReturnsExpectedResult(int elapsed, int durationMs, bool expectedExpired)
    {
        long baseTimestamp = 100_000;
        var notification = new Notification("test", NotificationKind.Info, baseTimestamp);

        bool expired = notification.IsExpired(baseTimestamp + elapsed, durationMs);

        Assert.Equal(expectedExpired, expired);
    }
}
