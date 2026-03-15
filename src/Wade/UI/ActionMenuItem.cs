using Wade.Terminal;

namespace Wade.UI;

internal sealed class ActionMenuItem
{
    public required string Label { get; init; }
    public string Shortcut { get; init; } = "";
    public AppAction Action { get; init; }
    public int Data { get; init; }
    public ActionMenuItem[]? SubItems { get; init; }
    public bool IsSubmenu => SubItems is not null;
}
