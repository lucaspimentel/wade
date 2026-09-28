//! Port of the decode loop in src/Wade/Terminal/WindowsInputSource.cs
//! (ProcessRecords / DecodeRecords). Pure: no console access, so the fixture
//! harnesses run identically on every platform.

use crate::console_key::ConsoleKey;
use super::{InputEvent, KeyEvent, MouseButton, MouseEvent, ResizeEvent};

pub const KEY_EVENT_TYPE: u16 = 0x0001;
pub const MOUSE_EVENT_TYPE: u16 = 0x0002;
pub const WINDOW_BUFFER_SIZE_EVENT_TYPE: u16 = 0x0004;

pub const MOUSE_MOVED: u32 = 0x0001;
pub const MOUSE_WHEELED: u32 = 0x0004;
pub const FROM_LEFT_1ST_BUTTON_PRESSED: u32 = 0x0001;
pub const RIGHTMOST_BUTTON_PRESSED: u32 = 0x0002;

const SHIFT_PRESSED: u32 = 0x0010;
const LEFT_CTRL_PRESSED: u32 = 0x0008;
const RIGHT_CTRL_PRESSED: u32 = 0x0004;
const LEFT_ALT_PRESSED: u32 = 0x0002;
const RIGHT_ALT_PRESSED: u32 = 0x0001;

/// A portable stand-in for the Win32 INPUT_RECORD union. The Windows syscall
/// layer converts real INPUT_RECORD values into these; the fixture harnesses
/// synthesize them directly, which is what makes the decode logic testable
/// without a console.
#[derive(Clone, Copy, Debug)]
pub enum RawRecord {
    Key {
        key_down: bool,
        virtual_key_code: u16,
        /// UTF-16 code unit, mirroring C# `char UnicodeChar`.
        unicode_char: u16,
        control_key_state: u32,
    },
    Mouse {
        x: i16,
        y: i16,
        button_state: u32,
        event_flags: u32,
    },
    BufferSize,
    Other,
}

fn make_key_event(virtual_key_code: u16, unicode_char: u16, control_key_state: u32) -> KeyEvent {
    KeyEvent {
        key: ConsoleKey(virtual_key_code),
        key_char: unicode_char,
        shift: control_key_state & SHIFT_PRESSED != 0,
        alt: control_key_state & (LEFT_ALT_PRESSED | RIGHT_ALT_PRESSED) != 0,
        control: control_key_state & (LEFT_CTRL_PRESSED | RIGHT_CTRL_PRESSED) != 0,
    }
}

