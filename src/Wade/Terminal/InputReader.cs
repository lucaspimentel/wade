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
    ShowHelp,
    Refresh,
    Search,
    ToggleHiddenFiles,
}

internal static class InputReader
{
    public static AppAction MapKey(KeyEvent key)
    {
        if (key.KeyChar == '?')
            return AppAction.ShowHelp;

        if (key.KeyChar == '/')
            return AppAction.Search;

        if (key.KeyChar == '.')
            return AppAction.ToggleHiddenFiles;

        if (key.Key == ConsoleKey.R && key.Control)
            return AppAction.Refresh;

        return key.Key switch
        {
            ConsoleKey.UpArrow or ConsoleKey.K => AppAction.NavigateUp,
            ConsoleKey.DownArrow or ConsoleKey.J => AppAction.NavigateDown,
            ConsoleKey.RightArrow or ConsoleKey.Enter or ConsoleKey.L => AppAction.Open,
            ConsoleKey.LeftArrow or ConsoleKey.Backspace or ConsoleKey.H => AppAction.Back,
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
