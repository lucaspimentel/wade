//! Port of `MarkdigRenderer`: Markdown to styled preview lines. The C#
//! code walks a Markdig AST (pipe tables enabled); this port builds the
//! equivalent tree from pulldown-cmark events (tables enabled) and then
//! renders it with the C# rules.
//!
//! Layout runs on UTF-16 code units, as C# strings do, so wrapping, padding
//! and truncation land on the same columns for astral characters (emoji);
//! lines convert back to `String` with one style per character.
//!
//! Markdig specifics reproduced: a link-reference-definition group is a
//! (silent) block taking the slot of the first definition paragraph; HTML
//! blocks render nothing but still separate blocks; HTML entities are
//! dropped; tight list items are paragraphs; a fenced code block's info is
//! its first word.

use pulldown_cmark::{CodeBlockKind, Event, LinkType, Options, Parser, Tag, TagEnd};

use crate::highlight::theme::{get_style, PLAIN};
use crate::highlight::{StyledLine, TokenKind};
use crate::input::CancelToken;
use crate::screen::{CellStyle, Color};

const fn style(r: u8, g: u8, b: u8, bold: bool, dim: bool) -> CellStyle {
    CellStyle {
        fg: Some(Color { r, g, b }),
        bg: None,
        bold,
        dim,
        inverse: false,
        underline: false,
        strikethrough: false,
    }
}

// Heading colors by level (H1 = brightest, H6 = dimmest)
const H1_STYLE: CellStyle = style(100, 180, 255, true, false);
const H2_STYLE: CellStyle = style(86, 156, 214, true, false);
const H3_STYLE: CellStyle = style(78, 201, 176, true, false);
const H4_STYLE: CellStyle = style(78, 201, 176, false, false);
const H5_STYLE: CellStyle = style(156, 220, 254, false, false);
const H6_STYLE: CellStyle = style(156, 220, 254, false, true);

const PLAIN_STYLE: CellStyle = style(200, 200, 200, false, false);
const BOLD_STYLE: CellStyle = style(200, 200, 200, true, false);
const CODE_SPAN_STYLE: CellStyle = style(206, 145, 120, false, false);
const LINK_TEXT_STYLE: CellStyle = style(78, 201, 176, false, false);
const LINK_URL_STYLE: CellStyle = style(78, 201, 176, false, true);
const CODE_BLOCK_BG: CellStyle = CellStyle {
    bg: Some(Color { r: 30, g: 30, b: 46 }),
    ..style(200, 200, 200, false, false)
};
const BLOCKQUOTE_BAR_STYLE: CellStyle = style(100, 100, 120, false, true);
const BLOCKQUOTE_TEXT_STYLE: CellStyle = style(160, 160, 175, false, false);
const HR_STYLE: CellStyle = style(100, 100, 120, false, true);
const LIST_MARKER_STYLE: CellStyle = style(180, 180, 180, false, false);
const TABLE_BAR_STYLE: CellStyle = style(100, 100, 120, false, false);

// YAML frontmatter
const FRONTMATTER_BORDER_STYLE: CellStyle = style(100, 85, 140, false, true);
const FRONTMATTER_PUNCT_STYLE: CellStyle = style(130, 130, 145, false, true);

const DEFAULT_STYLE: CellStyle = CellStyle {
    fg: None,
    bg: None,
    bold: false,
    dim: false,
    inverse: false,
    underline: false,
    strikethrough: false,
};

/// Fenced code info strings mapped to file extensions for highlighting.
const INFO_TO_EXTENSION: &[(&str, &str)] = &[
    ("csharp", ".cs"),
    ("cs", ".cs"),
    ("c#", ".cs"),
    ("javascript", ".js"),
    ("js", ".js"),
    ("jsx", ".jsx"),
    ("typescript", ".ts"),
    ("ts", ".ts"),
    ("tsx", ".tsx"),
    ("python", ".py"),
    ("py", ".py"),
    ("go", ".go"),
    ("golang", ".go"),
    ("rust", ".rs"),
    ("rs", ".rs"),
    ("java", ".java"),
    ("bash", ".sh"),
    ("sh", ".sh"),
    ("shell", ".sh"),
    ("zsh", ".sh"),
    ("powershell", ".ps1"),
    ("ps1", ".ps1"),
    ("pwsh", ".ps1"),
    ("css", ".css"),
    ("scss", ".scss"),
    ("json", ".json"),
    ("yaml", ".yaml"),
    ("yml", ".yaml"),
    ("toml", ".toml"),
    ("xml", ".xml"),
    ("html", ".html"),
    ("htm", ".html"),
    ("markdown", ".md"),
    ("md", ".md"),
];

// ── UTF-16 text helpers ────────────────────────────────────────────────

type Units = Vec<u16>;

fn units(text: &str) -> Units {
    text.encode_utf16().collect()
}

fn repeat_unit(ch: char, count: usize) -> Units {
    let mut buf = [0u16; 2];
    let encoded = ch.encode_utf16(&mut buf);
    encoded.iter().copied().cycle().take(count * encoded.len()).collect()
}

const SPACE: u16 = b' ' as u16;
const NEWLINE: u16 = b'\n' as u16;

/// A line in units with one style per unit; becomes a `StyledLine` with one
/// style per character (a split surrogate decodes as U+FFFD).
fn to_styled_line(text: &[u16], styles: Option<&[CellStyle]>) -> StyledLine {
    let decoded = String::from_utf16_lossy(text);

    let char_styles = styles.map(|styles| {
        let mut out = Vec::with_capacity(text.len());
        let mut index = 0;

        for ch in char::decode_utf16(text.iter().copied()) {
            out.push(styles.get(index).copied().unwrap_or(DEFAULT_STYLE));
            index += ch.map_or(1, char::len_utf16);
        }

        out
    });

    StyledLine {
        text: decoded,
        spans: None,
        char_styles,
    }
}

// ── Document model (the Markdig AST subset the renderer reads) ─────────

#[derive(Debug)]
enum Inline {
    Literal(String),
    Emphasis { strong: bool, children: Vec<Inline> },
    Code(String),
    Link { url: String, children: Vec<Inline> },
    Image { children: Vec<Inline> },
    Autolink(String),
    LineBreak { hard: bool },
    Html(String),
    /// Markdig's `HtmlEntityInline`: a leaf the renderer ignores.
    Entity,
}

