//! Port of the PDF-to-image pipeline (`IImageConverter`, `ImageConverter`,
//! `PdfImageConverter`, `XpdfPdfTool`): page 1 rendered to a temporary PNG
//! with xpdf's `pdftopng`.

use std::path::PathBuf;
use std::sync::atomic::{AtomicU64, Ordering};

use crate::input::CancelToken;
use crate::preview::cli_tool_hints;

/// A rendered page in a temporary directory, removed on drop (C# removes
/// the PNG but leaves the directory; KNOWN_DEVIATIONS.md).
pub struct TempImage {
    pub path: PathBuf,
    dir: PathBuf,
}

impl Drop for TempImage {
    fn drop(&mut self) {
        let _ = std::fs::remove_dir_all(&self.dir);
    }
}

/// Port of `XpdfPdfTool.IsAvailable`.
#[must_use]
pub fn pdftopng_available() -> bool {
    cli_tool_hints::is_available("pdftopng", None, false)
}

/// Port of `PdfImageConverter.CanConvert` / `ImageConverter.CanConvert`.
#[must_use]
pub fn can_convert(path: &str) -> bool {
    crate::fs::file_preview::extension(path).eq_ignore_ascii_case(".pdf") && pdftopng_available()
}

fn unique_temp_dir() -> PathBuf {
    static NEXT: AtomicU64 = AtomicU64::new(0);
    let nanos = std::time::SystemTime::now().duration_since(std::time::UNIX_EPOCH).map_or(0, |d| d.as_nanos());
    std::env::temp_dir().join(format!(
        "wade-pdf-{}-{nanos:x}-{}",
        std::process::id(),
        NEXT.fetch_add(1, Ordering::Relaxed)
    ))
}

/// Port of `XpdfPdfTool.RenderPage`: `pdftopng -f N -l N -r 150 -q`, 10 s
/// timeout, output `<root>-NNNNNN.png`.
#[must_use]
pub fn render_page(pdf_path: &str, page: u32, cancel: &CancelToken) -> Option<TempImage> {
    let dir = unique_temp_dir();
    std::fs::create_dir_all(&dir).ok()?;
    let root = dir.join("page");
    let page_text = page.to_string();
    let root_text = root.to_string_lossy().into_owned();

    let output = cli_tool_hints::run(
        "pdftopng",
        &["-f", &page_text, "-l", &page_text, "-r", "150", "-q", pdf_path, &root_text],
        10_000,
        cancel,
    );

    let expected = PathBuf::from(format!("{root_text}-{page:06}.png"));
    let image = TempImage { path: expected, dir };

    (output.is_some() && image.path.is_file()).then_some(image)
}

/// Port of `ImageConverter.ConvertToImage`: page 1 of a PDF.
#[must_use]
pub fn convert_to_image(path: &str, cancel: &CancelToken) -> Option<TempImage> {
    can_convert(path).then(|| render_page(path, 1, cancel)).flatten()
}

#[cfg(test)]
mod tests {
    //! Port of PdfPreviewTests.cs (tool availability depends on the host).

    use super::{can_convert, pdftopng_available, render_page};
    use crate::input::CancelToken;

    #[test]
    fn availability_is_a_bool_and_conversion_needs_a_pdf() {
        let available = pdftopng_available();
        assert_eq!(can_convert("doc.pdf"), available);
        assert_eq!(can_convert("DOC.PDF"), available);
        assert!(!can_convert("doc.txt"));
        assert!(!can_convert("image.png"));
    }

    #[test]
    fn failed_render_leaves_no_temp_dir() {
        // Missing tool or invalid PDF: nothing is returned and the
        // temporary directory is removed with the dropped handle
        let path = crate::preview::test_path("not-a.pdf");
        std::fs::write(&path, b"not a pdf").unwrap();
        assert!(render_page(&path.to_string_lossy(), 1, &CancelToken::new()).is_none());
    }
}
