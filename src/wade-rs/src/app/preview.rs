//! Port of the App preview flow (App.cs): the selection-change trigger,
//! `BuildPreviewContext`, `ReloadActiveProvider`, `ClearPreviewCache`, the
//! loader event handlers, the right-pane file preview, the "Change
//! preview" menu, expanded-preview mode, and the image (Sixel) paths.

use crate::app::input_reader::AppAction;
use crate::app::App;
use crate::highlight::StyledLine;
use crate::input::{
    CombinedPreviewReadyEvent, ImagePreviewReadyEvent, InputMode, KeyEvent, MetadataReadyEvent, MouseEvent,
    PreviewLoadingCompleteEvent, PreviewReadyEvent,
};
use crate::preview::providers::NonePreviewProvider;
use crate::preview::{registry, MetadataProvider, MetadataSection, PreviewContext, PreviewProvider};
use crate::screen::{CellStyle, Color, ScreenBuffer};
use crate::ui::action_palette::{ActionMenuItem, ActionMenuLevel};
use crate::ui::layout::Rect;
use crate::ui::metadata_renderer;
use crate::ui::pane_renderer::PaneRenderer;

/// C# `MetaSeparatorStyle`.
const META_SEPARATOR_STYLE: CellStyle = CellStyle {
    fg: Some(Color { r: 60, g: 60, b: 80 }),
    bg: None,
    bold: false,
    dim: false,
    inverse: false,
    underline: false,
    strikethrough: false,
};


/// The App's preview cache (C# `_cachedPreview*`, `_applicable*`, ...).
#[derive(Default)]
pub struct PreviewState {
    pub(crate) loader: crate::app::preview_loader::PreviewLoader,
    pub(crate) active_provider_index: usize,
    pub(crate) applicable_providers: Option<Vec<&'static dyn PreviewProvider>>,
    pub(crate) applicable_metadata_providers: Option<Vec<&'static dyn MetadataProvider>>,
    pub(crate) active_context: Option<PreviewContext>,
    pub(crate) cached_path: Option<String>,
    pub(crate) pending_path: Option<String>,
    pub(crate) cached_styled_lines: Option<Vec<StyledLine>>,
    pub(crate) cached_file_type_label: Option<String>,
    pub(crate) cached_encoding: Option<String>,
    pub(crate) cached_line_ending: Option<String>,
    pub(crate) cached_metadata_sections: Option<Vec<MetadataSection>>,
    pub(crate) cached_metadata_file_type_label: Option<String>,
    pub(crate) loading: bool,
    pub(crate) is_rendered: bool,
    pub(crate) is_placeholder: bool,
    pub(crate) expanded_scroll_offset: usize,
    // Image previews (C# `_cachedSixelData`, `_cachedImagePath`, ...)
    pub(crate) cached_sixel_data: Option<String>,
    pub(crate) cached_image_path: Option<String>,
    pub(crate) cached_image_pixel_width: i32,
    pub(crate) cached_image_pixel_height: i32,
    pub(crate) is_image_preview: bool,
    pub(crate) is_combined_preview: bool,
    /// The next flush writes the cached Sixel data.
    pub(crate) sixel_pending: bool,
    /// Row the image starts on below a text/metadata header; 0 for the
    /// pane top.
    pub(crate) sixel_image_top: i32,
}

impl PreviewState {
    /// C# `(_isImagePreview ? _cachedImagePath : _cachedPreviewPath)`, when
    /// either is set.
    pub(crate) fn reload_path(&self) -> Option<String> {
        if self.cached_path.is_none() && self.cached_image_path.is_none() {
            return None;
        }

        if self.is_image_preview { self.cached_image_path.clone() } else { self.cached_path.clone() }
    }

    /// Drops the image before a reload at a new size.
    fn drop_image_for_reload(&mut self) {
        self.cached_sixel_data = None;
        self.sixel_pending = false;
        self.cached_styled_lines = None;
    }
}

impl App {
    /// Port of `BuildPreviewContext`.
    pub(crate) fn build_preview_context(&mut self, pane_width: i32, pane_height: i32) -> PreviewContext {
        let entries = self.get_visible_entries();
        let selected = entries.get(self.selected_index);

        // C# TryGetValue: the default (None) status when the path is absent
        let git_status = match (&self.git_statuses, selected) {
            (Some(statuses), Some(entry)) => Some(
                crate::fs::git_utils::statuses_get(statuses, &entry.full_path).unwrap_or(crate::fs::GitFileStatus::NONE),
            ),
            _ => None,
        };

        PreviewContext {
            pane_width_cells: pane_width,
            pane_height_cells: pane_height,
            cell_pixel_width: self.capabilities.cell_pixel_width,
            cell_pixel_height: self.capabilities.cell_pixel_height,
            is_cloud_placeholder: selected.is_some_and(|entry| entry.is_cloud_placeholder),
            is_broken_symlink: selected.is_some_and(|entry| entry.is_broken_symlink),
            git_status,
            repo_root: self.current_repo_root.clone(),
            pdf_preview_enabled: self.config.pdf_preview_enabled,
            pdf_metadata_enabled: self.config.pdf_metadata_enabled,
            markdown_preview_enabled: self.config.markdown_preview_enabled,
            ffprobe_enabled: self.config.ffprobe_enabled,
            mediainfo_enabled: self.config.mediainfo_enabled,
            zip_preview_enabled: self.config.zip_preview_enabled,
            image_previews_enabled: self.image_previews_effective,
            sixel_supported: self.capabilities.sixel_supported,
            archive_metadata_enabled: self.config.archive_metadata_enabled,
        }
    }