#[derive(Debug)]
enum Block {
    Heading { level: usize, inlines: Vec<Inline> },
    Paragraph(Vec<Inline>),
    Code { info: Option<String>, lines: Vec<String> },
    List { ordered: bool, start: Option<u64>, loose: bool, items: Vec<Vec<Block>> },
    Quote(Vec<Block>),
    Rule,
    Table { columns: usize, rows: Vec<Vec<Vec<Inline>>> },
    /// `LinkReferenceDefinitionGroup` and `HtmlBlock`: render nothing.
    Silent,
}

enum Frame {
    Container(Vec<Block>),
    Item { blocks: Vec<Block>, implicit: Option<Vec<Inline>>, explicit_paragraph: bool },
    List { ordered: bool, start: Option<u64>, loose: bool, items: Vec<Vec<Block>> },
    Leaf { kind: LeafKind, inlines: Vec<Inline> },
    Code { info: Option<String>, text: String },
    Html,
    Table { columns: usize, rows: Vec<Vec<Vec<Inline>>>, row: Vec<Vec<Inline>> },
    Cell(Vec<Inline>),
    Inline(InlineFrame),
}

enum LeafKind {
    Paragraph,
    Heading(usize),
}

enum InlineFrame {
    Emphasis { strong: bool, children: Vec<Inline> },
    Link { url: String, children: Vec<Inline> },
    Image { children: Vec<Inline> },
    Autolink { text: String },
}

struct Builder<'a> {
    source: &'a str,
    stack: Vec<Frame>,
    /// Source start of each root-level block, for placing the definition group.
    root_starts: Vec<usize>,
}

impl<'a> Builder<'a> {
    fn new(source: &'a str) -> Self {
        Self {
            source,
            stack: vec![Frame::Container(Vec::new())],
            root_starts: Vec::new(),
        }
    }

    fn push_inline(&mut self, inline: Inline) {
        match self.stack.last_mut().expect("root frame") {
            Frame::Inline(InlineFrame::Emphasis { children, .. })
            | Frame::Inline(InlineFrame::Link { children, .. })
            | Frame::Inline(InlineFrame::Image { children }) => children.push(inline),
            Frame::Inline(InlineFrame::Autolink { text }) => {
                if let Inline::Literal(literal) = inline {
                    text.push_str(&literal);
                }
            }
            Frame::Leaf { inlines, .. } | Frame::Cell(inlines) => inlines.push(inline),
            Frame::Item { implicit, .. } => implicit.get_or_insert_with(Vec::new).push(inline),
            _ => {}
        }
    }

    /// Ends a tight item's implicit paragraph before a block starts in it.
    fn flush_implicit(&mut self) {
        if let Some(Frame::Item { blocks, implicit, .. }) = self.stack.last_mut()
            && let Some(inlines) = implicit.take()
        {
            blocks.push(Block::Paragraph(inlines));
        }
    }

    fn push_block(&mut self, block: Block) {
        match self.stack.last_mut().expect("root frame") {
            Frame::Container(blocks) | Frame::Item { blocks, .. } => blocks.push(block),
            _ => {}
        }
    }

    fn start_block(&mut self, frame: Frame, start: usize) {
        self.flush_implicit();
        if self.stack.len() == 1 {
            self.root_starts.push(start);
        }

        if let (Frame::Leaf { kind: LeafKind::Paragraph, .. }, Some(Frame::Item { explicit_paragraph, .. })) =
            (&frame, self.stack.last_mut())
        {
            *explicit_paragraph = true;
        }

        self.stack.push(frame);
    }

    fn handle(&mut self, event: Event<'_>, range: std::ops::Range<usize>) {
        match event {
            Event::Start(tag) => self.start(tag, range.start),
            Event::End(tag) => self.end(tag),
            Event::Text(text) => {
                if matches!(self.stack.last(), Some(Frame::Code { .. })) {
                    if let Some(Frame::Code { text: code, .. }) = self.stack.last_mut() {
                        code.push_str(&text);
                    }
                    return;
                }

                // Markdig keeps entities as HtmlEntityInline, which no
                // renderer path reads
                let raw = &self.source[range];
                if raw.len() > 2 && raw.starts_with('&') && raw.ends_with(';') && *raw != *text {
                    self.push_inline(Inline::Entity);
                } else {
                    self.push_inline(Inline::Literal(text.into_string()));
                }
            }
            Event::Code(text) => self.push_inline(Inline::Code(text.into_string())),
            Event::InlineHtml(html) => self.push_inline(Inline::Html(html.into_string())),
            Event::Html(_) => {}
            Event::SoftBreak => self.push_inline(Inline::LineBreak { hard: false }),
            Event::HardBreak => self.push_inline(Inline::LineBreak { hard: true }),
            Event::Rule => {
                self.flush_implicit();
                if self.stack.len() == 1 {
                    self.root_starts.push(range.start);
                }
                self.push_block(Block::Rule);
            }
            _ => {}
        }
    }

    fn start(&mut self, tag: Tag<'_>, start: usize) {
        match tag {
            Tag::Paragraph => self.start_block(
                Frame::Leaf {
                    kind: LeafKind::Paragraph,
                    inlines: Vec::new(),
                },
                start,
            ),
            Tag::Heading { level, .. } => self.start_block(
                Frame::Leaf {
                    kind: LeafKind::Heading(level as usize),
                    inlines: Vec::new(),
                },
                start,
            ),
            Tag::BlockQuote(_) => self.start_block(Frame::Container(Vec::new()), start),
            Tag::CodeBlock(kind) => {
                let info = match kind {
                    CodeBlockKind::Fenced(info) => info.split_whitespace().next().map(str::to_string),
                    CodeBlockKind::Indented => None,
                };
                self.start_block(Frame::Code { info, text: String::new() }, start);
            }
            Tag::HtmlBlock => self.start_block(Frame::Html, start),
            Tag::List(first) => self.start_block(
                Frame::List {
                    ordered: first.is_some(),
                    start: first,
                    loose: false,
                    items: Vec::new(),
                },
                start,
            ),
            Tag::Item => self.stack.push(Frame::Item {
                blocks: Vec::new(),
                implicit: None,
                explicit_paragraph: false,
            }),
            Tag::Table(alignments) => self.start_block(
                Frame::Table {
                    columns: alignments.len(),
                    rows: Vec::new(),
                    row: Vec::new(),
                },
                start,
            ),
            Tag::TableHead | Tag::TableRow => {}
            Tag::TableCell => self.stack.push(Frame::Cell(Vec::new())),
            Tag::Emphasis => self.stack.push(Frame::Inline(InlineFrame::Emphasis {
                strong: false,
                children: Vec::new(),
            })),
            Tag::Strong => self.stack.push(Frame::Inline(InlineFrame::Emphasis {
                strong: true,
                children: Vec::new(),
            })),
            Tag::Link { link_type, dest_url, .. } => {
                let frame = if matches!(link_type, LinkType::Autolink | LinkType::Email) {
                    InlineFrame::Autolink { text: String::new() }
                } else {
                    InlineFrame::Link {
                        url: dest_url.into_string(),
                        children: Vec::new(),
                    }
                };
                self.stack.push(Frame::Inline(frame));
            }
            Tag::Image { .. } => self.stack.push(Frame::Inline(InlineFrame::Image { children: Vec::new() })),
            _ => {}
        }
    }

