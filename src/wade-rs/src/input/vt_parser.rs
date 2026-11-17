//! Decodes the bytes a unix terminal sends in raw mode into key, mouse and
//! paste events. Pure, so it is tested on every platform. The
//! bracketed-paste state lives in the parser instance (one per input
//! source).

use super::{InputEvent, KeyEvent, MouseButton, MouseEvent};
use crate::console_key::ConsoleKey;

#[derive(Default)]
pub struct VtParser {
    in_bracketed_paste: bool,
    paste_buffer: Vec<u8>,
    /// The unfinished end of the last read (an escape sequence
    /// or UTF-8 character cut by the read boundary, or a lone ESC), joined
    /// with the next read.
    pending: Vec<u8>,
}

const ESC: u8 = 0x1B;
const PASTE_END: &[u8] = b"\x1b[201~";

fn key(key: ConsoleKey, key_char: char, shift: bool, alt: bool, control: bool) -> InputEvent {
    InputEvent::Key(KeyEvent { key, key_char, shift, alt, control })
}

impl VtParser {
    #[must_use]
    pub fn new() -> Self {
        Self::default()
    }

    /// Decodes one read. Bytes that may be the start of a sequence the next
    /// read completes are held back (`has_pending`).
    pub fn parse(&mut self, data: &[u8]) -> Vec<InputEvent> {
        let mut bytes = std::mem::take(&mut self.pending);
        bytes.extend_from_slice(data);
        self.decode(&bytes, false)
    }

    /// Bytes are held back, waiting for the next read.
    #[must_use]
    pub fn has_pending(&self) -> bool {
        !self.pending.is_empty()
    }

    /// No more bytes came: decodes what was held back as it stands. A lone
    /// ESC is the Escape key; a cut-off character is U+FFFD.
    pub fn flush(&mut self) -> Vec<InputEvent> {
        let bytes = std::mem::take(&mut self.pending);
        self.decode(&bytes, true)
    }

    fn decode(&mut self, bytes: &[u8], at_end: bool) -> Vec<InputEvent> {
        let mut events = Vec::new();
        let mut i = 0;

        while i < bytes.len() {
            if let Some(used) = self.decode_one(&bytes[i..], at_end, &mut events) {
                i += used;
            } else {
                self.pending = bytes[i..].to_vec();
                break;
            }
        }

        events
    }

