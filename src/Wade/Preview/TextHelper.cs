namespace Wade.Preview;

internal static class TextHelper
{
    public static List<string> WrapText(string text, int maxWidth)
    {
        var result = new List<string>();

        string normalized = string.Join(' ', text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

        if (normalized.Length <= maxWidth)
        {
            result.Add(normalized);
            return result;
        }

        int pos = 0;

        while (pos < normalized.Length)
        {
            if (pos + maxWidth >= normalized.Length)
            {
                result.Add(normalized[pos..]);
                break;
            }

            int breakAt = normalized.LastIndexOf(' ', pos + maxWidth, maxWidth);

            if (breakAt <= pos)
            {
                breakAt = pos + maxWidth;
            }

            result.Add(normalized[pos..breakAt]);
            pos = breakAt + 1;
        }

        return result;
    }
}
