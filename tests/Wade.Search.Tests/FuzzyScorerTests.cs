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

        // Should fall back to full path score since "Wade" doesn't match "App.cs"
        Assert.Equal(directScore, score);
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
}
