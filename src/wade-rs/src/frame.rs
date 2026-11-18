//! One frame's escape bytes: the synchronized-update pair, the cell diff a
//! `ScreenBuffer` diffs for us, and the image payloads.

use crate::screen::ScreenBuffer;

/// The pair every frame is written between, so the terminal presents it at once
/// (Windows Terminal 1.24+, kitty, WezTerm, foot, Alacritty 0.13+, iTerm2, Warp,
/// Contour, Zellij, tmux 3.7b+; others ignore it).
pub const BEGIN_SYNCHRONIZED_UPDATE: &str = "\x1b[?2026h";
/// The close of that pair. Both restore paths write it first, so a normal exit, a
/// panic during unwinding or the SIGTERM/SIGHUP path cannot leave the terminal
/// deferring updates, and an unmatched close is a no-op. No capability query is
/// sent: an unknown private mode is ignored.
pub const END_SYNCHRONIZED_UPDATE: &str = "\x1b[?2026l";

/// What a frame has to write for images, split by how the terminal places them.
#[derive(Default)]
pub struct FrameImage {
    /// Kitty transmit and deletes, and anything tmux-wrapped: their content
    /// carries the position, so they ride the frame's own write.
    pub freestanding: String,
    /// A Sixel or iTerm2 image: the cursor move that places it, then the payload.
    /// Written after the frame, in writes of their own, with the cursor shown.
    pub at_cursor: Option<(String, String)>,
}

impl FrameImage {
    /// A payload that carries its own position: kitty commands, a tmux wrapper.
    pub fn freestanding(payload: impl Into<String>) -> Self {
        Self {
            freestanding: payload.into(),
            at_cursor: None,
        }
    }

    /// A payload the terminal places at the cursor.
    pub fn at_cursor(cursor_move: String, payload: String) -> Self {
        Self {
            freestanding: String::new(),
            at_cursor: Some((cursor_move, payload)),
        }
    }

    /// Folds another image's writes into this one, keeping the freestanding bytes
    /// in order. A frame holds at most one cursor-placed payload.
    pub fn merge(&mut self, other: Self) {
        self.freestanding.push_str(&other.freestanding);
        if other.at_cursor.is_some() {
            self.at_cursor = other.at_cursor;
        }
    }

    /// Whether there is nothing for the frame to write.
    pub fn is_empty(&self) -> bool {
        self.freestanding.is_empty() && self.at_cursor.is_none()
    }
}

/// Writes one frame as a synchronized update: the requested clear, the cell
/// diff, then the image payload the cell grid cannot carry. Writes nothing,
/// not even the brackets, when there is nothing to show.
///
/// A payload the terminal places at the cursor goes out in writes of its own,
/// after the frame and with the cursor shown: ConPTY drops a cursor move it is
/// holding while the cursor is hidden (`KNOWN_DEVIATIONS.md`).
pub fn write_frame(out: &mut impl std::io::Write, buffer: &mut ScreenBuffer, pending_clear: bool, image: &FrameImage) {
    if pending_clear {
        // The clear owns its repaint: every cell goes out again
        buffer.force_full_redraw();
    }

    let mut diff = String::new();
    buffer.serialize(&mut diff);

    if pending_clear || !diff.is_empty() || !image.is_empty() {
        let _ = out.write_all(BEGIN_SYNCHRONIZED_UPDATE.as_bytes());

        if pending_clear {
            let _ = out.write_all(crate::ansi::CLEAR_SCREEN.as_bytes());
        }

        let _ = out.write_all(diff.as_bytes());
        let _ = out.write_all(image.freestanding.as_bytes());
        let _ = out.write_all(END_SYNCHRONIZED_UPDATE.as_bytes());
        let _ = out.flush();
    }

    if let Some((cursor_move, payload)) = &image.at_cursor {
        for chunk in [crate::ansi::SHOW_CURSOR, cursor_move, payload, crate::ansi::HIDE_CURSOR] {
            let _ = out.write_all(chunk.as_bytes());
            let _ = out.flush();
        }
    }
}

#[cfg(test)]
mod tests {
    use super::{BEGIN_SYNCHRONIZED_UPDATE, END_SYNCHRONIZED_UPDATE, FrameImage, write_frame};

    /// Records what a frame wrote, in how many writes, and how often it flushed.
    #[derive(Default)]
    struct FrameSink {
        bytes: Vec<u8>,
        chunks: Vec<usize>,
        flushes: usize,
    }

    impl std::io::Write for FrameSink {
        fn write(&mut self, buf: &[u8]) -> std::io::Result<usize> {
            self.bytes.extend_from_slice(buf);
            self.chunks.push(buf.len());
            Ok(buf.len())
        }

        fn flush(&mut self) -> std::io::Result<()> {
            self.flushes += 1;
            Ok(())
        }
    }

    /// A buffer whose every cell holds `ch`.
    fn filled_buffer(width: i32, height: i32, ch: char) -> crate::screen::ScreenBuffer {
        let mut buffer = crate::screen::ScreenBuffer::new(width, height);

        for row in 0..height {
            buffer.fill_row(row, 0, width, ch, crate::screen::CellStyle::default());
        }

        buffer
    }