    fn end(&mut self, tag: TagEnd) {
        match tag {
            TagEnd::Paragraph | TagEnd::Heading(_) => {
                if let Some(Frame::Leaf { kind, inlines }) = self.stack.pop() {
                    self.push_block(match kind {
                        LeafKind::Paragraph => Block::Paragraph(inlines),
                        LeafKind::Heading(level) => Block::Heading { level, inlines },
                    });
                }
            }
            TagEnd::BlockQuote(_) => {
                if let Some(Frame::Container(blocks)) = self.stack.pop() {
                    self.push_block(Block::Quote(blocks));
                }
            }
            TagEnd::CodeBlock => {
                if let Some(Frame::Code { info, text }) = self.stack.pop() {
                    let mut lines: Vec<String> =
                        text.split('\n').map(|line| line.strip_suffix('\r').unwrap_or(line).to_string()).collect();
                    if text.ends_with('\n') || text.is_empty() {
                        lines.pop();
                    }
                    self.push_block(Block::Code { info, lines });
                }
            }
            TagEnd::HtmlBlock => {
                self.stack.pop();
                self.push_block(Block::Silent);
            }
            TagEnd::List(_) => {
                if let Some(Frame::List {
                    ordered,
                    start,
                    loose,
                    items,
                }) = self.stack.pop()
                {
                    self.push_block(Block::List {
                        ordered,
                        start,
                        loose,
                        items,
                    });
                }
            }
            TagEnd::Item => {
                self.flush_implicit();
                if let Some(Frame::Item {
                    blocks,
                    explicit_paragraph,
                    ..
                }) = self.stack.pop()
                    && let Some(Frame::List { loose, items, .. }) = self.stack.last_mut()
                {
                    *loose |= explicit_paragraph;
                    items.push(blocks);
                }
            }
            TagEnd::TableCell => {
                if let Some(Frame::Cell(inlines)) = self.stack.pop()
                    && let Some(Frame::Table { row, .. }) = self.stack.last_mut()
                {
                    row.push(inlines);
                }
            }
            TagEnd::TableHead | TagEnd::TableRow => {
                if let Some(Frame::Table { rows, row, .. }) = self.stack.last_mut() {
                    rows.push(std::mem::take(row));
                }
            }
            TagEnd::Table => {
                if let Some(Frame::Table { columns, rows, .. }) = self.stack.pop() {
                    self.push_block(Block::Table { columns, rows });
                }
            }
            TagEnd::Emphasis | TagEnd::Strong | TagEnd::Link | TagEnd::Image => {
                let inline = match self.stack.pop() {
                    Some(Frame::Inline(InlineFrame::Emphasis { strong, children })) => Inline::Emphasis { strong, children },
                    Some(Frame::Inline(InlineFrame::Link { url, children })) => Inline::Link { url, children },
                    Some(Frame::Inline(InlineFrame::Image { children })) => Inline::Image { children },
                    Some(Frame::Inline(InlineFrame::Autolink { text })) => Inline::Autolink(text),
                    _ => return,
                };
                self.push_inline(inline);
            }
            _ => {}
        }
    }

    fn finish(mut self) -> (Vec<Block>, Vec<usize>) {
        while self.stack.len() > 1 {
            self.stack.pop();
        }

        match self.stack.pop() {
            Some(Frame::Container(blocks)) => (blocks, self.root_starts),
            _ => (Vec::new(), Vec::new()),
        }
    }
}

/// Parses `text` into root blocks, inserting the silent definition group
/// where Markdig puts it.
fn parse(text: &str) -> Vec<Block> {
    let parser = Parser::new_ext(text, Options::ENABLE_TABLES);
    let first_definition = parser
        .reference_definitions()
        .iter()
        .map(|(_, definition)| definition.span.clone())
        .min_by_key(|span| span.start);

    let mut builder = Builder::new(text);
    for (event, range) in parser.into_offset_iter() {
        builder.handle(event, range);
    }

    let (mut blocks, starts) = builder.finish();

    if let Some(span) = first_definition {
        let mut index = starts.iter().position(|&start| start >= span.start).unwrap_or(blocks.len());

        // Definitions followed directly (no blank line) by paragraph text
        // share Markdig's paragraph, which closes before the group is added
        if index < blocks.len()
            && matches!(blocks[index], Block::Paragraph(_))
            && !text[span.end.min(starts[index])..starts[index]].contains("\n\n")
            && !text[span.end.min(starts[index])..starts[index]].trim().is_empty()
        {
            index += 1;
        }

        blocks.insert(index.min(blocks.len()), Block::Silent);
    }

    blocks
}

// ── Rendering ──────────────────────────────────────────────────────────

type Spans = Vec<(Units, CellStyle)>;

/// Port of `Render`: reads the file (BOM-aware, like `File.ReadAllText`)
/// and renders it; `None` when unreadable.
#[must_use]
pub fn render(file_path: &str, width: i32, cancel: &CancelToken) -> Option<Vec<StyledLine>> {
    let bytes = std::fs::read(file_path).ok()?;
    let text = read_all_text(&bytes);
    render_text(&text, width, cancel)
}

/// `File.ReadAllText`: UTF-8 unless a UTF-16 or UTF-8 BOM says otherwise.
fn read_all_text(bytes: &[u8]) -> String {
    if let Some(rest) = bytes.strip_prefix(b"\xEF\xBB\xBF") {
        return String::from_utf8_lossy(rest).into_owned();
    }

    if let Some(rest) = bytes.strip_prefix(b"\xFF\xFE") {
        return crate::fs::lnk::decode_utf16le(rest);
    }

    if let Some(rest) = bytes.strip_prefix(b"\xFE\xFF") {
        let swapped: Vec<u8> = rest.chunks(2).flat_map(|pair| pair.iter().rev().copied()).collect();
        return crate::fs::lnk::decode_utf16le(&swapped);
    }

    String::from_utf8_lossy(bytes).into_owned()
}

