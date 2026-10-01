//! Port of `ImagePreview`: decode an image, fit it to the pane (never
//! upscaling), and encode it as Sixel.

use image::imageops::FilterType;

use crate::input::CancelToken;

/// Port of `ImagePreviewResult`.
#[derive(Clone, Debug, PartialEq, Eq)]
pub struct ImagePreviewResult {
    pub sixel_data: String,
    pub pixel_width: i32,
    pub pixel_height: i32,
    pub label: String,
}

const IMAGE_EXTENSIONS: &[&str] = &[".png", ".jpg", ".jpeg", ".gif", ".bmp", ".webp", ".tga", ".tiff", ".pbm"];

/// Port of `IsImageFile`.
#[must_use]
pub fn is_image_file(path: &str) -> bool {
    let ext = crate::fs::file_preview::extension(path);
    !ext.is_empty() && IMAGE_EXTENSIONS.iter().any(|known| known.eq_ignore_ascii_case(ext))
}

/// The C# fit: the smaller of the two scale factors, capped at 1.0, each
/// side at least one pixel.
#[must_use]
pub fn fit_size(src_width: u32, src_height: u32, max_width: i32, max_height: i32) -> (u32, u32) {
    let scale = (f64::from(max_width) / f64::from(src_width))
        .min(f64::from(max_height) / f64::from(src_height))
        .min(1.0);

    let width = ((f64::from(src_width) * scale) as u32).max(1);
    let height = ((f64::from(src_height) * scale) as u32).max(1);
    (width, height)
}

/// Port of `Load`. `None` when the pane is empty, the file can't be decoded,
/// or the load is cancelled. Scaling uses a fast triangle filter rather than
/// ImageSharp's bicubic resampler (KNOWN_DEVIATIONS.md).
#[must_use]
pub fn load(
    path: &str,
    pane_width_cells: i32,
    pane_height_cells: i32,
    cell_pixel_width: i32,
    cell_pixel_height: i32,
    cancel: &CancelToken,
) -> Option<ImagePreviewResult> {
    let max_width = pane_width_cells.checked_mul(cell_pixel_width)?;
    let max_height = pane_height_cells.checked_mul(cell_pixel_height)?;
    if max_width <= 0 || max_height <= 0 {
        return None;
    }

    let decoded = image::ImageReader::open(path).ok()?.with_guessed_format().ok()?.decode().ok()?;
    if cancel.is_cancelled() {
        return None;
    }

    let (src_width, src_height) = (decoded.width(), decoded.height());
    if src_width == 0 || src_height == 0 {
        return None;
    }

    let (width, height) = fit_size(src_width, src_height, max_width, max_height);
    let mut rgba = decoded.into_rgba8();
    if (width, height) != (src_width, src_height) {
        rgba = image::imageops::resize(&rgba, width, height, FilterType::Triangle);
    }

    if cancel.is_cancelled() {
        return None;
    }

    let sixel_data = super::sixel::encode(rgba.as_raw(), width as usize, height as usize, 256);
    if cancel.is_cancelled() {
        return None;
    }

    let ext = crate::fs::file_preview::extension(path).trim_start_matches('.').to_uppercase();

    Some(ImagePreviewResult {
        sixel_data,
        pixel_width: width as i32,
        pixel_height: height as i32,
        label: format!("{ext} Image ({src_width} x {src_height})"),
    })
}

#[cfg(test)]
mod tests {
    //! Port of ImagePreviewTests.cs.

    use super::{fit_size, is_image_file, load};
    use crate::input::CancelToken;

    #[test]
    fn image_extensions() {
        for ext in [".png", ".jpg", ".jpeg", ".gif", ".bmp", ".webp", ".tga", ".tiff", ".pbm"] {
            assert!(is_image_file(&format!("test{ext}")), "{ext}");
        }
        for ext in [".txt", ".cs", ".exe", ".dll", ".json", ""] {
            assert!(!is_image_file(&format!("test{ext}")), "{ext}");
        }
        assert!(is_image_file("PHOTO.JPG"));
    }

    #[test]
    fn fit_never_upscales_and_keeps_aspect() {
        assert_eq!(fit_size(100, 50, 800, 800), (100, 50));
        assert_eq!(fit_size(4000, 3000, 640, 384), (512, 384));
        assert_eq!(fit_size(10_000, 1, 80, 80), (80, 1));
    }

    fn small_bmp() -> String {
        let path = crate::preview::test_path("small.bmp");
        let img = image::RgbImage::from_fn(4, 3, |x, y| image::Rgb([(x * 60) as u8, (y * 80) as u8, 128]));
        img.save(&path).unwrap();
        path.to_string_lossy().into_owned()
    }

    #[test]
    fn small_bmp_produces_sixel() {
        let result = load(&small_bmp(), 80, 24, 8, 16, &CancelToken::new()).expect("result");
        assert!(result.sixel_data.starts_with("\x1bPq"));
        assert_eq!((result.pixel_width, result.pixel_height), (4, 3));
        assert_eq!(result.label, "BMP Image (4 x 3)");
    }

    #[test]
    fn missing_file_empty_pane_and_cancel_return_none() {
        assert!(load("/no/such/file.png", 80, 24, 8, 16, &CancelToken::new()).is_none());
        assert!(load(&small_bmp(), 0, 24, 8, 16, &CancelToken::new()).is_none());

        let cancel = CancelToken::new();
        cancel.cancel();
        assert!(load(&small_bmp(), 80, 24, 8, 16, &cancel).is_none());
    }

    /// Timing check (run with `cargo test --release -- --ignored`): a
    /// 4000x3000 noisy JPEG decoded, fitted to a 120x40-cell pane, encoded.
    #[test]
    #[ignore = "timing check, release builds only"]
    fn large_photo_previews_quickly() {
        let path = crate::preview::test_path("large.jpg");
        let img = image::RgbImage::from_fn(4000, 3000, |x, y| {
            let n = x.wrapping_mul(2_654_435_761).wrapping_add(y.wrapping_mul(40_503));
            image::Rgb([(x / 16) as u8 ^ n as u8, (y / 12) as u8, (n >> 8) as u8])
        });
        img.save(&path).unwrap();

        let start = std::time::Instant::now();
        let result = load(&path.to_string_lossy(), 120, 40, 8, 16, &CancelToken::new()).expect("result");
        let elapsed = start.elapsed();

        eprintln!("4000x3000 JPEG -> {}x{} sixel ({} bytes) in {elapsed:?}", result.pixel_width, result.pixel_height, result.sixel_data.len());
        assert!(elapsed < std::time::Duration::from_millis(1500), "{elapsed:?}");
    }
}
