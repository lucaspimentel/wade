namespace Wade.Search;

/// <summary>
/// Greedy subsequence scorer with boundary-aware scoring, inspired by fzf v1.
/// Matches query characters in order against a target string with gaps allowed.
/// Boundary bonuses (path separators, dots, camelCase) reward matches at meaningful positions.
/// </summary>
internal static class FuzzyScorer
{
    // Scoring constants (fzf-inspired, proven to produce good rankings)
    internal const int ScoreMatch = 16;
    internal const int PenaltyGapStart = -3;
    internal const int PenaltyGapExtension = -1;
    internal const int BonusBoundary = 8;
    internal const int BonusBoundaryDelimiter = 9;
    internal const int BonusCamel = 7;
    internal const int BonusNonWord = 8;
    internal const int BonusConsecutive = 4;
    internal const int BonusFirstCharMultiplier = 2;
    internal const int BonusCaseMatch = 1;
    internal const int FileNameBonus = 1000;

    private static readonly char[] s_separators = Path.DirectorySeparatorChar == Path.AltDirectorySeparatorChar
        ? [Path.DirectorySeparatorChar]
        : [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar];

    /// <summary>
    /// Score a query against a target string using greedy subsequence matching.
    /// Returns <see cref="int.MinValue"/> if the query is not a subsequence of the target.
    /// Higher scores are better.
    /// </summary>
    internal static int Score(ReadOnlySpan<char> query, ReadOnlySpan<char> target)
    {
        int queryLen = query.Length;
        int targetLen = target.Length;

        if (queryLen == 0)
        {
            return 0;
        }

        if (queryLen > targetLen)
        {
            return int.MinValue;
        }

        // Pre-lowercase the query once.
        Span<char> queryLower = queryLen <= 64 ? stackalloc char[queryLen] : new char[queryLen];
        for (int i = 0; i < queryLen; i++)
        {
            queryLower[i] = char.ToLowerInvariant(query[i]);
        }

        // Forward scan: greedily find the first subsequence match.
        Span<int> forwardPositions = queryLen <= 64 ? stackalloc int[queryLen] : new int[queryLen];
        int qi = 0;

        for (int ti = 0; ti < targetLen && qi < queryLen; ti++)
        {
            if (char.ToLowerInvariant(target[ti]) == queryLower[qi])
            {
                forwardPositions[qi] = ti;
                qi++;
            }
        }

        if (qi < queryLen)
        {
            return int.MinValue; // Not all query chars found.
        }

        // Backward scan: from the last forward match, walk backward to tighten the match span.
        // This finds a shorter-span match ending at the same position, which typically scores better.
        Span<int> matchPositions = queryLen <= 64 ? stackalloc int[queryLen] : new int[queryLen];
        int lastForwardPos = forwardPositions[queryLen - 1];
        qi = queryLen - 1;

        for (int ti = lastForwardPos; ti >= 0 && qi >= 0; ti--)
        {
            if (char.ToLowerInvariant(target[ti]) == queryLower[qi])
            {
                matchPositions[qi] = ti;
                qi--;
            }
        }

        // Score the matched positions.
        return ComputeScore(query, target, matchPositions);
    }

    /// <summary>
    /// Score a query with filename priority. Scores the query against just the filename
    /// portion first (with a large bonus); falls back to scoring the full relative path.
    /// Returns the higher of the two scores, or <see cref="int.MinValue"/> if no match.
    /// </summary>
    internal static int ScoreWithFileNamePriority(ReadOnlySpan<char> query, ReadOnlySpan<char> relativePath, int fileNameStart)
    {
        int fullScore = Score(query, relativePath);

        // If the filename portion exists and is different from the full path, try scoring it.
        if (fileNameStart > 0 && fileNameStart < relativePath.Length)
        {
            ReadOnlySpan<char> fileName = relativePath[fileNameStart..];
            int fileNameScore = Score(query, fileName);

            if (fileNameScore != int.MinValue)
            {
                fileNameScore += FileNameBonus;

                if (fileNameScore > fullScore)
                {
                    return fileNameScore;
                }
            }
        }

        return fullScore;
    }

    private static int ComputeScore(ReadOnlySpan<char> query, ReadOnlySpan<char> target, ReadOnlySpan<int> matchPositions)
    {
        int score = 0;
        int prevPosition = -1;
        int prevBonus = 0;

        for (int qi = 0; qi < matchPositions.Length; qi++)
        {
            int pos = matchPositions[qi];

            // Base match score.
            score += ScoreMatch;

            // Case-sensitive bonus.
            if (query[qi] == target[pos])
            {
                score += BonusCaseMatch;
            }

            // Boundary bonus for this position.
            int bonus = GetBoundaryBonus(target, pos);

            // Consecutive match bonus: if this match immediately follows the previous one,
            // use the higher of the current bonus, the previous bonus, or the minimum consecutive bonus.
            if (prevPosition >= 0 && pos == prevPosition + 1)
            {
                bonus = Math.Max(bonus, Math.Max(prevBonus, BonusConsecutive));
            }

            // First character multiplier.
            if (qi == 0)
            {
                bonus *= BonusFirstCharMultiplier;
            }

            score += bonus;

            // Gap penalty.
            if (prevPosition >= 0)
            {
                int gap = pos - prevPosition - 1;

                if (gap > 0)
                {
                    score += PenaltyGapStart + PenaltyGapExtension * (gap - 1);
                }
            }
            else if (pos > 0)
            {
                // Gap before the first match.
                score += PenaltyGapStart + PenaltyGapExtension * (pos - 1);
            }

            prevPosition = pos;
            prevBonus = bonus;
        }

        return score;
    }

    private static int GetBoundaryBonus(ReadOnlySpan<char> target, int position)
    {
        if (position == 0)
        {
            // Start of string treated as delimiter boundary.
            return BonusBoundaryDelimiter;
        }

        char prev = target[position - 1];
        char curr = target[position];

        if (IsDelimiter(prev))
        {
            return BonusBoundaryDelimiter;
        }

        if (IsNonWord(prev) && IsWord(curr))
        {
            return BonusBoundary;
        }

        if (char.IsLower(prev) && char.IsUpper(curr))
        {
            return BonusCamel;
        }

        if (!char.IsDigit(prev) && char.IsDigit(curr))
        {
            return BonusCamel;
        }

        if (IsNonWord(curr))
        {
            return BonusNonWord;
        }

        return 0;
    }

    private static bool IsDelimiter(char c)
    {
        for (int i = 0; i < s_separators.Length; i++)
        {
            if (c == s_separators[i])
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsNonWord(char c) =>
        !char.IsLetterOrDigit(c);

    private static bool IsWord(char c) =>
        char.IsLetterOrDigit(c);
}
