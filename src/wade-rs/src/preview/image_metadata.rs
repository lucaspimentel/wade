//! Port of `ImageMetadataProvider`: resolution, format, color depth and
//! frame count from the image header, plus an EXIF section. Uses the
//! `image` crate's decoders and `kamadak-exif` instead of ImageSharp, so
//! format names, bit depths and frame counts follow those crates
//! (KNOWN_DEVIATIONS.md).

use std::io::{BufReader, Read};

use exif::{In, Tag, Value};
use image::ImageDecoder as _;

use super::{MetadataEntry, MetadataProvider, MetadataResult, MetadataSection, PreviewContext};
use crate::imaging::image_preview::is_image_file;
use crate::input::CancelToken;

pub struct ImageMetadataProvider;

impl MetadataProvider for ImageMetadataProvider {
    fn label(&self) -> &'static str {
        "Image"
    }

    fn can_provide_metadata(&self, path: &str, _context: &PreviewContext) -> bool {
        is_image_file(path)
    }

    fn get_metadata(&self, path: &str, _context: &PreviewContext, cancel: &CancelToken) -> Option<MetadataResult> {
        if cancel.is_cancelled() {
            return None;
        }

        let reader = image::ImageReader::open(path).ok()?.with_guessed_format().ok()?;
        let format = reader.format()?;
        let decoder = reader.into_decoder().ok()?;
        let (width, height) = decoder.dimensions();
        let bits_per_pixel = decoder.original_color_type().bits_per_pixel();
        drop(decoder);

        if cancel.is_cancelled() {
            return None;
        }

        let mut image_entries = vec![
            MetadataEntry::new("Resolution", &format!("{width} \u{00d7} {height}")),
            MetadataEntry::new("Format", format_name(format)),
            MetadataEntry::new("Color depth", &format!("{bits_per_pixel} bpp")),
        ];

        if let Some(frames) = frame_count(path, format).filter(|&frames| frames > 1) {
            image_entries.push(MetadataEntry::new("Frames", &frames.to_string()));
        }

        let mut sections = vec![MetadataSection {
            header: Some("Image".to_string()),
            entries: image_entries,
        }];

        if let Some(exif_entries) = read_exif(path).filter(|entries| !entries.is_empty()) {
            sections.push(MetadataSection {
                header: Some("EXIF".to_string()),
                entries: exif_entries,
            });
        }

        let ext = crate::fs::file_preview::extension(path).trim_start_matches('.').to_uppercase();

        Some(MetadataResult {
            sections,
            file_type_label: Some(format!("{ext} ({width} \u{00d7} {height})")),
        })
    }
}

/// ImageSharp's format names, uppercased.
fn format_name(format: image::ImageFormat) -> &'static str {
    use image::ImageFormat as F;

    match format {
        F::Png => "PNG",
        F::Jpeg => "JPEG",
        F::Gif => "GIF",
        F::Bmp => "BMP",
        F::WebP => "WEBP",
        F::Tga => "TGA",
        F::Tiff => "TIFF",
        F::Pnm => "PBM",
        _ => "IMAGE",
    }
}

/// Frame counts from the container headers (no frame decoding): GIF image
/// descriptors, the APNG `acTL` chunk, WebP `ANMF` chunks.
fn frame_count(path: &str, format: image::ImageFormat) -> Option<usize> {
    let mut data = Vec::new();
    std::fs::File::open(path).ok()?.read_to_end(&mut data).ok()?;

    match format {
        image::ImageFormat::Gif => gif_frames(&data),
        image::ImageFormat::Png => png_frames(&data),
        image::ImageFormat::WebP => webp_frames(&data),
        _ => None,
    }
}

fn gif_frames(data: &[u8]) -> Option<usize> {
    let flags = *data.get(10)?;
    let mut pos = 13;
    if flags & 0x80 != 0 {
        pos += 3 << ((flags & 0x07) + 1);
    }

    let skip_sub_blocks = |mut pos: usize| -> Option<usize> {
        loop {
            let len = usize::from(*data.get(pos)?);
            pos += 1;
            if len == 0 {
                return Some(pos);
            }
            pos += len;
        }
    };

    let mut frames = 0;
    loop {
        match *data.get(pos)? {
            0x2c => {
                frames += 1;
                let local = *data.get(pos + 9)?;
                pos += 10;
                if local & 0x80 != 0 {
                    pos += 3 << ((local & 0x07) + 1);
                }
                pos = skip_sub_blocks(pos + 1)?; // LZW minimum code size, then data
            }
            0x21 => pos = skip_sub_blocks(pos + 2)?,
            0x3b => return Some(frames),
            _ => return Some(frames),
        }
    }
}