    /// The selection-change half of `SetApplicableProviders` shared by the
    /// right pane and `EnterExpandedPreview`.
    fn set_applicable_providers(&mut self, path: &str, pane_width: i32, pane_height: i32) {
        self.preview.active_provider_index = 0;
        let context = self.build_preview_context(pane_width, pane_height);
        self.preview.applicable_metadata_providers = self
            .config
            .file_metadata_enabled
            .then(|| registry::applicable_metadata_providers(path, &context));
        self.preview.applicable_providers = Some(if self.config.file_previews_enabled {
            registry::applicable_preview_providers(path, &context)
        } else {
            Vec::new()
        });
        self.preview.active_context = Some(context);
    }

    /// Port of `ReloadActiveProvider`.
    pub(crate) fn reload_active_provider(&mut self, path: &str, include_metadata: bool) {
        let Some(context) = self.preview.active_context.clone() else {
            return;
        };

        let preview_provider = self
            .preview
            .applicable_providers
            .as_ref()
            .filter(|providers| !providers.is_empty())
            .map(|providers| providers[self.preview.active_provider_index.min(providers.len() - 1)]);

        let metadata_providers = if include_metadata {
            self.preview.applicable_metadata_providers.clone()
        } else {
            None
        };

        if preview_provider.is_none() && metadata_providers.as_ref().is_none_or(Vec::is_empty) {
            return;
        }

        let state = &mut self.preview;
        state.pending_path = Some(path.to_string());
        state.loading = true;
        state.cached_styled_lines = None;
        state.cached_file_type_label = None;
        state.cached_encoding = None;
        state.cached_line_ending = None;
        if include_metadata {
            state.cached_metadata_sections = None;
            state.cached_metadata_file_type_label = None;
        }

        state.cached_sixel_data = None;
        state.cached_image_path = None;
        state.is_image_preview = false;
        state.is_combined_preview = false;
        state.is_rendered = false;
        state.is_placeholder = false;
        let sender = self.pipeline.sender();
        self.preview
            .loader
            .begin_load(path, metadata_providers.unwrap_or_default(), preview_provider, context, sender);
    }

    /// Port of `ClearPreviewCache` (also leaves expanded preview).
    pub(crate) fn clear_preview_cache(&mut self) {
        if self.input_mode == InputMode::ExpandedPreview {
            self.input_mode = InputMode::Normal;
        }

        if self.preview.is_image_preview {
            self.request_full_redraw = true;
        }

        self.preview.loader.cancel();
        let loader = std::mem::take(&mut self.preview.loader);
        self.preview = crate::app::preview::PreviewState {
            loader,
            ..Default::default()
        };
    }

    /// Port of `HandlePreviewReady`.
    pub fn handle_preview_ready(&mut self, event: PreviewReadyEvent) {
        if self.preview.pending_path.as_deref() != Some(event.path.as_str()) {
            return;
        }

        let state = &mut self.preview;
        state.cached_path = Some(event.path);
        state.cached_styled_lines = Some(event.styled_lines);
        state.cached_file_type_label = event.file_type_label;
        state.loading = false;
        state.is_rendered = event.is_rendered;
        state.is_placeholder = event.is_placeholder;
        state.is_image_preview = false;
        state.is_combined_preview = false;
        state.cached_sixel_data = None;
        state.cached_image_path = None;
    }

    /// Port of `HandleImagePreviewReady`.
    pub fn handle_image_preview_ready(&mut self, event: ImagePreviewReadyEvent) {
        if self.preview.pending_path.as_deref() != Some(event.path.as_str()) {
            return;
        }

        let state = &mut self.preview;
        state.cached_image_path = Some(event.path.clone());
        state.cached_path = Some(event.path);
        state.cached_sixel_data = Some(event.sixel_data);
        state.cached_image_pixel_width = event.pixel_width;
        state.cached_image_pixel_height = event.pixel_height;
        state.cached_file_type_label = Some(event.file_type_label);
        state.cached_styled_lines = None;
        state.is_image_preview = true;
        state.is_combined_preview = false;
        state.loading = false;
    }

    /// Port of `HandleCombinedPreviewReady`.
    pub fn handle_combined_preview_ready(&mut self, event: CombinedPreviewReadyEvent) {
        if self.preview.pending_path.as_deref() != Some(event.path.as_str()) {
            return;
        }

        let state = &mut self.preview;
        state.cached_image_path = Some(event.path.clone());
        state.cached_path = Some(event.path);
        state.cached_styled_lines = Some(event.styled_lines);
        state.cached_sixel_data = Some(event.sixel_data);
        state.cached_image_pixel_width = event.pixel_width;
        state.cached_image_pixel_height = event.pixel_height;
        state.cached_file_type_label = event.file_type_label;
        state.is_image_preview = false;
        state.is_combined_preview = true;
        state.is_rendered = event.is_rendered;
        state.loading = false;
    }

    /// Port of `HandlePreviewLoadingComplete`.
    pub fn handle_preview_loading_complete(&mut self, event: PreviewLoadingCompleteEvent) {
        if self.preview.pending_path.as_deref() != Some(event.path.as_str()) {
            return;
        }

        self.preview.cached_path = Some(event.path);
        self.preview.loading = false;
    }