/// Port of `RenderText`. `None` only when cancelled.
#[must_use]
pub fn render_text(text: &str, width: i32, cancel: &CancelToken) -> Option<Vec<StyledLine>> {
    let width = width.max(4) as usize;
    let mut lines = Vec::new();
    let mut body = text;

    if let Some((frontmatter, remaining)) = try_extract_frontmatter(text) {
        render_frontmatter(&frontmatter, &mut lines, width);
        lines.push(StyledLine::plain(""));
        body = remaining;
    }

    if !body.trim().is_empty() {
        let blocks = parse(body);
        let mut document = Vec::new();
        render_blocks(&blocks, &mut document, width, 0, 0, cancel)?;
        trim_trailing_empty(&mut document);
        lines.extend(document);
    }

    trim_trailing_empty(&mut lines);
    Some(lines)
}

fn trim_trailing_empty(lines: &mut Vec<StyledLine>) {
    while lines.last().is_some_and(|line| line.text.is_empty()) {
        lines.pop();
    }
}

/// Port of `TryExtractFrontmatter`: a leading `---` line through the next
/// `---` line.
fn try_extract_frontmatter(text: &str) -> Option<(String, &str)> {
    let first_newline = text.find('\n')?;
    if text[..first_newline].trim_end_matches('\r') != "---" {
        return None;
    }

    let content_start = first_newline + 1;
    let mut pos = content_start;

    while pos < text.len() {
        let line_end = text[pos..].find('\n').map(|i| pos + i);
        let next_pos = line_end.map_or(text.len(), |end| end + 1);
        let line = match line_end {
            Some(end) => text[pos..end].trim_end_matches('\r'),
            None => &text[pos..],
        };

        if line == "---" {
            let content = text[content_start..pos].trim_end_matches(['\r', '\n']).to_string();
            let remaining = if next_pos < text.len() { &text[next_pos..] } else { "" };
            return Some((content, remaining));
        }

        pos = next_pos;
    }

    None
}

/// Port of `RenderFrontmatter`: aligned key/value rows between borders.
fn render_frontmatter(content: &str, lines: &mut Vec<StyledLine>, width: usize) {
    let mut entries: Vec<(Units, Units)> = Vec::new();
    let mut max_key_len = 0;

    for raw_line in content.split('\n') {
        let line = raw_line.trim_end_matches('\r');
        if line.is_empty() {
            continue;
        }

        match line.find(':') {
            Some(colon) if colon > 0 => {
                let key = units(&line[..colon]);
                let value = units(line[colon + 1..].trim_start());
                max_key_len = max_key_len.max(key.len());
                entries.push((key, value));
            }
            _ => entries.push((Vec::new(), units(line))),
        }
    }

    if entries.is_empty() {
        return;
    }

    // Header: ─── frontmatter ─────────...
    let mut header = repeat_unit('\u{2500}', 3);
    header.extend(units(" frontmatter "));
    let fill = width.saturating_sub(header.len());
    header.extend(repeat_unit('\u{2500}', fill));
    header.truncate(width.min(header.len()));
    lines.push(to_styled_line(&header, Some(&vec![FRONTMATTER_BORDER_STYLE; header.len()])));

    const LEFT_PAD: usize = 1;
    let value_col = LEFT_PAD + max_key_len + 2;
    let value_width = 6.max(width as isize - value_col as isize) as usize;
    let key_style = get_style(TokenKind::Key);

    for (key, value) in entries {
        if key.is_empty() {
            let mut text = vec![SPACE; LEFT_PAD];
            text.extend(value);
            lines.push(to_styled_line(&text, None));
            continue;
        }

        let mut first_prefix = vec![SPACE; LEFT_PAD];
        first_prefix.extend(&key);
        first_prefix.extend(vec![SPACE; max_key_len - key.len()]);
        first_prefix.extend(units(": "));

        let mut prefix_styles = vec![DEFAULT_STYLE; LEFT_PAD];
        prefix_styles.extend(vec![key_style; max_key_len]);
        prefix_styles.extend([FRONTMATTER_PUNCT_STYLE, FRONTMATTER_PUNCT_STYLE]);

        let cont_prefix = vec![SPACE; value_col];
        let cont_styles = vec![DEFAULT_STYLE; value_col];

        if value.is_empty() {
            lines.push(to_styled_line(&first_prefix, Some(&prefix_styles)));
        } else {
            let value_style = frontmatter_value_style(&value);
            wrap_frontmatter_value(
                &value,
                value_style,
                value_width,
                (&first_prefix, &prefix_styles),
                (&cont_prefix, &cont_styles),
                lines,
            );
        }
    }

    lines.push(to_styled_line(&repeat_unit('\u{2500}', width), Some(&vec![FRONTMATTER_BORDER_STYLE; width])));
}

/// Port of `WrapFrontmatterValue`.
fn wrap_frontmatter_value(
    value: &[u16],
    value_style: CellStyle,
    line_width: usize,
    first: (&[u16], &[CellStyle]),
    cont: (&[u16], &[CellStyle]),
    lines: &mut Vec<StyledLine>,
) {
    let mut is_first = true;
    let mut pos = 0;

    while pos < value.len() {
        let (prefix, prefix_styles) = if is_first { first } else { cont };
        let remaining = value.len() - pos;
        let mut take = remaining.min(line_width);

        if take < remaining {
            let mut break_at = take;
            while break_at > 0 && value[pos + break_at - 1] != SPACE {
                break_at -= 1;
            }
            if break_at == 0 {
                break_at = take;
            }
            take = break_at;
        }

        let mut actual = take;
        while actual > 0 && value[pos + actual - 1] == SPACE {
            actual -= 1;
        }

        let mut text = prefix.to_vec();
        text.extend(&value[pos..pos + actual]);
        let mut styles = prefix_styles.to_vec();
        styles.extend(vec![value_style; actual]);
        lines.push(to_styled_line(&text, Some(&styles)));

        pos += take;
        while pos < value.len() && value[pos] == SPACE {
            pos += 1;
        }

        is_first = false;
    }
}

