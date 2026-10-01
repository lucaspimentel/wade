using System.Text;
using Xunit;

namespace Wade.Search.Tests;

/// <summary>
/// Exact FuzzyScorer scores and match positions for the shared cases in
/// tests/golden/search/scorer.cases.txt. The same golden is verified by
/// src/wade-rs/src/search/scorer.rs, so the two scorers must agree exactly.
/// </summary>
public class ScorerGoldenTests
{
    private static readonly char[] s_separators = Path.DirectorySeparatorChar == Path.AltDirectorySeparatorChar
        ? [Path.DirectorySeparatorChar]
        : [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar];

    [Fact]
    public void ScorerCases_MatchGolden()
    {
        string dir = FindGoldenDir();
        string casesPath = Path.Combine(dir, "scorer.cases.txt");
        string goldenPath = Path.Combine(dir, "scorer.golden.txt");

        var sb = new StringBuilder();

        foreach (string line in File.ReadAllLines(casesPath))
        {
            if (line.Length == 0 || line.StartsWith('#'))
            {
                continue;
            }

            string[] fields = line.Split('\t');
            sb.Append(line).Append("\t=> ").Append(Evaluate(fields[0], Expand(fields[1]), Expand(fields[2]))).Append('\n');
        }

        string actual = sb.ToString();

        if (Environment.GetEnvironmentVariable("WADE_UPDATE_GOLDENS") == "1" && !File.Exists(goldenPath))
        {
            File.WriteAllText(goldenPath, actual, new UTF8Encoding(false));
        }

        Assert.True(File.Exists(goldenPath), $"Missing golden file {goldenPath}; run with WADE_UPDATE_GOLDENS=1 to generate");
        Assert.Equal(File.ReadAllText(goldenPath).ReplaceLineEndings("\n"), actual);
    }

    private static string Evaluate(string kind, string query, string target)
    {
        int fileNameStart = target.LastIndexOfAny(s_separators) + 1;
        int score;
        int[] positions;

        switch (kind)
        {
            case "score":
                score = FuzzyScorer.Score(query, target, out positions);
                break;
            case "exact":
                score = FuzzyScorer.ExactScore(query, target, caseSensitive: false, out positions);
                break;
            case "exact-cs":
                score = FuzzyScorer.ExactScore(query, target, caseSensitive: true, out positions);
                break;
            case "fname":
                score = FuzzyScorer.ScoreWithFileNamePriority(query, target, fileNameStart, out positions);
                break;
            case "fname-exact":
                score = FuzzyScorer.ExactScoreWithFileNamePriority(query, target, fileNameStart, caseSensitive: false, out positions);
                break;
            case "fname-exact-cs":
                score = FuzzyScorer.ExactScoreWithFileNamePriority(query, target, fileNameStart, caseSensitive: true, out positions);
                break;
            default:
                throw new InvalidOperationException($"Unknown kind {kind}");
        }

        string scoreText = score == int.MinValue ? "none" : score.ToString(System.Globalization.CultureInfo.InvariantCulture);
        return $"{scoreText} [{string.Join(",", positions)}]";
    }

    private static string Expand(string s) => s.Replace("{/}", Path.DirectorySeparatorChar.ToString());

    private static string FindGoldenDir()
    {
        string? dir = AppContext.BaseDirectory;
        while (dir is not null)
        {
            string candidate = Path.Combine(dir, "tests", "golden", "search");
            if (Directory.Exists(candidate))
            {
                return candidate;
            }

            dir = Path.GetDirectoryName(dir.TrimEnd(Path.DirectorySeparatorChar));
        }

        throw new InvalidOperationException("Could not locate tests/golden/search directory");
    }
}