    /// Port of `HandleMetadataReady`.
    pub fn handle_metadata_ready(&mut self, event: MetadataReadyEvent) {
        if self.preview.pending_path.as_deref() != Some(event.path.as_str())
            && self.preview.cached_path.as_deref() != Some(event.path.as_str())
        {
            return;
        }

        let state = &mut self.preview;
        state.cached_metadata_sections = Some(event.sections);
        state.cached_metadata_file_type_label.clone_from(&event.file_type_label);
        state.cached_encoding = event.encoding;
        state.cached_line_ending = event.line_ending;

        // Use the metadata file type label if the preview hasn't provided one
        if state.cached_file_type_label.is_none() && event.file_type_label.is_some() {
            state.cached_file_type_label = event.file_type_label;
        }
    }

    /// Port of `HandleSelectPreviewProvider`.
    pub(crate) fn handle_select_preview_provider(&mut self, index: i32) {
        let Some(providers) = &self.preview.applicable_providers else {
            return;
        };

        let Ok(index) = usize::try_from(index) else {
            return;
        };

        if index >= providers.len() {
            return;
        }

        let entries = self.get_visible_entries();
        let Some(selected) = entries.get(self.selected_index) else {
            return;
        };

        if selected.is_directory {
            return;
        }

        let path = selected.full_path.clone();
        let was_image = self.preview.is_image_preview || self.preview.is_combined_preview;
        self.preview.active_provider_index = index;
        self.reload_active_provider(&path, true);

        if was_image {
            self.request_full_redraw = true;
        }
    }

    /// True when Open on a file should enter expanded preview (App.cs:713).
    pub(crate) fn active_provider_is_previewable(&self) -> bool {
        self.preview
            .applicable_providers
            .as_ref()
            .and_then(|providers| providers.get(self.preview.active_provider_index))
            .is_some_and(|provider| provider.label() != NonePreviewProvider.label())
    }

    /// Port of `ShowPreviewMenu`.
    pub(crate) fn show_preview_menu(&mut self) {
        let Some(items) = self.build_preview_menu_items() else {
            return;
        };

        self.input_mode = InputMode::ActionPalette;
        self.modal.action_menu_stack.clear();
        self.modal.action_menu_stack.push(ActionMenuLevel::new("Change preview", items));
    }

    /// Port of `BuildPreviewMenuItems`: only when more than one provider
    /// applies; the active one is marked with a bullet.
    pub(crate) fn build_preview_menu_items(&self) -> Option<Vec<ActionMenuItem>> {
        let providers = self.preview.applicable_providers.as_ref().filter(|providers| providers.len() > 1)?;

        Some(
            providers
                .iter()
                .enumerate()
                .map(|(i, provider)| {
                    let prefix = if i == self.preview.active_provider_index { "\u{25cf} " } else { "  " };
                    let mut item = ActionMenuItem::new(&format!("{prefix}{}", provider.label()), "", AppAction::SelectPreviewProvider);
                    item.data = i as i32;
                    item
                })
                .collect(),
        )
    }

    /// Port of `EnterExpandedPreview`.
    pub(crate) fn enter_expanded_preview(&mut self) {
        self.input_mode = InputMode::ExpandedPreview;
        self.preview.expanded_scroll_offset = 0;
        let expanded = self.layout.expanded_pane;

        if let Some(path) = self.preview.reload_path() {
            self.preview.drop_image_for_reload();
            self.preview.active_context = Some(self.build_preview_context(expanded.width, expanded.height));
            self.reload_active_provider(&path, false);
        } else {
            // No preview cached (e.g. right pane was hidden): load the selection
            let entries = self.get_visible_entries();

            if let Some(selected) = entries.get(self.selected_index) {
                let path = selected.full_path.clone();
                self.set_applicable_providers(&path, expanded.width, expanded.height);
                self.reload_active_provider(&path, true);
            }
        }

        self.request_full_redraw = true;
    }

    /// Port of `LeaveExpandedPreview`.
    pub(crate) fn leave_expanded_preview(&mut self) {
        self.input_mode = InputMode::Normal;
        self.preview.expanded_scroll_offset = 0;

        if let Some(path) = self.preview.reload_path() {
            self.preview.drop_image_for_reload();
            let right = self.layout.right_pane;
            self.preview.active_context = Some(self.build_preview_context(right.width, right.height));
            self.reload_active_provider(&path, true);
        }

        crate::app::clear_screen();
        self.request_full_redraw = true;
    }

    /// Port of the resize branch's preview reload (App.cs:401-413).
    pub(crate) fn reload_preview_after_resize(&mut self) {
        let pane = if self.input_mode == InputMode::ExpandedPreview {
            self.layout.expanded_pane
        } else {
            self.layout.right_pane
        };

        if let Some(path) = self.preview.reload_path() {
            self.preview.drop_image_for_reload();
            self.preview.active_context = Some(self.build_preview_context(pane.width, pane.height));
            self.reload_active_provider(&path, true);
        }
    }

    fn expanded_max_scroll(&self) -> Option<usize> {
        let height = usize::try_from(self.layout.expanded_pane.height).unwrap_or(0);
        self.preview
            .cached_styled_lines
            .as_ref()
            .map(|lines| lines.len().saturating_sub(height))
    }

