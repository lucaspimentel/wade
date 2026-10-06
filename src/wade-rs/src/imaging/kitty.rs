//! Rust-only: the kitty graphics protocol with Unicode placeholders
//! (https://sw.kovidgoyal.net/kitty/graphics-protocol/#unicode-placeholders).
//! The image is transmitted once with a virtual placement; the screen then
//! shows it through U+10EEEE cells whose foreground color is the image id
//! and whose two diacritics are the cell's row and column in the image.

use std::io::Write;
use std::sync::atomic::{AtomicU32, Ordering};

use crate::screen::Color;

/// The placeholder character.
pub const PLACEHOLDER: char = '\u{10EEEE}';

/// Largest chunk of base64 payload per escape sequence.
const CHUNK: usize = 4096;

/// Ids fit the 24-bit foreground color (no third diacritic).
const MAX_ID: u32 = 0xFF_FFFF;

static NEXT_ID: AtomicU32 = AtomicU32::new(1);

/// Row/column diacritics, from kitty's `gen/rowcolumn-diacritics.txt`:
/// index N encodes row or column N.
pub const DIACRITICS: [char; 297] = [
    '\u{0305}', '\u{030D}', '\u{030E}', '\u{0310}', '\u{0312}', '\u{033D}', '\u{033E}', '\u{033F}', '\u{0346}', '\u{034A}',
    '\u{034B}', '\u{034C}', '\u{0350}', '\u{0351}', '\u{0352}', '\u{0357}', '\u{035B}', '\u{0363}', '\u{0364}', '\u{0365}',
    '\u{0366}', '\u{0367}', '\u{0368}', '\u{0369}', '\u{036A}', '\u{036B}', '\u{036C}', '\u{036D}', '\u{036E}', '\u{036F}',
    '\u{0483}', '\u{0484}', '\u{0485}', '\u{0486}', '\u{0487}', '\u{0592}', '\u{0593}', '\u{0594}', '\u{0595}', '\u{0597}',
    '\u{0598}', '\u{0599}', '\u{059C}', '\u{059D}', '\u{059E}', '\u{059F}', '\u{05A0}', '\u{05A1}', '\u{05A8}', '\u{05A9}',
    '\u{05AB}', '\u{05AC}', '\u{05AF}', '\u{05C4}', '\u{0610}', '\u{0611}', '\u{0612}', '\u{0613}', '\u{0614}', '\u{0615}',
    '\u{0616}', '\u{0617}', '\u{0657}', '\u{0658}', '\u{0659}', '\u{065A}', '\u{065B}', '\u{065D}', '\u{065E}', '\u{06D6}',
    '\u{06D7}', '\u{06D8}', '\u{06D9}', '\u{06DA}', '\u{06DB}', '\u{06DC}', '\u{06DF}', '\u{06E0}', '\u{06E1}', '\u{06E2}',
    '\u{06E4}', '\u{06E7}', '\u{06E8}', '\u{06EB}', '\u{06EC}', '\u{0730}', '\u{0732}', '\u{0733}', '\u{0735}', '\u{0736}',
    '\u{073A}', '\u{073D}', '\u{073F}', '\u{0740}', '\u{0741}', '\u{0743}', '\u{0745}', '\u{0747}', '\u{0749}', '\u{074A}',
    '\u{07EB}', '\u{07EC}', '\u{07ED}', '\u{07EE}', '\u{07EF}', '\u{07F0}', '\u{07F1}', '\u{07F3}', '\u{0816}', '\u{0817}',
    '\u{0818}', '\u{0819}', '\u{081B}', '\u{081C}', '\u{081D}', '\u{081E}', '\u{081F}', '\u{0820}', '\u{0821}', '\u{0822}',
    '\u{0823}', '\u{0825}', '\u{0826}', '\u{0827}', '\u{0829}', '\u{082A}', '\u{082B}', '\u{082C}', '\u{082D}', '\u{0951}',
    '\u{0953}', '\u{0954}', '\u{0F82}', '\u{0F83}', '\u{0F86}', '\u{0F87}', '\u{135D}', '\u{135E}', '\u{135F}', '\u{17DD}',
    '\u{193A}', '\u{1A17}', '\u{1A75}', '\u{1A76}', '\u{1A77}', '\u{1A78}', '\u{1A79}', '\u{1A7A}', '\u{1A7B}', '\u{1A7C}',
    '\u{1B6B}', '\u{1B6D}', '\u{1B6E}', '\u{1B6F}', '\u{1B70}', '\u{1B71}', '\u{1B72}', '\u{1B73}', '\u{1CD0}', '\u{1CD1}',
    '\u{1CD2}', '\u{1CDA}', '\u{1CDB}', '\u{1CE0}', '\u{1DC0}', '\u{1DC1}', '\u{1DC3}', '\u{1DC4}', '\u{1DC5}', '\u{1DC6}',
    '\u{1DC7}', '\u{1DC8}', '\u{1DC9}', '\u{1DCB}', '\u{1DCC}', '\u{1DD1}', '\u{1DD2}', '\u{1DD3}', '\u{1DD4}', '\u{1DD5}',
    '\u{1DD6}', '\u{1DD7}', '\u{1DD8}', '\u{1DD9}', '\u{1DDA}', '\u{1DDB}', '\u{1DDC}', '\u{1DDD}', '\u{1DDE}', '\u{1DDF}',
    '\u{1DE0}', '\u{1DE1}', '\u{1DE2}', '\u{1DE3}', '\u{1DE4}', '\u{1DE5}', '\u{1DE6}', '\u{1DFE}', '\u{20D0}', '\u{20D1}',
    '\u{20D4}', '\u{20D5}', '\u{20D6}', '\u{20D7}', '\u{20DB}', '\u{20DC}', '\u{20E1}', '\u{20E7}', '\u{20E9}', '\u{20F0}',
    '\u{2CEF}', '\u{2CF0}', '\u{2CF1}', '\u{2DE0}', '\u{2DE1}', '\u{2DE2}', '\u{2DE3}', '\u{2DE4}', '\u{2DE5}', '\u{2DE6}',
    '\u{2DE7}', '\u{2DE8}', '\u{2DE9}', '\u{2DEA}', '\u{2DEB}', '\u{2DEC}', '\u{2DED}', '\u{2DEE}', '\u{2DEF}', '\u{2DF0}',
    '\u{2DF1}', '\u{2DF2}', '\u{2DF3}', '\u{2DF4}', '\u{2DF5}', '\u{2DF6}', '\u{2DF7}', '\u{2DF8}', '\u{2DF9}', '\u{2DFA}',
    '\u{2DFB}', '\u{2DFC}', '\u{2DFD}', '\u{2DFE}', '\u{2DFF}', '\u{A66F}', '\u{A67C}', '\u{A67D}', '\u{A6F0}', '\u{A6F1}',
    '\u{A8E0}', '\u{A8E1}', '\u{A8E2}', '\u{A8E3}', '\u{A8E4}', '\u{A8E5}', '\u{A8E6}', '\u{A8E7}', '\u{A8E8}', '\u{A8E9}',
    '\u{A8EA}', '\u{A8EB}', '\u{A8EC}', '\u{A8ED}', '\u{A8EE}', '\u{A8EF}', '\u{A8F0}', '\u{A8F1}', '\u{AAB0}', '\u{AAB2}',
    '\u{AAB3}', '\u{AAB7}', '\u{AAB8}', '\u{AABE}', '\u{AABF}', '\u{AAC1}', '\u{FE20}', '\u{FE21}', '\u{FE22}', '\u{FE23}',
    '\u{FE24}', '\u{FE25}', '\u{FE26}', '\u{10A0F}', '\u{10A38}', '\u{1D185}', '\u{1D186}', '\u{1D187}', '\u{1D188}', '\u{1D189}',
    '\u{1D1AA}', '\u{1D1AB}', '\u{1D1AC}', '\u{1D1AD}', '\u{1D242}', '\u{1D243}', '\u{1D244}',];

