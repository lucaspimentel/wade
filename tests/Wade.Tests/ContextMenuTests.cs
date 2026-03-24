using Wade.Terminal;
using Wade.UI;

namespace Wade.Tests;

public sealed class ContextMenuTests
{
    [Fact]
    public void MoveDown_WrapsToStart()
    {
        ActionMenuItem[] items =
        [
            new() { Label = "A", Action = AppAction.Copy },
            new() { Label = "B", Action = AppAction.Cut },
            new() { Label = "C", Action = AppAction.Paste },
        ];

        var state = new ContextMenuState(items, 0, 0) { SelectedIndex = 2 };

        state.MoveDown();

        Assert.Equal(0, state.SelectedIndex);
    }

    [Fact]
    public void MoveUp_WrapsToEnd()
    {
        ActionMenuItem[] items =
        [
            new() { Label = "A", Action = AppAction.Copy },
            new() { Label = "B", Action = AppAction.Cut },
            new() { Label = "C", Action = AppAction.Paste },
        ];

        var state = new ContextMenuState(items, 0, 0) { SelectedIndex = 0 };

        state.MoveUp();

        Assert.Equal(2, state.SelectedIndex);
    }

    [Fact]
    public void MoveDown_AdvancesNormally()
    {
        ActionMenuItem[] items =
        [
            new() { Label = "A", Action = AppAction.Copy },
            new() { Label = "B", Action = AppAction.Cut },
        ];

        var state = new ContextMenuState(items, 0, 0) { SelectedIndex = 0 };

        state.MoveDown();

        Assert.Equal(1, state.SelectedIndex);
    }

    [Fact]
    public void MoveUp_AdvancesNormally()
    {
        ActionMenuItem[] items =
        [
            new() { Label = "A", Action = AppAction.Copy },
            new() { Label = "B", Action = AppAction.Cut },
        ];

        var state = new ContextMenuState(items, 0, 0) { SelectedIndex = 1 };

        state.MoveUp();

        Assert.Equal(0, state.SelectedIndex);
    }

    [Fact]
    public void MoveDown_EmptyItems_DoesNothing()
    {
        var state = new ContextMenuState([], 0, 0);

        state.MoveDown();

        Assert.Equal(0, state.SelectedIndex);
    }

    [Fact]
    public void MoveUp_EmptyItems_DoesNothing()
    {
        var state = new ContextMenuState([], 0, 0);

        state.MoveUp();

        Assert.Equal(0, state.SelectedIndex);
    }

    // ── ContextMenuRenderer.GetMenuRect Clamping ─────────────────────────────

    [Fact]
    public void GetMenuRect_AnchorFitsOnScreen_ReturnsAnchorPosition()
    {
        ActionMenuItem[] items =
        [
            new() { Label = "Copy", Shortcut = "c", Action = AppAction.Copy },
            new() { Label = "Paste", Shortcut = "v", Action = AppAction.Paste },
        ];

        var state = new ContextMenuState(items, 5, 10);

        Rect rect = ContextMenuRenderer.GetMenuRect(80, 24, state);

        Assert.Equal(10, rect.Left);
        Assert.Equal(5, rect.Top);
    }

    [Fact]
    public void GetMenuRect_AnchorNearRightEdge_ClampsLeft()
    {
        ActionMenuItem[] items =
        [
            new() { Label = "Copy", Shortcut = "c", Action = AppAction.Copy },
            new() { Label = "Paste", Shortcut = "v", Action = AppAction.Paste },
        ];

        var state = new ContextMenuState(items, 5, 75);

        Rect rect = ContextMenuRenderer.GetMenuRect(80, 24, state);

        // Box should not extend past screen width
        Assert.True(rect.Right <= 80);
    }

    [Fact]
    public void GetMenuRect_AnchorNearBottomEdge_ClampsUp()
    {
        ActionMenuItem[] items =
        [
            new() { Label = "Copy", Shortcut = "c", Action = AppAction.Copy },
            new() { Label = "Paste", Shortcut = "v", Action = AppAction.Paste },
        ];

        var state = new ContextMenuState(items, 22, 10);

        Rect rect = ContextMenuRenderer.GetMenuRect(80, 24, state);

        // Box should not extend past screen height
        Assert.True(rect.Bottom <= 24);
    }

    [Fact]
    public void GetMenuRect_AnchorAtBottomRightCorner_ClampsToFit()
    {
        ActionMenuItem[] items =
        [
            new() { Label = "Copy", Shortcut = "c", Action = AppAction.Copy },
            new() { Label = "Paste", Shortcut = "v", Action = AppAction.Paste },
        ];

        var state = new ContextMenuState(items, 23, 79);

        Rect rect = ContextMenuRenderer.GetMenuRect(80, 24, state);

        Assert.True(rect.Right <= 80);
        Assert.True(rect.Bottom <= 24);
        Assert.True(rect.Left >= 0);
        Assert.True(rect.Top >= 0);
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(10, 10)]
    public void GetMenuRect_Height_EqualsItemCountPlusBorders(int anchorRow, int anchorCol)
    {
        ActionMenuItem[] items =
        [
            new() { Label = "A", Action = AppAction.Copy },
            new() { Label = "B", Action = AppAction.Cut },
            new() { Label = "C", Action = AppAction.Paste },
        ];

        var state = new ContextMenuState(items, anchorRow, anchorCol);

        Rect rect = ContextMenuRenderer.GetMenuRect(80, 24, state);

        Assert.Equal(items.Length + 2, rect.Height);
    }
}
