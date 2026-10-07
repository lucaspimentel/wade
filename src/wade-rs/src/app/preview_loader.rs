//! Port of `PreviewLoader`: loads metadata (from every applicable provider)
//! and then the preview on a background thread, injecting events. A new
//! load cancels the previous one.

use std::sync::mpsc::Sender;

use crate::fs::file_preview;
use crate::input::{
    CancelToken, CombinedPreviewReadyEvent, ImagePreviewReadyEvent, InputEvent, MetadataReadyEvent,
    PreviewLoadingCompleteEvent, PreviewReadyEvent,
};
use crate::preview::{MetadataProvider, PreviewContext, PreviewProvider};
use crate::ui::metadata_renderer;

#[derive(Default)]
pub struct PreviewLoader {
    cancel: Option<CancelToken>,
}

impl PreviewLoader {
    #[must_use]
    pub fn new() -> Self {
        Self::default()
    }

    /// Port of `BeginLoad(path, metadataProviders, previewProvider, context)`.
    pub fn begin_load(
        &mut self,
        path: &str,
        metadata_providers: Vec<&'static dyn MetadataProvider>,
        preview_provider: Option<&'static dyn PreviewProvider>,
        context: PreviewContext,
        out: Sender<InputEvent>,
    ) {
        self.cancel();
        let cancel = CancelToken::new();
        self.cancel = Some(cancel.clone());
        let path = path.to_string();

        std::thread::spawn(move || {
            load_metadata_and_preview(&path, &metadata_providers, preview_provider, context, &cancel, &out);
        });
    }

    /// Port of `Cancel`.
    pub fn cancel(&mut self) {
        if let Some(cancel) = self.cancel.take() {
            cancel.cancel();
        }
    }
}

