//! Port of src/Wade/Terminal/WindowsInputSource.cs: Win32 console input via
//! ReadConsoleInputW. Windows-only; the decode logic itself lives in the
//! portable `decode` module so it can be fixture-tested on any platform.

use std::collections::VecDeque;

use super::decode::{KEY_EVENT_TYPE, MOUSE_EVENT_TYPE, RawRecord, WINDOW_BUFFER_SIZE_EVENT_TYPE, decode_records};
use super::{CancelToken, InputEvent, InputSource};

use windows_sys::Win32::Foundation::{HANDLE, WAIT_OBJECT_0};
use windows_sys::Win32::System::Console::{
    CONSOLE_SCREEN_BUFFER_INFO, GetConsoleScreenBufferInfo, GetNumberOfConsoleInputEvents, GetStdHandle, INPUT_RECORD,
    ReadConsoleInputW, STD_INPUT_HANDLE, STD_OUTPUT_HANDLE,
};
use windows_sys::Win32::System::Threading::WaitForSingleObject;

const WAIT_TIMEOUT_MS: u32 = 100;

/// Port of `WindowsInputSource`: polls the console input handle with a
/// 100 ms wait so cancellation is checked between batches.
pub struct WindowsInputSource {
    stdin_handle: HANDLE,
    pending: VecDeque<InputEvent>,
}

impl WindowsInputSource {
    /// # Panics
    /// Panics if the standard input handle cannot be retrieved.
    #[must_use]
    pub fn new() -> Self {
        Self {
            stdin_handle: unsafe { GetStdHandle(STD_INPUT_HANDLE) },
            pending: VecDeque::new(),
        }
    }

    /// Reads all pending records at once to detect paste bursts.
    /// Returns true if any events were decoded.
    fn read_batch(&mut self, window: (i32, i32)) -> bool {
        unsafe {
            let mut count: u32 = 0;
            if GetNumberOfConsoleInputEvents(self.stdin_handle, &mut count) == 0 || count == 0 {
                return false;
            }

            let mut records: Vec<INPUT_RECORD> = Vec::with_capacity(count as usize);
            records.resize_with(count as usize, INPUT_RECORD::default);

            let mut events_read: u32 = 0;
            if ReadConsoleInputW(self.stdin_handle, records.as_mut_ptr(), count, &mut events_read) == 0
                || events_read == 0
            {
                return false;
            }

            let raw: Vec<RawRecord> = records[..events_read as usize].iter().map(to_raw_record).collect();
            let decoded = decode_records(&raw, window.0, window.1);
            let any = !decoded.is_empty();
            self.pending.extend(decoded);
            any
        }
    }
}

// Safety: stdin_handle is the process-wide stdin HANDLE (valid on any
// thread), and all access is through &mut self, so the struct is effectively
// owned during use. Mirrors the C# reader thread using the same handle.
unsafe impl Send for WindowsInputSource {}

impl Default for WindowsInputSource {
    fn default() -> Self {
        Self::new()
    }
}

impl InputSource for WindowsInputSource {
    fn read_next(&mut self, cancel: &CancelToken) -> Option<InputEvent> {
        loop {
            if cancel.is_cancelled() {
                return None;
            }

            if let Some(evt) = self.pending.pop_front() {
                return Some(evt);
            }

            let wait_result = unsafe { WaitForSingleObject(self.stdin_handle, WAIT_TIMEOUT_MS) };
            if wait_result != WAIT_OBJECT_0 {
                continue; // timeout or error: loop back and check cancellation
            }

            if !self.read_batch(window_size()) {
                continue;
            }
        }
    }

    fn try_take(&mut self) -> Option<InputEvent> {
        self.try_take_impl()
    }
}

fn to_raw_record(record: &INPUT_RECORD) -> RawRecord {
    unsafe {
        match record.EventType {
            KEY_EVENT_TYPE => {
                let k = &record.Event.KeyEvent;
                RawRecord::Key {
                    key_down: k.bKeyDown != 0,
                    virtual_key_code: k.wVirtualKeyCode,
                    unicode_char: k.uChar.UnicodeChar,
                    control_key_state: k.dwControlKeyState,
                }
            }
            MOUSE_EVENT_TYPE => {
                let m = &record.Event.MouseEvent;
                RawRecord::Mouse {
                    x: m.dwMousePosition.X,
                    y: m.dwMousePosition.Y,
                    button_state: m.dwButtonState,
                    event_flags: m.dwEventFlags,
                }
            }
            WINDOW_BUFFER_SIZE_EVENT_TYPE => RawRecord::BufferSize,
            _ => RawRecord::Other,
        }
    }
}

/// Port of `Console.WindowWidth`/`Console.WindowHeight` via the console
/// screen buffer info's `srWindow` rectangle.
fn window_size() -> (i32, i32) {
    let handle = unsafe { GetStdHandle(STD_OUTPUT_HANDLE) };
    let mut info = CONSOLE_SCREEN_BUFFER_INFO::default();
    let ok = unsafe { GetConsoleScreenBufferInfo(handle, &mut info) };
    if ok != 0 {
        let width = i32::from(info.srWindow.Right) - i32::from(info.srWindow.Left) + 1;
        let height = i32::from(info.srWindow.Bottom) - i32::from(info.srWindow.Top) + 1;
        (width, height)
    } else {
        (80, 25)
    }
}

impl WindowsInputSource {
    /// Non-blocking drain: pops an already-decoded event, or reads a batch
    /// if one is immediately available (0 ms wait).
    fn try_take_impl(&mut self) -> Option<InputEvent> {
        if let Some(evt) = self.pending.pop_front() {
            return Some(evt);
        }

        unsafe {
            if WaitForSingleObject(self.stdin_handle, 0) != WAIT_OBJECT_0 {
                return None;
            }
        }

        self.read_batch(window_size());
        self.pending.pop_front()
    }
}

/// Public window size for the app loop's startup dimensions.
#[must_use]
pub fn window_size_pub() -> Option<(i32, i32)> {
    Some(window_size())
}
