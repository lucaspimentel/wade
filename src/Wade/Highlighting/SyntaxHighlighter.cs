namespace Wade.Highlighting;

internal static class SyntaxHighlighter
{
    public static StyledLine[] Highlight(string[] lines, int headerLineCount, string filePath)
    {
        var lang = LanguageMap.GetLanguage(filePath);
        var result = new StyledLine[lines.Length];

        // Header lines pass through with null spans
        for (int i = 0; i < Math.Min(headerLineCount, lines.Length); i++)
            result[i] = new StyledLine(lines[i], null);

        if (lang is null)
        {
            for (int i = headerLineCount; i < lines.Length; i++)
                result[i] = new StyledLine(lines[i], null);
            return result;
        }

        byte state = 0;
        for (int i = headerLineCount; i < lines.Length; i++)
            result[i] = lang.TokenizeLine(lines[i], ref state);

        return result;
    }
}
