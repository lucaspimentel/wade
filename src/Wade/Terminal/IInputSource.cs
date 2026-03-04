namespace Wade.Terminal;

internal interface IInputSource : IDisposable
{
    InputEvent? ReadNext(CancellationToken ct);
}
