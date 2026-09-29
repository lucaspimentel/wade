using System.Text;

namespace Wade.Tests;

public class PaneRendererGoldenTests
{
    private static readonly string GoldenDir = FindGoldenDir();

    public static TheoryData<string> FixtureFiles =>
        new([.. Directory.GetFiles(GoldenDir, "*.scn").Select(Path.GetFileName).Cast<string>()]);

    [Theory]
    [MemberData(nameof(FixtureFiles))]
    public void RendererFixture_MatchesCSharpOutput(string fixtureFileName)
    {
        string fixturePath = Path.Combine(GoldenDir, fixtureFileName);
        string goldenPath = Path.ChangeExtension(fixturePath, ".golden.txt");
        string actual = RendererFixtureRunner.Run(fixturePath);

        if (Environment.GetEnvironmentVariable("WADE_UPDATE_GOLDENS") == "1" && !File.Exists(goldenPath))
        {
            File.WriteAllText(goldenPath, actual, new UTF8Encoding(false));
        }

        Assert.True(File.Exists(goldenPath), $"Missing golden file {goldenPath}; run with WADE_UPDATE_GOLDENS=1 to generate");
        string expected = File.ReadAllText(goldenPath);
        if (actual == "PLATFORM-SKIPPED")
        {
            // Platform-gated scenario: not comparable on this OS
            return;
        }

        Assert.Equal(expected, actual);
    }

    private static string FindGoldenDir()
    {
        string? dir = AppContext.BaseDirectory;
        while (dir is not null)
        {
            string candidate = Path.Combine(dir, "tests", "golden", "renderer");
            if (Directory.Exists(candidate))
            {
                return candidate;
            }

            dir = Path.GetDirectoryName(dir.TrimEnd(Path.DirectorySeparatorChar));
        }

        throw new InvalidOperationException("Could not locate tests/golden/renderer directory");
    }
}
