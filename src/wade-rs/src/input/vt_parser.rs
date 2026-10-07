//! Port of `VtParser` (src/Wade/Terminal/UnixInputSource.cs): decodes the
//! bytes a unix terminal sends in raw mode into key, mouse and paste
//! events. Pure, so it is tested on every platform. C# keeps the
//! bracketed-paste state in statics; here it lives in the parser instance
//! (one per input source).

use super::{InputEvent, KeyEvent, MouseButton, MouseEvent};
use crate::console_key::ConsoleKey;

#[derive(Default)]
pub struct VtParser {
    in_bracketed_paste: bool,
    paste_buffer: Vec<u8>,
}

const ESC: u8 = 0x1B;

fn key(key: ConsoleKey, key_char: u16, shift: bool, alt: bool, control: bool) -> InputEvent {
    InputEvent::Key(KeyEvent { key, key_char, shift, alt, control })
}

impl VtParser {
    #[must_use]
    pub fn new() -> Self {
        Self::default()
    }

    /// Port of `VtParser.Parse`.
    pub fn parse(&mut self, data: &[u8]) -> Vec<InputEvent> {
        let mut events = Vec::new();
        let mut i = 0;

        while i < data.len() {
            let b = data[i];

            // Inside bracketed paste: collect bytes until ESC[201~
            if self.in_bracketed_paste {
                if b == ESC && i + 5 <= data.len() && data.get(i + 1..i + 6) == Some(b"[201~".as_slice()) {
                    let pasted = String::from_utf8_lossy(&std::mem::take(&mut self.paste_buffer)).into_owned();
                    self.in_bracketed_paste = false;

                    if !pasted.is_empty() {
                        events.push(InputEvent::Paste(pasted));
                    }

                    i += 6;
                } else {
                    self.paste_buffer.push(b);
                    i += 1;
                }

                continue;
            }

            match b {
                ESC if data.get(i + 1) == Some(&b'[') => {
                    i += 2;
                    i += self.parse_csi(&data[i..], &mut events);
                }
                ESC if data.get(i + 1) == Some(&b'O') => {
                    i += 2;

                    if let Some(&final_byte) = data.get(i) {
                        let ss3 = match final_byte {
                            b'A' => ConsoleKey::UpArrow,
                            b'B' => ConsoleKey::DownArrow,
                            b'C' => ConsoleKey::RightArrow,
                            b'D' => ConsoleKey::LeftArrow,
                            b'P' => ConsoleKey::F1,
                            b'Q' => ConsoleKey::F2,
                            b'R' => ConsoleKey::F3,
                            b'S' => ConsoleKey::F4,
                            b'H' => ConsoleKey::Home,
                            b'F' => ConsoleKey::End,
                            _ => ConsoleKey(0),
                        };

                        if ss3 != ConsoleKey(0) {
                            events.push(key(ss3, 0, false, false, false));
                        }

                        i += 1;
                    }
                }
                ESC => {
                    events.push(key(ConsoleKey::Escape, 0x1B, false, false, false));
                    i += 1;
                }
                0x0D => {
                    events.push(key(ConsoleKey::Enter, u16::from(b'\r'), false, false, false));
                    i += 1;
                }
                0x0A => {
                    events.push(key(ConsoleKey::Enter, u16::from(b'\n'), false, false, false));
                    i += 1;
                }
                0x09 => {
                    events.push(key(ConsoleKey::Tab, u16::from(b'\t'), false, false, false));
                    i += 1;
                }
                0x7F => {
                    events.push(key(ConsoleKey::Backspace, 0x7F, false, false, false));
                    i += 1;
                }
                0x08 => {
                    events.push(key(ConsoleKey::Backspace, 0x08, false, false, false));
                    i += 1;
                }
                0x01..=0x1A => {
                    // Ctrl+A through Ctrl+Z
                    events.push(key(ConsoleKey(u16::from(b'A' + b - 1)), u16::from(b), false, false, true));
                    i += 1;
                }
                0x20..=0x7E => {
                    events.push(key(char_to_console_key(b), u16::from(b), false, false, false));
                    i += 1;
                }
                0x80.. => {
                    let len = utf8_sequence_length(b);

                    if let Some(bytes) = data.get(i..i + len) {
                        events.push(key(ConsoleKey(0), decode_utf8_char(bytes), false, false, false));
                    }

                    i += len;
                }
                _ => i += 1,
            }
        }

        events
    }