/// Port of `GetFrontmatterValueStyle`.
fn frontmatter_value_style(value: &[u16]) -> CellStyle {
    let text = String::from_utf16_lossy(value);
    let mut chars = text.chars();
    let first = chars.next();

    match first {
        None => PLAIN,
        Some('"' | '\'') => get_style(TokenKind::String),
        _ if matches!(text.as_str(), "true" | "false" | "yes" | "no" | "null" | "~") => get_style(TokenKind::Constant),
        Some(c) if crate::text::is_digit(c) => get_style(TokenKind::Number),
        Some('-') if chars.next().is_some_and(crate::text::is_digit) => get_style(TokenKind::Number),
        _ => PLAIN,
    }
}

/// Port of `RenderBlocks`: blocks separated by blank (quoted) lines.
fn render_blocks(
    blocks: &[Block],
    lines: &mut Vec<StyledLine>,
    width: usize,
    indent: usize,
    quote_depth: usize,
    cancel: &CancelToken,
) -> Option<()> {
    for (i, block) in blocks.iter().enumerate() {
        if cancel.is_cancelled() {
            return None;
        }

        match block {
            Block::Heading { level, inlines } => render_heading(*level, inlines, lines, width, indent, quote_depth),
            Block::Paragraph(inlines) => {
                let mut spans = Spans::new();
                collect_inlines(inlines, &mut spans, PLAIN_STYLE);
                emit_wrapped_line(&spans, lines, width, indent, quote_depth, None);
            }
            Block::Code { info, lines: code } => {
                let lang = info
                    .as_deref()
                    .filter(|info| !info.is_empty())
                    .and_then(|info| INFO_TO_EXTENSION.iter().find(|(name, _)| name.eq_ignore_ascii_case(info)))
                    .and_then(|(_, ext)| crate::highlight::get_language(&format!("dummy{ext}")));
                render_code_lines(code, lines, width, indent, quote_depth, lang);
            }
            Block::List {
                ordered,
                start,
                loose,
                items,
            } => render_list(*ordered, *start, *loose, items, lines, width, indent, quote_depth, cancel)?,
            Block::Quote(children) => render_blocks(children, lines, width, indent, quote_depth + 1, cancel)?,
            Block::Rule => render_horizontal_rule(lines, width, indent, quote_depth),
            Block::Table { columns, rows } => render_table(*columns, rows, lines, width, indent, quote_depth),
            Block::Silent => {}
        }

        if i + 1 < blocks.len() {
            lines.push(make_quoted_line(&[], None, quote_depth, 0));
        }
    }

    Some(())
}

fn render_heading(level: usize, inlines: &[Inline], lines: &mut Vec<StyledLine>, width: usize, indent: usize, quote_depth: usize) {
    let style = match level {
        1 => H1_STYLE,
        2 => H2_STYLE,
        3 => H3_STYLE,
        4 => H4_STYLE,
        5 => H5_STYLE,
        _ => H6_STYLE,
    };

    let mut prefix = repeat_unit('#', level);
    prefix.push(SPACE);
    let mut spans: Spans = vec![(prefix, style)];
    collect_inlines(inlines, &mut spans, style);
    emit_wrapped_line(&spans, lines, width, indent, quote_depth, None);
}

/// Port of `RenderCodeLines`: truncated, highlighted, padded to the width
/// on the code background.
fn render_code_lines(
    code: &[String],
    lines: &mut Vec<StyledLine>,
    width: usize,
    indent: usize,
    quote_depth: usize,
    lang: Option<&'static dyn crate::highlight::Language>,
) {
    let prefix_len = indent + quote_depth * 2;
    let content_width = (width as isize - prefix_len as isize).max(2) as usize;
    let mut state = 0u8;

    for line in code {
        let mut text = units(line);
        text.truncate(content_width);

        let mut char_styles = vec![CODE_BLOCK_BG; text.len()];

        if let Some(lang) = lang {
            let decoded = String::from_utf16_lossy(&text);
            let highlighted = lang.tokenize_line(&decoded, &mut state);

            // Per-char styles of the highlighted line, spread to UTF-16 units
            let per_char: Option<Vec<CellStyle>> = if let Some(spans) = &highlighted.spans {
                Some(
                    (0..decoded.chars().count())
                        .map(|ci| {
                            spans
                                .iter()
                                .find(|span| ci >= span.start && ci < span.start + span.len)
                                .map_or(CODE_BLOCK_BG, |span| {
                                    let token = get_style(span.kind);
                                    CellStyle {
                                        bg: CODE_BLOCK_BG.bg,
                                        ..token
                                    }
                                })
                        })
                        .collect(),
                )
            } else {
                highlighted.char_styles.as_ref().map(|styles| {
                    (0..decoded.chars().count())
                        .map(|ci| {
                            let hs = styles.get(ci).copied().unwrap_or(CODE_BLOCK_BG);
                            CellStyle {
                                fg: hs.fg.or(CODE_BLOCK_BG.fg),
                                bg: CODE_BLOCK_BG.bg,
                                ..hs
                            }
                        })
                        .collect()
                })
            };

            if let Some(per_char) = per_char {
                let mut unit = 0;
                for (ci, ch) in decoded.chars().enumerate() {
                    for _ in 0..ch.len_utf16() {
                        if unit < char_styles.len() {
                            char_styles[unit] = per_char[ci];
                        }
                        unit += 1;
                    }
                }
            }
        }

        if text.len() < content_width {
            let pad = content_width - text.len();
            text.extend(vec![SPACE; pad]);
            char_styles.extend(vec![CODE_BLOCK_BG; pad]);
        }

        lines.push(make_quoted_line(&text, Some(&char_styles), quote_depth, indent));
    }
}

