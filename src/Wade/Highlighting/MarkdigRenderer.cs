using System.Collections.Frozen;
using Markdig;
using Markdig.Extensions.Tables;
using Markdig.Helpers;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;
using Wade.Terminal;

namespace Wade.Highlighting;

internal static class MarkdigRenderer
{
    private static readonly MarkdownPipeline s_pipeline = new MarkdownPipelineBuilder()
        .UsePipeTables()
        .Build();

    // Heading colors by level (H1 = brightest, H6 = dimmest)
    private static readonly CellStyle s_h1Style = new(new Color(100, 180, 255), null, Bold: true);
    private static readonly CellStyle s_h2Style = new(new Color(86, 156, 214), null, Bold: true);
    private static readonly CellStyle s_h3Style = new(new Color(78, 201, 176), null, Bold: true);
    private static readonly CellStyle s_h4Style = new(new Color(78, 201, 176), null);
    private static readonly CellStyle s_h5Style = new(new Color(156, 220, 254), null);
    private static readonly CellStyle s_h6Style = new(new Color(156, 220, 254), null, Dim: true);

    private static readonly CellStyle s_plainStyle = new(new Color(200, 200, 200), null);
    private static readonly CellStyle s_boldStyle = new(new Color(200, 200, 200), null, Bold: true);
    private static readonly CellStyle s_codeSpanStyle = new(new Color(206, 145, 120), null); // salmon
    private static readonly CellStyle s_linkTextStyle = new(new Color(78, 201, 176), null); // teal
    private static readonly CellStyle s_linkUrlStyle = new(new Color(78, 201, 176), null, Dim: true); // teal dim
    private static readonly CellStyle s_codeBlockBg = new(new Color(200, 200, 200), new Color(30, 30, 46));
    private static readonly CellStyle s_blockquoteBarStyle = new(new Color(100, 100, 120), null, Dim: true);
    private static readonly CellStyle s_blockquoteTextStyle = new(new Color(160, 160, 175), null);
    private static readonly CellStyle s_hrStyle = new(new Color(100, 100, 120), null, Dim: true);
    private static readonly CellStyle s_listMarkerStyle = new(new Color(180, 180, 180), null);
    private static readonly CellStyle s_tableBarStyle = new(new Color(100, 100, 120), null);