    /// Decodes the event at the start of `data`; returns the bytes used,
    /// or `None` when `data` ends before the event does (never when
    /// `at_end`).
    fn decode_one(&mut self, data: &[u8], at_end: bool, events: &mut Vec<InputEvent>) -> Option<usize> {
        // Inside bracketed paste: collect bytes until ESC[201~
        if self.in_bracketed_paste {
            return self.decode_paste(data, at_end, events);
        }

        let b = data[0];
        let incomplete = |events: &mut Vec<InputEvent>| {
            // Cut off with nothing more to come: the ESC stands alone and
            // the bytes after it are decoded on their own
            at_end.then(|| {
                events.push(key(ConsoleKey::Escape, '\x1b', false, false, false));
                1
            })
        };

        match b {
            ESC if data.len() == 1 => incomplete(events),
            ESC if data[1] == b'[' => {
                let params_end = data[2..].iter().position(|b| !(0x20..=0x3F).contains(b));
                if params_end.is_none() {
                    return incomplete(events);
                }
                Some(2 + self.parse_csi(&data[2..], events))
            }
            ESC if data[1] == b'O' => {
                let Some(&final_byte) = data.get(2) else {
                    return incomplete(events);
                };
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
                    events.push(key(ss3, '\0', false, false, false));
                }

                Some(3)
            }
            // ESC before a character is Alt+that character, as terminals
            // send it
            ESC if (0x20..=0x7E).contains(&data[1]) => {
                let c = data[1];
                events.push(key(char_to_console_key(c), char::from(c), false, true, false));
                Some(2)
            }
            ESC if data[1] == 0x7F => {
                events.push(key(ConsoleKey::Backspace, '\x7f', false, true, false));
                Some(2)
            }
            ESC if data[1] >= 0x80 => {
                let len = utf8_sequence_length(data[1]);
                if data.len() < 1 + len && !at_end {
                    return None;
                }
                let end = (1 + len).min(data.len());
                events.push(key(ConsoleKey(0), decode_utf8_char(&data[1..end]), false, true, false));
                Some(end)
            }
            ESC => {
                events.push(key(ConsoleKey::Escape, '\x1b', false, false, false));
                Some(1)
            }
            0x0D => {
                events.push(key(ConsoleKey::Enter, '\r', false, false, false));
                Some(1)
            }
            0x0A => {
                events.push(key(ConsoleKey::Enter, '\n', false, false, false));
                Some(1)
            }
            0x09 => {
                events.push(key(ConsoleKey::Tab, '\t', false, false, false));
                Some(1)
            }
            0x7F => {
                events.push(key(ConsoleKey::Backspace, '\x7f', false, false, false));
                Some(1)
            }
            0x08 => {
                events.push(key(ConsoleKey::Backspace, '\x08', false, false, false));
                Some(1)
            }
            0x01..=0x1A => {
                // Ctrl+A through Ctrl+Z
                events.push(key(ConsoleKey(u16::from(b'A' + b - 1)), char::from(b), false, false, true));
                Some(1)
            }
            0x20..=0x7E => {
                events.push(key(char_to_console_key(b), char::from(b), false, false, false));
                Some(1)
            }
            0x80.. => {
                let len = utf8_sequence_length(b);
                if data.len() < len && !at_end {
                    return None;
                }
                let end = len.min(data.len());
                events.push(key(ConsoleKey(0), decode_utf8_char(&data[..end]), false, false, false));
                Some(end)
            }
            _ => Some(1),
        }
    }

    /// Paste bytes up to `ESC[201~`. An end marker cut by the read boundary
    /// is held back.
    fn decode_paste(&mut self, data: &[u8], at_end: bool, events: &mut Vec<InputEvent>) -> Option<usize> {
        if let Some(end) = data.windows(PASTE_END.len()).position(|w| w == PASTE_END) {
            self.paste_buffer.extend_from_slice(&data[..end]);
            let pasted = String::from_utf8_lossy(&std::mem::take(&mut self.paste_buffer)).into_owned();
            self.in_bracketed_paste = false;

            if !pasted.is_empty() {
                events.push(InputEvent::Paste(pasted));
            }

            return Some(end + PASTE_END.len());
        }

        // Keep back a tail that may be the start of the end marker
        let tail = (1..PASTE_END.len().min(data.len() + 1))
            .rev()
            .find(|&n| data.ends_with(&PASTE_END[..n]))
            .unwrap_or(0);
        let take = if at_end { data.len() } else { data.len() - tail };
        self.paste_buffer.extend_from_slice(&data[..take]);
        (take > 0).then_some(take)
    }

    /// A CSI sequence: returns the bytes consumed after `ESC [`.
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

        // A key in the extended (`CSI u`) form. Terminals send it unprompted:
        // iTerm2's per-profile "Report modifiers using CSI u", xterm with
        // `formatOtherKeys`, tmux with `extended-keys` in the CSI-u format,
        // and flags a program left pushed when it crashed. wade never pushes
        // or pops the kitty keyboard protocol: on Windows the input path reads
        // key records and ConPTY's win32-input-mode already carries the full
        // detail, and on Unix the protocol would only add Shift+Enter,
        // Ctrl+Enter and a Ctrl+I / Ctrl+M vocabulary the keymap does not
        // bind, plus removal of the lone-ESC wait.
        if final_byte == b'u' {
            events.extend(parse_csi_u(params));
            return i;
        }

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
                events.push(key(ConsoleKey::Tab, '\0', true, false, false));
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

        events.push(key(csi, '\0', shift, alt, ctrl));
        i
    }
}

/// The first two `;`-separated numbers.
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