fn png_frames(data: &[u8]) -> Option<usize> {
    let mut pos = 8;
    while pos + 8 <= data.len() {
        let len = u32::from_be_bytes(data[pos..pos + 4].try_into().ok()?) as usize;
        let kind = &data[pos + 4..pos + 8];
        if kind == b"acTL" {
            let frames = data.get(pos + 8..pos + 12)?;
            return Some(u32::from_be_bytes(frames.try_into().ok()?) as usize);
        }
        if kind == b"IDAT" {
            return Some(1);
        }
        pos += 12 + len;
    }
    None
}

fn webp_frames(data: &[u8]) -> Option<usize> {
    let mut pos = 12;
    let mut frames = 0;
    while pos + 8 <= data.len() {
        let kind = &data[pos..pos + 4];
        let len = u32::from_le_bytes(data[pos + 4..pos + 8].try_into().ok()?) as usize;
        if kind == b"ANMF" {
            frames += 1;
        }
        pos += 8 + len + (len & 1);
    }
    Some(frames.max(1))
}

/// The EXIF section entries; `None` when the file has no EXIF.
fn read_exif(path: &str) -> Option<Vec<MetadataEntry>> {
    let file = std::fs::File::open(path).ok()?;
    let exif = exif::Reader::new().read_from_container(&mut BufReader::new(file)).ok()?;
    exif.fields().next()?;

    let string = |tag: Tag| -> Option<String> {
        let field = exif.get_field(tag, In::PRIMARY)?;
        let Value::Ascii(parts) = &field.value else {
            return None;
        };
        let text = String::from_utf8_lossy(parts.first()?).trim_matches(char::from(0)).trim().to_string();
        (!text.is_empty()).then_some(text)
    };

    let rationals = |tag: Tag| -> Option<Vec<f64>> {
        match &exif.get_field(tag, In::PRIMARY)?.value {
            Value::Rational(values) => Some(values.iter().map(exif::Rational::to_f64).collect()),
            _ => None,
        }
    };

    let mut entries = Vec::new();

    if let Some(camera) = format_camera(string(Tag::Make).as_deref(), string(Tag::Model).as_deref()) {
        entries.push(MetadataEntry::new("Camera", &camera));
    }

    if let Some(date) = string(Tag::DateTimeOriginal) {
        entries.push(MetadataEntry::new("Date taken", &date));
    }

    let exposure_time = match exif.get_field(Tag::ExposureTime, In::PRIMARY).map(|f| &f.value) {
        Some(Value::Rational(values)) => values.first().copied(),
        _ => None,
    };
    let iso = match exif.get_field(Tag::PhotographicSensitivity, In::PRIMARY).map(|f| &f.value) {
        Some(Value::Short(values)) => values.first().copied(),
        _ => None,
    };
    if let Some(exposure) = format_exposure(
        exposure_time.map(|r| (r.num, r.denom)),
        rationals(Tag::FNumber).and_then(|v| v.first().copied()),
        iso,
    ) {
        entries.push(MetadataEntry::new("Exposure", &exposure));
    }

    if let Some(focal) = rationals(Tag::FocalLength).and_then(|v| v.first().copied()) {
        entries.push(MetadataEntry::new("Focal length", &format!("{} mm", format_one_decimal(focal))));
    }

    if let (Some(lat), Some(lon)) = (rationals(Tag::GPSLatitude), rationals(Tag::GPSLongitude))
        && let Some(gps) = format_gps(&lat, &lon, string(Tag::GPSLatitudeRef).as_deref(), string(Tag::GPSLongitudeRef).as_deref())
    {
        entries.push(MetadataEntry::new("GPS", &gps));
    }

    if let Some(software) = string(Tag::Software) {
        entries.push(MetadataEntry::new("Software", &software));
    }

    Some(entries)
}

/// .NET `{value:0.#}`: at most one decimal, no trailing ".0".
fn format_one_decimal(value: f64) -> String {
    let text = format!("{value:.1}");
    text.strip_suffix(".0").map_or(text.clone(), str::to_string)
}

/// Port of `FormatCamera`: make and model, without repeating the make.
#[must_use]
pub fn format_camera(make: Option<&str>, model: Option<&str>) -> Option<String> {
    match (make, model) {
        (None, None) => None,
        (None, Some(model)) => Some(model.to_string()),
        (Some(make), None) => Some(make.to_string()),
        (Some(make), Some(model)) => {
            if model.len() >= make.len() && model.is_char_boundary(make.len()) && model[..make.len()].eq_ignore_ascii_case(make) {
                Some(model.to_string())
            } else {
                Some(format!("{make} {model}"))
            }
        }
    }
}