    /// Port of `ParseCsi`: returns the bytes consumed after `ESC [`.
    fn parse_csi(&mut self, data: &[u8], events: &mut Vec<InputEvent>) -> usize {
        let mut i = 0;

        // Parameter and intermediate bytes, then a final byte
        while i < data.len() && (0x20..=0x3F).contains(&data[i]) {
            i += 1;
        }

        if i >= data.len() {
            return i;
        }

        let final_byte = data[i];
        let params = &data[..i];
        i += 1;

        // SGR mouse: ESC [ < Cb ; Cx ; Cy M/m
        if params.first() == Some(&b'<') {
            events.push(parse_sgr_mouse(&params[1..], final_byte));
            return i;
        }

        let (param1, param2) = parse_csi_params(params);

        // xterm modifier: 1 + flags (Shift=1, Alt=2, Ctrl=4)
        let modifiers = if param2 > 0 { param2 - 1 } else { 0 };
        let (shift, alt, ctrl) = (modifiers & 1 != 0, modifiers & 2 != 0, modifiers & 4 != 0);

        let csi = match final_byte {
            b'A' => ConsoleKey::UpArrow,
            b'B' => ConsoleKey::DownArrow,
            b'C' => ConsoleKey::RightArrow,
            b'D' => ConsoleKey::LeftArrow,
            b'H' => ConsoleKey::Home,
            b'F' => ConsoleKey::End,
            b'Z' => {
                events.push(key(ConsoleKey::Tab, 0, true, false, false));
                return i;
            }
            b'~' if param1 == 200 => {
                // Start of bracketed paste: collect bytes until ESC[201~
                self.in_bracketed_paste = true;
                self.paste_buffer.clear();
                return i;
            }
            b'~' => match param1 {
                1 => ConsoleKey::Home,
                2 => ConsoleKey::Insert,
                3 => ConsoleKey::Delete,
                4 => ConsoleKey::End,
                5 => ConsoleKey::PageUp,
                6 => ConsoleKey::PageDown,
                15 => ConsoleKey::F5,
                17 => ConsoleKey::F6,
                18 => ConsoleKey::F7,
                19 => ConsoleKey::F8,
                20 => ConsoleKey::F9,
                21 => ConsoleKey::F10,
                23 => ConsoleKey::F11,
                24 => ConsoleKey::F12,
                _ => return i,
            },
            _ => return i,
        };

        events.push(key(csi, 0, shift, alt, ctrl));
        i
    }
}

/// Port of `ParseCsiParams`: the first two `;`-separated numbers.
fn parse_csi_params(data: &[u8]) -> (i32, i32) {
    let mut param1 = 0i32;
    let mut current = 0i32;
    let mut in_second = false;

    for &b in data {
        if b == b';' {
            param1 = current;
            current = 0;
            in_second = true;
        } else if b.is_ascii_digit() {
            current = current.wrapping_mul(10).wrapping_add(i32::from(b - b'0'));
        }
    }

    if in_second { (param1, current) } else { (current, 0) }
}

/// Port of `ParseSgrMouse`.
fn parse_sgr_mouse(params: &[u8], final_byte: u8) -> InputEvent {
    let (mut cb, mut cx, mut cy) = (0i32, 0i32, 0i32);
    let mut field = 0;
    let mut current = 0i32;

    for &b in params {
        if b == b';' {
            match field {
                0 => cb = current,
                1 => cx = current,
                _ => {}
            }

            current = 0;
            field += 1;
        } else if b.is_ascii_digit() {
            current = current.wrapping_mul(10).wrapping_add(i32::from(b - b'0'));
        }
    }

    if field == 2 {
        cy = current;
    }

    let button = if cb >= 64 {
        if cb == 64 { MouseButton::ScrollUp } else { MouseButton::ScrollDown }
    } else {
        match cb & 0x03 {
            0 => MouseButton::Left,
            1 => MouseButton::Middle,
            2 => MouseButton::Right,
            _ => MouseButton::None,
        }
    };

    // Cx, Cy are 1-based
    InputEvent::Mouse(MouseEvent {
        button,
        row: cy - 1,
        col: cx - 1,
        is_release: final_byte == b'm',
    })
}

/// Port of `CharToConsoleKey`.
fn char_to_console_key(b: u8) -> ConsoleKey {
    match b {
        b'a'..=b'z' => ConsoleKey(u16::from(b - b'a' + b'A')),
        b'A'..=b'Z' | b'0'..=b'9' => ConsoleKey(u16::from(b)),
        b' ' => ConsoleKey::Spacebar,
        b'/' => ConsoleKey::Divide,
        b'*' => ConsoleKey::Multiply,
        b'-' => ConsoleKey::OemMinus,
        b'+' => ConsoleKey::OemPlus,
        b'.' => ConsoleKey::OemPeriod,
        b',' => ConsoleKey::OemComma,
        _ => ConsoleKey(0),
    }
}

/// Port of `Utf8SequenceLength`.
const fn utf8_sequence_length(lead: u8) -> usize {
    if lead & 0xE0 == 0xC0 {
        2
    } else if lead & 0xF0 == 0xE0 {
        3
    } else if lead & 0xF8 == 0xF0 {
        4
    } else {
        1
    }
}

/// Port of `DecodeUtf8Char`: the first UTF-16 unit of the decoded text
/// (a high surrogate for astral characters), U+FFFD when invalid.
fn decode_utf8_char(bytes: &[u8]) -> u16 {
    String::from_utf8_lossy(bytes).encode_utf16().next().unwrap_or(0xFFFD)
}

#[cfg(test)]
mod tests {
    use super::*;

    fn parse(data: &[u8]) -> Vec<InputEvent> {
        VtParser::new().parse(data)
    }

