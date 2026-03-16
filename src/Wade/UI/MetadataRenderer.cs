using Wade.Highlighting;
using Wade.Preview;
using Wade.Terminal;

namespace Wade.UI;

internal static class MetadataRenderer
{
    private static readonly Color LabelColor = new(120, 120, 140);
    private static readonly Color ValueColor = new(200, 200, 200);
    private static readonly Color HeaderColor = new(180, 180, 200);
    private static readonly Color DividerColor = new(80, 80, 100);

    public static StyledLine[] Render(MetadataSection[] sections, int maxWidth = 0)
    {
        var lines = new List<StyledLine>();

        // Determine label column width across all sections
        int labelWidth = 0;
        foreach (MetadataSection section in sections)
        {
            foreach (MetadataEntry entry in section.Entries)
            {
                if (entry.Label.Length > 0 && entry.Label.Length > labelWidth)
                {
                    labelWidth = entry.Label.Length;
                }
            }
        }

        // Add padding after label
        labelWidth += 2;

        for (int s = 0; s < sections.Length; s++)
        {
            MetadataSection section = sections[s];

            // Blank line between sections (but not before the first)
            if (s > 0)
            {
                lines.Add(new StyledLine("", null));
            }

            // Section header
            if (section.Header is not null)
            {
                string headerText = $"  {section.Header}";
                var headerStyles = new CellStyle[headerText.Length];
                var headerStyle = new CellStyle(HeaderColor, null, Bold: true);
                Array.Fill(headerStyles, headerStyle);
                lines.Add(new StyledLine(headerText, null, headerStyles));

                string divider = "  " + new string('\u2500', Math.Min(section.Header.Length + 4, maxWidth > 4 ? maxWidth - 4 : 20));
                var dividerStyles = new CellStyle[divider.Length];
                Array.Fill(dividerStyles, new CellStyle(DividerColor, null));
                lines.Add(new StyledLine(divider, null, dividerStyles));
            }

            // Entries
            foreach (MetadataEntry entry in section.Entries)
            {
                if (entry.Label.Length == 0)
                {
                    // List item (no label) — indented value
                    string text = $"    {entry.Value}";
                    var styles = new CellStyle[text.Length];
                    Array.Fill(styles, new CellStyle(ValueColor, null));
                    lines.Add(new StyledLine(text, null, styles));
                }
                else
                {
                    string labelPart = "  " + entry.Label.PadRight(labelWidth);
                    string valuePart = entry.Value;
                    string text = labelPart + valuePart;

                    var styles = new CellStyle[text.Length];
                    var labelStyle = new CellStyle(LabelColor, null, Dim: true);
                    var valueStyle = new CellStyle(ValueColor, null);

                    for (int i = 0; i < labelPart.Length && i < styles.Length; i++)
                    {
                        styles[i] = labelStyle;
                    }

                    for (int i = labelPart.Length; i < styles.Length; i++)
                    {
                        styles[i] = valueStyle;
                    }

                    lines.Add(new StyledLine(text, null, styles));
                }
            }
        }

        return lines.ToArray();
    }
}