/// Port of `WindowsInputSource.DecodeRecords`: decodes raw console input
/// records into input events. `window_width`/`window_height` mirror the live
/// console size the C# call site passes in.
pub fn decode_records(
    records: &[RawRecord],
    window_width: i32,
    window_height: i32,
) -> Vec<InputEvent> {
    let mut pending = Vec::new();
    let mut i = 0;

    while i < records.len() {
        match records[i] {
            RawRecord::Key {
                key_down,
                virtual_key_code,
                unicode_char,
                control_key_state,
            } => {
                if !key_down {
                    i += 1;
                    continue; // skip key-up events
                }

                // Check for paste burst: consecutive printable key-down events
                if u32::from(unicode_char) >= u32::from(' ') {
                    let start = i;
                    let mut paste_chars: Vec<u16> = vec![unicode_char];
                    i += 1;

                    while i < records.len() {
                        match records[i] {
                            // Skip key-up events (pasted text interleaves key-down/key-up)
                            RawRecord::Key { key_down: false, .. } => {
                                i += 1;
                                continue;
                            }
                            RawRecord::Key {
                                unicode_char: next_char,
                                ..
                            } => {
                                if u32::from(next_char) < u32::from(' ') {
                                    break;
                                }

                                paste_chars.push(next_char);
                                i += 1;
                            }
                            _ => break,
                        }
                    }

                    if paste_chars.len() > 1 {
                        // Multiple chars in one batch = paste
                        pending.push(InputEvent::Paste(String::from_utf16_lossy(&paste_chars)));
                    } else {
                        // Single char = normal keystroke; modifiers come from
                        // the first record of the burst
                        let RawRecord::Key {
                            virtual_key_code,
                            unicode_char,
                            control_key_state,
                            ..
                        } = records[start]
                        else {
                            unreachable!("burst starts with a key record");
                        };

                        pending.push(InputEvent::Key(make_key_event(
                            virtual_key_code,
                            unicode_char,
                            control_key_state,
                        )));
                    }

                    continue;
                }

                pending.push(InputEvent::Key(make_key_event(
                    virtual_key_code,
                    unicode_char,
                    control_key_state,
                )));

                i += 1;
            }
            RawRecord::Mouse {
                x,
                y,
                button_state,
                event_flags,
            } => {
                if event_flags == MOUSE_MOVED {
                    i += 1;
                    continue; // ignore mouse move
                }

                if event_flags == MOUSE_WHEELED {
                    // High word of dwButtonState is a signed wheel delta
                    let hi_word = ((button_state >> 16) & 0xFFFF) as u16;
                    let signed = i16::from_ne_bytes(hi_word.to_ne_bytes());
                    let scroll_button = if signed > 0 {
                        MouseButton::ScrollUp
                    } else {
                        MouseButton::ScrollDown
                    };
                    pending.push(InputEvent::Mouse(MouseEvent {
                        button: scroll_button,
                        row: i32::from(y),
                        col: i32::from(x),
                        is_release: false,
                    }));
                } else if event_flags == 0 {
                    // button press or release
                    if button_state & FROM_LEFT_1ST_BUTTON_PRESSED != 0 {
                        pending.push(InputEvent::Mouse(MouseEvent {
                            button: MouseButton::Left,
                            row: i32::from(y),
                            col: i32::from(x),
                            is_release: false,
                        }));
                    } else if button_state & RIGHTMOST_BUTTON_PRESSED != 0 {
                        pending.push(InputEvent::Mouse(MouseEvent {
                            button: MouseButton::Right,
                            row: i32::from(y),
                            col: i32::from(x),
                            is_release: false,
                        }));
                    } else if button_state == 0 {
                        pending.push(InputEvent::Mouse(MouseEvent {
                            button: MouseButton::Left,
                            row: i32::from(y),
                            col: i32::from(x),
                            is_release: true,
                        }));
                    }
                }

                i += 1;
            }
            RawRecord::BufferSize => {
                pending.push(InputEvent::Resize(ResizeEvent {
                    width: window_width,
                    height: window_height,
                }));
                i += 1;
            }
            RawRecord::Other => {
                i += 1;
            }
        }
    }

    pending
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn single_keystroke_becomes_key_event() {
        let events = decode_records(
            &[RawRecord::Key {
                key_down: true,
                virtual_key_code: 65,
                unicode_char: u16::try_from(u32::from('A')).unwrap(),
                control_key_state: 0,
            }],
            80,
            25,
        );
        assert_eq!(
            events,
            vec![InputEvent::Key(KeyEvent {
                key: ConsoleKey(65),
                key_char: u16::try_from(u32::from('A')).unwrap(),
                shift: false,
                alt: false,
                control: false,
            })]
        );
    }

    #[test]
    fn key_up_events_are_skipped() {
        let events = decode_records(
            &[
                RawRecord::Key { key_down: true, virtual_key_code: 72, unicode_char: 72u16.wrapping_add(0x1000), control_key_state: 0 },
                RawRecord::Key { key_down: false, virtual_key_code: 72, unicode_char: 72u16.wrapping_add(0x1000), control_key_state: 0 },
                RawRecord::Key { key_down: true, virtual_key_code: 72, unicode_char: 72u16.wrapping_add(0x1000), control_key_state: 0 },
            ],
            80,
            25,
        );
        assert_eq!(events.len(), 1);
    }
}
