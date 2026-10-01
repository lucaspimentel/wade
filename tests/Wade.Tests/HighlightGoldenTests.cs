using System.Globalization;
using System.Text;
using Wade.Highlighting;
using Wade.Highlighting.Languages;
using Wade.Terminal;

namespace Wade.Tests;

/// <summary>
/// Shared syntax-highlighting goldens: tests/golden/highlight/*.cases hold
/// inputs, this test writes C#'s exact output to *.golden.txt, and
/// src/wade-rs/tests/highlight_golden.rs reproduces it.
/// </summary>
public class HighlightGoldenTests
{
    private static readonly string GoldenDir = FindGoldenDir();

    public static TheoryData<string> CaseFiles =>
        new([.. Directory.GetFiles(GoldenDir, "*.cases")
            .Select(Path.GetFileName)
            .Where(name => name != "language-map.cases")
            .Cast<string>()]);

    [Theory]
    [MemberData(nameof(CaseFiles))]
    public void HighlightCases_MatchGolden(string casesFileName)
    {
        string[] lines = File.ReadAllLines(Path.Combine(GoldenDir, casesFileName));
        string target = "";
        var cases = new List<List<string>>();

        foreach (string line in lines)
        {
            if (cases.Count == 0 && line.StartsWith("file: ", StringComparison.Ordinal))
            {
                target = line["file: ".Length..];
            }
            else if (line == "%%%%")
            {
                cases.Add([]);
            }
            else if (cases.Count > 0)
            {
                cases[^1].Add(line);
            }
        }

        var sb = new StringBuilder();

        for (int c = 0; c < cases.Count; c++)
        {
            sb.Append("%%%% case ").Append(c).Append('\n');
            StyledLine[] styled = Highlight([.. cases[c]], target);

            foreach (StyledLine styledLine in styled)
            {
                AppendLine(sb, styledLine);
            }
        }

        AssertGolden(Path.ChangeExtension(Path.Combine(GoldenDir, casesFileName), ".golden.txt"), sb.ToString());
    }

    [Fact]
    public void LanguageMapCases_MatchGolden()
    {
        var sb = new StringBuilder();

        foreach (string line in File.ReadAllLines(Path.Combine(GoldenDir, "language-map.cases")))
        {
            if (line.Length == 0 || line.StartsWith('#'))
            {
                continue;
            }

            ILanguage? lang = LanguageMap.GetLanguage(line);
            sb.Append(line).Append(" => ").Append(lang?.GetType().Name ?? "none").Append('\n');
        }

        AssertGolden(Path.Combine(GoldenDir, "language-map.golden.txt"), sb.ToString());
    }

    private static StyledLine[] Highlight(string[] lines, string target)
    {
        if (target != "diff")
        {
            return SyntaxHighlighter.Highlight(lines, target);
        }

        // The diff preview tokenizes with DiffLanguage directly (not via LanguageMap)
        var diff = new DiffLanguage();
        byte state = 0;
        var result = new StyledLine[lines.Length];
        for (int i = 0; i < lines.Length; i++)
        {
            result[i] = diff.TokenizeLine(lines[i], ref state);
        }

        return result;
    }

    private static void AppendLine(StringBuilder sb, StyledLine line)
    {
        sb.Append("text ").Append(Escape(line.Text)).Append('\n');

        if (line.Spans is { } spans)
        {
            sb.Append("spans");
            foreach (StyledSpan span in spans)
            {
                sb.Append(' ').Append(span.Start).Append('+').Append(span.Length).Append(':').Append(span.Kind);
            }

            sb.Append('\n');
        }

        if (line.CharStyles is { } styles)
        {
            // Run-length encoded: start+length:style
            sb.Append("chars");
            int i = 0;
            while (i < styles.Length)
            {
                int j = i + 1;
                while (j < styles.Length && styles[j] == styles[i])
                {
                    j++;
                }

                sb.Append(' ').Append(i).Append('+').Append(j - i).Append(':').Append(FormatStyle(styles[i]));
                i = j;
            }

            sb.Append('\n');
        }
    }

    private static string FormatStyle(CellStyle style)
    {
        static string FormatColor(Color? color) =>
            color is { } c ? string.Create(CultureInfo.InvariantCulture, $"{c.R},{c.G},{c.B}") : "-";

        var flags = new StringBuilder();
        if (style.Bold) flags.Append('b');
        if (style.Dim) flags.Append('d');
        if (style.Inverse) flags.Append('i');
        if (style.Underline) flags.Append('u');
        if (style.Strikethrough) flags.Append('s');

        return $"{FormatColor(style.Fg)}/{FormatColor(style.Bg)}/{(flags.Length > 0 ? flags : "-")}";
    }

    /// <summary>Escapes backslash and non-printable characters as \uXXXX.</summary>
    private static string Escape(string text)
    {
        var sb = new StringBuilder();
        foreach (char ch in text)
        {
            if (ch == '\\')
            {
                sb.Append(@"\\");
            }
            else if (ch < ' ' || ch == '\u007f')
            {
                sb.Append(string.Create(CultureInfo.InvariantCulture, $"\\u{(int)ch:x4}"));
            }
            else
            {
                sb.Append(ch);
            }
        }

        return sb.ToString();
    }

    private static void AssertGolden(string goldenPath, string actual)
    {
        if (Environment.GetEnvironmentVariable("WADE_UPDATE_GOLDENS") == "1" && !File.Exists(goldenPath))
        {
            File.WriteAllText(goldenPath, actual, new UTF8Encoding(false));
        }

        Assert.True(File.Exists(goldenPath), $"Missing golden file {goldenPath}; run with WADE_UPDATE_GOLDENS=1 to generate");
        Assert.Equal(File.ReadAllText(goldenPath).ReplaceLineEndings("\n"), actual);
    }

    private static string FindGoldenDir()
    {
        string? dir = AppContext.BaseDirectory;
        while (dir is not null)
        {
            string candidate = Path.Combine(dir, "tests", "golden", "highlight");
            if (Directory.Exists(candidate))
            {
                return candidate;
            }

            dir = Path.GetDirectoryName(dir.TrimEnd(Path.DirectorySeparatorChar));
        }

        throw new InvalidOperationException("Could not locate tests/golden/highlight directory");
    }
}
