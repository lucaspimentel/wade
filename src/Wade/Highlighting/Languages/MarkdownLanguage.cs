using System.Text.RegularExpressions;

namespace Wade.Highlighting.Languages;

internal sealed partial class MarkdownLanguage : ILanguage
{
    private const byte StateNormal    = 0;
    private const byte StateCodeFence = 1;

    [GeneratedRegex(@"\[([^\]]+)\]\(([^)]+)\)", RegexOptions.None)]
    private static partial Regex LinkPattern();

    [GeneratedRegex(@"(`+)(.+?)\1", RegexOptions.None)]
    private static partial Regex CodeSpanPattern();

    [GeneratedRegex(@"\*\*(.+?)\*\*|__(.+?)__", RegexOptions.None)]
    private static partial Regex BoldPattern();

    [GeneratedRegex(@"(?<!\*)\*(?!\*)(.+?)(?<!\*)\*(?!\*)|(?<!_)_(?!_)(.+?)(?<!_)_(?!_)", RegexOptions.None)]
    private static partial Regex ItalicPattern();

    public StyledLine TokenizeLine(string line, ref byte state)
    {
        if (line.Length == 0)
            return new StyledLine(line, null);

        var spans = new List<StyledSpan>();

        // Inside a fenced code block
        if (state == StateCodeFence)
        {
            if (line.TrimStart().StartsWith("```", StringComparison.Ordinal) ||
                line.TrimStart().StartsWith("~~~", StringComparison.Ordinal))
            {
                spans.Add(new StyledSpan(0, line.Length, TokenKind.CodeSpan));
                state = StateNormal;
            }
            else
            {
                spans.Add(new StyledSpan(0, line.Length, TokenKind.CodeSpan));
            }
            return new StyledLine(line, [.. spans]);
        }

        // Fenced code block open
        string trimmed = line.TrimStart();
        if (trimmed.StartsWith("```", StringComparison.Ordinal) ||
            trimmed.StartsWith("~~~", StringComparison.Ordinal))
        {
            spans.Add(new StyledSpan(0, line.Length, TokenKind.CodeSpan));
            state = StateCodeFence;
            return new StyledLine(line, [.. spans]);
        }

        // Heading: # ... ######
        if (line.Length > 0 && line[0] == '#')
        {
            spans.Add(new StyledSpan(0, line.Length, TokenKind.Heading));
            return new StyledLine(line, [.. spans]);
        }

        // Blockquote: > ...
        if (line.Length > 0 && line[0] == '>')
        {
            spans.Add(new StyledSpan(0, 1, TokenKind.Operator));
            return new StyledLine(line, [.. spans]);
        }

        // Horizontal rule: --- or *** or ___
        if (trimmed == "---" || trimmed == "***" || trimmed == "___" ||
            (trimmed.Length >= 3 && trimmed.All(c => c == '-' || c == ' ') && trimmed.Count(c => c == '-') >= 3) ||
            (trimmed.Length >= 3 && trimmed.All(c => c == '*' || c == ' ') && trimmed.Count(c => c == '*') >= 3))
        {
            spans.Add(new StyledSpan(0, line.Length, TokenKind.Operator));
            return new StyledLine(line, [.. spans]);
        }

        // List item: - or * or + or numbered
        if (trimmed.Length > 0 && (trimmed[0] == '-' || trimmed[0] == '*' || trimmed[0] == '+'))
        {
            int indent = line.Length - trimmed.Length;
            spans.Add(new StyledSpan(indent, 1, TokenKind.Punctuation));
        }
        else if (trimmed.Length > 0 && char.IsDigit(trimmed[0]))
        {
            int i = 0;
            while (i < trimmed.Length && char.IsDigit(trimmed[i])) i++;
            if (i < trimmed.Length && trimmed[i] == '.')
            {
                int indent = line.Length - trimmed.Length;
                spans.Add(new StyledSpan(indent, i + 1, TokenKind.Punctuation));
            }
        }

        // Inline patterns (applied to the whole line)
        ApplyInlinePatterns(line, spans);

        return spans.Count == 0 ? new StyledLine(line, null) : new StyledLine(line, [.. spans]);
    }

    private static void ApplyInlinePatterns(string line, List<StyledSpan> spans)
    {
        // Code spans: `code` or ``code``
        foreach (Match m in CodeSpanPattern().Matches(line))
            spans.Add(new StyledSpan(m.Index, m.Length, TokenKind.CodeSpan));

        // Bold: **text** or __text__
        foreach (Match m in BoldPattern().Matches(line))
            spans.Add(new StyledSpan(m.Index, m.Length, TokenKind.Bold));

        // Italic: *text* or _text_
        foreach (Match m in ItalicPattern().Matches(line))
            spans.Add(new StyledSpan(m.Index, m.Length, TokenKind.Italic));

        // Links: [text](url)
        foreach (Match m in LinkPattern().Matches(line))
            spans.Add(new StyledSpan(m.Index, m.Length, TokenKind.Link));
    }
}