    /// Port of `HandleExpandedPreviewKey`.
    pub(crate) fn handle_expanded_preview_key(&mut self, key: &KeyEvent) {
        use crate::console_key::ConsoleKey;

        let page = usize::try_from(self.layout.expanded_pane.height).unwrap_or(0);

        match key.key {
            ConsoleKey::LeftArrow | ConsoleKey::H | ConsoleKey::Backspace | ConsoleKey::Escape | ConsoleKey::Q => {
                self.leave_expanded_preview();
            }
            ConsoleKey::UpArrow | ConsoleKey::K => {
                self.preview.expanded_scroll_offset = self.preview.expanded_scroll_offset.saturating_sub(1);
            }
            ConsoleKey::DownArrow | ConsoleKey::J => {
                if let Some(max_scroll) = self.expanded_max_scroll()
                    && self.preview.expanded_scroll_offset < max_scroll
                {
                    self.preview.expanded_scroll_offset += 1;
                }
            }
            ConsoleKey::PageUp => {
                self.preview.expanded_scroll_offset = self.preview.expanded_scroll_offset.saturating_sub(page);
            }
            ConsoleKey::PageDown => {
                if let Some(max_scroll) = self.expanded_max_scroll() {
                    self.preview.expanded_scroll_offset = max_scroll.min(self.preview.expanded_scroll_offset + page);
                }
            }
            ConsoleKey::Home => self.preview.expanded_scroll_offset = 0,
            ConsoleKey::End => {
                if let Some(max_scroll) = self.expanded_max_scroll() {
                    self.preview.expanded_scroll_offset = max_scroll;
                }
            }
            _ if key.key_char == u16::from(b'y') || key.key_char == u16::from(b'Y') => {
                let preview_path = self.preview.cached_path.clone().or_else(|| self.preview.cached_image_path.clone());

                if let Some(path) = preview_path {
                    if key.key_char == u16::from(b'y') {
                        self.copy_text_to_clipboard(&path, "Copied path to clipboard");
                    } else {
                        self.copy_git_relative_path(&path);
                    }
                }
            }
            _ => {}
        }
    }

    /// Port of `HandleExpandedPreviewMouse`.
    pub(crate) fn handle_expanded_preview_mouse(&mut self, mouse: &MouseEvent) {
        use crate::input::MouseButton;

        if mouse.button == MouseButton::ScrollUp {
            self.preview.expanded_scroll_offset = self.preview.expanded_scroll_offset.saturating_sub(1);
        } else if mouse.button == MouseButton::ScrollDown
            && let Some(max_scroll) = self.expanded_max_scroll()
            && self.preview.expanded_scroll_offset < max_scroll
        {
            self.preview.expanded_scroll_offset += 1;
        }
    }

    /// Port of the file branch of the right pane (App.cs:1161-1235).
    pub(crate) fn render_file_preview(&mut self, buffer: &mut ScreenBuffer, path: &str) {
        let pane = self.layout.right_pane;

        if self.preview.cached_path.as_deref() != Some(path) && self.preview.pending_path.as_deref() != Some(path) {
            self.set_applicable_providers(path, pane.width, pane.height);

            let no_previews = self.preview.applicable_providers.as_ref().is_some_and(Vec::is_empty);
            if no_previews && self.preview.applicable_metadata_providers.as_ref().is_some_and(Vec::is_empty) {
                // Literal C#: ClearPreviewCache also drops the context and the
                // metadata provider list the message branch below tests
                self.clear_preview_cache();
                self.preview.applicable_providers = Some(Vec::new());
                self.preview.cached_path = Some(path.to_string());
            } else {
                let was_image = self.preview.is_image_preview || self.preview.is_combined_preview;
                self.reload_active_provider(path, true);
                if was_image {
                    buffer.force_full_redraw();
                }
            }
        }

        let state = &self.preview;

        if state.applicable_providers.as_ref().is_some_and(Vec::is_empty)
            && state.applicable_metadata_providers.as_ref().is_some_and(Vec::is_empty)
        {
            let context = state.active_context.as_ref();
            let message = if context.is_some_and(|c| c.is_broken_symlink) {
                "[broken symlink]"
            } else if context.is_some_and(|c| c.is_cloud_placeholder) {
                "[cloud file \u{2013} not downloaded]"
            } else {
                crate::preview::cli_tool_hints::get_hint(path).unwrap_or("[no preview available]")
            };
            PaneRenderer::render_message(buffer, pane, message);
        } else if state.loading && state.cached_metadata_sections.is_none() {
            PaneRenderer::render_message(buffer, pane, "[loading\u{2026}]");
        } else if let Some(sections) = state.cached_metadata_sections.as_ref().filter(|_| {
            !state.loading
                && !state.is_image_preview
                && !state.is_combined_preview
                && (state.cached_styled_lines.is_none() || state.is_placeholder)
        }) {
            // Metadata only (no preview provider, or a placeholder preview)
            let metadata_lines = metadata_renderer::render(sections, pane.width);
            PaneRenderer::render_preview(buffer, pane, &metadata_lines, 0, false);
        } else if let (Some(sections), true, true) =
            (&state.cached_metadata_sections, state.is_image_preview, state.cached_sixel_data.is_some())
        {
            // Metadata above the image
            let image_top = render_metadata_with_image(buffer, pane, sections);
            self.preview.sixel_image_top = image_top;
            self.preview.sixel_pending = true;
        } else if let (Some(sections), Some(lines), false) =
            (&state.cached_metadata_sections, &state.cached_styled_lines, state.is_placeholder)
        {
            render_metadata_with_text(buffer, pane, sections, lines, state.is_rendered);
        } else if let (true, Some(lines), true) =
            (state.is_combined_preview, &state.cached_styled_lines, state.cached_sixel_data.is_some())
        {
            let image_top = render_combined_preview(buffer, pane, lines, state.is_rendered);
            self.preview.sixel_image_top = image_top;
            self.preview.sixel_pending = true;
        } else if state.is_image_preview && state.cached_sixel_data.is_some() {
            // Claim the pane for the image (the Sixel bypasses the cells)
            fill_blank(buffer, pane, pane.top);
            self.preview.sixel_image_top = 0;
            self.preview.sixel_pending = true;
        } else if let Some(lines) = &state.cached_styled_lines {
            PaneRenderer::render_preview(buffer, pane, lines, 0, !state.is_rendered);
        }
    }

