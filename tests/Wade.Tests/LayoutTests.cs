using Wade.UI;

namespace Wade.Tests;

public class LayoutTests
{
    [Theory]
    [InlineData(120, 40)]
    [InlineData(80, 24)]
    [InlineData(200, 60)]
    public void Calculate_ProducesNonOverlappingPanes(int width, int height)
    {
        var layout = new Layout();
        layout.Calculate(width, height);

        // All panes should have positive dimensions
        Assert.True(layout.LeftPane.Width > 0);
        Assert.True(layout.CenterPane.Width > 0);
        Assert.True(layout.RightPane.Width > 0);

        // Panes should not overlap horizontally (borders at LeftPane.Right and CenterPane.Right)
        Assert.True(layout.CenterPane.Left > layout.LeftPane.Right);
        Assert.True(layout.RightPane.Left > layout.CenterPane.Right);

        // Right pane should fit within terminal width
        Assert.True(layout.RightPane.Right <= width);

        // Status bar at the bottom
        Assert.Equal(height - 1, layout.StatusBar.Top);
        Assert.Equal(width, layout.StatusBar.Width);
    }

    [Fact]
    public void Calculate_ContentHeightExcludesStatusBar()
    {
        var layout = new Layout();
        layout.Calculate(100, 30);

        Assert.Equal(29, layout.LeftPane.Height);
        Assert.Equal(29, layout.CenterPane.Height);
        Assert.Equal(29, layout.RightPane.Height);
    }
}