/// A fresh image id in `1..=0xFFFFFF`, wrapping (never 0).
pub fn next_image_id() -> u32 {
    next_id_after(&NEXT_ID)
}

fn next_id_after(counter: &AtomicU32) -> u32 {
    let mut id = counter.fetch_add(1, Ordering::Relaxed) & MAX_ID;
    while id == 0 {
        id = counter.fetch_add(1, Ordering::Relaxed) & MAX_ID;
    }
    id
}

/// The foreground color that names image `id` in a placeholder cell.
#[must_use]
pub const fn id_color(id: u32) -> Color {
    Color { r: (id >> 16) as u8, g: (id >> 8) as u8, b: id as u8 }
}

/// Standard base64 with padding.
#[must_use]
pub fn base64(data: &[u8]) -> String {
    const TABLE: &[u8; 64] = b"ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789+/";
    let mut out = String::with_capacity(data.len().div_ceil(3) * 4);

    for chunk in data.chunks(3) {
        let b = [chunk[0], chunk.get(1).copied().unwrap_or(0), chunk.get(2).copied().unwrap_or(0)];
        let n = (u32::from(b[0]) << 16) | (u32::from(b[1]) << 8) | u32::from(b[2]);
        out.push(TABLE[(n >> 18) as usize & 63] as char);
        out.push(TABLE[(n >> 12) as usize & 63] as char);
        out.push(if chunk.len() > 1 { TABLE[(n >> 6) as usize & 63] as char } else { '=' });
        out.push(if chunk.len() > 2 { TABLE[n as usize & 63] as char } else { '=' });
    }

    out
}