/// A key reported in the extended form: `code[:alt[:base]] [;mods[:event]]
/// [;text]`. `None` drops the sequence: a malformed parameter, a release
/// event, an unmapped key or a modifier combination wade cannot represent.
fn parse_csi_u(params: &[u8]) -> Option<InputEvent> {
    let mut fields = params.split(|&b| b == b';');
    let (code_text, _) = split_once(fields.next()?, b':');
    let code = parse_decimal(code_text)?;

    // `mods` is 1 + the modifier bits; an absent or empty field means none
    let (bits, event) = match fields.next() {
        None => (0, 1),
        Some(field) => {
            let (mods_text, event_text) = split_once(field, b':');
            let bits = if mods_text.is_empty() { 0 } else { parse_decimal(mods_text)?.checked_sub(1)? };
            let event = if event_text.is_empty() { 1 } else { parse_decimal(event_text)? };
            (bits, event)
        }
    };

    // 3 is a release, which wade has no use for; Super, Hyper and Meta are
    // modifiers it cannot represent
    if event == 3 || (bits & (8 | 16 | 32)) != 0 {
        return None;
    }

    let (shift, alt, control) = (bits & 1 != 0, bits & 2 != 0, bits & 4 != 0);

    let (console_key, mut key_char) = match code {
        13 | 0xE001 => (ConsoleKey::Enter, '\r'),
        9 | 0xE002 => (ConsoleKey::Tab, '\t'),
        27 | 0xE000 => (ConsoleKey::Escape, '\x1b'),
        127 | 0xE003 => (ConsoleKey::Backspace, '\x7f'),
        0xE004 => (ConsoleKey::Insert, '\0'),
        0xE005 => (ConsoleKey::Delete, '\0'),
        0xE006 => (ConsoleKey::LeftArrow, '\0'),
        0xE007 => (ConsoleKey::RightArrow, '\0'),
        0xE008 => (ConsoleKey::UpArrow, '\0'),
        0xE009 => (ConsoleKey::DownArrow, '\0'),
        0xE00A => (ConsoleKey::PageUp, '\0'),
        0xE00B => (ConsoleKey::PageDown, '\0'),
        0xE00C => (ConsoleKey::Home, '\0'),
        0xE00D => (ConsoleKey::End, '\0'),
        0xE014..=0xE027 => (ConsoleKey(ConsoleKey::F1.vk() + (code - 0xE014) as u16), '\0'),
        0x0000..=0x007F => (char_to_console_key(code as u8), char::from_u32(code)?),
        0xE000..=0xF8FF | 0xF0000..=0xFFFFD | 0x100000..=0x10FFFD => return None,
        _ => (ConsoleKey(0), char::from_u32(code)?),
    };

    // Ctrl+<letter> carries its control byte, as the legacy encoding and the
    // Windows console do. Alt means AltGr, which types a character.
    if control && !alt && (0x20..=0x7E).contains(&code) {
        key_char = char::from_u32(code & 0x1F)?;
    } else if shift && !control && (u32::from(b'a')..=u32::from(b'z')).contains(&code) {
        // The kitty protocol reports the unshifted code point with Shift as a
        // modifier; xterm, tmux and iTerm2 report the shifted one already
        key_char = char::from_u32(code - 32)?;
    }

    Some(key(console_key, key_char, shift, alt, control))
}

/// The two halves of `data` around its first `sep`, the second empty when
/// there is none.
fn split_once(data: &[u8], sep: u8) -> (&[u8], &[u8]) {
    match data.iter().position(|&b| b == sep) {
        Some(index) => (&data[..index], &data[index + 1..]),
        None => (data, &[]),
    }
}

/// The decimal value of `data`: `None` when it is empty, carries a non-digit
/// or does not fit in a `u32`.
fn parse_decimal(data: &[u8]) -> Option<u32> {
    let mut value = 0u32;

    for &b in data {
        if !b.is_ascii_digit() {
            return None;
        }

        value = value.checked_mul(10)?.checked_add(u32::from(b - b'0'))?;
    }

    (!data.is_empty()).then_some(value)
}

/// An SGR mouse report (`ESC [ < b ; x ; y M/m`).
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

/// The console key of a typed byte.
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

/// The byte length of a UTF-8 sequence from its lead byte.
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

/// The decoded character, U+FFFD when invalid or cut off.
fn decode_utf8_char(bytes: &[u8]) -> char {
    std::str::from_utf8(bytes)
        .ok()
        .and_then(|s| s.chars().next())
        .unwrap_or(char::REPLACEMENT_CHARACTER)
}

#[cfg(test)]
mod tests {
    use super::*;