/// Port of `LoadMetadataAndPreview`. Metadata first (sections merged in
/// provider order, the first file-type label wins), then the preview with
/// the pane height reduced by the rendered metadata (at most half).
pub fn load_metadata_and_preview(
    path: &str,
    metadata_providers: &[&'static dyn MetadataProvider],
    preview_provider: Option<&'static dyn PreviewProvider>,
    mut context: PreviewContext,
    cancel: &CancelToken,
    out: &Sender<InputEvent>,
) {
    if cancel.is_cancelled() {
        return;
    }

    if !metadata_providers.is_empty() {
        let mut sections = Vec::new();
        let mut file_type_label = None;

        for provider in metadata_providers {
            if cancel.is_cancelled() {
                return;
            }

            if let Some(result) = provider.get_metadata(path, &context, cancel) {
                sections.extend(result.sections);
                file_type_label = file_type_label.or(result.file_type_label);
            }
        }

        if !sections.is_empty() && !cancel.is_cancelled() {
            // Encoding and line ending for text files; cloud placeholders are
            // not opened (that would trigger a download)
            let (encoding, line_ending) = if context.is_cloud_placeholder {
                (None, None)
            } else {
                let metadata = file_preview::detect_file_metadata(path);
                if metadata.is_binary {
                    (None, None)
                } else {
                    (Some(metadata.encoding), metadata.line_ending)
                }
            };

            let rendered_rows = metadata_renderer::render(&sections, context.pane_width_cells).len() as i32;

            if out
                .send(InputEvent::MetadataReady(MetadataReadyEvent {
                    path: path.to_string(),
                    sections,
                    file_type_label,
                    encoding,
                    line_ending,
                }))
                .is_err()
            {
                return;
            }

            // Leave the preview the rows below the metadata (+1 separator)
            let metadata_rows = (rendered_rows + 1).min(context.pane_height_cells / 2);
            let available_rows = context.pane_height_cells - metadata_rows;
            if available_rows > 0 {
                context.pane_height_cells = available_rows;
            }
        }
    }

    if cancel.is_cancelled() {
        return;
    }

    match preview_provider {
        Some(provider) => load_with_provider(path, provider, &context, cancel, out),
        None => {
            let _ =
                out.send(InputEvent::PreviewLoadingComplete(PreviewLoadingCompleteEvent { path: path.to_string() }));
        }
    }
}

/// Port of `LoadWithProvider`: combined, image or text results.
fn load_with_provider(
    path: &str,
    provider: &dyn PreviewProvider,
    context: &PreviewContext,
    cancel: &CancelToken,
    out: &Sender<InputEvent>,
) {
    if cancel.is_cancelled() {
        return;
    }

    let Some(result) = provider.get_preview(path, context, cancel) else {
        return;
    };

    if cancel.is_cancelled() {
        return;
    }

    let _ = match (result.image, result.text_lines) {
        (Some(image), Some(styled_lines)) => out.send(InputEvent::CombinedPreviewReady(CombinedPreviewReadyEvent {
            path: path.to_string(),
            styled_lines,
            image,
            pixel_width: result.image_pixel_width,
            pixel_height: result.image_pixel_height,
            file_type_label: result.file_type_label,
            is_rendered: result.is_rendered,
        })),
        (Some(image), None) => out.send(InputEvent::ImagePreviewReady(ImagePreviewReadyEvent {
            path: path.to_string(),
            image,
            pixel_width: result.image_pixel_width,
            pixel_height: result.image_pixel_height,
            file_type_label: result.file_type_label.unwrap_or_else(|| "Image".to_string()),
        })),
        (None, Some(styled_lines)) => out.send(InputEvent::PreviewReady(PreviewReadyEvent {
            path: path.to_string(),
            styled_lines,
            file_type_label: result.file_type_label,
            is_rendered: result.is_rendered,
            is_placeholder: result.is_placeholder,
        })),
        (None, None) => Ok(()),
    };
}

#[cfg(test)]
mod tests {
    //! Port of PreviewLoaderTests.cs.

    use std::sync::mpsc::{Receiver, channel};
    use std::time::Duration;

    use super::PreviewLoader;
    use crate::highlight::StyledLine;
    use crate::input::{CancelToken, InputEvent, MetadataReadyEvent, PreviewReadyEvent};
    use crate::preview::providers::TextPreviewProvider;
    use crate::preview::{
        MetadataEntry, MetadataProvider, MetadataResult, MetadataSection, PreviewContext, PreviewProvider,
        PreviewResult,
    };

    fn default_context() -> PreviewContext {
        PreviewContext {
            pane_width_cells: 40,
            pane_height_cells: 30,
            cell_pixel_width: 8,
            cell_pixel_height: 16,
            is_cloud_placeholder: false,
            is_broken_symlink: false,
            git_status: None,
            repo_root: None,
            pdf_preview_enabled: true,
            pdf_metadata_enabled: true,
            markdown_preview_enabled: true,
            ffprobe_enabled: true,
            mediainfo_enabled: true,
            zip_preview_enabled: true,
            image_previews_enabled: true,
            image_protocol: Some(crate::imaging::ImageProtocol::Sixel),
            archive_metadata_enabled: true,
        }
    }

    struct StubMetadataProvider;

    impl MetadataProvider for StubMetadataProvider {
        fn label(&self) -> &'static str {
            "Stub"
        }

        fn can_provide_metadata(&self, _path: &str, _context: &PreviewContext) -> bool {
            true
        }

        fn get_metadata(&self, path: &str, _context: &PreviewContext, _cancel: &CancelToken) -> Option<MetadataResult> {
            let name = std::path::Path::new(path).file_name().unwrap().to_string_lossy().into_owned();
            Some(MetadataResult {
                sections: vec![MetadataSection {
                    header: Some("File".to_string()),
                    entries: vec![MetadataEntry::new("Name", &name)],
                }],
                file_type_label: None,
            })
        }
    }

    /// Blocks until cancelled (C# SlowPreviewProvider: Task.Delay(ct)).
    struct SlowPreviewProvider;

    impl PreviewProvider for SlowPreviewProvider {
        fn label(&self) -> &'static str {
            "Slow"
        }

        fn can_preview(&self, _path: &str, _context: &PreviewContext) -> bool {
            true
        }

        fn get_preview(&self, _path: &str, _context: &PreviewContext, cancel: &CancelToken) -> Option<PreviewResult> {
            for _ in 0..500 {
                if cancel.is_cancelled() {
                    return None;
                }

                std::thread::sleep(Duration::from_millis(10));
            }

            Some(PreviewResult {
                text_lines: Some(vec![StyledLine::plain("slow")]),
                ..PreviewResult::default()
            })
        }
    }

    static TEXT: TextPreviewProvider = TextPreviewProvider;
    static SLOW: SlowPreviewProvider = SlowPreviewProvider;
    static STUB: StubMetadataProvider = StubMetadataProvider;

    fn temp_file(name: &str, contents: &str) -> String {
        let dir = std::env::temp_dir().join(format!("wade-preview-loader-{}", std::process::id()));
        std::fs::create_dir_all(&dir).unwrap();
        let path = dir.join(name);
        std::fs::write(&path, contents).unwrap();
        path.to_string_lossy().into_owned()
    }

    fn next_preview_for(rx: &Receiver<InputEvent>, path: &str) -> PreviewReadyEvent {
        loop {
            match rx.recv_timeout(Duration::from_secs(5)).expect("event within 5s") {
                InputEvent::PreviewReady(preview) if preview.path == path => return preview,
                _ => {}
            }
        }
    }

    fn next_metadata(rx: &Receiver<InputEvent>) -> MetadataReadyEvent {
        loop {
            if let InputEvent::MetadataReady(metadata) =
                rx.recv_timeout(Duration::from_secs(5)).expect("event within 5s")
            {
                return metadata;
            }
        }
    }

    #[test]
    fn begin_load_posts_preview_ready_event() {
        let (tx, rx) = channel();
        let mut loader = PreviewLoader::new();
        let path = temp_file("ready.txt", "hello world");

        loader.begin_load(&path, Vec::new(), Some(&TEXT), default_context(), tx);

        let preview = next_preview_for(&rx, &path);
        assert!(!preview.styled_lines.is_empty());
    }

    #[test]
    fn begin_load_cancels_previous_load() {
        let (tx, rx) = channel();
        let mut loader = PreviewLoader::new();
        let first = temp_file("first.txt", "first");
        let second = temp_file("second.txt", "second");

        loader.begin_load(&first, Vec::new(), Some(&TEXT), default_context(), tx.clone());
        loader.begin_load(&second, Vec::new(), Some(&TEXT), default_context(), tx);

        assert_eq!(next_preview_for(&rx, &second).path, second);
    }

    #[test]
    fn begin_load_with_slow_provider_cancels_on_second_load() {
        let (tx, rx) = channel();
        let mut loader = PreviewLoader::new();
        let first = temp_file("slow1.txt", "first");
        let second = temp_file("slow2.txt", "second");

        loader.begin_load(&first, Vec::new(), Some(&SLOW), default_context(), tx.clone());
        std::thread::sleep(Duration::from_millis(50));
        loader.begin_load(&second, Vec::new(), Some(&TEXT), default_context(), tx);

        assert_eq!(next_preview_for(&rx, &second).path, second);

        // The slow load was cancelled: it never reports
        while let Ok(event) = rx.recv_timeout(Duration::from_millis(100)) {
            assert!(!matches!(event, InputEvent::PreviewReady(ref p) if p.path == first));
        }
    }

    #[test]
    fn cancel_prevents_event() {
        let (tx, rx) = channel();
        let mut loader = PreviewLoader::new();
        let path = temp_file("cancel.txt", "hello world");

        loader.begin_load(&path, Vec::new(), Some(&SLOW), default_context(), tx);
        loader.cancel();

        assert!(rx.recv_timeout(Duration::from_millis(200)).is_err());
    }

    #[test]
    fn begin_load_cloud_placeholder_does_not_open_file_for_encoding_detection() {
        let (tx, rx) = channel();
        let mut loader = PreviewLoader::new();
        let path = temp_file("cloud.txt", "hello world\n");
        let context = PreviewContext {
            is_cloud_placeholder: true,
            ..default_context()
        };

        loader.begin_load(&path, vec![&STUB], None, context, tx);

        let metadata = next_metadata(&rx);
        assert_eq!(metadata.path, path);
        assert_eq!(metadata.encoding, None);
        assert_eq!(metadata.line_ending, None);
    }

    #[test]
    fn begin_load_non_cloud_file_detects_encoding_and_line_ending() {
        let (tx, rx) = channel();
        let mut loader = PreviewLoader::new();
        let path = temp_file("lf.txt", "hello\nworld\n");

        loader.begin_load(&path, vec![&STUB], None, default_context(), tx);

        let metadata = next_metadata(&rx);
        assert_eq!(metadata.path, path);
        assert_eq!(metadata.encoding.as_deref(), Some("UTF-8"));
        assert_eq!(metadata.line_ending.as_deref(), Some("LF"));

        // No preview provider: loading completes without a preview
        assert!(matches!(
            rx.recv_timeout(Duration::from_secs(5)).expect("completion"),
            InputEvent::PreviewLoadingComplete(ref complete) if complete.path == path
        ));
    }
}
