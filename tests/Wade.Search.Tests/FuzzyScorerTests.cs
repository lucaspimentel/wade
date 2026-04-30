using Xunit;

namespace Wade.Search.Tests;

public class FuzzyScorerTests
{
    [Theory]
    [InlineData("abc", "abc", true)]         // exact match
    [InlineData("abc", "aXbXc", true)]       // subsequence with gaps
    [InlineData("app", "App.cs", true)]      // case insensitive
    [InlineData("pdf", "report.pdf", true)]  // match after dot boundary
    [InlineData("cs", "App.cs", true)]       // extension match
    [InlineData("wade", "Wade", true)]       // case insensitive full match
    [InlineData("NP", "NullPointerException", true)] // camelCase initials
    public void Score_MatchesSubsequence(string query, string target, bool shouldMatch)
    {
        int score = FuzzyScorer.Score(query, target);
        Assert.True(shouldMatch ? score > int.MinValue : score == int.MinValue);
    }

    [Theory]
    [InlineData("abc", "acb")]     // wrong order
    [InlineData("xyz", "abc")]     // no chars match
    [InlineData("abcd", "abc")]    // query longer than target
    [InlineData("test", "")]       // empty target
    public void Score_NoMatch_ReturnsMinValue(string query, string target)
    {
        Assert.Equal(int.MinValue, FuzzyScorer.Score(query, target));
    }

    [Fact]
    public void Score_EmptyQuery_ReturnsZero()
    {
        Assert.Equal(0, FuzzyScorer.Score("", "anything"));
    }

    [Fact]
    public void Score_BoundaryMatch_ScoresHigherThanMidWord()
    {
        // "p" at dot boundary in ".pdf" vs "p" mid-word in "upping"
        // Use targets where 'p' appears at a boundary in one and mid-word in the other.
        int boundaryScore = FuzzyScorer.Score("p", ".pdf");
        int midWordScore = FuzzyScorer.Score("p", "upping");

        Assert.True(boundaryScore > midWordScore,
            $"Boundary score {boundaryScore} should be > mid-word score {midWordScore}");
    }

    [Fact]
    public void Score_ConsecutiveMatch_ScoresHigherThanSpread()
    {
        // "wade" consecutive in "Wade" vs spread across "W_a_d_e"
        int consecutiveScore = FuzzyScorer.Score("wade", "Wade");
        int spreadScore = FuzzyScorer.Score("wade", "W_a_d_e");

        Assert.True(consecutiveScore > spreadScore,
            $"Consecutive score {consecutiveScore} should be > spread score {spreadScore}");
    }

    [Fact]
    public void Score_CamelCaseMatch_ScoresWell()
    {
        // "NP" matching camelCase boundaries in "NullPointer" vs mid-word in "xnxpx"
        int camelScore = FuzzyScorer.Score("NP", "NullPointer");
        int midScore = FuzzyScorer.Score("NP", "xnxpx");

        Assert.True(camelScore > midScore,
            $"CamelCase score {camelScore} should be > mid-word score {midScore}");
    }

    [Fact]
    public void Score_ExactCaseMatch_ScoresHigherThanCaseInsensitive()
    {
        int exactCase = FuzzyScorer.Score("App", "App.cs");
        int lowerCase = FuzzyScorer.Score("app", "App.cs");

        Assert.True(exactCase > lowerCase,
            $"Exact case score {exactCase} should be > case-insensitive score {lowerCase}");
    }

    [Fact]
    public void Score_TightMatch_ScoresHigherThanLoose()
    {
        // "abc" tight (consecutive) vs "abc" spread with same starting position
        int tightScore = FuzzyScorer.Score("abc", "abcxxx");
        int looseScore = FuzzyScorer.Score("abc", "axxbxxcxxx");

        Assert.True(tightScore > looseScore,
            $"Tight score {tightScore} should be > loose score {looseScore}");
    }

    [Fact]
    public void ScoreWithFileNamePriority_FileNameMatch_GetsBonusOverPathMatch()
    {
        string path = string.Join(Path.DirectorySeparatorChar.ToString(), "src", "Wade", "App.cs");
        int fileNameStart = path.LastIndexOf(Path.DirectorySeparatorChar) + 1;

        // "App" matches in the filename — should get FileNameBonus
        int fileNamePriorityScore = FuzzyScorer.ScoreWithFileNamePriority("App", path, fileNameStart);

        // Score against full path only (no filename bonus)
        int fullPathScore = FuzzyScorer.Score("App", path);

        Assert.True(fileNamePriorityScore > fullPathScore,
            $"Filename priority score {fileNamePriorityScore} should be > full path score {fullPathScore}");
    }