/// Transmits `rgba` (`width` x `height`) as image `id` with a virtual
/// placement of `cols` x `rows` cells: zlib-compressed RGBA, base64, split
/// into chunks; only the first carries the keys. Replies are suppressed.
#[must_use]
pub fn encode_transmit(rgba: &[u8], width: u32, height: u32, id: u32, cols: i32, rows: i32) -> String {
    let mut encoder = flate2::write::ZlibEncoder::new(Vec::new(), flate2::Compression::fast());
    let _ = encoder.write_all(rgba);
    let compressed = encoder.finish().unwrap_or_default();
    let payload = base64(&compressed);

    let chunks: Vec<&str> = payload.as_bytes().chunks(CHUNK).map(|c| std::str::from_utf8(c).unwrap_or_default()).collect();
    let mut out = String::with_capacity(payload.len() + chunks.len() * 16 + 64);

    for (i, chunk) in chunks.iter().enumerate() {
        let more = u8::from(i + 1 < chunks.len());
        if i == 0 {
            out.push_str(&format!("\x1b_Ga=T,U=1,f=32,o=z,s={width},v={height},i={id},c={cols},r={rows},q=2,m={more};"));
        } else {
            out.push_str(&format!("\x1b_Gm={more};"));
        }
        out.push_str(chunk);
        out.push_str("\x1b\\");
    }

    out
}

/// Deletes image `id` and its placements, freeing its data.
#[must_use]
pub fn encode_delete(id: u32) -> String {
    format!("\x1b_Ga=d,d=I,i={id},q=2\x1b\\")
}

/// Cells an image of `pixels` covers at `cell` pixels per cell, at most
/// `max` (and the diacritics table).
#[must_use]
pub fn cells_for(pixels: i32, cell: i32, max: i32) -> i32 {
    let cell = cell.max(1);
    ((pixels + cell - 1) / cell).clamp(1, max.clamp(1, DIACRITICS.len() as i32))
}

#[cfg(test)]
mod tests {
    use std::io::Read;
    use std::sync::atomic::AtomicU32;

    use super::*;

    #[test]
    fn base64_known_vectors() {
        for (input, expected) in [("", ""), ("f", "Zg=="), ("fo", "Zm8="), ("foo", "Zm9v"), ("foob", "Zm9vYg=="), ("foobar", "Zm9vYmFy")] {
            assert_eq!(base64(input.as_bytes()), expected, "{input}");
        }
        assert_eq!(base64(&[0xff, 0xfe, 0xfd]), "//79");
    }