    // Mapping from fenced code block info strings to file extensions for syntax highlighting
    private static readonly FrozenDictionary<string, string> s_infoToExtension =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["csharp"] = ".cs", ["cs"] = ".cs", ["c#"] = ".cs",
            ["javascript"] = ".js", ["js"] = ".js", ["jsx"] = ".jsx",
            ["typescript"] = ".ts", ["ts"] = ".ts", ["tsx"] = ".tsx",
            ["python"] = ".py", ["py"] = ".py",
            ["go"] = ".go", ["golang"] = ".go",
            ["rust"] = ".rs", ["rs"] = ".rs",
            ["java"] = ".java",
            ["bash"] = ".sh", ["sh"] = ".sh", ["shell"] = ".sh", ["zsh"] = ".sh",
            ["powershell"] = ".ps1", ["ps1"] = ".ps1", ["pwsh"] = ".ps1",
            ["css"] = ".css", ["scss"] = ".scss",
            ["json"] = ".json",
            ["yaml"] = ".yaml", ["yml"] = ".yaml",
            ["toml"] = ".toml",
            ["xml"] = ".xml", ["html"] = ".html", ["htm"] = ".html",
            ["markdown"] = ".md", ["md"] = ".md",
        }
        .ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);

    public static StyledLine[]? Render(string filePath, int width, CancellationToken ct)
    {
        string text;

        try
        {
            text = File.ReadAllText(filePath);
        }
        catch
        {
            return null;
        }

        if (width < 4)
        {
            width = 4;
        }

        MarkdownDocument doc = Markdown.Parse(text, s_pipeline);
        return RenderDocument(doc, width, ct);
    }

    internal static StyledLine[] RenderDocument(MarkdownDocument document, int width, CancellationToken ct)
    {
        var lines = new List<StyledLine>();
        RenderBlocks(document, lines, width, indent: 0, quoteDepth: 0, ct);

        // Remove trailing empty lines
        while (lines.Count > 0 && lines[^1].Text.Length == 0)
        {
            lines.RemoveAt(lines.Count - 1);
        }

        return lines.ToArray();
    }

    private static void RenderBlocks(
        ContainerBlock container,
        List<StyledLine> lines,
        int width,
        int indent,
        int quoteDepth,
        CancellationToken ct)
    {
        for (int i = 0; i < container.Count; i++)
        {
            ct.ThrowIfCancellationRequested();

            Block block = container[i];

            switch (block)
            {
                case HeadingBlock heading:
                    RenderHeading(heading, lines, width, indent, quoteDepth);
                    break;

                case ParagraphBlock paragraph:
                    RenderParagraph(paragraph, lines, width, indent, quoteDepth);
                    break;

                case FencedCodeBlock fencedCode:
                    RenderFencedCodeBlock(fencedCode, lines, width, indent, quoteDepth);
                    break;

                case CodeBlock codeBlock:
                    RenderCodeBlock(codeBlock, lines, width, indent, quoteDepth);
                    break;

                case ListBlock list:
                    RenderList(list, lines, width, indent, quoteDepth, ct);
                    break;

                case QuoteBlock quote:
                    RenderBlocks(quote, lines, width, indent, quoteDepth + 1, ct);
                    break;

                case ThematicBreakBlock:
                    RenderHorizontalRule(lines, width, indent, quoteDepth);
                    break;

                case Table table:
                    RenderTable(table, lines, width, indent, quoteDepth);
                    break;

                case LinkReferenceDefinitionGroup:
                    // Skip link reference definitions — they are metadata, not rendered content
                    break;

                default:
                    // Unknown block type — try to render as container or skip
                    if (block is ContainerBlock unknownContainer)
                    {
                        RenderBlocks(unknownContainer, lines, width, indent, quoteDepth, ct);
                    }

                    break;
            }

            // Add blank line between blocks (except last)
            if (i < container.Count - 1)
            {
                lines.Add(MakeQuotedLine("", null, quoteDepth, width));
            }
        }
    }

    private static void RenderHeading(
        HeadingBlock heading,
        List<StyledLine> lines,
        int width,
        int indent,
        int quoteDepth)
    {
        CellStyle style = heading.Level switch
        {
            1 => s_h1Style,
            2 => s_h2Style,
            3 => s_h3Style,
            4 => s_h4Style,
            5 => s_h5Style,
            _ => s_h6Style,
        };

        string prefix = new string('#', heading.Level) + " ";
        var spans = new List<(string Text, CellStyle Style)>
        {
            (prefix, style),
        };
        CollectInlines(heading.Inline, spans, style);
        EmitWrappedLine(spans, lines, width, indent, quoteDepth);
    }

    private static void RenderParagraph(
        ParagraphBlock paragraph,
        List<StyledLine> lines,
        int width,
        int indent,
        int quoteDepth)
    {
        var spans = new List<(string Text, CellStyle Style)>();
        CollectInlines(paragraph.Inline, spans, s_plainStyle);
        EmitWrappedLine(spans, lines, width, indent, quoteDepth);
    }

    private static void RenderFencedCodeBlock(
        FencedCodeBlock fencedCode,
        List<StyledLine> lines,
        int width,
        int indent,
        int quoteDepth)
    {
        string? info = fencedCode.Info;
        ILanguage? lang = null;

        if (!string.IsNullOrEmpty(info) && s_infoToExtension.TryGetValue(info, out string? ext))
        {
            lang = LanguageMap.GetLanguage($"dummy{ext}");
        }

        RenderCodeLines(fencedCode, lines, width, indent, quoteDepth, lang);
    }

    private static void RenderCodeBlock(
        CodeBlock codeBlock,
        List<StyledLine> lines,
        int width,
        int indent,
        int quoteDepth) =>
        RenderCodeLines(codeBlock, lines, width, indent, quoteDepth, lang: null);

    private static void RenderCodeLines(
        LeafBlock codeBlock,
        List<StyledLine> lines,
        int width,
        int indent,
        int quoteDepth,
        ILanguage? lang)
    {
        int prefixLen = indent + quoteDepth * 2;
        int contentWidth = width - prefixLen;

        if (contentWidth < 2)
        {
            contentWidth = 2;
        }

        byte state = 0;

        foreach (StringLine line in codeBlock.Lines)
        {
            string text = line.Slice.ToString();

            // Truncate to fit width
            if (text.Length > contentWidth)
            {
                text = text[..contentWidth];
            }

            CellStyle[] charStyles;

            if (lang is not null)
            {
                StyledLine highlighted = lang.TokenizeLine(text, ref state);

                // Merge syntax highlighting with code block background
                charStyles = new CellStyle[text.Length];

                if (highlighted.Spans is { } spans)
                {
                    // Use syntax theme colors with code block background
                    for (int ci = 0; ci < text.Length; ci++)
                    {
                        CellStyle baseStyle = s_codeBlockBg;

                        foreach (StyledSpan span in spans)
                        {
                            if (ci >= span.Start && ci < span.Start + span.Length)
                            {
                                CellStyle tokenStyle = SyntaxTheme.GetStyle(span.Kind);
                                baseStyle = new CellStyle(
                                    tokenStyle.Fg,
                                    s_codeBlockBg.Bg,
                                    tokenStyle.Bold,
                                    tokenStyle.Dim,
                                    tokenStyle.Inverse,
                                    tokenStyle.Underline,
                                    tokenStyle.Strikethrough);
                                break;
                            }
                        }

                        charStyles[ci] = baseStyle;
                    }
                }
                else if (highlighted.CharStyles is { } hlStyles)
                {
                    // Language produced per-char styles; overlay with code block background
                    for (int ci = 0; ci < text.Length; ci++)
                    {
                        CellStyle hs = ci < hlStyles.Length ? hlStyles[ci] : s_codeBlockBg;
                        charStyles[ci] = new CellStyle(
                            hs.Fg ?? s_codeBlockBg.Fg,
                            s_codeBlockBg.Bg,
                            hs.Bold, hs.Dim, hs.Inverse, hs.Underline, hs.Strikethrough);
                    }
                }
                else
                {
                    Array.Fill(charStyles, s_codeBlockBg);
                }
            }
            else
            {
                charStyles = new CellStyle[text.Length];
                Array.Fill(charStyles, s_codeBlockBg);
            }

            // Pad to fill background
            if (text.Length < contentWidth)
            {
                string padded = text + new string(' ', contentWidth - text.Length);
                var paddedStyles = new CellStyle[padded.Length];
                Array.Copy(charStyles, paddedStyles, charStyles.Length);
                Array.Fill(paddedStyles, s_codeBlockBg, charStyles.Length, padded.Length - charStyles.Length);
                lines.Add(MakeQuotedLine(padded, paddedStyles, quoteDepth, width, indent));
            }
            else
            {
                lines.Add(MakeQuotedLine(text, charStyles, quoteDepth, width, indent));
            }
        }
    }

    private static void RenderList(
        ListBlock list,
        List<StyledLine> lines,
        int width,
        int indent,
        int quoteDepth,
        CancellationToken ct)
    {
        int itemNumber = list.IsOrdered ? (list.OrderedStart is { } s && int.TryParse(s, out int n) ? n : 1) : 0;

        for (int i = 0; i < list.Count; i++)
        {
            ct.ThrowIfCancellationRequested();

            if (list[i] is not ListItemBlock item)
            {
                continue;
            }

            string marker = list.IsOrdered ? $"{itemNumber}. " : "- ";
            int markerLen = marker.Length;
            int childIndent = indent + markerLen;

            // Render first block with marker prefix
            if (item.Count > 0 && item[0] is ParagraphBlock firstPara)
            {
                var spans = new List<(string Text, CellStyle Style)>
                {
                    (marker, s_listMarkerStyle),
                };
                CollectInlines(firstPara.Inline, spans, s_plainStyle);
                EmitWrappedLine(spans, lines, width, indent, quoteDepth, hangingIndent: childIndent);
            }
            else
            {
                // Emit just the marker for non-paragraph first blocks
                var markerStyles = new CellStyle[marker.Length];
                Array.Fill(markerStyles, s_listMarkerStyle);
                lines.Add(MakeQuotedLine(new string(' ', indent) + marker, PrependIndent(markerStyles, indent), quoteDepth, width));
            }

            // Render remaining child blocks with increased indent
            for (int j = 1; j < item.Count; j++)
            {
                if (item[j] is ParagraphBlock para)
                {
                    var spans = new List<(string Text, CellStyle Style)>();
                    CollectInlines(para.Inline, spans, s_plainStyle);
                    EmitWrappedLine(spans, lines, width, childIndent, quoteDepth);
                }
                else if (item[j] is ListBlock nestedList)
                {
                    RenderList(nestedList, lines, width, childIndent, quoteDepth, ct);
                }
                else if (item[j] is ContainerBlock childContainer)
                {
                    RenderBlocks(childContainer, lines, width, childIndent, quoteDepth, ct);
                }
            }

            if (itemNumber > 0)
            {
                itemNumber++;
            }

            // Blank line between list items only if original had loose items
            if (i < list.Count - 1 && list.IsLoose)
            {
                lines.Add(MakeQuotedLine("", null, quoteDepth, width));
            }
        }
    }

    private static void RenderHorizontalRule(
        List<StyledLine> lines,
        int width,
        int indent,
        int quoteDepth)
    {
        int prefixLen = indent + quoteDepth * 2;
        int contentWidth = width - prefixLen;

        if (contentWidth < 3)
        {
            contentWidth = 3;
        }

        var hrText = new string('─', contentWidth);
        var charStyles = new CellStyle[hrText.Length];
        Array.Fill(charStyles, s_hrStyle);
        lines.Add(MakeQuotedLine(hrText, charStyles, quoteDepth, width, indent));
    }

    private static void RenderTable(
        Table table,
        List<StyledLine> lines,
        int width,
        int indent,
        int quoteDepth)
    {
        int prefixLen = indent + quoteDepth * 2;
        int maxWidth = width - prefixLen;

        if (maxWidth < 4)
        {
            maxWidth = 4;
        }

        int colCount = table.ColumnDefinitions.Count;

        if (colCount == 0)
        {
            return;
        }

        // Collect all cell text
        var rows = new List<string[]>();

        foreach (Block rowBlock in table)
        {
            if (rowBlock is not TableRow row)
            {
                continue;
            }

            var cells = new string[colCount];

            for (int c = 0; c < colCount; c++)
            {
                if (c < row.Count && row[c] is TableCell cell)
                {
                    cells[c] = GetPlainText(cell);
                }
                else
                {
                    cells[c] = "";
                }
            }

            rows.Add(cells);
        }

        if (rows.Count == 0)
        {
            return;
        }

        // Compute column widths
        var colWidths = new int[colCount];

        foreach (string[] row in rows)
        {
            for (int c = 0; c < colCount; c++)
            {
                if (row[c].Length > colWidths[c])
                {
                    colWidths[c] = row[c].Length;
                }
            }
        }

        // Ensure minimum width of 1
        for (int c = 0; c < colCount; c++)
        {
            if (colWidths[c] < 1)
            {
                colWidths[c] = 1;
            }
        }

        // Render rows
        for (int r = 0; r < rows.Count; r++)
        {
            string[] row = rows[r];
            var text = new System.Text.StringBuilder();
            var styles = new List<CellStyle>();

            for (int c = 0; c < colCount; c++)
            {
                if (c > 0)
                {
                    text.Append(" │ ");
                    styles.Add(s_tableBarStyle);
                    styles.Add(s_tableBarStyle);
                    styles.Add(s_tableBarStyle);
                }

                string cell = row[c].PadRight(colWidths[c]);
                text.Append(cell);

                CellStyle style = r == 0 ? s_boldStyle : s_plainStyle;

                for (int ci = 0; ci < cell.Length; ci++)
                {
                    styles.Add(style);
                }
            }

            string lineText = text.ToString();

            // Truncate to fit
            if (lineText.Length > maxWidth)
            {
                lineText = lineText[..maxWidth];
                styles.RemoveRange(maxWidth, styles.Count - maxWidth);
            }

            lines.Add(MakeQuotedLine(lineText, styles.ToArray(), quoteDepth, width, indent));

            // Separator after header row
            if (r == 0)
            {
                var sep = new System.Text.StringBuilder();
                var sepStyles = new List<CellStyle>();

                for (int c = 0; c < colCount; c++)
                {
                    if (c > 0)
                    {
                        sep.Append("─┼─");
                        sepStyles.Add(s_tableBarStyle);
                        sepStyles.Add(s_tableBarStyle);
                        sepStyles.Add(s_tableBarStyle);
                    }

                    var bar = new string('─', colWidths[c]);
                    sep.Append(bar);

                    for (int ci = 0; ci < bar.Length; ci++)
                    {
                        sepStyles.Add(s_tableBarStyle);
                    }
                }

                string sepText = sep.ToString();

                if (sepText.Length > maxWidth)
                {
                    sepText = sepText[..maxWidth];
                    sepStyles.RemoveRange(maxWidth, sepStyles.Count - maxWidth);
                }

                lines.Add(MakeQuotedLine(sepText, sepStyles.ToArray(), quoteDepth, width, indent));
            }
        }
    }

    private static string GetPlainText(ContainerBlock block)
    {
        var sb = new System.Text.StringBuilder();

        foreach (Block child in block)
        {
            if (child is LeafBlock leaf && leaf.Inline is not null)
            {
                foreach (Inline inline in leaf.Inline)
                {
                    AppendPlainText(inline, sb);
                }
            }
        }

        return sb.ToString();
    }

    private static void AppendPlainText(Inline inline, System.Text.StringBuilder sb)
    {
        switch (inline)
        {
            case LiteralInline lit:
                sb.Append(lit.Content);
                break;
            case CodeInline code:
                sb.Append(code.Content);
                break;
            case ContainerInline container:
                foreach (Inline child in container)
                {
                    AppendPlainText(child, sb);
                }

                break;
            case LineBreakInline:
                sb.Append(' ');
                break;
        }
    }

    private static void CollectInlines(
        ContainerInline? inlineContainer,
        List<(string Text, CellStyle Style)> spans,
        CellStyle currentStyle)
    {
        if (inlineContainer is null)
        {
            return;
        }

        foreach (Inline inline in inlineContainer)
        {
            switch (inline)
            {
                case LiteralInline lit:
                    spans.Add((lit.Content.ToString(), currentStyle));
                    break;

                case EmphasisInline emphasis:
                {
                    CellStyle emphStyle = (emphasis.DelimiterCount >= 2, currentStyle.Bold, currentStyle.Dim) switch
                    {
                        (true, _, true) => new CellStyle(currentStyle.Fg, currentStyle.Bg, Bold: true, Dim: true),
                        (true, _, _) => new CellStyle(currentStyle.Fg, currentStyle.Bg, Bold: true),
                        (false, true, _) => new CellStyle(currentStyle.Fg, currentStyle.Bg, Bold: true, Dim: true),
                        (false, _, _) => new CellStyle(currentStyle.Fg, currentStyle.Bg, Dim: true),
                    };

                    CollectInlines(emphasis, spans, emphStyle);
                    break;
                }

                case CodeInline code:
                    spans.Add((code.Content, s_codeSpanStyle));
                    break;

                case LinkInline link:
                {
                    if (link.IsImage)
                    {
                        string alt = "";

                        if (link.FirstChild is LiteralInline altLit)
                        {
                            alt = altLit.Content.ToString();
                        }

                        spans.Add(($"[image: {alt}]", s_linkUrlStyle));
                    }
                    else
                    {
                        // Collect link text with link styling
                        CollectInlines(link, spans, s_linkTextStyle);

                        // Show URL if it differs from the link text
                        if (link.Url is { } url)
                        {
                            string linkText = GetInlineText(link);

                            if (!string.Equals(linkText, url, StringComparison.Ordinal))
                            {
                                spans.Add(($" ({url})", s_linkUrlStyle));
                            }
                        }
                    }

                    break;
                }

                case AutolinkInline autolink:
                    spans.Add((autolink.Url, s_linkTextStyle));
                    break;

                case LineBreakInline lineBreak:
                    if (lineBreak.IsHard)
                    {
                        spans.Add(("\n", currentStyle));
                    }
                    else
                    {
                        spans.Add((" ", currentStyle));
                    }

                    break;

                case HtmlInline html:
                    spans.Add((html.Tag, s_codeSpanStyle));
                    break;

                case ContainerInline container:
                    CollectInlines(container, spans, currentStyle);
                    break;
            }
        }
    }

    private static string GetInlineText(ContainerInline container)
    {
        var sb = new System.Text.StringBuilder();

        foreach (Inline inline in container)
        {
            switch (inline)
            {
                case LiteralInline lit:
                    sb.Append(lit.Content);
                    break;
                case CodeInline code:
                    sb.Append(code.Content);
                    break;
                case ContainerInline child:
                    sb.Append(GetInlineText(child));
                    break;
            }
        }

        return sb.ToString();
    }

    private static void EmitWrappedLine(
        List<(string Text, CellStyle Style)> spans,
        List<StyledLine> lines,
        int width,
        int indent,
        int quoteDepth,
        int hangingIndent = -1)
    {
        if (hangingIndent < 0)
        {
            hangingIndent = indent;
        }

        int prefixLen = quoteDepth * 2;
        int firstLineWidth = width - prefixLen - indent;
        int contLineWidth = width - prefixLen - hangingIndent;

        if (firstLineWidth < 2)
        {
            firstLineWidth = 2;
        }

        if (contLineWidth < 2)
        {
            contLineWidth = 2;
        }

        // Flatten spans into chars with styles
        var chars = new List<char>();
        var charStyles = new List<CellStyle>();

        foreach ((string text, CellStyle style) in spans)
        {
            foreach (char c in text)
            {
                chars.Add(c);
                charStyles.Add(style);
            }
        }

        bool firstLine = true;
        int pos = 0;

        while (pos < chars.Count)
        {
            int lineIndent = firstLine ? indent : hangingIndent;
            int maxChars = firstLine ? firstLineWidth : contLineWidth;

            // Find how many chars fit on this line
            int remaining = chars.Count - pos;

            if (remaining <= maxChars)
            {
                // Everything fits
                EmitSingleLine(chars, charStyles, pos, remaining, lines, lineIndent, quoteDepth, width);
                break;
            }

            // Find word break point
            int breakAt = FindWordBreak(chars, pos, maxChars);
            EmitSingleLine(chars, charStyles, pos, breakAt - pos, lines, lineIndent, quoteDepth, width);

            // Skip whitespace at break point
            pos = breakAt;

            while (pos < chars.Count && chars[pos] == ' ')
            {
                pos++;
            }

            firstLine = false;
        }

        if (chars.Count == 0)
        {
            lines.Add(MakeQuotedLine("", null, quoteDepth, width));
        }
    }

    private static int FindWordBreak(List<char> chars, int start, int maxChars)
    {
        int end = start + maxChars;

        // If there's a newline in range, break there
        for (int i = start; i < end && i < chars.Count; i++)
        {
            if (chars[i] == '\n')
            {
                return i + 1; // consume the newline
            }
        }

        if (end >= chars.Count)
        {
            return chars.Count;
        }

        // Walk back to find a space
        int breakAt = end;

        while (breakAt > start && chars[breakAt - 1] != ' ')
        {
            breakAt--;
        }

        // If no space found (very long word), break at maxChars
        if (breakAt == start)
        {
            breakAt = end;
        }

        return breakAt;
    }

    private static void EmitSingleLine(
        List<char> chars,
        List<CellStyle> charStyles,
        int start,
        int length,
        List<StyledLine> lines,
        int indent,
        int quoteDepth,
        int width)
    {
        // Handle embedded newlines by splitting
        for (int i = start; i < start + length; i++)
        {
            if (chars[i] == '\n')
            {
                // Emit up to the newline
                int before = i - start;

                if (before > 0)
                {
                    EmitSingleLineCore(chars, charStyles, start, before, lines, indent, quoteDepth, width);
                }
                else
                {
                    lines.Add(MakeQuotedLine("", null, quoteDepth, width));
                }

                // Recurse for the rest
                int after = length - (i - start) - 1;

                if (after > 0)
                {
                    EmitSingleLine(chars, charStyles, i + 1, after, lines, indent, quoteDepth, width);
                }

                return;
            }
        }

        EmitSingleLineCore(chars, charStyles, start, length, lines, indent, quoteDepth, width);
    }

    private static void EmitSingleLineCore(
        List<char> chars,
        List<CellStyle> charStyles,
        int start,
        int length,
        List<StyledLine> lines,
        int indent,
        int quoteDepth,
        int width)
    {
        var text = new string(chars.GetRange(start, length).ToArray());
        CellStyle[] styles = charStyles.GetRange(start, length).ToArray();
        lines.Add(MakeQuotedLine(text, styles, quoteDepth, width, indent));
    }

    private static StyledLine MakeQuotedLine(
        string text,
        CellStyle[]? charStyles,
        int quoteDepth,
        int width,
        int indent = 0)
    {
        if (quoteDepth == 0 && indent == 0)
        {
            return new StyledLine(text, null, charStyles);
        }

        string prefix = "";
        var prefixStyles = new List<CellStyle>();

        // Add blockquote bars
        for (int q = 0; q < quoteDepth; q++)
        {
            prefix += "│ ";
            prefixStyles.Add(s_blockquoteBarStyle);
            prefixStyles.Add(s_blockquoteBarStyle);
        }

        // Add indent
        if (indent > 0)
        {
            prefix += new string(' ', indent);

            for (int i = 0; i < indent; i++)
            {
                prefixStyles.Add(CellStyle.Default);
            }
        }

        string fullText = prefix + text;

        if (charStyles is null)
        {
            // Blockquote text gets blockquote text style
            if (quoteDepth > 0 && text.Length > 0)
            {
                var allStyles = new CellStyle[fullText.Length];
                prefixStyles.CopyTo(allStyles);
                Array.Fill(allStyles, s_blockquoteTextStyle, prefixStyles.Count, text.Length);
                return new StyledLine(fullText, null, allStyles);
            }

            if (prefixStyles.Count > 0)
            {
                return new StyledLine(fullText, null, prefixStyles.ToArray());
            }

            return new StyledLine(fullText, null);
        }

        var fullStyles = new CellStyle[fullText.Length];
        prefixStyles.CopyTo(fullStyles);

        // Apply blockquote text dimming to content styles when inside a quote
        if (quoteDepth > 0)
        {
            for (int i = 0; i < charStyles.Length && prefixStyles.Count + i < fullStyles.Length; i++)
            {
                CellStyle s = charStyles[i];
                fullStyles[prefixStyles.Count + i] = new CellStyle(
                    s.Fg ?? s_blockquoteTextStyle.Fg,
                    s.Bg,
                    s.Bold, true, s.Inverse, s.Underline, s.Strikethrough);
            }
        }
        else
        {
            Array.Copy(charStyles, 0, fullStyles, prefixStyles.Count, charStyles.Length);
        }

        return new StyledLine(fullText, null, fullStyles);
    }

    private static CellStyle[] PrependIndent(CellStyle[] styles, int indent)
    {
        if (indent <= 0)
        {
            return styles;
        }

        var result = new CellStyle[indent + styles.Length];
        Array.Fill(result, CellStyle.Default, 0, indent);
        Array.Copy(styles, 0, result, indent, styles.Length);
        return result;
    }
}