    /// Port of `RenderExpandedPreview` (the status bar is drawn by the
    /// caller with the cached preview path).
    pub(crate) fn render_expanded_preview_pane(&mut self, buffer: &mut ScreenBuffer) {
        let pane = self.layout.expanded_pane;
        let state = &self.preview;

        if state.loading {
            PaneRenderer::render_message(buffer, pane, "[loading\u{2026}]");
        } else if let (true, Some(lines), true) =
            (state.is_combined_preview, &state.cached_styled_lines, state.cached_sixel_data.is_some())
        {
            let image_top = render_combined_preview(buffer, pane, lines, state.is_rendered);
            self.preview.sixel_image_top = image_top;
            self.preview.sixel_pending = true;
        } else if state.is_image_preview && state.cached_sixel_data.is_some() {
            fill_blank(buffer, pane, pane.top);
            self.preview.sixel_image_top = 0;
            self.preview.sixel_pending = true;
        } else if let Some(lines) = &self.preview.cached_styled_lines {
            PaneRenderer::render_preview(buffer, pane, lines, self.preview.expanded_scroll_offset, !self.preview.is_rendered);
        }
    }

    /// The Properties overlay's metadata: cached sections, minus the
    /// file-name section; None when nothing remains (App.cs:1279).
    pub(crate) fn properties_metadata_sections(&self, entry_name: &str) -> Option<Vec<MetadataSection>> {
        let sections: Vec<MetadataSection> = self
            .preview
            .cached_metadata_sections
            .as_ref()?
            .iter()
            .filter(|section| section.header.as_deref() != Some(entry_name))
            .cloned()
            .collect();

        (!sections.is_empty()).then_some(sections)
    }
}

/// Fills `pane` from `top` down with blank cells so the buffer claims the
/// area the Sixel image draws over.
fn fill_blank(buffer: &mut ScreenBuffer, pane: Rect, top: i32) {
    for row in top..pane.top + pane.height {
        buffer.fill_row(row, pane.left, pane.width, ' ', CellStyle::default());
    }
}

/// Port of `RenderMetadataWithImage`: metadata on top (at most half the
/// pane, the last row a separator), blank image area below. Returns the
/// image's top row.
pub fn render_metadata_with_image(buffer: &mut ScreenBuffer, pane: Rect, sections: &[MetadataSection]) -> i32 {
    let metadata_lines = metadata_renderer::render(sections, pane.width);
    let metadata_rows = (metadata_lines.len() as i32 + 1).min(pane.height / 2);

    let metadata_rect = Rect::new(pane.left, pane.top, pane.width, metadata_rows);
    PaneRenderer::render_preview(buffer, metadata_rect, &metadata_lines, 0, false);
    buffer.fill_row(pane.top + metadata_rows - 1, pane.left, pane.width, '\u{2500}', META_SEPARATOR_STYLE);

    let image_top = pane.top + metadata_rows;
    fill_blank(buffer, pane, image_top);
    image_top
}

/// Port of `RenderCombinedPreview`: text on top (at most half the pane, at
/// least one row), blank image area below. Returns the image's top row.
pub fn render_combined_preview(buffer: &mut ScreenBuffer, pane: Rect, lines: &[StyledLine], is_rendered: bool) -> i32 {
    let text_rows = (lines.len() as i32).min(pane.height / 2).max(1);

    let text_rect = Rect::new(pane.left, pane.top, pane.width, text_rows);
    PaneRenderer::render_preview(buffer, text_rect, lines, 0, !is_rendered);

    let image_top = pane.top + text_rows;
    fill_blank(buffer, pane, image_top);
    image_top
}

/// Port of `RenderMetadataWithText`: metadata on top (at most half the
/// pane, the last row a separator), the preview below.
pub fn render_metadata_with_text(
    buffer: &mut ScreenBuffer,
    pane: Rect,
    sections: &[MetadataSection],
    lines: &[StyledLine],
    is_rendered: bool,
) {
    let metadata_lines = metadata_renderer::render(sections, pane.width);

    let metadata_rows = (metadata_lines.len() as i32 + 1).min(pane.height / 2);
    let preview_rows = pane.height - metadata_rows;

    let metadata_rect = Rect::new(pane.left, pane.top, pane.width, metadata_rows);
    PaneRenderer::render_preview(buffer, metadata_rect, &metadata_lines, 0, false);

    buffer.fill_row(pane.top + metadata_rows - 1, pane.left, pane.width, '\u{2500}', META_SEPARATOR_STYLE);

    if preview_rows > 0 {
        let preview_rect = Rect::new(pane.left, pane.top + metadata_rows, pane.width, preview_rows);
        PaneRenderer::render_preview(buffer, preview_rect, lines, 0, !is_rendered);
    }
}

