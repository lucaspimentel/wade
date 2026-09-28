//! Port of System.ConsoleKey: a virtual-key code classification.
//!
//! C# code freely casts raw virtual-key codes to ConsoleKey
//! (`(ConsoleKey)k.wVirtualKeyCode`), so any u16 value is valid. This port
//! models that honestly with a newtype over u16 plus named constants whose
//! discriminants match the .NET enum exactly
//! (dotnet/runtime src/libraries/System.Console/src/System/ConsoleKey.cs).

#[derive(Clone, Copy, PartialEq, Eq, Hash, Debug)]
pub struct ConsoleKey(pub u16);

impl ConsoleKey {
    #[must_use]
    pub const fn vk(self) -> u16 {
        self.0
    }
}

#[allow(non_upper_case_globals)]
impl ConsoleKey {
    pub const None: Self = Self(0x0);
    pub const Backspace: Self = Self(0x8);
    pub const Tab: Self = Self(0x9);
    pub const Clear: Self = Self(0xC);
    pub const Enter: Self = Self(0xD);
    pub const Pause: Self = Self(0x13);
    pub const Escape: Self = Self(0x1B);
    pub const Spacebar: Self = Self(0x20);
    pub const PageUp: Self = Self(0x21);
    pub const PageDown: Self = Self(0x22);
    pub const End: Self = Self(0x23);
    pub const Home: Self = Self(0x24);
    pub const LeftArrow: Self = Self(0x25);
    pub const UpArrow: Self = Self(0x26);
    pub const RightArrow: Self = Self(0x27);
    pub const DownArrow: Self = Self(0x28);
    pub const Select: Self = Self(0x29);
    pub const Print: Self = Self(0x2A);
    pub const Execute: Self = Self(0x2B);
    pub const PrintScreen: Self = Self(0x2C);
    pub const Insert: Self = Self(0x2D);
    pub const Delete: Self = Self(0x2E);
    pub const Help: Self = Self(0x2F);
    pub const D0: Self = Self(0x30);
    pub const D1: Self = Self(0x31);
    pub const D2: Self = Self(0x32);
    pub const D3: Self = Self(0x33);
    pub const D4: Self = Self(0x34);
    pub const D5: Self = Self(0x35);
    pub const D6: Self = Self(0x36);
    pub const D7: Self = Self(0x37);
    pub const D8: Self = Self(0x38);
    pub const D9: Self = Self(0x39);
    pub const A: Self = Self(0x41);
    pub const B: Self = Self(0x42);
    pub const C: Self = Self(0x43);
    pub const D: Self = Self(0x44);
    pub const E: Self = Self(0x45);
    pub const F: Self = Self(0x46);
    pub const G: Self = Self(0x47);
    pub const H: Self = Self(0x48);
    pub const I: Self = Self(0x49);
    pub const J: Self = Self(0x4A);
    pub const K: Self = Self(0x4B);
    pub const L: Self = Self(0x4C);
    pub const M: Self = Self(0x4D);
    pub const N: Self = Self(0x4E);
    pub const O: Self = Self(0x4F);
    pub const P: Self = Self(0x50);
    pub const Q: Self = Self(0x51);
    pub const R: Self = Self(0x52);
    pub const S: Self = Self(0x53);
    pub const T: Self = Self(0x54);
    pub const U: Self = Self(0x55);
    pub const V: Self = Self(0x56);
    pub const W: Self = Self(0x57);
    pub const X: Self = Self(0x58);
    pub const Y: Self = Self(0x59);
    pub const Z: Self = Self(0x5A);
    pub const LeftWindows: Self = Self(0x5B);
    pub const RightWindows: Self = Self(0x5C);
    pub const Applications: Self = Self(0x5D);
    pub const Sleep: Self = Self(0x5F);
    pub const NumPad0: Self = Self(0x60);
    pub const NumPad1: Self = Self(0x61);
    pub const NumPad2: Self = Self(0x62);
    pub const NumPad3: Self = Self(0x63);
    pub const NumPad4: Self = Self(0x64);
    pub const NumPad5: Self = Self(0x65);
    pub const NumPad6: Self = Self(0x66);
    pub const NumPad7: Self = Self(0x67);
    pub const NumPad8: Self = Self(0x68);
    pub const NumPad9: Self = Self(0x69);
    pub const Multiply: Self = Self(0x6A);
    pub const Add: Self = Self(0x6B);
    pub const Separator: Self = Self(0x6C);
    pub const Subtract: Self = Self(0x6D);
    pub const Decimal: Self = Self(0x6E);
    pub const Divide: Self = Self(0x6F);
    pub const F1: Self = Self(0x70);
    pub const F2: Self = Self(0x71);
    pub const F3: Self = Self(0x72);
    pub const F4: Self = Self(0x73);
    pub const F5: Self = Self(0x74);
    pub const F6: Self = Self(0x75);
    pub const F7: Self = Self(0x76);
    pub const F8: Self = Self(0x77);
    pub const F9: Self = Self(0x78);
    pub const F10: Self = Self(0x79);
    pub const F11: Self = Self(0x7A);
    pub const F12: Self = Self(0x7B);
    pub const F13: Self = Self(0x7C);
    pub const F14: Self = Self(0x7D);
    pub const F15: Self = Self(0x7E);
    pub const F16: Self = Self(0x7F);
    pub const F17: Self = Self(0x80);
    pub const F18: Self = Self(0x81);
    pub const F19: Self = Self(0x82);
    pub const F20: Self = Self(0x83);
    pub const F21: Self = Self(0x84);
    pub const F22: Self = Self(0x85);
    pub const F23: Self = Self(0x86);
    pub const F24: Self = Self(0x87);
    pub const BrowserBack: Self = Self(0xA6);
    pub const BrowserForward: Self = Self(0xA7);
    pub const BrowserRefresh: Self = Self(0xA8);
    pub const BrowserStop: Self = Self(0xA9);
    pub const BrowserSearch: Self = Self(0xAA);
    pub const BrowserFavorites: Self = Self(0xAB);
    pub const BrowserHome: Self = Self(0xAC);
    pub const VolumeMute: Self = Self(0xAD);
    pub const VolumeDown: Self = Self(0xAE);
    pub const VolumeUp: Self = Self(0xAF);
    pub const MediaNext: Self = Self(0xB0);
    pub const MediaPrevious: Self = Self(0xB1);
    pub const MediaStop: Self = Self(0xB2);
    pub const MediaPlay: Self = Self(0xB3);
    pub const LaunchMail: Self = Self(0xB4);
    pub const LaunchMediaSelect: Self = Self(0xB5);
    pub const LaunchApp1: Self = Self(0xB6);
    pub const LaunchApp2: Self = Self(0xB7);
    pub const Oem1: Self = Self(0xBA);
    pub const OemPlus: Self = Self(0xBB);
    pub const OemComma: Self = Self(0xBC);
    pub const OemMinus: Self = Self(0xBD);
    pub const OemPeriod: Self = Self(0xBE);
    pub const Oem2: Self = Self(0xBF);
    pub const Oem3: Self = Self(0xC0);
    pub const Oem4: Self = Self(0xDB);
    pub const Oem5: Self = Self(0xDC);
    pub const Oem6: Self = Self(0xDD);
    pub const Oem7: Self = Self(0xDE);
    pub const Oem8: Self = Self(0xDF);
    pub const Oem102: Self = Self(0xE2);
    pub const Process: Self = Self(0xE5);
    pub const Packet: Self = Self(0xE7);
    pub const Attention: Self = Self(0xF6);
    pub const CrSel: Self = Self(0xF7);
    pub const ExSel: Self = Self(0xF8);
    pub const EraseEndOfFile: Self = Self(0xF9);
    pub const Play: Self = Self(0xFA);
    pub const Zoom: Self = Self(0xFB);
    pub const NoName: Self = Self(0xFC);
    pub const Pa1: Self = Self(0xFD);
    pub const OemClear: Self = Self(0xFE);
}

#[cfg(test)]
mod tests {
    use super::ConsoleKey;

    #[test]
    fn discriminants_match_dotnet_values() {
        assert_eq!(ConsoleKey::UpArrow.vk(), 38);
        assert_eq!(ConsoleKey::DownArrow.vk(), 40);
        assert_eq!(ConsoleKey::A.vk(), 65);
        assert_eq!(ConsoleKey::Escape.vk(), 27);
        assert_eq!(ConsoleKey::Enter.vk(), 13);
        assert_eq!(ConsoleKey::F1.vk(), 112);
        assert_eq!(ConsoleKey::OemMinus.vk(), 189);
    }
}