    #[test]
    fn diacritics_table() {
        assert_eq!(DIACRITICS.len(), 297);
        assert_eq!(DIACRITICS[0], '\u{0305}');
        assert_eq!(DIACRITICS[1], '\u{030D}');
        assert_eq!(DIACRITICS[296], '\u{1D244}');
    }

    #[test]
    fn ids_skip_zero_and_wrap() {
        let counter = AtomicU32::new(MAX_ID);
        assert_eq!(next_id_after(&counter), MAX_ID);
        assert_eq!(next_id_after(&counter), 1, "0 is skipped after the wrap");
        assert_eq!(next_id_after(&counter), 2);
        assert_eq!(id_color(0x12_3456), Color { r: 0x12, g: 0x34, b: 0x56 });
    }

    /// Splits a transmit string into (keys, payload) pairs.
    fn commands(data: &str) -> Vec<(String, String)> {
        data.split("\x1b\\")
            .filter(|s| !s.is_empty())
            .map(|cmd| {
                let body = cmd.strip_prefix("\x1b_G").expect("APC G");
                let (keys, payload) = body.split_once(';').expect("keys;payload");
                (keys.to_string(), payload.to_string())
            })
            .collect()
    }

    fn decode_base64(text: &str) -> Vec<u8> {
        const TABLE: &[u8; 64] = b"ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789+/";
        let mut bits = 0u32;
        let mut count = 0;
        let mut out = Vec::new();
        for c in text.bytes().filter(|&c| c != b'=') {
            bits = (bits << 6) | TABLE.iter().position(|&t| t == c).unwrap() as u32;
            count += 6;
            if count >= 8 {
                count -= 8;
                out.push((bits >> count) as u8);
            }
        }
        out
    }

    #[test]
    fn transmit_is_chunked_and_round_trips() {
        // Pseudo-random pixels so zlib can't shrink them below one chunk
        let mut state = 12345u32;
        let rgba: Vec<u8> = (0..64 * 64 * 4)
            .map(|_| {
                state = state.wrapping_mul(1_103_515_245).wrapping_add(12345);
                (state >> 16) as u8
            })
            .collect();

        let cmds = commands(&encode_transmit(&rgba, 64, 64, 7, 8, 4));
        assert!(cmds.len() > 1, "{}", cmds.len());
        assert_eq!(cmds[0].0, "a=T,U=1,f=32,o=z,s=64,v=64,i=7,c=8,r=4,q=2,m=1");
        for (keys, _) in &cmds[1..cmds.len() - 1] {
            assert_eq!(keys, "m=1");
        }
        assert_eq!(cmds.last().unwrap().0, "m=0");
        assert!(cmds.iter().all(|(_, payload)| payload.len() <= CHUNK && payload.len() % 4 == 0));

        let joined: String = cmds.iter().map(|(_, p)| p.as_str()).collect();
        let mut inflated = Vec::new();
        flate2::read::ZlibDecoder::new(decode_base64(&joined).as_slice()).read_to_end(&mut inflated).unwrap();
        assert_eq!(inflated, rgba);
    }

    #[test]
    fn small_images_are_one_command() {
        let cmds = commands(&encode_transmit(&[0; 16], 2, 2, 3, 1, 1));
        assert_eq!(cmds.len(), 1);
        assert_eq!(cmds[0].0, "a=T,U=1,f=32,o=z,s=2,v=2,i=3,c=1,r=1,q=2,m=0");
    }

    #[test]
    fn delete_and_cell_counts() {
        assert_eq!(encode_delete(5), "\x1b_Ga=d,d=I,i=5,q=2\x1b\\");
        assert_eq!(cells_for(80, 8, 100), 10);
        assert_eq!(cells_for(81, 8, 100), 11);
        assert_eq!(cells_for(800, 8, 50), 50, "clamped to the pane");
        assert_eq!(cells_for(100_000, 1, 10_000), 297, "clamped to the diacritics");
        assert_eq!(cells_for(0, 8, 10), 1);
    }
}