#[cfg(test)]
mod tests {
    use std::time::{Duration, Instant};

    use crate::app::input_reader::AppAction;
    use crate::app::{App, AppConfig};
    use crate::console_key::ConsoleKey;
    use crate::input::{InputEvent, InputMode, KeyEvent, MetadataReadyEvent, PreviewReadyEvent};
    use crate::screen::ScreenBuffer;

    /// An App listing a fresh directory with `files`, git off, laid out at
    /// 80x25 with the right pane shown.
    fn app_with(name: &str, files: &[(&str, &str)]) -> (App, std::path::PathBuf) {
        let root = crate::preview::test_path(name);
        std::fs::create_dir_all(&root).unwrap();
        for (file, contents) in files {
            std::fs::write(root.join(file), contents).unwrap();
        }

        let mut app = App::new(AppConfig {
            git_status_enabled: false,
            ..AppConfig::default()
        });
        app.current_path = root.to_string_lossy().into_owned();
        app.set_screen_size(80, 25);
        app.layout.calculate(80, 25, true, true);
        (app, root)
    }

    /// Feeds loader events to the App until `done` holds (5s timeout).
    fn pump(app: &mut App, done: impl Fn(&App) -> bool) {
        let deadline = Instant::now() + Duration::from_secs(5);

        while !done(app) {
            assert!(Instant::now() < deadline, "timed out waiting for preview events");

            match app.pipeline.try_take() {
                Some(InputEvent::PreviewReady(event)) => app.handle_preview_ready(event),
                Some(InputEvent::ImagePreviewReady(event)) => app.handle_image_preview_ready(event),
                Some(InputEvent::CombinedPreviewReady(event)) => app.handle_combined_preview_ready(event),
                Some(InputEvent::MetadataReady(event)) => app.handle_metadata_ready(event),
                Some(InputEvent::PreviewLoadingComplete(event)) => app.handle_preview_loading_complete(event),
                Some(_) => {}
                None => std::thread::sleep(Duration::from_millis(5)),
            }
        }
    }

    fn render(app: &mut App) {
        let mut buffer = ScreenBuffer::new(80, 25);
        app.render(&mut buffer);
    }

    fn loaded(app: &App) -> bool {
        !app.preview.loading && app.preview.cached_path.is_some()
    }

    fn key(key: ConsoleKey) -> KeyEvent {
        KeyEvent {
            key,
            key_char: 0,
            shift: false,
            alt: false,
            control: false,
        }
    }

    #[test]
    fn selecting_a_text_file_loads_metadata_and_preview() {
        let (mut app, root) = app_with("app-load", &[("a.rs", "fn main() {}\n")]);
        render(&mut app);

        let path = root.join("a.rs").to_string_lossy().into_owned();
        assert_eq!(app.preview.pending_path.as_deref(), Some(path.as_str()));
        assert!(app.preview.loading);

        pump(&mut app, |app| loaded(app) && app.preview.cached_metadata_sections.is_some());

        let labels: Vec<&str> = app.preview.applicable_providers.as_ref().unwrap().iter().map(|p| p.label()).collect();
        assert_eq!(labels, ["Text", "None", "Hex dump"]);
        assert_eq!(app.preview.cached_styled_lines.as_ref().unwrap()[0].text, "fn main() {}");
        assert_eq!(app.preview.cached_file_type_label.as_deref(), Some("Rust"));
        assert_eq!(app.preview.cached_encoding.as_deref(), Some("UTF-8"));
        assert_eq!(app.preview.cached_line_ending.as_deref(), Some("LF"));
        assert_eq!(app.preview.cached_metadata_sections.as_ref().unwrap()[0].header.as_deref(), Some("a.rs"));
    }

    #[test]
    fn capabilities_feed_the_preview_context() {
        let (mut app, _root) = app_with("app-caps", &[("a.txt", "x")]);
        let context = app.build_preview_context(40, 20);
        assert_eq!((context.cell_pixel_width, context.cell_pixel_height), (8, 16));
        assert!(!context.sixel_supported && !context.image_previews_enabled);

        app.set_capabilities(crate::terminal_caps::TerminalCapabilities {
            sixel_supported: true,
            cell_pixel_width: 10,
            cell_pixel_height: 20,
        });
        let context = app.build_preview_context(40, 20);
        assert_eq!((context.cell_pixel_width, context.cell_pixel_height), (10, 20));
        assert!(context.sixel_supported && context.image_previews_enabled);

        // Image previews need both the config flag and Sixel support
        app.config.image_previews_enabled = false;
        app.set_capabilities(app.capabilities);
        assert!(!app.build_preview_context(40, 20).image_previews_enabled);
    }

    fn sixel_app(name: &str) -> (App, std::path::PathBuf) {
        let (mut app, root) = app_with(name, &[]);
        image::RgbImage::from_fn(64, 32, |x, y| image::Rgb([x as u8 * 4, y as u8 * 8, 90]))
            .save(root.join("pic.png"))
            .unwrap();
        app.set_capabilities(crate::terminal_caps::TerminalCapabilities {
            sixel_supported: true,
            cell_pixel_width: 8,
            cell_pixel_height: 16,
        });
        (app, root)
    }

