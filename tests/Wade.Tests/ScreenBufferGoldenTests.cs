using System.Text;

namespace Wade.Tests;

public class ScreenBufferGoldenTests
{
    private static readonly string GoldenDir = FindGoldenDir();

    public static TheoryData<string> ScenarioFiles =>
        new([.. Directory.GetFiles(GoldenDir, "*.scn").Select(Path.GetFileName).Cast<string>()]);

    [Theory]
    [MemberData(nameof(ScenarioFiles))]
    public void GoldenFrame_MatchesCSharpOutput(string scenarioFileName)
    {
        string scenarioPath = Path.Combine(GoldenDir, scenarioFileName);
        string goldenPath = Path.ChangeExtension(scenarioPath, ".golden.txt");
        var scenario = ScreenBufferScenarioRunner.Run(scenarioPath);
        string actual = ScreenBufferScenarioRunner.FormatGolden(scenario.FlushOutputs);

        if (Environment.GetEnvironmentVariable("WADE_UPDATE_GOLDENS") == "1" && !File.Exists(goldenPath))
        {
            File.WriteAllText(goldenPath, actual, new UTF8Encoding(false));
        }

        Assert.True(File.Exists(goldenPath), $"Missing golden file {goldenPath}; run with WADE_UPDATE_GOLDENS=1 to generate");
        string expected = File.ReadAllText(goldenPath);
        Assert.Equal(expected, actual);
    }

    private static string FindGoldenDir()
    {
        string? dir = AppContext.BaseDirectory;
        while (dir is not null)
        {
            string candidate = Path.Combine(dir, "tests", "golden", "screenbuffer");
            if (Directory.Exists(candidate))
            {
                return candidate;
            }

            dir = Path.GetDirectoryName(dir.TrimEnd(Path.DirectorySeparatorChar));
        }

        throw new InvalidOperationException("Could not locate tests/golden/screenbuffer directory");
    }
}