/// Port of `RenderList`.
#[allow(clippy::too_many_arguments)]
fn render_list(
    ordered: bool,
    start: Option<u64>,
    loose: bool,
    items: &[Vec<Block>],
    lines: &mut Vec<StyledLine>,
    width: usize,
    indent: usize,
    quote_depth: usize,
    cancel: &CancelToken,
) -> Option<()> {
    // int.TryParse of the start; 1 when it doesn't fit an int
    let mut item_number: u64 = if ordered {
        start.filter(|&n| n <= i32::MAX as u64).unwrap_or(1)
    } else {
        0
    };

    for (i, item) in items.iter().enumerate() {
        if cancel.is_cancelled() {
            return None;
        }

        let marker = if ordered { units(&format!("{item_number}. ")) } else { units("- ") };
        let child_indent = indent + marker.len();

        if let Some(Block::Paragraph(inlines)) = item.first() {
            let mut spans: Spans = vec![(marker.clone(), LIST_MARKER_STYLE)];
            collect_inlines(inlines, &mut spans, PLAIN_STYLE);
            emit_wrapped_line(&spans, lines, width, indent, quote_depth, Some(child_indent));
        } else {
            // Only the marker: Markdig doesn't render a non-paragraph first block
            let mut text = vec![SPACE; indent];
            text.extend(&marker);
            let mut styles = vec![DEFAULT_STYLE; indent];
            styles.extend(vec![LIST_MARKER_STYLE; marker.len()]);
            lines.push(make_quoted_line(&text, Some(&styles), quote_depth, 0));
        }

        for child in item.iter().skip(1) {
            match child {
                Block::Paragraph(inlines) => {
                    let mut spans = Spans::new();
                    collect_inlines(inlines, &mut spans, PLAIN_STYLE);
                    emit_wrapped_line(&spans, lines, width, child_indent, quote_depth, None);
                }
                Block::List {
                    ordered,
                    start,
                    loose,
                    items,
                } => render_list(*ordered, *start, *loose, items, lines, width, child_indent, quote_depth, cancel)?,
                // Other containers render their children (no extra quote depth)
                Block::Quote(children) => render_blocks(children, lines, width, child_indent, quote_depth, cancel)?,
                // Leaf blocks (code, rules, tables, headings) are skipped
                _ => {}
            }
        }

        if item_number > 0 {
            item_number += 1;
        }

        if i + 1 < items.len() && loose {
            lines.push(make_quoted_line(&[], None, quote_depth, 0));
        }
    }

    Some(())
}

/// Port of `RenderHorizontalRule`.
fn render_horizontal_rule(lines: &mut Vec<StyledLine>, width: usize, indent: usize, quote_depth: usize) {
    let content_width = (width as isize - (indent + quote_depth * 2) as isize).max(3) as usize;
    let text = repeat_unit('\u{2500}', content_width);
    lines.push(make_quoted_line(&text, Some(&vec![HR_STYLE; content_width]), quote_depth, indent));
}

/// Port of `RenderTable`: plain cell text in padded columns, bold header,
/// a rule under it, truncated to the width.
fn render_table(
    columns: usize,
    rows: &[Vec<Vec<Inline>>],
    lines: &mut Vec<StyledLine>,
    width: usize,
    indent: usize,
    quote_depth: usize,
) {
    let max_width = (width as isize - (indent + quote_depth * 2) as isize).max(4) as usize;
    if columns == 0 || rows.is_empty() {
        return;
    }

    let cells: Vec<Vec<Units>> = rows
        .iter()
        .map(|row| {
            (0..columns)
                .map(|c| row.get(c).map(|cell| units(&plain_text(cell))).unwrap_or_default())
                .collect()
        })
        .collect();

    let mut col_widths = vec![0usize; columns];
    for row in &cells {
        for (c, cell) in row.iter().enumerate() {
            col_widths[c] = col_widths[c].max(cell.len());
        }
    }
    for width in &mut col_widths {
        *width = (*width).max(1);
    }

    let bar = units(" \u{2502} ");
    let separator = units("\u{2500}\u{253C}\u{2500}");

    for (r, row) in cells.iter().enumerate() {
        let mut text = Units::new();
        let mut styles = Vec::new();
        let style = if r == 0 { BOLD_STYLE } else { PLAIN_STYLE };

        for (c, cell) in row.iter().enumerate() {
            if c > 0 {
                text.extend(&bar);
                styles.extend(vec![TABLE_BAR_STYLE; bar.len()]);
            }

            let mut padded = cell.clone();
            padded.extend(vec![SPACE; col_widths[c].saturating_sub(cell.len())]);
            styles.extend(vec![style; padded.len()]);
            text.extend(padded);
        }

        text.truncate(max_width);
        styles.truncate(max_width);
        lines.push(make_quoted_line(&text, Some(&styles), quote_depth, indent));

        if r == 0 {
            let mut sep = Units::new();
            for (c, &col_width) in col_widths.iter().enumerate() {
                if c > 0 {
                    sep.extend(&separator);
                }
                sep.extend(repeat_unit('\u{2500}', col_width));
            }

            sep.truncate(max_width);
            let sep_styles = vec![TABLE_BAR_STYLE; sep.len()];
            lines.push(make_quoted_line(&sep, Some(&sep_styles), quote_depth, indent));
        }
    }
}

/// Port of `GetPlainText`/`AppendPlainText`: literals, code, container
/// children, and a space per line break.
fn plain_text(inlines: &[Inline]) -> String {
    let mut out = String::new();

    fn append(inline: &Inline, out: &mut String) {
        match inline {
            Inline::Literal(text) | Inline::Code(text) => out.push_str(text),
            Inline::Emphasis { children, .. } | Inline::Link { children, .. } | Inline::Image { children } => {
                for child in children {
                    append(child, out);
                }
            }
            Inline::LineBreak { .. } => out.push(' '),
            Inline::Autolink(_) | Inline::Html(_) | Inline::Entity => {}
        }
    }

    for inline in inlines {
        append(inline, &mut out);
    }

    out
}

/// Port of `GetInlineText` (link text compared with its URL).
fn inline_text(inlines: &[Inline]) -> String {
    let mut out = String::new();

    for inline in inlines {
        match inline {
            Inline::Literal(text) | Inline::Code(text) => out.push_str(text),
            Inline::Emphasis { children, .. } | Inline::Link { children, .. } | Inline::Image { children } => {
                out.push_str(&inline_text(children));
            }
            _ => {}
        }
    }

    out
}