    #[test]
    fn an_empty_frame_writes_nothing_at_all() {
        let mut buffer = crate::screen::ScreenBuffer::new(4, 2);
        let mut sink = FrameSink::default();
        write_frame(&mut sink, &mut buffer, false, &FrameImage::default());
        assert!(sink.bytes.is_empty(), "{:?}", String::from_utf8_lossy(&sink.bytes));
        assert_eq!(sink.flushes, 0, "an empty frame does not even flush");
    }

    #[test]
    fn a_pending_clear_is_bracketed_and_repaints_every_cell() {
        let mut buffer = filled_buffer(4, 2, 'x');
        let mut sink = FrameSink::default();

        // The first frame consumes the diff, so no cell is dirty any more
        write_frame(&mut sink, &mut buffer, false, &FrameImage::default());
        assert!(!sink.bytes.is_empty());

        let mut sink = FrameSink::default();
        write_frame(&mut sink, &mut buffer, true, &FrameImage::default());

        let text = String::from_utf8(sink.bytes).unwrap();
        let rest = text.strip_prefix(BEGIN_SYNCHRONIZED_UPDATE).expect("begin bracket");
        let rest = rest.strip_prefix(crate::ansi::CLEAR_SCREEN).expect("the clear follows the bracket");
        assert!(rest.ends_with(END_SYNCHRONIZED_UPDATE));
        assert_eq!(rest.matches('x').count(), 8, "the clear repaints every cell");
        assert_eq!(sink.flushes, 1);
    }

    #[test]
    fn a_diff_without_a_clear_is_bracketed_once() {
        let mut buffer = filled_buffer(2, 1, 'x');
        let mut sink = FrameSink::default();
        write_frame(&mut sink, &mut buffer, false, &FrameImage::default());

        let text = String::from_utf8(sink.bytes).unwrap();
        assert_eq!(text.matches(BEGIN_SYNCHRONIZED_UPDATE).count(), 1);
        assert_eq!(text.matches(END_SYNCHRONIZED_UPDATE).count(), 1);
        assert!(!text.contains(crate::ansi::CLEAR_SCREEN));
        assert_eq!(sink.flushes, 1);

        // The diff is consumed: the next frame has nothing to write
        let mut sink = FrameSink::default();
        write_frame(&mut sink, &mut buffer, false, &FrameImage::default());
        assert_eq!(sink.flushes, 0);
    }

    #[test]
    fn a_freestanding_image_lands_after_the_diff_and_before_the_end_bracket() {
        let mut buffer = filled_buffer(2, 1, 'x');
        let mut sink = FrameSink::default();
        write_frame(&mut sink, &mut buffer, false, &FrameImage::freestanding("<image>"));

        let text = String::from_utf8(sink.bytes).unwrap();
        let diff_at = text.find('x').expect("the diff");
        let image_at = text.find("<image>").expect("the image");
        let end_at = text.find(END_SYNCHRONIZED_UPDATE).expect("the end bracket");
        assert!(diff_at < image_at && image_at < end_at, "{text:?}");
        assert_eq!(sink.flushes, 1);
    }

    #[test]
    fn an_image_alone_is_bracketed() {
        let mut buffer = crate::screen::ScreenBuffer::new(2, 1);
        let mut sink = FrameSink::default();
        write_frame(&mut sink, &mut buffer, false, &FrameImage::freestanding("<image>"));

        let expected = format!("{}<image>{}", BEGIN_SYNCHRONIZED_UPDATE, END_SYNCHRONIZED_UPDATE);
        assert_eq!(String::from_utf8(sink.bytes).unwrap(), expected);
        assert_eq!(sink.flushes, 1);
    }

    #[test]
    fn a_cursor_placed_image_is_written_with_the_cursor_shown() {
        let mut buffer = filled_buffer(2, 1, 'x');
        let cursor_move = crate::ansi::move_cursor(6, 8);
        let mut sink = FrameSink::default();
        write_frame(&mut sink, &mut buffer, false, &FrameImage::at_cursor(cursor_move.clone(), "<image>".to_string()));

        let payload = format!("{cursor_move}<image>");
        let text = String::from_utf8(sink.bytes).unwrap();
        let frame_end = text.find(END_SYNCHRONIZED_UPDATE).expect("the frame's bracket");
        assert!(!text[..frame_end].contains("<image>"), "the frame carries no image: {text:?}");
        assert_eq!(
            &text[frame_end + END_SYNCHRONIZED_UPDATE.len()..],
            format!("{}{payload}{}", crate::ansi::SHOW_CURSOR, crate::ansi::HIDE_CURSOR),
            "the frame, the move, the payload, and the cursor back on"
        );
        assert_eq!(sink.chunks.len(), 7, "{:?}", sink.chunks);
        assert_eq!(
            &sink.chunks[3..],
            [crate::ansi::SHOW_CURSOR.len(), cursor_move.len(), "<image>".len(), crate::ansi::HIDE_CURSOR.len()],
            "show, move, payload, hide: {:?}",
            sink.chunks
        );
        assert_eq!(sink.flushes, 5);

        // A payload that carries its own position stays in the frame's write
        let mut sink = FrameSink::default();
        write_frame(&mut sink, &mut buffer, false, &FrameImage::freestanding("<image>"));
        assert_eq!(sink.chunks.len(), 3, "{:?}", sink.chunks);
        assert!(String::from_utf8(sink.bytes).unwrap().contains("<image>"));
    }
}
