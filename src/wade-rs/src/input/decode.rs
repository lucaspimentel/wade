//! Port of the decode loop in src/Wade/Terminal/WindowsInputSource.cs
//! (ProcessRecords / DecodeRecords). Pure: no console access, so the fixture
//! harnesses run identically on every platform.

use super::{InputEvent, KeyEvent, MouseButton, MouseEvent, ResizeEvent};
use crate::console_key::ConsoleKey;

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
        // A lone surrogate (half of a pair split across reads) is no char
        key_char: char::from_u32(u32::from(unicode_char)).unwrap_or(char::REPLACEMENT_CHARACTER),
        shift: control_key_state & SHIFT_PRESSED != 0,
        alt: control_key_state & (LEFT_ALT_PRESSED | RIGHT_ALT_PRESSED) != 0,
        control: control_key_state & (LEFT_CTRL_PRESSED | RIGHT_CTRL_PRESSED) != 0,
    }
}

/// Port of `WindowsInputSource.DecodeRecords`: decodes raw console input
/// records into input events. `window_width`/`window_height` mirror the live
/// console size the C# call site passes in.
pub fn decode_records(records: &[RawRecord], window_width: i32, window_height: i32) -> Vec<InputEvent> {
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
                            RawRecord::Key { unicode_char: next_char, .. } => {
                                // Rust only: a line break or tab between pasted
                                // characters belongs to the paste, so pasting
                                // several lines never presses Enter in between
                                if is_paste_whitespace(next_char) && printable_follows(records, i + 1) {
                                    paste_chars.push(next_char);
                                    i += 1;
                                    continue;
                                }

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

                        pending
                            .push(InputEvent::Key(make_key_event(virtual_key_code, unicode_char, control_key_state)));
                    }

                    continue;
                }

                pending.push(InputEvent::Key(make_key_event(virtual_key_code, unicode_char, control_key_state)));

                i += 1;
            }
            RawRecord::Mouse { x, y, button_state, event_flags } => {
                if event_flags == MOUSE_MOVED {
                    i += 1;
                    continue; // ignore mouse move
                }

                if event_flags == MOUSE_WHEELED {
                    // High word of dwButtonState is a signed wheel delta
                    let hi_word = ((button_state >> 16) & 0xFFFF) as u16;
                    let signed = i16::from_ne_bytes(hi_word.to_ne_bytes());
                    let scroll_button = if signed > 0 { MouseButton::ScrollUp } else { MouseButton::ScrollDown };
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

/// Enter (CR or LF) or Tab, as a paste delivers line breaks and tabs.
fn is_paste_whitespace(unicode_char: u16) -> bool {
    matches!(unicode_char, 0x0D | 0x0A | 0x09)
}

/// A printable key-down comes later in the batch, past key-ups and more
/// line breaks or tabs.
fn printable_follows(records: &[RawRecord], from: usize) -> bool {
    for record in &records[from..] {
        match *record {
            RawRecord::Key { key_down: false, .. } => {}
            RawRecord::Key { unicode_char, .. } if is_paste_whitespace(unicode_char) => {}
            RawRecord::Key { unicode_char, .. } => return u32::from(unicode_char) >= u32::from(' '),
            _ => return false,
        }
    }

    false
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
                key_char: 'A',
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
                RawRecord::Key {
                    key_down: true,
                    virtual_key_code: 72,
                    unicode_char: 72u16.wrapping_add(0x1000),
                    control_key_state: 0,
                },
                RawRecord::Key {
                    key_down: false,
                    virtual_key_code: 72,
                    unicode_char: 72u16.wrapping_add(0x1000),
                    control_key_state: 0,
                },
                RawRecord::Key {
                    key_down: true,
                    virtual_key_code: 72,
                    unicode_char: 72u16.wrapping_add(0x1000),
                    control_key_state: 0,
                },
            ],
            80,
            25,
        );
        assert_eq!(events.len(), 1);
    }

    fn key_down(unicode_char: char) -> RawRecord {
        RawRecord::Key {
            key_down: true,
            virtual_key_code: 0,
            unicode_char: u16::try_from(u32::from(unicode_char)).unwrap(),
            control_key_state: 0,
        }
    }

    fn decode_chars(chars: &str) -> Vec<InputEvent> {
        let records: Vec<RawRecord> = chars.chars().map(key_down).collect();
        decode_records(&records, 80, 25)
    }

    fn is_key(event: &InputEvent, key_char: char) -> bool {
        matches!(event, InputEvent::Key(key) if key.key_char == key_char)
    }

    #[test]
    fn line_breaks_and_tabs_between_pasted_characters_stay_in_the_paste() {
        assert_eq!(decode_chars("ab\rcd"), [InputEvent::Paste("ab\rcd".to_string())]);
        assert_eq!(decode_chars("a\r\n\tb"), [InputEvent::Paste("a\r\n\tb".to_string())]);
        // A key-up between them changes nothing
        let records = [
            key_down('a'),
            key_down('\r'),
            RawRecord::Key {
                key_down: false,
                virtual_key_code: 13,
                unicode_char: 13,
                control_key_state: 0,
            },
            key_down('b'),
        ];
        assert_eq!(decode_records(&records, 80, 25), [InputEvent::Paste("a\rb".to_string())]);
    }

    #[test]
    fn a_trailing_line_break_is_still_enter() {
        let events = decode_chars("ab\r");
        assert_eq!(events.len(), 2);
        assert_eq!(events[0], InputEvent::Paste("ab".to_string()));
        assert!(is_key(&events[1], '\r'));

        // One character then Enter is a keystroke, then Enter
        let events = decode_chars("a\r");
        assert!(events.len() == 2 && is_key(&events[0], 'a') && is_key(&events[1], '\r'));
    }

    #[test]
    fn a_line_break_before_a_control_key_ends_the_paste() {
        // Ctrl+C after the line break: the Enter is not part of the paste
        let events = decode_chars("ab\r\u{3}");
        assert_eq!(events.len(), 3);
        assert_eq!(events[0], InputEvent::Paste("ab".to_string()));
        assert!(is_key(&events[1], '\r') && is_key(&events[2], '\u{3}'));
    }
}
