using BenchmarkDotNet.Attributes;
using Wade.Search;

namespace Wade.Benchmarks;

/// <summary>
/// Measures allocation behaviour of <see cref="FuzzyScorer"/> scoring methods.
/// Baseline: before the ScoreCore refactor.
/// Target: the WithFileNamePriority methods allocate exactly one int[] per call (the winner),
/// and the no-out overloads allocate zero.
/// </summary>
[MemoryDiagnoser]
public class FuzzyScorerBenchmarks
{
    // Realistic paths a user would encounter when navigating a .NET repository.
    private static readonly (string relativePath, int fileNameStart)[] s_paths =
    [
        (@"src\Wade\App.cs",                          @"src\Wade\".Length),
        (@"src\Wade\Highlighting\StyledLine.cs",      @"src\Wade\Highlighting\".Length),
        (@"src\Wade.Search\FuzzyScorer.cs",           @"src\Wade.Search\".Length),
        (@"src\Wade.Search\ActiveQuery.cs",           @"src\Wade.Search\".Length),
        (@"src\Wade.Search\SearchIndex.cs",           @"src\Wade.Search\".Length),
        (@"tests\Wade.Search.Tests\FuzzyScorerTests.cs", @"tests\Wade.Search.Tests\".Length),
        (@"tests\Wade.Tests\AppTests.cs",             @"tests\Wade.Tests\".Length),
        (@"benchmarks\Wade.Benchmarks\RenderBenchmarks.cs", @"benchmarks\Wade.Benchmarks\".Length),
        (@"README.md",                                0),
        (@"CHANGELOG.md",                             0),
    ];

    private const string FuzzyQuery = "apcs";   // matches App.cs, ActiveQuery.cs, etc.
    private const string ExactQuery = "Wade";   // matches every path

    [Benchmark(Baseline = true, Description = "ScoreWithFileNamePriority (score only)")]
    public int ScoreWithFileNamePriority_ScoreOnly()
    {
        int total = 0;
        foreach (var (rel, fnStart) in s_paths)
            total += FuzzyScorer.ScoreWithFileNamePriority(FuzzyQuery.AsSpan(), rel.AsSpan(), fnStart);
        return total;
    }

    [Benchmark(Description = "ScoreWithFileNamePriority (with positions)")]
    public int ScoreWithFileNamePriority_WithPositions()
    {
        int total = 0;
        foreach (var (rel, fnStart) in s_paths)
            total += FuzzyScorer.ScoreWithFileNamePriority(FuzzyQuery.AsSpan(), rel.AsSpan(), fnStart, out _);
        return total;
    }

    [Benchmark(Description = "ExactScoreWithFileNamePriority (with positions)")]
    public int ExactScoreWithFileNamePriority_WithPositions()
    {
        int total = 0;
        foreach (var (rel, fnStart) in s_paths)
            total += FuzzyScorer.ExactScoreWithFileNamePriority(ExactQuery.AsSpan(), rel.AsSpan(), fnStart, caseSensitive: false, out _);
        return total;
    }

    [Benchmark(Description = "Score (score only)")]
    public int Score_NoOut()
    {
        int total = 0;
        foreach (var (rel, _) in s_paths)
            total += FuzzyScorer.Score(FuzzyQuery.AsSpan(), rel.AsSpan());
        return total;
    }

    [Benchmark(Description = "Score (with positions)")]
    public int Score_WithPositions()
    {
        int total = 0;
        foreach (var (rel, _) in s_paths)
            total += FuzzyScorer.Score(FuzzyQuery.AsSpan(), rel.AsSpan(), out _);
        return total;
    }
}
