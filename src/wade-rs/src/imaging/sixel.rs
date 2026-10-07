//! Port of `SixelEncoder`: median-cut palette (up to 256 colors) over the
//! unique colors, nearest-color mapping, and the DCS Sixel sequence with
//! run-length encoding.

use std::collections::HashMap;
use std::fmt::Write as _;

/// A median-cut box over `colors[start..start + len]`.
#[derive(Clone, Copy)]
struct ColorBox {
    start: usize,
    len: usize,
    range: u32,
    channel: u32,
}

fn channel_value(color: u32, channel: u32) -> u32 {
    (color >> (16 - 8 * channel)) & 0xff
}

/// Port of `MakeBox`: the channel with the widest range (red wins ties,
/// then green).
fn make_box(colors: &[u32], start: usize, len: usize) -> ColorBox {
    let mut min = [255u32; 3];
    let mut max = [0u32; 3];

    for &color in &colors[start..start + len] {
        for channel in 0..3 {
            let value = channel_value(color, channel as u32);
            min[channel] = min[channel].min(value);
            max[channel] = max[channel].max(value);
        }
    }

    let ranges = [max[0].saturating_sub(min[0]), max[1].saturating_sub(min[1]), max[2].saturating_sub(min[2])];
    let channel = if ranges[0] >= ranges[1] && ranges[0] >= ranges[2] {
        0
    } else if ranges[1] >= ranges[0] && ranges[1] >= ranges[2] {
        1
    } else {
        2
    };

    ColorBox {
        start,
        len,
        range: ranges[channel],
        channel: channel as u32,
    }
}

/// Port of `MedianCutInPlace`: split the widest splittable box at its
/// median until `max_colors` boxes, then average each box.
fn median_cut(colors: &mut [u32], max_colors: usize) -> Vec<u32> {
    let mut boxes = vec![make_box(colors, 0, colors.len())];

    while boxes.len() < max_colors {
        // First splittable box with the largest range
        let mut best: Option<usize> = None;
        for (i, b) in boxes.iter().enumerate() {
            if b.len >= 2 && best.is_none_or(|j| b.range > boxes[j].range) {
                best = Some(i);
            }
        }

        let Some(index) = best else {
            break;
        };

        let ColorBox { start, len, channel, .. } = boxes[index];
        colors[start..start + len].sort_unstable_by_key(|&color| channel_value(color, channel));

        let mid = len / 2;
        boxes[index] = make_box(colors, start, mid);
        boxes.push(make_box(colors, start + mid, len - mid));
    }

    boxes
        .iter()
        .map(|b| {
            let mut sums = [0u64; 3];
            for &color in &colors[b.start..b.start + b.len] {
                for (channel, sum) in sums.iter_mut().enumerate() {
                    *sum += u64::from(channel_value(color, channel as u32));
                }
            }
            let n = b.len as u64;
            ((sums[0] / n) << 16 | (sums[1] / n) << 8 | (sums[2] / n)) as u32
        })
        .collect()
}

/// Port of `FindNearest`: squared RGB distance, first best wins.
fn find_nearest(color: u32, palette: &[u32]) -> u8 {
    let mut best = 0;
    let mut best_distance = i32::MAX;

    for (i, &entry) in palette.iter().enumerate() {
        let d = |channel| channel_value(color, channel) as i32 - channel_value(entry, channel) as i32;
        let distance = d(0) * d(0) + d(1) * d(1) + d(2) * d(2);

        if distance < best_distance {
            best_distance = distance;
            best = i;
            if distance == 0 {
                break;
            }
        }
    }

    best as u8
}

/// Port of `SixelEncoder.Encode`: RGBA pixels (alpha ignored) to a Sixel
/// DCS string; empty for a zero-sized image.
#[must_use]
pub fn encode(rgba: &[u8], width: usize, height: usize, max_colors: usize) -> String {
    if width == 0 || height == 0 || rgba.len() < width * height * 4 {
        return String::new();
    }

    let packed: Vec<u32> = rgba
        .chunks_exact(4)
        .take(width * height)
        .map(|p| u32::from(p[0]) << 16 | u32::from(p[1]) << 8 | u32::from(p[2]))
        .collect();

    let mut unique = packed.clone();
    unique.sort_unstable();
    unique.dedup();

    let palette = median_cut(&mut unique, max_colors.clamp(1, 256));

    let mut cache: HashMap<u32, u8> = HashMap::with_capacity(unique.len());
    let indexed: Vec<u8> = packed
        .iter()
        .map(|&color| *cache.entry(color).or_insert_with(|| find_nearest(color, &palette)))
        .collect();

    encode_sixel(&indexed, &palette, width, height)
}

