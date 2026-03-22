using Wade.Terminal;

namespace Wade.Highlighting.Languages;

internal sealed class DiffLanguage : ILanguage
{
    private static readonly CellStyle s_addedStyle = new(new Color(80, 200, 80), null); // green
    private static readonly CellStyle s_removedStyle = new(new Color(220, 80, 80), null); // red
    private static readonly CellStyle s_hunkHeaderStyle = new(new Color(80, 180, 220), null, Dim: true); // cyan/dim
    private static readonly CellStyle s_metadataStyle = new(new Color(140, 140, 140), null, Dim: true); // dim gray

    public StyledLine TokenizeLine(string line, ref byte state)
    {
        if (line.Length == 0)
        {
            return new StyledLine(line, null);
        }

        CellStyle? style = line[0] switch
        {
            '+' => line.StartsWith("+++") ? s_metadataStyle : s_addedStyle,
            '-' => line.StartsWith("---") ? s_metadataStyle : s_removedStyle,
            '@' when line.StartsWith("@@") => s_hunkHeaderStyle,
            'd' when line.StartsWith("diff ") => s_metadataStyle,
            'i' when line.StartsWith("index ") => s_metadataStyle,
            _ => null,
        };

        if (style is null)
        {
            return new StyledLine(line, null);
        }

        var charStyles = new CellStyle[line.Length];
        Array.Fill(charStyles, style.Value);
        return new StyledLine(line, null, charStyles);
    }
}
