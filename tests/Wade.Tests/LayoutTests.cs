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

    [Theory]
    [InlineData(120, 40)]
    [InlineData(80, 24)]
    [InlineData(200, 60)]
    public void Calculate_ExpandedPaneCoversFullWidthAndContentHeight(int width, int height)
    {
        var layout = new Layout();
        layout.Calculate(width, height);

        Assert.Equal(0, layout.ExpandedPane.Left);
        Assert.Equal(0, layout.ExpandedPane.Top);
        Assert.Equal(width, layout.ExpandedPane.Width);
        Assert.Equal(height - 1, layout.ExpandedPane.Height);
    }

    [Fact]
    public void CenterContent_SmallerThanPane_CentersContent()
    {
        var rect = new Rect(10, 5, 40, 30);
        (int row, int col) = rect.CenterContent(20, 10);

        Assert.Equal(15, row); // 5 + (30 - 10) / 2
        Assert.Equal(20, col); // 10 + (40 - 20) / 2
    }

    [Fact]
    public void CenterContent_SameSizeAsPane_ReturnsTopLeft()
    {
        var rect = new Rect(10, 5, 40, 30);
        (int row, int col) = rect.CenterContent(40, 30);

        Assert.Equal(5, row);
        Assert.Equal(10, col);
    }

    [Theory]
    [InlineData(120, 40)]
    [InlineData(80, 24)]
    [InlineData(200, 60)]
    public void Calculate_PreviewDisabled_HidesRightPane(int width, int height)
    {
        var layout = new Layout();
        layout.Calculate(width, height, previewPaneEnabled: false);

        // Right pane should have zero width
        Assert.Equal(0, layout.RightPane.Width);

        // Left and center should still have positive dimensions
        Assert.True(layout.LeftPane.Width > 0);
        Assert.True(layout.CenterPane.Width > 0);

        // Only 1 border column (between left and center)
        Assert.True(layout.CenterPane.Left > layout.LeftPane.Right);

        // Center pane should extend to terminal width
        Assert.Equal(width, layout.CenterPane.Right);

        // Status bar still at the bottom
        Assert.Equal(height - 1, layout.StatusBar.Top);
    }

    [Fact]
    public void CenterContent_LargerThanPane_ClampsToTopLeft()
    {
        var rect = new Rect(10, 5, 40, 30);
        (int row, int col) = rect.CenterContent(60, 50);

        Assert.Equal(5, row);
        Assert.Equal(10, col);
    }
}