    [Fact]
    public void ScoreWithFileNamePriority_AppRanksHigherForAppCs()
    {
        string appCs = string.Join(Path.DirectorySeparatorChar.ToString(), "src", "Wade", "App.cs");
        int appCsFileStart = appCs.LastIndexOf(Path.DirectorySeparatorChar) + 1;

        string configCs = string.Join(Path.DirectorySeparatorChar.ToString(), "src", "Applications", "Config.cs");
        int configCsFileStart = configCs.LastIndexOf(Path.DirectorySeparatorChar) + 1;

        int appScore = FuzzyScorer.ScoreWithFileNamePriority("App", appCs, appCsFileStart);
        int configScore = FuzzyScorer.ScoreWithFileNamePriority("App", configCs, configCsFileStart);

        Assert.True(appScore > configScore,
            $"App.cs score {appScore} should be > Config.cs score {configScore}");
    }

    [Fact]
    public void ScoreWithFileNamePriority_NoFileNameMatch_FallsBackToFullPath()
    {
        string path = string.Join(Path.DirectorySeparatorChar.ToString(), "src", "Wade", "App.cs");
        int fileNameStart = path.LastIndexOf(Path.DirectorySeparatorChar) + 1;

        // "Wade" matches in the directory, not the filename
        int score = FuzzyScorer.ScoreWithFileNamePriority("Wade", path, fileNameStart);
        int directScore = FuzzyScorer.Score("Wade", path);

        // Should fall back to full path score (minus depth penalty for 2 separators)
        int depth = path.Count(c => c == Path.DirectorySeparatorChar);
        Assert.Equal(directScore + FuzzyScorer.PenaltyDepth * depth, score);
    }

    [Fact]
    public void ScoreWithFileNamePriority_PdfFindsReportPdf()
    {
        string path = string.Join(Path.DirectorySeparatorChar.ToString(), "Documents", "report.pdf");
        int fileNameStart = path.LastIndexOf(Path.DirectorySeparatorChar) + 1;

        int score = FuzzyScorer.ScoreWithFileNamePriority("pdf", path, fileNameStart);

        Assert.True(score > int.MinValue, "Should find 'pdf' in 'report.pdf'");
        Assert.True(score >= FuzzyScorer.FileNameBonus,
            $"Score {score} should include filename bonus {FuzzyScorer.FileNameBonus}");
    }

    [Fact]
    public void Score_PathSeparatorBonus()
    {
        // Match at path separator boundary vs mid-word
        string path = string.Join(Path.DirectorySeparatorChar.ToString(), "src", "Wade");
        int delimiterScore = FuzzyScorer.Score("Wade", path);
        int midScore = FuzzyScorer.Score("Wade", "notwade");

        Assert.True(delimiterScore > midScore,
            $"Delimiter boundary score {delimiterScore} should be > mid-word score {midScore}");
    }

    [Fact]
    public void Score_MatchPositions_ExactMatch()
    {
        int score = FuzzyScorer.Score("abc", "abc", out int[] positions);
        Assert.True(score > int.MinValue);
        Assert.Equal([0, 1, 2], positions);
    }

    [Fact]
    public void Score_MatchPositions_SubsequenceWithGaps()
    {
        int score = FuzzyScorer.Score("ac", "abc", out int[] positions);
        Assert.True(score > int.MinValue);
        Assert.Equal([0, 2], positions);
    }

    [Fact]
    public void Score_MatchPositions_NoMatch_ReturnsEmpty()
    {
        int score = FuzzyScorer.Score("xyz", "abc", out int[] positions);
        Assert.Equal(int.MinValue, score);
        Assert.Empty(positions);
    }

    [Fact]
    public void Score_MatchPositions_EmptyQuery_ReturnsEmpty()
    {
        int score = FuzzyScorer.Score("", "abc", out int[] positions);
        Assert.Equal(0, score);
        Assert.Empty(positions);
    }

    [Fact]
    public void ScoreWithFileNamePriority_MatchPositions_FileNameMatch_OffsetsPositions()
    {
        // "App" matches filename "App.cs" at positions 0,1,2 within filename,
        // which is at offset fileNameStart in the full path.
        string path = string.Join(
            Path.DirectorySeparatorChar.ToString(), "src", "Wade", "App.cs");
        int fileNameStart = path.LastIndexOf(Path.DirectorySeparatorChar) + 1;

        FuzzyScorer.ScoreWithFileNamePriority(
            "App", path, fileNameStart, out int[] positions);

        // Positions should be offset by fileNameStart
        Assert.Equal(
            [fileNameStart, fileNameStart + 1, fileNameStart + 2], positions);
    }

