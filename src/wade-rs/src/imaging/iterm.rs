//! Rust-only: the iTerm2 inline image protocol
//! (https://iterm2.com/documentation-images.html), also shown by WezTerm.
//! The fitted image is sent as PNG, sized in cells so the terminal scales it
//! into the image area whatever the real cell size is. Like Sixel, it is
//! written after the flush at the image position and cells drawn over it
//! erase it.

use image::ImageEncoder;

/// The `OSC 1337 ; File=` sequence for `rgba` (`width` x `height`) shown in
/// a `cols` x `rows` cell box, aspect kept, the cursor left in place.
#[must_use]
pub fn encode(rgba: &[u8], width: u32, height: u32, cols: i32, rows: i32) -> String {
    let mut png = Vec::new();
    let encoder = image::codecs::png::PngEncoder::new(&mut png);
    if encoder.write_image(rgba, width, height, image::ExtendedColorType::Rgba8).is_err() {
        return String::new();
    }

    format!(
        "\x1b]1337;File=inline=1;size={};width={cols};height={rows};preserveAspectRatio=1;doNotMoveCursor=1:{}\x07",
        png.len(),
        super::kitty::base64(&png)
    )
}

#[cfg(test)]
mod tests {
    use super::encode;

    #[test]
    fn sequence_keys_and_png_payload() {
        let rgba: Vec<u8> = (0..6 * 4 * 4).map(|i| (i * 7) as u8).collect();
        let out = encode(&rgba, 6, 4, 3, 2);

        let body = out.strip_prefix("\x1b]1337;File=").and_then(|s| s.strip_suffix('\x07')).expect("OSC 1337 ... BEL");
        let (keys, payload) = body.split_once(':').expect("keys:payload");
        let png = crate::imaging::kitty::tests::decode_base64(payload);

        assert_eq!(
            keys,
            format!("inline=1;size={};width=3;height=2;preserveAspectRatio=1;doNotMoveCursor=1", png.len())
        );
        assert!(png.starts_with(b"\x89PNG\r\n\x1a\n"));
        let decoded = image::load_from_memory(&png).unwrap().into_rgba8();
        assert_eq!(decoded.dimensions(), (6, 4));
        assert_eq!(decoded.as_raw(), &rgba);
    }
}
