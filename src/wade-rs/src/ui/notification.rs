//! Port of src/Wade/UI/Notification.cs.

#[derive(Clone, Copy, PartialEq, Eq, Debug)]
pub enum NotificationKind {
    Info,
    Success,
    Error,
}

/// Mirrors C# `Notification(string Message, NotificationKind Kind, long Timestamp)`.
#[derive(Clone, PartialEq, Eq, Debug)]
pub struct Notification {
    pub message: String,
    pub kind: NotificationKind,
    pub timestamp_ms: i64,
}

impl Notification {
    #[must_use]
    pub fn is_expired(&self, current_tick_ms: i64, duration_ms: i64) -> bool {
        current_tick_ms - self.timestamp_ms >= duration_ms
    }
}