    [Fact]
    public void ScoreWithFileNamePriority_MatchPositions_PathMatch_NoOffset()
    {
        // "Wade" matches in directory part, not filename — positions relative to full path
        string path = string.Join(
            Path.DirectorySeparatorChar.ToString(), "src", "Wade", "App.cs");
        int fileNameStart = path.LastIndexOf(Path.DirectorySeparatorChar) + 1;

        FuzzyScorer.ScoreWithFileNamePriority(
            "Wade", path, fileNameStart, out int[] positions);

        int wadeStart = path.IndexOf("Wade");
        Assert.Equal(
            [wadeStart, wadeStart + 1, wadeStart + 2, wadeStart + 3],
            positions);
    }

    [Theory]
    [InlineData("foo", "foobar", false)]    // exact substring at start
    [InlineData("bar", "foobar", false)]    // exact substring at end
    [InlineData("oob", "foobar", false)]    // exact substring middle
    [InlineData("FOO", "foobar", false)]    // case-insensitive (smart-case off)
    public void ExactScore_Match(string query, string target, bool caseSensitive)
    {
        int score = FuzzyScorer.ExactScore(query, target, caseSensitive);
        Assert.True(score > int.MinValue, $"Expected match, got score={score}");
    }

    [Theory]
    [InlineData("foB", "foobar", false)]    // not contiguous
    [InlineData("xyz", "foobar", false)]    // chars absent
    [InlineData("foobars", "foobar", false)] // longer than target
    [InlineData("Foo", "foobar", true)]      // case-sensitive miss
    public void ExactScore_NoMatch_ReturnsMinValue(string query, string target, bool caseSensitive)
    {
        Assert.Equal(int.MinValue, FuzzyScorer.ExactScore(query, target, caseSensitive));
    }

    [Fact]
    public void ExactScore_EmptyQuery_ReturnsZero()
    {
        Assert.Equal(0, FuzzyScorer.ExactScore("", "anything", caseSensitive: false));
    }

    [Fact]
    public void ExactScore_MatchPositions_AreContiguous()
    {
        FuzzyScorer.ExactScore("oob", "foobar", caseSensitive: false, out int[] positions);
        Assert.Equal([1, 2, 3], positions);
    }

    [Fact]
    public void ExactScore_NormalizesPathSeparators()
    {
        // Query uses '/'; target uses platform separator. Should still match.
        string sep = Path.DirectorySeparatorChar.ToString();
        string target = "src" + sep + "Wade";

        int score = FuzzyScorer.ExactScore("src/Wade", target, caseSensitive: false);

        Assert.True(score > int.MinValue, "Path separator normalization should let `/` match the platform separator");
    }

    [Fact]
    public void ExactScore_CaseSensitive_DistinguishesCase()
    {
        Assert.Equal(int.MinValue, FuzzyScorer.ExactScore("App", "app.cs", caseSensitive: true));
        Assert.True(FuzzyScorer.ExactScore("App", "App.cs", caseSensitive: true) > int.MinValue);
    }

    [Fact]
    public void ExactScoreWithFileNamePriority_FileNameMatch_GetsBonus()
    {
        string path = string.Join(Path.DirectorySeparatorChar.ToString(), "src", "Wade", "App.cs");
        int fileNameStart = path.LastIndexOf(Path.DirectorySeparatorChar) + 1;

        int priorityScore = FuzzyScorer.ExactScoreWithFileNamePriority(
            "App", path, fileNameStart, caseSensitive: false, out _);
        int fullScore = FuzzyScorer.ExactScore("App", path, caseSensitive: false);

        Assert.True(priorityScore > fullScore,
            $"Filename-priority score {priorityScore} should beat full-path score {fullScore}");
    }

    [Fact]
    public void ExactScoreWithFileNamePriority_MatchPositions_OffsetIntoFullPath()
    {
        string path = string.Join(Path.DirectorySeparatorChar.ToString(), "src", "Wade", "App.cs");
        int fileNameStart = path.LastIndexOf(Path.DirectorySeparatorChar) + 1;

        FuzzyScorer.ExactScoreWithFileNamePriority(
            "App", path, fileNameStart, caseSensitive: false, out int[] positions);

        Assert.Equal([fileNameStart, fileNameStart + 1, fileNameStart + 2], positions);
    }

    [Fact]
    public void ExactScore_NonContiguous_FailsWhereFuzzySucceeds()
    {
        // 'aBc' is a fuzzy subsequence of 'aXbXc' but not a contiguous substring.
        Assert.True(FuzzyScorer.Score("abc", "aXbXc") > int.MinValue);
        Assert.Equal(int.MinValue, FuzzyScorer.ExactScore("abc", "aXbXc", caseSensitive: false));
    }
}