/// Port of `CollectInlines`.
fn collect_inlines(inlines: &[Inline], spans: &mut Spans, current: CellStyle) {
    for inline in inlines {
        match inline {
            Inline::Literal(text) => spans.push((units(text), current)),
            Inline::Emphasis { strong, children } => {
                let emphasis = match (*strong, current.bold, current.dim) {
                    (true, _, true) | (false, true, _) => CellStyle {
                        fg: current.fg,
                        bg: current.bg,
                        bold: true,
                        dim: true,
                        ..DEFAULT_STYLE
                    },
                    (true, _, _) => CellStyle {
                        fg: current.fg,
                        bg: current.bg,
                        bold: true,
                        ..DEFAULT_STYLE
                    },
                    (false, _, _) => CellStyle {
                        fg: current.fg,
                        bg: current.bg,
                        dim: true,
                        ..DEFAULT_STYLE
                    },
                };
                collect_inlines(children, spans, emphasis);
            }
            Inline::Code(text) => spans.push((units(text), CODE_SPAN_STYLE)),
            Inline::Image { children } => {
                let alt = match children.first() {
                    Some(Inline::Literal(text)) => text.as_str(),
                    _ => "",
                };
                spans.push((units(&format!("[image: {alt}]")), LINK_URL_STYLE));
            }
            Inline::Link { url, children } => {
                collect_inlines(children, spans, LINK_TEXT_STYLE);
                if inline_text(children) != *url {
                    spans.push((units(&format!(" ({url})")), LINK_URL_STYLE));
                }
            }
            Inline::Autolink(url) => spans.push((units(url), LINK_TEXT_STYLE)),
            Inline::LineBreak { hard } => spans.push((units(if *hard { "\n" } else { " " }), current)),
            Inline::Html(tag) => spans.push((units(tag), CODE_SPAN_STYLE)),
            Inline::Entity => {}
        }
    }
}

/// Port of `EmitWrappedLine`: word wrap with an optional hanging indent.
fn emit_wrapped_line(
    spans: &Spans,
    lines: &mut Vec<StyledLine>,
    width: usize,
    indent: usize,
    quote_depth: usize,
    hanging_indent: Option<usize>,
) {
    let hanging_indent = hanging_indent.unwrap_or(indent);
    let prefix_len = quote_depth * 2;
    let first_line_width = (width as isize - prefix_len as isize - indent as isize).max(2) as usize;
    let cont_line_width = (width as isize - prefix_len as isize - hanging_indent as isize).max(2) as usize;

    let mut chars = Units::new();
    let mut styles = Vec::new();
    for (text, style) in spans {
        chars.extend(text);
        styles.extend(vec![*style; text.len()]);
    }

    let mut first = true;
    let mut pos = 0;

    while pos < chars.len() {
        let line_indent = if first { indent } else { hanging_indent };
        let max_chars = if first { first_line_width } else { cont_line_width };
        let remaining = chars.len() - pos;

        if remaining <= max_chars {
            emit_single_line(&chars, &styles, pos, remaining, lines, line_indent, quote_depth);
            break;
        }

        let break_at = find_word_break(&chars, pos, max_chars);
        emit_single_line(&chars, &styles, pos, break_at - pos, lines, line_indent, quote_depth);

        pos = break_at;
        while pos < chars.len() && chars[pos] == SPACE {
            pos += 1;
        }

        first = false;
    }

    if chars.is_empty() {
        lines.push(make_quoted_line(&[], None, quote_depth, 0));
    }
}

/// Port of `FindWordBreak`.
fn find_word_break(chars: &[u16], start: usize, max_chars: usize) -> usize {
    let end = start + max_chars;

    if let Some(newline) = (start..end.min(chars.len())).find(|&i| chars[i] == NEWLINE) {
        return newline + 1;
    }

    if end >= chars.len() {
        return chars.len();
    }

    let mut break_at = end;
    while break_at > start && chars[break_at - 1] != SPACE {
        break_at -= 1;
    }

    if break_at == start { end } else { break_at }
}

/// Port of `EmitSingleLine`: splits at embedded newlines.
fn emit_single_line(
    chars: &[u16],
    styles: &[CellStyle],
    start: usize,
    length: usize,
    lines: &mut Vec<StyledLine>,
    indent: usize,
    quote_depth: usize,
) {
    if let Some(i) = (start..start + length).find(|&i| chars[i] == NEWLINE) {
        let before = i - start;
        if before > 0 {
            lines.push(make_quoted_line(&chars[start..i], Some(&styles[start..i]), quote_depth, indent));
        } else {
            lines.push(make_quoted_line(&[], None, quote_depth, 0));
        }

        let after = length - before - 1;
        if after > 0 {
            emit_single_line(chars, styles, i + 1, after, lines, indent, quote_depth);
        }
        return;
    }

    lines.push(make_quoted_line(
        &chars[start..start + length],
        Some(&styles[start..start + length]),
        quote_depth,
        indent,
    ));
}

/// Port of `MakeQuotedLine`: quote bars and indent before the text; quoted
/// content is dimmed.
fn make_quoted_line(text: &[u16], char_styles: Option<&[CellStyle]>, quote_depth: usize, indent: usize) -> StyledLine {
    if quote_depth == 0 && indent == 0 {
        return to_styled_line(text, char_styles);
    }

    let mut prefix = Units::new();
    let mut prefix_styles = Vec::new();

    for _ in 0..quote_depth {
        prefix.extend(units("\u{2502} "));
        prefix_styles.extend([BLOCKQUOTE_BAR_STYLE, BLOCKQUOTE_BAR_STYLE]);
    }

    prefix.extend(vec![SPACE; indent]);
    prefix_styles.extend(vec![DEFAULT_STYLE; indent]);

    let mut full = prefix;
    full.extend(text);

    let Some(char_styles) = char_styles else {
        if quote_depth > 0 && !text.is_empty() {
            let mut styles = prefix_styles;
            styles.extend(vec![BLOCKQUOTE_TEXT_STYLE; text.len()]);
            return to_styled_line(&full, Some(&styles));
        }

        if !prefix_styles.is_empty() {
            return to_styled_line(&full, Some(&prefix_styles));
        }

        return to_styled_line(&full, None);
    };

    let mut styles = prefix_styles;
    if quote_depth > 0 {
        styles.extend(char_styles.iter().map(|s| CellStyle {
            fg: s.fg.or(BLOCKQUOTE_TEXT_STYLE.fg),
            dim: true,
            ..*s
        }));
    } else {
        styles.extend(char_styles);
    }

    to_styled_line(&full, Some(&styles))
}

#[cfg(test)]
mod tests {
    //! Port of MarkdigRendererTests.cs and FrontmatterTests.

    use super::{render_text, try_extract_frontmatter};
    use crate::highlight::StyledLine;
    use crate::input::CancelToken;
    use crate::screen::{CellStyle, Color};

    fn render(markdown: &str, width: i32) -> Vec<StyledLine> {
        render_text(markdown, width, &CancelToken::new()).expect("not cancelled")
    }

    fn style_at(line: &StyledLine, ch: char) -> CellStyle {
        let index = line.text.chars().position(|c| c == ch).expect("char present");
        line.char_styles.as_ref().expect("char styles")[index]
    }

