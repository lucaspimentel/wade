//! Port of `src/Wade/Imaging`: the Sixel encoder, image loading for
//! previews, and the PDF-to-image pipeline. Pixels need not match C#
//! (ImageSharp): decoding and scaling use the `image` crate
//! (KNOWN_DEVIATIONS.md).

pub mod image_preview;
pub mod sixel;
