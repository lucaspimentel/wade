namespace Wade.Terminal;

internal enum AppAction
{
    None,
    NavigateUp,
    NavigateDown,
    Open,
    Back,
    Quit,
    PageUp,
    PageDown,
    Home,
    End,
}

internal static class InputReader
{
    public static AppAction Read()
    {
        var key = Console.ReadKey(intercept: true);

        return key.Key switch
        {
            ConsoleKey.UpArrow or ConsoleKey.K => AppAction.NavigateUp,
            ConsoleKey.DownArrow or ConsoleKey.J => AppAction.NavigateDown,
            ConsoleKey.RightArrow or ConsoleKey.Enter => AppAction.Open,
            ConsoleKey.LeftArrow or ConsoleKey.Backspace => AppAction.Back,
            ConsoleKey.Escape => AppAction.Quit,
            ConsoleKey.Q => AppAction.Quit,
            ConsoleKey.PageUp => AppAction.PageUp,
            ConsoleKey.PageDown => AppAction.PageDown,
            ConsoleKey.Home => AppAction.Home,
            ConsoleKey.End => AppAction.End,
            _ => AppAction.None,
        };
    }
}