    /// One read with nothing after it, as the reader decodes it.
    fn parse(data: &[u8]) -> Vec<InputEvent> {
        let mut parser = VtParser::new();
        let mut events = parser.parse(data);
        events.extend(parser.flush());
        events
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
            assert_eq!(key.key_char, char::from(c));
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
    fn csi_u_named_keys_and_text() {
        for (seq, expected) in [
            (&b"\x1b[97u"[..], (ConsoleKey::A, 'a')),
            (b"\x1b[122u", (ConsoleKey::Z, 'z')),
            (b"\x1b[65u", (ConsoleKey::A, 'A')),
            (b"\x1b[48u", (ConsoleKey::D0, '0')),
            (b"\x1b[32u", (ConsoleKey::Spacebar, ' ')),
            (b"\x1b[46u", (ConsoleKey::OemPeriod, '.')),
            (b"\x1b[13u", (ConsoleKey::Enter, '\r')),
            (b"\x1b[9u", (ConsoleKey::Tab, '\t')),
            (b"\x1b[27u", (ConsoleKey::Escape, '\x1b')),
            (b"\x1b[127u", (ConsoleKey::Backspace, '\x7f')),
            ("\x1b[233u".as_bytes(), (ConsoleKey(0), 'é')),
            ("\x1b[128512u".as_bytes(), (ConsoleKey(0), '😀')),
        ] {
            assert_eq!(keys(&parse(seq)), [(expected.0, expected.1, false)], "{seq:?}");
        }
    }

    #[test]
    fn csi_u_extended_keys() {
        for (seq, expected) in [
            (&b"\x1b[57352u"[..], ConsoleKey::UpArrow),
            (b"\x1b[57349u", ConsoleKey::Delete),
            (b"\x1b[57348u", ConsoleKey::Insert),
            (b"\x1b[57364u", ConsoleKey::F1),
            (b"\x1b[57383u", ConsoleKey::F20),
        ] {
            assert_eq!(keys(&parse(seq)), [(expected, '\0', false)], "{seq:?}");
        }

        // Keypad and F21 and above are code points wade has no key for
        assert!(parse(b"\x1b[57399u").is_empty());
        assert!(parse(b"\x1b[57384u").is_empty());
    }

    #[test]
    fn csi_u_modifiers() {
        // Ctrl+I is its own key with a control byte, not Tab
        let key = single_key(b"\x1b[105;5u");
        assert_eq!((key.key, key.key_char, key.control), (ConsoleKey::I, '\t', true));
        assert_ne!(key.key, ConsoleKey::Tab);

        let key = single_key(b"\x1b[97;5u");
        assert_eq!((key.key, key.key_char, key.control), (ConsoleKey::A, '\u{1}', true));

        // Ctrl+[ is its own key, not Escape
        let key = single_key(b"\x1b[91;5u");
        assert_eq!((key.key, key.key_char, key.control), (ConsoleKey(0), '\x1b', true));

        // Shift with an unshifted code point (kitty) and with a shifted one
        // (xterm, tmux, iTerm2) both type the upper-case letter
        for seq in [&b"\x1b[97;2u"[..], b"\x1b[65;2u"] {
            let key = single_key(seq);
            assert_eq!((key.key, key.key_char, key.shift, key.control), (ConsoleKey::A, 'A', true, false), "{seq:?}");
        }

        // AltGr arrives as Ctrl+Alt and still types its character
        let key = single_key(b"\x1b[97;7u");
        assert_eq!((key.key_char, key.control, key.alt), ('a', true, true));
    }

    #[test]
    fn csi_u_lock_modifiers_are_ignored() {
        for mods in [65, 129] {
            let seq = format!("\x1b[97;{mods}u");
            assert_eq!(keys(&parse(seq.as_bytes())), [(ConsoleKey::A, 'a', false)], "{seq}");
        }
    }

    #[test]
    fn csi_u_unsupported_modifiers_are_dropped() {
        // Super, Hyper and Meta, alone and with the modifiers wade knows
        for mods in [9, 17, 33, 13, 25, 41] {
            let seq = format!("\x1b[97;{mods}u");
            assert!(parse(seq.as_bytes()).is_empty(), "{seq}");
        }
    }

    #[test]
    fn csi_u_event_types_and_extra_fields() {
        assert!(parse(b"\x1b[97;1:3u").is_empty(), "release");
        assert_eq!(single_key(b"\x1b[97;1:2u").key, ConsoleKey::A, "repeat is a press");
        assert_eq!(single_key(b"\x1b[97;;97u").key, ConsoleKey::A, "the text field is ignored");
        assert_eq!(single_key(b"\x1b[97:3;1:1u").key, ConsoleKey::A, "the alternate-key field is ignored");
        assert_eq!(single_key(b"\x1b[97;5;97u").key_char, '\u{1}', "the text field does not override the control byte");
    }

    #[test]
    fn csi_u_invalid_parameters_are_dropped() {
        for seq in [
            &b"\x1b[u"[..],
            b"\x1b[;5u",
            b"\x1b[<1u",
            b"\x1b[>4u",
            b"\x1b[?u",
            b"\x1b[97;=u",
            b"\x1b[97;0u",
            b"\x1b[55296u",
            b"\x1b[1114112u",
            b"\x1b[4294967296u",
            b"\x1b[99999999999u",
            b"\x1b[97;99999999999u",
        ] {
            assert!(parse(seq).is_empty(), "{seq:?}");
        }
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
        assert_eq!(single_key(&[0xC3, 0xA9]).key_char, 'é');
        assert_eq!(single_key(&[0xE2, 0x82, 0xAC]).key_char, '€');
        assert_eq!(single_key("😀".as_bytes()).key_char, '😀');
        // A truncated sequence with nothing after it is U+FFFD
        assert_eq!(single_key(&[0xE2, 0x82]).key_char, char::REPLACEMENT_CHARACTER);
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

    fn keys(events: &[InputEvent]) -> Vec<(ConsoleKey, char, bool)> {
        events
            .iter()
            .map(|event| match event {
                InputEvent::Key(key) => (key.key, key.key_char, key.alt),
                other => panic!("expected key events, got {other:?}"),
            })
            .collect()
    }

    #[test]
    fn escape_before_a_character_is_alt() {
        assert_eq!(keys(&parse(b"\x1bj")), [(ConsoleKey::J, 'j', true)]);
        assert_eq!(keys(&parse(b"\x1bJ")), [(ConsoleKey::J, 'J', true)]);
        assert_eq!(keys(&parse("\x1bé".as_bytes())), [(ConsoleKey(0), 'é', true)]);
        assert_eq!(keys(&parse(b"\x1b\x7f")), [(ConsoleKey::Backspace, '\x7f', true)]);
        // Before a control character or another ESC, Escape stands alone
        assert_eq!(keys(&parse(b"\x1b\r")), [(ConsoleKey::Escape, '\x1b', false), (ConsoleKey::Enter, '\r', false)]);
        assert_eq!(
            keys(&parse(b"\x1b\x1b")),
            [(ConsoleKey::Escape, '\x1b', false), (ConsoleKey::Escape, '\x1b', false)]
        );
    }

    #[test]
    fn a_lone_escape_waits_for_the_next_read() {
        let mut parser = VtParser::new();
        assert!(parser.parse(b"j\x1b").len() == 1 && parser.has_pending());
        assert_eq!(keys(&parser.parse(b"[B")), [(ConsoleKey::DownArrow, '\0', false)]);
        assert!(!parser.has_pending());

        assert!(parser.parse(b"\x1b").is_empty());
        assert_eq!(keys(&parser.flush()), [(ConsoleKey::Escape, '\x1b', false)]);
    }

    #[test]
    fn sequences_and_characters_cut_by_a_read_are_joined() {
        let mut parser = VtParser::new();
        for (first, second) in [
            (&b"\x1b["[..], &b"1;5C"[..]),
            (b"\x1b[1;", b"5C"),
            (b"\x1bO", b"B"),
            (b"\x1b[<0;10", b";5M"),
            (b"\x1b[97;5", b"u"),
            (b"\xf0\x9f", b"\x98\x80"),
        ] {
            assert!(parser.parse(first).is_empty(), "{first:?} is held back");
            assert!(parser.has_pending());
            assert_eq!(parser.parse(second).len(), 1, "{first:?} + {second:?}");
            assert!(!parser.has_pending());
        }

        assert!(parser.parse("é".as_bytes()[..1].as_ref()).is_empty());
        assert_eq!(keys(&parser.parse(&"é".as_bytes()[1..])), [(ConsoleKey(0), 'é', false)]);
    }

    #[test]
    fn an_unfinished_sequence_with_nothing_after_it_is_escape_then_text() {
        assert_eq!(keys(&parse(b"\x1b[")), [(ConsoleKey::Escape, '\x1b', false), (ConsoleKey(0), '[', false)]);
    }

    #[test]
    fn a_paste_end_marker_cut_by_a_read_is_joined() {
        let mut parser = VtParser::new();
        for cut in 1..PASTE_END.len() {
            let events = parser.parse(&[b"\x1b[200~text".as_slice(), &PASTE_END[..cut]].concat());
            assert!(events.is_empty(), "cut {cut}");
            let events = parser.parse(&[&PASTE_END[cut..], b"x".as_slice()].concat());
            assert_eq!(events.len(), 2, "cut {cut}");
            assert_eq!(events[0], InputEvent::Paste("text".to_string()), "cut {cut}");
        }
    }
}
