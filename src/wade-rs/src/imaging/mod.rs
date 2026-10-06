//! Port of `src/Wade/Imaging`: the Sixel encoder, image loading for
//! previews, and the PDF-to-image pipeline. Pixels need not match C#
//! (ImageSharp): decoding and scaling use the `image` crate
//! (KNOWN_DEVIATIONS.md).

pub mod image_preview;
pub mod iterm;
pub mod kitty;
pub mod pdf;
pub mod sixel;

/// Rust-only: the image protocol previews are encoded for.
#[derive(Clone, Copy, Debug, PartialEq, Eq)]
pub enum ImageProtocol {
    Sixel,
    Kitty,
    Iterm,
}

/// Rust-only: the `image_protocol` config key.
#[derive(Clone, Copy, Debug, Default, PartialEq, Eq)]
pub enum ImageProtocolSetting {
    /// Kitty, then iTerm2, then Sixel: the first one detected.
    #[default]
    Auto,
    Kitty,
    Iterm,
    Sixel,
}

impl ImageProtocolSetting {
    #[must_use]
    pub fn parse(value: &str) -> Option<Self> {
        match value.to_ascii_lowercase().as_str() {
            "auto" => Some(Self::Auto),
            "kitty" => Some(Self::Kitty),
            "iterm" => Some(Self::Iterm),
            "sixel" => Some(Self::Sixel),
            _ => None,
        }
    }

    #[must_use]
    pub const fn name(self) -> &'static str {
        match self {
            Self::Auto => "auto",
            Self::Kitty => "kitty",
            Self::Iterm => "iterm",
            Self::Sixel => "sixel",
        }
    }

    /// The protocol to use with these terminal capabilities; a forced
    /// protocol still has to be detected.
    #[must_use]
    pub const fn resolve(self, caps: &crate::terminal_caps::TerminalCapabilities) -> Option<ImageProtocol> {
        match self {
            Self::Auto | Self::Kitty if caps.kitty_graphics => Some(ImageProtocol::Kitty),
            Self::Auto | Self::Iterm if caps.iterm_images => Some(ImageProtocol::Iterm),
            Self::Auto | Self::Sixel if caps.sixel_supported => Some(ImageProtocol::Sixel),
            _ => None,
        }
    }
}

/// Rust-only: an encoded image, ready to write.
#[derive(Clone, Debug, PartialEq, Eq)]
pub enum ImageData {
    /// A Sixel string, written after each frame at the image's position.
    Sixel(String),
    /// A kitty transmit command (written once) and the `cols` x `rows`
    /// block of placeholder cells that shows image `id`.
    Kitty { transmit: String, id: u32, cols: i32, rows: i32 },
    /// An iTerm2 inline image (`OSC 1337`), written like a Sixel.
    Iterm(String),
}

#[cfg(test)]
mod tests {
    use super::{ImageProtocol, ImageProtocolSetting as Setting};
    use crate::terminal_caps::TerminalCapabilities as Caps;

    #[test]
    fn protocol_resolution() {
        let caps = |sixel, kitty| Caps { sixel_supported: sixel, kitty_graphics: kitty, ..Caps::DEFAULT };
        let all = Caps { sixel_supported: true, kitty_graphics: true, iterm_images: true, ..Caps::DEFAULT };
        let iterm_sixel = Caps { sixel_supported: true, iterm_images: true, ..Caps::DEFAULT };
        let cases = [
            (Setting::Auto, caps(true, true), Some(ImageProtocol::Kitty)),
            (Setting::Auto, caps(true, false), Some(ImageProtocol::Sixel)),
            (Setting::Auto, caps(false, true), Some(ImageProtocol::Kitty)),
            (Setting::Auto, caps(false, false), None),
            (Setting::Auto, all, Some(ImageProtocol::Kitty)),
            (Setting::Auto, iterm_sixel, Some(ImageProtocol::Iterm)),
            (Setting::Sixel, caps(true, true), Some(ImageProtocol::Sixel)),
            (Setting::Sixel, all, Some(ImageProtocol::Sixel)),
            (Setting::Sixel, caps(false, true), None),
            (Setting::Kitty, caps(true, true), Some(ImageProtocol::Kitty)),
            (Setting::Kitty, caps(true, false), None),
            (Setting::Iterm, all, Some(ImageProtocol::Iterm)),
            (Setting::Iterm, caps(true, true), None),
        ];
        for (setting, caps, expected) in cases {
            assert_eq!(setting.resolve(&caps), expected, "{setting:?} {caps:?}");
        }
    }

    #[test]
    fn setting_names_round_trip() {
        for setting in [Setting::Auto, Setting::Kitty, Setting::Iterm, Setting::Sixel] {
            assert_eq!(Setting::parse(setting.name()), Some(setting));
        }
        assert_eq!(Setting::parse("KITTY"), Some(Setting::Kitty));
        assert_eq!(Setting::parse("iterm2"), None);
    }
}
