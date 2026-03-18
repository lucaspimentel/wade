namespace Wade.UI;

internal sealed class ConfigItem
{
    public required string Label { get; init; }
    public int Indent { get; init; }
    public required Func<string> FormatValue { get; init; }
    public required Action Toggle { get; init; }
    public Func<bool>? EnabledWhen { get; init; }
    public bool IsEnabled => EnabledWhen?.Invoke() ?? true;
    public Action? CycleNext { get; init; }
    public Action? CyclePrev { get; init; }
    public bool IsCycleable => CycleNext is not null;
}
