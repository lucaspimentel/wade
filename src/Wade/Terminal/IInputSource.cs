namespace Wade.Terminal;

internal interface IInputSource : IDisposable
{
    public InputEvent? ReadNext(CancellationToken ct);
}
