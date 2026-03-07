namespace Wade.UI;

internal enum NotificationKind { Info, Success, Error }

internal readonly record struct Notification(string Message, NotificationKind Kind, long Timestamp)
{
    public bool IsExpired(long currentTick, int durationMs = 4000) =>
        currentTick - Timestamp >= durationMs;
}
