using Wade.Highlighting;
using Wade.Preview;
using Wade.UI;

namespace Wade.Tests;

public class MetadataRendererTests
{
    [Fact]
    public void Render_SingleSection_ProducesHeaderAndEntries()
    {
        MetadataSection[] sections =
        [
            new("Test Section", [new MetadataEntry("Key", "Value"), new MetadataEntry("Name", "Test")]),
        ];

        StyledLine[] lines = MetadataRenderer.Render(sections);

        Assert.True(lines.Length >= 4); // header + divider + 2 entries
        Assert.Contains("Test Section", lines[0].Text);
        Assert.Contains('\u2500', lines[1].Text); // divider character
        Assert.Contains("Key", lines[2].Text);
        Assert.Contains("Value", lines[2].Text);
        Assert.Contains("Name", lines[3].Text);
        Assert.Contains("Test", lines[3].Text);
    }

    [Fact]
    public void Render_MultipleSections_HasBlankLineSeparator()
    {
        MetadataSection[] sections =
        [
            new("First", [new MetadataEntry("A", "1")]),
            new("Second", [new MetadataEntry("B", "2")]),
        ];

        StyledLine[] lines = MetadataRenderer.Render(sections);

        // Find blank line between sections
        bool foundBlank = false;
        for (int i = 1; i < lines.Length; i++)
        {
            if (lines[i].Text == "")
            {
                foundBlank = true;
                break;
            }
        }

        Assert.True(foundBlank, "Expected blank line separator between sections");
    }

    [Fact]
    public void Render_EmptyLabelEntry_RendersAsListItem()
    {
        MetadataSection[] sections =
        [
            new("Items", [new MetadataEntry("", "item one"), new MetadataEntry("", "item two")]),
        ];

        StyledLine[] lines = MetadataRenderer.Render(sections);

        // List items should be indented with 4 spaces
        var itemLines = lines.Where(l => l.Text.Contains("item")).ToList();
        Assert.Equal(2, itemLines.Count);
        Assert.StartsWith("    ", itemLines[0].Text);
        Assert.StartsWith("    ", itemLines[1].Text);
    }

    [Fact]
    public void Render_NullHeader_SkipsHeaderAndDivider()
    {
        MetadataSection[] sections =
        [
            new(null, [new MetadataEntry("Key", "Value")]),
        ];

        StyledLine[] lines = MetadataRenderer.Render(sections);

        // Should only have the entry line, no header or divider
        Assert.Single(lines);
        Assert.Contains("Key", lines[0].Text);
        Assert.Contains("Value", lines[0].Text);
    }

    [Fact]
    public void Render_EntriesHaveCharStyles()
    {
        MetadataSection[] sections =
        [
            new("Section", [new MetadataEntry("Label", "Value")]),
        ];

        StyledLine[] lines = MetadataRenderer.Render(sections);

        // Header and entry lines should have CharStyles set
        foreach (StyledLine line in lines)
        {
            Assert.NotNull(line.CharStyles);
            Assert.Equal(line.Text.Length, line.CharStyles!.Length);
        }
    }
}