    #[test]
    fn image_preview_loads_and_writes_sixel_after_render() {
        let (mut app, _root) = sixel_app("app-image");
        render(&mut app);
        pump(&mut app, |app| loaded(app) && app.preview.cached_metadata_sections.is_some());

        assert!(app.preview.is_image_preview);
        assert_eq!(app.preview.cached_file_type_label.as_deref(), Some("PNG Image (64 x 32)"));
        assert_eq!((app.preview.cached_image_pixel_width, app.preview.cached_image_pixel_height), (64, 32));

        // Metadata above the image: the Sixel starts below the header
        render(&mut app);
        assert!(app.preview.sixel_pending);
        assert!(app.preview.sixel_image_top > app.layout.right_pane.top);

        let sixel = app.take_pending_sixel().expect("sixel written");
        let cursor = crate::ansi::move_cursor(app.preview.sixel_image_top, app.layout.right_pane.left);
        assert!(sixel.starts_with(&format!("{cursor}\x1bPq")));
        assert!(app.take_pending_sixel().is_none(), "written once per render");
    }

    #[test]
    fn sixel_is_suppressed_under_modals_and_without_sixel_support() {
        let (mut app, _root) = sixel_app("app-image-modal");
        render(&mut app);
        pump(&mut app, |app| loaded(app) && app.preview.cached_metadata_sections.is_some());
        render(&mut app);

        app.input_mode = InputMode::Help;
        assert!(app.take_pending_sixel().is_none());

        let (mut plain, _root) = app_with("app-image-nosixel", &[]);
        image::RgbImage::new(8, 8).save(_root.join("pic.png")).unwrap();
        render(&mut plain);
        pump(&mut plain, loaded);
        assert!(!plain.preview.is_image_preview, "no Sixel support: no image provider");
    }

    #[test]
    fn expanded_image_is_centered() {
        let (mut app, _root) = sixel_app("app-image-expanded");
        render(&mut app);
        pump(&mut app, |app| loaded(app) && app.preview.cached_metadata_sections.is_some());

        app.dispatch(AppAction::Open);
        assert_eq!(app.input_mode, InputMode::ExpandedPreview);
        pump(&mut app, |app| loaded(app) && app.preview.is_image_preview);
        render(&mut app);

        // 64x32 px over 8x16 px cells: 8x2 cells centered in the 80x24 pane
        let expected = app.layout.expanded_pane.center_content(8, 2);
        let sixel = app.take_pending_sixel().expect("sixel");
        assert!(sixel.starts_with(&crate::ansi::move_cursor(expected.0, expected.1)));
    }

    #[test]
    fn events_for_other_paths_are_ignored() {
        let (mut app, _root) = app_with("app-stale", &[("a.txt", "x")]);
        app.preview.pending_path = Some("/current".to_string());

        app.handle_preview_ready(PreviewReadyEvent {
            path: "/stale".to_string(),
            styled_lines: Vec::new(),
            file_type_label: None,
            is_rendered: false,
            is_placeholder: false,
        });
        app.handle_metadata_ready(MetadataReadyEvent {
            path: "/stale".to_string(),
            sections: Vec::new(),
            file_type_label: Some("X".to_string()),
            encoding: None,
            line_ending: None,
        });

        assert!(app.preview.cached_styled_lines.is_none());
        assert!(app.preview.cached_metadata_sections.is_none());
    }

    #[test]
    fn metadata_label_fills_a_missing_preview_label() {
        let (mut app, _root) = app_with("app-label", &[("a.txt", "x")]);
        app.preview.pending_path = Some("/p".to_string());

        app.handle_metadata_ready(MetadataReadyEvent {
            path: "/p".to_string(),
            sections: Vec::new(),
            file_type_label: Some("Meta".to_string()),
            encoding: None,
            line_ending: None,
        });
        assert_eq!(app.preview.cached_file_type_label.as_deref(), Some("Meta"));

        app.handle_preview_ready(PreviewReadyEvent {
            path: "/p".to_string(),
            styled_lines: Vec::new(),
            file_type_label: None,
            is_rendered: false,
            is_placeholder: false,
        });
        // C#: the preview's label (null) replaces it
        assert_eq!(app.preview.cached_file_type_label, None);
    }

    #[test]
    fn clear_preview_cache_resets_state_and_leaves_expanded_preview() {
        let (mut app, _root) = app_with("app-clear", &[("a.txt", "hello\n")]);
        render(&mut app);
        pump(&mut app, loaded);

        app.input_mode = InputMode::ExpandedPreview;
        app.preview.expanded_scroll_offset = 3;
        app.clear_preview_cache();

        assert_eq!(app.input_mode, InputMode::Normal);
        assert!(app.preview.cached_path.is_none());
        assert!(app.preview.applicable_providers.is_none());
        assert!(app.preview.cached_metadata_sections.is_none());
        assert_eq!(app.preview.expanded_scroll_offset, 0);
    }