/// Port of `FormatExposure`: shutter (`0.#s` or `1/Ns`), aperture, ISO.
#[must_use]
pub fn format_exposure(exposure_time: Option<(u32, u32)>, f_number: Option<f64>, iso: Option<u16>) -> Option<String> {
    let mut parts = Vec::new();

    if let Some((num, denom)) = exposure_time.filter(|&(num, denom)| num > 0 && denom > 0) {
        let seconds = f64::from(num) / f64::from(denom);
        parts.push(if seconds >= 1.0 {
            format!("{}s", format_one_decimal(seconds))
        } else {
            // Math.Round: banker's rounding
            format!("1/{}s", (1.0 / seconds).round_ties_even() as i64)
        });
    }

    if let Some(f) = f_number {
        parts.push(format!("f/{}", format_one_decimal(f)));
    }

    if let Some(iso) = iso {
        parts.push(format!("ISO {iso}"));
    }

    (!parts.is_empty()).then(|| parts.join(", "))
}

/// Port of `FormatGps`: degrees/minutes/seconds to signed decimal degrees.
#[must_use]
pub fn format_gps(lat: &[f64], lon: &[f64], lat_ref: Option<&str>, lon_ref: Option<&str>) -> Option<String> {
    let [d1, m1, s1] = <[f64; 3]>::try_from(lat).ok()?;
    let [d2, m2, s2] = <[f64; 3]>::try_from(lon).ok()?;

    let mut latitude = d1 + m1 / 60.0 + s1 / 3600.0;
    let mut longitude = d2 + m2 / 60.0 + s2 / 3600.0;
    if lat_ref == Some("S") {
        latitude = -latitude;
    }
    if lon_ref == Some("W") {
        longitude = -longitude;
    }

    Some(format!("{latitude:.6}, {longitude:.6}"))
}

#[cfg(test)]
mod tests {
    //! Port of ImageMetadataProviderTests.cs plus EXIF formatting checks.

    use exif::experimental::Writer;
    use exif::{Field, In, Rational, Tag, Value};

    use super::{format_camera, format_exposure, format_gps, ImageMetadataProvider};
    use crate::input::CancelToken;
    use crate::preview::{registry, test_context, test_path, MetadataProvider, MetadataResult};

    fn entries(result: &MetadataResult, header: &str) -> Vec<(String, String)> {
        result
            .sections
            .iter()
            .find(|s| s.header.as_deref() == Some(header))
            .map(|s| s.entries.iter().map(|e| (e.label.clone(), e.value.clone())).collect())
            .unwrap_or_default()
    }

    #[test]
    fn applies_to_image_extensions_only() {
        for path in ["photo.png", "photo.jpg", "photo.jpeg", "photo.gif", "photo.bmp", "photo.webp"] {
            assert!(ImageMetadataProvider.can_provide_metadata(path, &test_context()), "{path}");
        }
        for path in ["readme.txt", "doc.pdf", "app.exe"] {
            assert!(!ImageMetadataProvider.can_provide_metadata(path, &test_context()), "{path}");
        }
    }

    #[test]
    fn png_dimensions_format_and_depth() {
        let path = test_path("photo.png");
        image::RgbaImage::new(320, 240).save(&path).unwrap();

        let result = ImageMetadataProvider
            .get_metadata(&path.to_string_lossy(), &test_context(), &CancelToken::new())
            .expect("metadata");
        let image = entries(&result, "Image");
        assert!(image.contains(&("Resolution".into(), "320 \u{00d7} 240".into())));
        assert!(image.contains(&("Format".into(), "PNG".into())));
        assert!(image.contains(&("Color depth".into(), "32 bpp".into())));
        assert_eq!(result.file_type_label.as_deref(), Some("PNG (320 \u{00d7} 240)"));

        let cancel = CancelToken::new();
        cancel.cancel();
        assert!(ImageMetadataProvider.get_metadata(&path.to_string_lossy(), &test_context(), &cancel).is_none());
    }

    #[test]
    fn invalid_image_returns_none_and_registry_lists_provider() {
        let path = test_path("broken.png");
        std::fs::write(&path, b"not a png").unwrap();
        assert!(ImageMetadataProvider.get_metadata(&path.to_string_lossy(), &test_context(), &CancelToken::new()).is_none());

        let labels: Vec<&str> =
            registry::applicable_metadata_providers("photo.png", &test_context()).iter().map(|p| p.label()).collect();
        assert_eq!(labels, ["File info", "Image"]);
    }

    #[test]
    fn animated_gif_reports_frames() {
        let path = test_path("anim.gif");
        let file = std::fs::File::create(&path).unwrap();
        let mut encoder = image::codecs::gif::GifEncoder::new(file);
        let frames = (0..3).map(|i| image::Frame::new(image::RgbaImage::from_pixel(4, 4, image::Rgba([i * 80, 0, 0, 255]))));
        encoder.encode_frames(frames).unwrap();
        drop(encoder);

        let result = ImageMetadataProvider
            .get_metadata(&path.to_string_lossy(), &test_context(), &CancelToken::new())
            .expect("metadata");
        assert!(entries(&result, "Image").contains(&("Frames".into(), "3".into())));
    }