    fn color(r: u8, g: u8, b: u8) -> Option<Color> {
        Some(Color { r, g, b })
    }

    #[test]
    fn heading_h1_is_bold_and_prefix_matches_level() {
        let lines = render("# Hello", 80);
        assert_eq!(lines.len(), 1);
        assert_eq!(lines[0].text, "# Hello");
        assert!(lines[0].char_styles.as_ref().unwrap().iter().all(|s| s.bold));

        for level in 1..=3 {
            let lines = render(&format!("{} H{level}", "#".repeat(level)), 80);
            assert_eq!(lines.len(), 1);
            assert!(lines[0].text.starts_with(&format!("{} ", "#".repeat(level))));
        }
    }

    #[test]
    fn paragraphs_render_and_wrap() {
        assert_eq!(render("Hello world", 80)[0].text, "Hello world");

        let lines = render("one two three four five six", 15);
        assert!(lines.len() > 1);
        assert!(lines[0].text.chars().count() <= 15);
    }

    #[test]
    fn inline_styles() {
        assert_eq!(style_at(&render("Use `foo` here", 80)[0], 'f').fg, color(206, 145, 120));
        assert_eq!(style_at(&render("[click](https://example.com)", 80)[0], 'c').fg, color(78, 201, 176));
        assert!(style_at(&render("some **bold** text", 80)[0], 'b').bold);
        assert!(style_at(&render("some *italic* text", 80)[0], 'i').dim);
    }

    #[test]
    fn fenced_code_has_background_and_highlighting() {
        let lines = render("```\nvar x = 1;\n```", 80);
        let code = lines.iter().find(|l| l.text.trim_start().starts_with("var")).unwrap();
        assert_eq!(code.char_styles.as_ref().unwrap()[0].bg, color(30, 30, 46));

        let lines = render("```csharp\nvar x = 1;\n```", 80);
        let code = lines.iter().find(|l| l.text.contains("var")).unwrap();
        assert_eq!(style_at(code, 'v').bg, color(30, 30, 46));
    }

    #[test]
    fn lists_quotes_rules_tables_images() {
        assert!(render("- item one\n- item two", 80)[0].text.starts_with("- "));
        assert!(render("1. first\n2. second", 80)[0].text.starts_with("1. "));

        let quote = render("> quoted text", 80);
        assert_eq!(quote.len(), 1);
        assert!(quote[0].text.starts_with("\u{2502} ") && quote[0].text.contains("quoted text"));

        let lines = render("above\n\n---\n\nbelow", 80);
        let rule = lines.iter().find(|l| l.text.contains('\u{2500}')).unwrap();
        assert!(rule.char_styles.as_ref().unwrap().iter().all(|s| s.dim));

        assert!(render("", 80).is_empty());

        let table = render("| A | B |\n|---|---|\n| 1 | 2 |", 80);
        assert!(table.len() >= 3);
        assert!(table[0].text.contains('A') && table[0].text.contains('B'));
        assert!(table[1].text.contains('\u{2500}'));

        let nested = render("- outer\n  - inner", 80);
        let indent = |l: &StyledLine| l.text.len() - l.text.trim_start().len();
        let inner = nested.iter().find(|l| l.text.contains("inner")).unwrap();
        assert!(indent(inner) > indent(&nested[0]));

        let image = render("![alt text](image.png)", 80);
        assert_eq!(image.len(), 1);
        assert!(image[0].text.contains("[image: alt text]"));
    }

    #[test]
    fn frontmatter_extraction() {
        let (fm, body) = try_extract_frontmatter("---\nkey: value\n---\n# Body").unwrap();
        assert_eq!((fm.as_str(), body), ("key: value", "# Body"));

        let (fm, body) = try_extract_frontmatter("---\r\nkey: value\r\n---\r\nbody").unwrap();
        assert_eq!((fm.as_str(), body), ("key: value", "body"));

        assert!(try_extract_frontmatter("# Normal markdown\nno frontmatter").is_none());
        assert!(try_extract_frontmatter("---\nkey: value\n# No closing marker").is_none());

        let (fm, body) = try_extract_frontmatter("---\nname: test\n---\n").unwrap();
        assert_eq!((fm.as_str(), body), ("name: test", ""));

        let (fm, _) = try_extract_frontmatter("---\nname: foo\ndescription: bar baz\nmodel: haiku\n---\ncontent").unwrap();
        assert!(fm.contains("name: foo") && fm.contains("description: bar baz") && fm.contains("model: haiku"));
    }

    #[test]
    fn frontmatter_rendering() {
        let lines = render("---\nname: hello\n---\n# Body", 80);
        assert!(lines.iter().any(|l| l.text.contains("frontmatter")));
        let footer = lines
            .iter()
            .rev()
            .find(|l| l.text.chars().count() > 4 && l.text.chars().all(|c| c == '\u{2500}'))
            .unwrap();
        assert!(footer.char_styles.as_ref().unwrap().iter().all(|s| s.dim));
        assert!(lines.iter().any(|l| l.text.contains("Body")));

        let lines = render("---\nname: hello\n---\n", 80);
        let row = lines.iter().find(|l| l.text.contains("hello")).unwrap();
        assert!(row.text.contains("name"));
        assert_eq!(style_at(row, 'n').fg, color(156, 220, 254));

        let lines = render("---\ndesc: \"some description\"\n---\n", 80);
        let row = lines.iter().find(|l| l.text.contains("some description")).unwrap();
        assert_eq!(style_at(row, '"').fg, color(206, 145, 120));

        let long = vec!["word"; 20].join(" ");
        let lines = render(&format!("---\ntitle: {long}\n---\n"), 40);
        assert!(lines.iter().filter(|l| l.text.contains("word")).count() > 1);

        let lines = render("---\nname: foo\ndescription: bar\n---\n", 80);
        let foo = lines.iter().find(|l| l.text.contains("foo")).unwrap().text.find("foo");
        let bar = lines.iter().find(|l| l.text.contains("bar")).unwrap().text.find("bar");
        assert_eq!(foo, bar);

        let lines = render("# Plain heading\n\nsome text", 80);
        assert!(!lines.iter().any(|l| l.text.contains("frontmatter")));
        assert!(lines.iter().any(|l| l.text.contains("Plain heading")));
    }

    #[test]
    fn cancelled_render_returns_none() {
        let cancel = CancelToken::new();
        cancel.cancel();
        assert!(render_text("# Heading\n\ntext", 80, &cancel).is_none());
    }
}