    #[test]
    fn file_system_change_clears_preview_only_when_selection_vanishes() {
        let (mut app, root) = app_with("app-fsc", &[("a.txt", "a\n"), ("b.txt", "b\n")]);
        render(&mut app);
        pump(&mut app, loaded);

        std::fs::write(root.join("c.txt"), "c").unwrap();
        app.handle_file_system_changed(crate::input::FileSystemChangedEvent {
            directory_path: app.current_path.clone(),
            full_refresh: false,
        });
        assert!(app.preview.cached_path.is_some(), "selection survived: preview kept");

        std::fs::remove_file(root.join("a.txt")).unwrap();
        app.handle_file_system_changed(crate::input::FileSystemChangedEvent {
            directory_path: app.current_path.clone(),
            full_refresh: false,
        });
        assert!(app.preview.cached_path.is_none(), "selection deleted: preview cleared");
    }

    #[test]
    fn preview_menu_marks_active_provider_and_palette_selects_another() {
        let (mut app, _root) = app_with("app-menu", &[("a.txt", "hello\n")]);
        render(&mut app);
        pump(&mut app, loaded);

        let items = app.build_preview_menu_items().expect("three providers");
        let labels: Vec<&str> = items.iter().map(|item| item.label.as_str()).collect();
        assert_eq!(labels, ["\u{25cf} Text", "  None", "  Hex dump"]);
        assert_eq!(items[2].data, 2);

        app.dispatch(AppAction::ShowPreviewMenu);
        assert_eq!(app.input_mode, InputMode::ActionPalette);
        assert_eq!(app.modal.action_menu_stack.last().unwrap().title, "Change preview");

        app.handle_action_palette_key(key(ConsoleKey::DownArrow));
        app.handle_action_palette_key(key(ConsoleKey::DownArrow));
        app.handle_action_palette_key(key(ConsoleKey::Enter));

        assert_eq!(app.input_mode, InputMode::Normal);
        assert_eq!(app.preview.active_provider_index, 2);
        pump(&mut app, loaded);
        assert!(app.preview.is_rendered, "hex dump is a rendered preview");
    }

    #[test]
    fn preview_menu_needs_more_than_one_provider() {
        let (mut app, _root) = app_with("app-menu-one", &[("a.txt", "hello\n")]);
        app.preview.applicable_providers = Some(vec![&crate::preview::providers::NonePreviewProvider]);

        assert!(app.build_preview_menu_items().is_none());
        app.dispatch(AppAction::ShowPreviewMenu);
        assert_eq!(app.input_mode, InputMode::Normal);
    }

    #[test]
    fn palette_escape_pops_a_level_then_closes() {
        let (mut app, _root) = app_with("app-esc", &[("a.txt", "hello\n")]);
        app.show_action_palette();
        app.push_palette_level("Sub", Vec::new(), 0);

        app.handle_action_palette_key(key(ConsoleKey::Escape));
        assert_eq!(app.modal.action_menu_stack.len(), 1);
        assert_eq!(app.input_mode, InputMode::ActionPalette);

        app.handle_action_palette_key(key(ConsoleKey::Escape));
        assert!(app.modal.action_menu_stack.is_empty());
        assert_eq!(app.input_mode, InputMode::Normal);
    }

    #[test]
    fn open_on_file_enters_expanded_preview_and_scrolls_within_bounds() {
        let body: String = (0..60).map(|i| format!("line {i}\n")).collect();
        let (mut app, _root) = app_with("app-expand", &[("long.txt", &body)]);
        render(&mut app);
        pump(&mut app, loaded);

        app.dispatch(AppAction::Open);
        assert_eq!(app.input_mode, InputMode::ExpandedPreview);
        pump(&mut app, loaded);

        // 60 lines in a 24-row expanded pane: at most 36 rows of scroll
        app.handle_modal_key(key(ConsoleKey::End));
        assert_eq!(app.preview.expanded_scroll_offset, 36);
        app.handle_modal_key(key(ConsoleKey::DownArrow));
        assert_eq!(app.preview.expanded_scroll_offset, 36);
        app.handle_modal_key(key(ConsoleKey::PageUp));
        assert_eq!(app.preview.expanded_scroll_offset, 12);
        app.handle_modal_key(key(ConsoleKey::Home));
        assert_eq!(app.preview.expanded_scroll_offset, 0);
        app.handle_modal_key(key(ConsoleKey::UpArrow));
        assert_eq!(app.preview.expanded_scroll_offset, 0);

        app.handle_modal_key(key(ConsoleKey::Escape));
        assert_eq!(app.input_mode, InputMode::Normal);
    }

    #[test]
    fn open_on_file_with_none_provider_stays_in_normal_mode() {
        let (mut app, _root) = app_with("app-none", &[("a.bin", "\0\0\0\0")]);
        render(&mut app);
        pump(&mut app, loaded);

        // Binary: None is the default provider
        assert!(!app.active_provider_is_previewable());
        app.dispatch(AppAction::Open);
        assert_eq!(app.input_mode, InputMode::Normal);
    }

    #[test]
    fn properties_metadata_drops_the_file_name_section() {
        let (mut app, _root) = app_with("app-props", &[("a.txt", "x")]);
        app.preview.cached_metadata_sections = Some(vec![crate::preview::MetadataSection {
            header: Some("a.txt".to_string()),
            entries: Vec::new(),
        }]);
        assert!(app.properties_metadata_sections("a.txt").is_none());

        app.preview.cached_metadata_sections.as_mut().unwrap().push(crate::preview::MetadataSection {
            header: Some("Archive".to_string()),
            entries: Vec::new(),
        });
        let sections = app.properties_metadata_sections("a.txt").unwrap();
        assert_eq!(sections.len(), 1);
        assert_eq!(sections[0].header.as_deref(), Some("Archive"));
    }
}