    /// A JPEG with an APP1 EXIF segment holding `fields`.
    fn jpeg_with_exif(name: &str, fields: &[Field]) -> String {
        let mut tiff = std::io::Cursor::new(Vec::new());
        let mut writer = Writer::new();
        for field in fields {
            writer.push_field(field);
        }
        writer.write(&mut tiff, false).unwrap();

        let mut jpeg = std::io::Cursor::new(Vec::new());
        image::RgbImage::new(16, 8).write_to(&mut jpeg, image::ImageFormat::Jpeg).unwrap();
        let jpeg = jpeg.into_inner();

        let payload = [b"Exif\0\0".as_slice(), tiff.get_ref()].concat();
        let mut out = jpeg[..2].to_vec(); // SOI
        out.extend([0xff, 0xe1]);
        out.extend(u16::try_from(payload.len() + 2).unwrap().to_be_bytes());
        out.extend(payload);
        out.extend(&jpeg[2..]);

        let path = test_path(name);
        std::fs::write(&path, out).unwrap();
        path.to_string_lossy().into_owned()
    }

    fn ascii(tag: Tag, text: &str) -> Field {
        Field {
            tag,
            ifd_num: In::PRIMARY,
            value: Value::Ascii(vec![text.as_bytes().to_vec()]),
        }
    }

    fn rational(tag: Tag, values: &[(u32, u32)]) -> Field {
        Field {
            tag,
            ifd_num: In::PRIMARY,
            value: Value::Rational(values.iter().map(|&(num, denom)| Rational { num, denom }).collect()),
        }
    }

    #[test]
    fn exif_section_from_jpeg() {
        let path = jpeg_with_exif(
            "exif.jpg",
            &[
                ascii(Tag::Make, "Canon"),
                ascii(Tag::Model, "Canon EOS R5"),
                ascii(Tag::DateTimeOriginal, "2024:01:02 03:04:05"),
                rational(Tag::ExposureTime, &[(1, 250)]),
                rational(Tag::FNumber, &[(28, 10)]),
                Field {
                    tag: Tag::PhotographicSensitivity,
                    ifd_num: In::PRIMARY,
                    value: Value::Short(vec![400]),
                },
                rational(Tag::FocalLength, &[(50, 1)]),
                rational(Tag::GPSLatitude, &[(42, 1), (21, 1), (36, 1)]),
                ascii(Tag::GPSLatitudeRef, "N"),
                rational(Tag::GPSLongitude, &[(71, 1), (3, 1), (36, 1)]),
                ascii(Tag::GPSLongitudeRef, "W"),
                ascii(Tag::Software, "  wade test  "),
            ],
        );

        let result = ImageMetadataProvider.get_metadata(&path, &test_context(), &CancelToken::new()).expect("metadata");
        let exif = entries(&result, "EXIF");
        let expected = [
            ("Camera", "Canon EOS R5"),
            ("Date taken", "2024:01:02 03:04:05"),
            ("Exposure", "1/250s, f/2.8, ISO 400"),
            ("Focal length", "50 mm"),
            ("GPS", "42.360000, -71.060000"),
            ("Software", "wade test"),
        ];
        let expected: Vec<(String, String)> = expected.iter().map(|(l, v)| ((*l).into(), (*v).into())).collect();
        assert_eq!(exif, expected);
    }

    #[test]
    fn formatting_helpers() {
        assert_eq!(format_camera(Some("Canon"), Some("EOS R5")).as_deref(), Some("Canon EOS R5"));
        assert_eq!(format_camera(Some("Canon"), Some("Canon EOS R5")).as_deref(), Some("Canon EOS R5"));
        assert_eq!(format_camera(None, Some("EOS R5")).as_deref(), Some("EOS R5"));
        assert_eq!(format_camera(Some("Canon"), None).as_deref(), Some("Canon"));
        assert_eq!(format_camera(None, None), None);

        assert_eq!(format_exposure(Some((2, 1)), None, None).as_deref(), Some("2s"));
        assert_eq!(format_exposure(Some((13, 10)), None, None).as_deref(), Some("1.3s"));
        assert_eq!(format_exposure(Some((0, 1)), None, Some(100)).as_deref(), Some("ISO 100"));
        assert_eq!(format_exposure(None, None, None), None);

        assert_eq!(format_gps(&[1.0, 30.0, 0.0], &[2.0, 0.0, 0.0], Some("S"), Some("E")).as_deref(), Some("-1.500000, 2.000000"));
        assert_eq!(format_gps(&[1.0, 2.0], &[2.0, 0.0, 0.0], None, None), None);
    }
}