    fn single_key(data: &[u8]) -> KeyEvent {
        match parse(data).as_slice() {
            [InputEvent::Key(key)] => *key,
            other => panic!("expected one key event, got {other:?}"),
        }
    }

    fn single_mouse(data: &[u8]) -> MouseEvent {
        match parse(data).as_slice() {
            [InputEvent::Mouse(mouse)] => *mouse,
            other => panic!("expected one mouse event, got {other:?}"),
        }
    }

    #[test]
    fn printable_ascii() {
        for (c, expected) in [
            (b'a', ConsoleKey::A),
            (b'z', ConsoleKey::Z),
            (b'A', ConsoleKey::A),
            (b'Z', ConsoleKey::Z),
            (b'0', ConsoleKey::D0),
            (b'9', ConsoleKey::D9),
            (b' ', ConsoleKey::Spacebar),
        ] {
            let key = single_key(&[c]);
            assert_eq!(key.key, expected);
            assert_eq!(key.key_char, u16::from(c));
        }
    }

    #[test]
    fn csi_sequences() {
        for (suffix, expected) in [
            ("A", ConsoleKey::UpArrow),
            ("B", ConsoleKey::DownArrow),
            ("C", ConsoleKey::RightArrow),
            ("D", ConsoleKey::LeftArrow),
            ("H", ConsoleKey::Home),
            ("F", ConsoleKey::End),
            ("5~", ConsoleKey::PageUp),
            ("6~", ConsoleKey::PageDown),
            ("1~", ConsoleKey::Home),
            ("4~", ConsoleKey::End),
            ("2~", ConsoleKey::Insert),
            ("3~", ConsoleKey::Delete),
        ] {
            assert_eq!(single_key(format!("\x1b[{suffix}").as_bytes()).key, expected, "{suffix}");
        }
    }

    #[test]
    fn csi_modifiers() {
        let key = single_key(b"\x1b[1;5C");
        assert_eq!((key.key, key.shift, key.alt, key.control), (ConsoleKey::RightArrow, false, false, true));
        let key = single_key(b"\x1b[1;4A");
        assert_eq!((key.shift, key.alt, key.control), (true, true, false));
        let key = single_key(b"\x1b[Z");
        assert_eq!((key.key, key.shift), (ConsoleKey::Tab, true));
    }

    #[test]
    fn standalone_escape_and_specials() {
        assert_eq!(single_key(&[0x1B]).key, ConsoleKey::Escape);
        assert_eq!(single_key(&[0x0D]).key, ConsoleKey::Enter);
        assert_eq!(single_key(&[0x09]).key, ConsoleKey::Tab);
        assert_eq!(single_key(&[0x7F]).key, ConsoleKey::Backspace);
    }

    #[test]
    fn control_characters() {
        for (b, expected) in
            [(0x01, ConsoleKey::A), (0x03, ConsoleKey::C), (0x04, ConsoleKey::D), (0x1A, ConsoleKey::Z)]
        {
            let key = single_key(&[b]);
            assert_eq!(key.key, expected);
            assert!(key.control);
        }
    }

    #[test]
    fn sgr_mouse() {
        let press = single_mouse(b"\x1b[<0;10;5M");
        assert_eq!((press.button, press.row, press.col, press.is_release), (MouseButton::Left, 4, 9, false));
        let release = single_mouse(b"\x1b[<0;10;5m");
        assert_eq!((release.button, release.is_release), (MouseButton::Left, true));
        assert_eq!(single_mouse(b"\x1b[<64;10;5M").button, MouseButton::ScrollUp);
        assert_eq!(single_mouse(b"\x1b[<65;10;5M").button, MouseButton::ScrollDown);
        assert_eq!(single_mouse(b"\x1b[<2;10;5M").button, MouseButton::Right);
    }

    #[test]
    fn utf8_multibyte() {
        assert_eq!(single_key(&[0xC3, 0xA9]).key_char, 'é' as u16);
        assert_eq!(single_key(&[0xE2, 0x82, 0xAC]).key_char, '€' as u16);
        // Truncated sequences are skipped
        assert!(parse(&[0xE2, 0x82]).is_empty());
    }

    #[test]
    fn ss3_keys() {
        for (b, expected) in
            [(b'P', ConsoleKey::F1), (b'Q', ConsoleKey::F2), (b'R', ConsoleKey::F3), (b'S', ConsoleKey::F4)]
        {
            assert_eq!(single_key(&[0x1B, b'O', b]).key, expected);
        }
    }

    #[test]
    fn bracketed_paste_spans_reads() {
        let mut parser = VtParser::new();
        assert!(parser.parse(b"\x1b[200~caf\xc3").is_empty());
        let events = parser.parse(b"\xa9 ok\x1b[201~x");
        assert_eq!(events.len(), 2);
        assert_eq!(events[0], InputEvent::Paste("café ok".to_string()));
        assert!(matches!(events[1], InputEvent::Key(KeyEvent { key: ConsoleKey::X, .. })));
    }
}
