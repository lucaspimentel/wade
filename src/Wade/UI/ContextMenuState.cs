namespace Wade.UI;

internal sealed class ContextMenuState(ActionMenuItem[] items, int anchorRow, int anchorCol)
{
    public ActionMenuItem[] Items { get; } = items;

    public int AnchorRow { get; } = anchorRow;

    public int AnchorCol { get; } = anchorCol;

    public int SelectedIndex { get; set; }

    public void MoveUp()
    {
        if (Items.Length == 0)
        {
            return;
        }

        SelectedIndex = SelectedIndex <= 0 ? Items.Length - 1 : SelectedIndex - 1;
    }

    public void MoveDown()
    {
        if (Items.Length == 0)
        {
            return;
        }

        SelectedIndex = SelectedIndex >= Items.Length - 1 ? 0 : SelectedIndex + 1;
    }
}
