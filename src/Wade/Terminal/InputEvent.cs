namespace Wade.Terminal;

internal abstract record InputEvent;

internal sealed record KeyEvent(ConsoleKey Key, char KeyChar, bool Shift, bool Alt, bool Control) : InputEvent;

internal sealed record MouseEvent(MouseButton Button, int Row, int Col, bool IsRelease) : InputEvent;

internal sealed record ResizeEvent(int Width, int Height) : InputEvent;

internal enum MouseButton
{
    Left,
    Middle,
    Right,
    ScrollUp,
    ScrollDown,
    None,
}
