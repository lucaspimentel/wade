namespace Wade.Search;

internal enum QueryMode
{
    Fuzzy,
    ExactSubstring,
}

internal sealed record SearchQuery(QueryMode Mode, string Text, bool CaseSensitive)
{
    internal bool IsEmpty => Text.Length == 0;

    internal static SearchQuery Parse(string raw)
    {
        if (string.IsNullOrEmpty(raw))
        {
            return new SearchQuery(QueryMode.Fuzzy, string.Empty, CaseSensitive: false);
        }

        if (raw[0] == '\'')
        {
            string text = raw.Length > 1 ? raw[1..] : string.Empty;
            return new SearchQuery(QueryMode.ExactSubstring, text, HasUpper(text));
        }

        return new SearchQuery(QueryMode.Fuzzy, raw, CaseSensitive: false);
    }

    private static bool HasUpper(string s)
    {
        for (int i = 0; i < s.Length; i++)
        {
            if (char.IsUpper(s[i]))
            {
                return true;
            }
        }

        return false;
    }
}
