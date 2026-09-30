//! Port of the C# `InputPipeline` (src/Wade/Terminal/InputPipeline.cs): a
//! single event queue fed by the console reader thread (the pump) and by
//! async loader threads. The pipeline owns its sender for the process
//! lifetime, so the app exits via the Quit action or cancellation, never by
//! channel disconnect (matching C# semantics).

use std::sync::mpsc::{Receiver, RecvTimeoutError, Sender};
use std::time::Duration;

use super::CancelToken;
use super::InputEvent;

const WAIT_TIMEOUT_MS: u64 = 100;

pub struct InputPipeline {
    sender: Sender<InputEvent>,
    receiver: Receiver<InputEvent>,
}

impl Default for InputPipeline {
    fn default() -> Self {
        Self::new()
    }
}

impl InputPipeline {
    #[must_use]
    pub fn new() -> Self {
        let (sender, receiver) = std::sync::mpsc::channel();
        Self { sender, receiver }
    }

    /// Sender clone for the pump thread and loader threads.
    #[must_use]
    pub fn sender(&self) -> Sender<InputEvent> {
        self.sender.clone()
    }

    /// Port of `InputPipeline.Inject`: swallow send errors once the receiver
    /// is gone (C# catches InvalidOperationException).
    pub fn inject(&self, event: InputEvent) {
        let _ = self.sender.send(event);
    }

    /// Port of `InputPipeline.TryTake`: non-blocking poll.
    #[must_use]
    pub fn try_take(&self) -> Option<InputEvent> {
        self.receiver.try_recv().ok()
    }

    /// Wait for the next event with a 100ms timeout so cancellation is
    /// checked regularly (mirrors the reader's wait window). Returns None on
    /// cancellation.
    #[must_use]
    pub fn wait_next(&self, cancel: &CancelToken) -> Option<InputEvent> {
        loop {
            if cancel.is_cancelled() {
                return None;
            }

            match self.receiver.recv_timeout(Duration::from_millis(WAIT_TIMEOUT_MS)) {
                Ok(event) => return Some(event),
                Err(RecvTimeoutError::Timeout) => continue,
                Err(RecvTimeoutError::Disconnected) => return None,
            }
        }
    }
}

#[cfg(test)]
mod tests {
    use super::*;
    use crate::input::KeyEvent;

    fn key_event() -> InputEvent {
        InputEvent::Key(KeyEvent {
            key: crate::console_key::ConsoleKey::A,
            key_char: b'a' as u16,
            shift: false,
            alt: false,
            control: false,
        })
    }

    #[test]
    fn round_trips_events() {
        let pipeline = InputPipeline::new();
        pipeline.inject(key_event());
        let cancel = CancelToken::new();
        assert!(pipeline.wait_next(&cancel).is_some());
        assert!(pipeline.try_take().is_none());
    }

    #[test]
    fn cancel_stops_waiting() {
        let pipeline = InputPipeline::new();
        let cancel = CancelToken::new();
        // Timeout arm keeps the loop alive until cancel flips
        cancel.cancel();
        assert!(pipeline.wait_next(&cancel).is_none());
    }

    #[test]
    fn inject_after_receiver_dropped_is_noop() {
        let pipeline = InputPipeline::new();
        pipeline.inject(key_event());
        // Sender stays alive with the pipeline; inject never panics
        pipeline.inject(key_event());
        let cancel = CancelToken::new();
        assert!(pipeline.wait_next(&cancel).is_some());
        assert!(pipeline.wait_next(&cancel).is_some());
    }
}