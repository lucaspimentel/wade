namespace Wade.UI;

internal sealed class ActionMenuLevel
{
    public ActionMenuLevel(string title, ActionMenuItem[] items)
    {
        Title = title;
        Items = items;
        Filter = new TextInput();
    }

    public string Title { get; }

    public ActionMenuItem[] Items { get; }

    public TextInput Filter { get; }

    public int SelectedIndex { get; set; }

    public int ScrollOffset { get; set; }

    public List<ActionMenuItem> GetFilteredItems()
    {
        string filter = Filter.Value;

        if (string.IsNullOrEmpty(filter))
        {
            return [.. Items];
        }

        var result = new List<ActionMenuItem>();

        foreach (ActionMenuItem item in Items)
        {
            if (item.Label.Contains(filter, StringComparison.OrdinalIgnoreCase))
            {
                result.Add(item);
            }
        }

        return result;
    }
}