/// Port of `EncodeSixel`. Each band's per-color sixel rows are built in one
/// pass over its pixels; colors appear in palette order as in C#.
fn encode_sixel(indexed: &[u8], palette: &[u32], width: usize, height: usize) -> String {
    let mut out = String::with_capacity(width * height / 2);
    let _ = write!(out, "\x1bPq\"1;1;{width};{height}");

    for (i, &color) in palette.iter().enumerate() {
        let scale = |channel| channel_value(color, channel) * 100 / 255;
        let _ = write!(out, "#{i};2;{};{};{}", scale(0), scale(1), scale(2));
    }

    let band_count = height.div_ceil(6);
    let mut rows = vec![0u8; palette.len() * width];
    let mut present = vec![false; palette.len()];

    for band in 0..band_count {
        let band_y = band * 6;
        let band_height = 6.min(height - band_y);
        rows.fill(0);
        present.fill(false);

        for bit in 0..band_height {
            let row = &indexed[(band_y + bit) * width..(band_y + bit + 1) * width];
            for (x, &color) in row.iter().enumerate() {
                rows[usize::from(color) * width + x] |= 1 << bit;
                present[usize::from(color)] = true;
            }
        }

        let mut first_color = true;
        for color in (0..present.len()).filter(|&c| present[c]) {
            if !first_color {
                out.push('$');
            }
            first_color = false;
            let _ = write!(out, "#{color}");

            let sixels = &rows[color * width..(color + 1) * width];
            let mut run_start = 0;
            while run_start < width {
                let value = sixels[run_start];
                let mut run_end = run_start + 1;
                while run_end < width && sixels[run_end] == value {
                    run_end += 1;
                }

                let run = run_end - run_start;
                let ch = char::from(value + 63);
                if run >= 4 {
                    let _ = write!(out, "!{run}{ch}");
                } else {
                    for _ in 0..run {
                        out.push(ch);
                    }
                }
                run_start = run_end;
            }
        }

        if band + 1 < band_count {
            out.push('-');
        }
    }

    out.push_str("\x1b\\");
    out
}

#[cfg(test)]
mod tests {
    //! Port of SixelEncoderTests.cs plus palette checks.

    use super::encode;

    fn solid(width: usize, height: usize, rgb: [u8; 3]) -> Vec<u8> {
        (0..width * height).flat_map(|_| [rgb[0], rgb[1], rgb[2], 255]).collect()
    }

    #[test]
    fn single_pixel_has_dcs_header_and_terminator() {
        let sixel = encode(&solid(1, 1, [255, 0, 0]), 1, 1, 256);
        assert!(sixel.starts_with("\x1bPq\"1;1;1;1"));
        assert!(sixel.ends_with("\x1b\\"));
        assert!(sixel.contains("#0;2;100;0;0"));
    }

    #[test]
    fn small_image_contains_palette_and_data() {
        // 2x2: red, green / blue, white
        let rgba = [255, 0, 0, 255, 0, 255, 0, 255, 0, 0, 255, 255, 255, 255, 255, 255];
        let sixel = encode(&rgba, 2, 2, 256);
        assert!(sixel.contains("\"1;1;2;2"));
        for register in ["100;0;0", "0;100;0", "0;0;100", "100;100;100"] {
            assert!(sixel.contains(register), "{register}");
        }
        assert_eq!(sixel.matches('$').count(), 3, "four colors in the one band");
    }

    #[test]
    fn repeated_color_uses_rle() {
        let sixel = encode(&solid(10, 1, [0, 0, 255]), 10, 1, 256);
        // One row set: sixel value 1 -> '@', ten times
        assert!(sixel.contains("#0!10@"));
    }

    #[test]
    fn bands_split_every_six_rows() {
        let sixel = encode(&solid(3, 13, [10, 20, 30]), 3, 13, 256);
        assert_eq!(sixel.matches('-').count(), 2);
        assert!(sixel.contains("#0~~~-#0~~~-#0@@@"));
    }

    #[test]
    fn zero_sizes_return_empty() {
        assert_eq!(encode(&[], 0, 0, 256), "");
        assert_eq!(encode(&[], 0, 5, 256), "");
        assert_eq!(encode(&[], 5, 0, 256), "");
    }

    #[test]
    fn palette_is_capped() {
        let rgba: Vec<u8> = (0..64 * 64u32)
            .flat_map(|i| [(i % 251) as u8, (i * 7 % 253) as u8, (i * 13 % 255) as u8, 255])
            .collect();
        let sixel = encode(&rgba, 64, 64, 16);
        assert!(sixel.contains("#15;2;"));
        assert!(!sixel.contains("#16;2;"));
    }
}
