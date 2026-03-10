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
    ToggleMark,
    CycleSortMode,
    ToggleSortDirection,
    GoToPath,
    OpenExternal,
    Rename,
    Delete,
    DeletePermanently,
    Copy,
    Cut,
    Paste,
    NewFile,
    NewDirectory,
    ShowConfig,
    OpenTerminal,
    QuitNoCd,
    ShowProperties,
    ShowActionPalette,
}

internal static class InputReader
{
    public static AppAction MapKey(KeyEvent key)
    {
        if (key.Key == ConsoleKey.P && key.Control && !key.Shift)
        {
            return AppAction.ShowActionPalette;
        }

        if (key.KeyChar == '?')
        {
            return AppAction.ShowHelp;
        }

        if (key.KeyChar == '/')
        {
            return AppAction.Search;
        }

        if (key.KeyChar == '.')
        {
            return AppAction.ToggleHiddenFiles;
        }

        if (key.KeyChar == 's')
        {
            return AppAction.CycleSortMode;
        }

        if (key.KeyChar == 'S')
        {
            return AppAction.ToggleSortDirection;
        }

        if (key.KeyChar == 'Q')
        {
            return AppAction.QuitNoCd;
        }

        if (key.KeyChar == 'q')
        {
            return AppAction.Quit;
        }

        if (key.KeyChar == ',')
        {
            return AppAction.ShowConfig;
        }

        if (key.KeyChar == 'i')
        {
            return AppAction.ShowProperties;
        }

        if (key.KeyChar == 'g')
        {
            return AppAction.GoToPath;
        }

        if (key.KeyChar == 'o')
        {
            return AppAction.OpenExternal;
        }

        if (key.Key == ConsoleKey.Spacebar)
        {
            return AppAction.ToggleMark;
        }

        if (key.Key == ConsoleKey.R && key.Control)
        {
            return AppAction.Refresh;
        }

        if (key.Key == ConsoleKey.T && key.Control)
        {
            return AppAction.OpenTerminal;
        }

        if (key.KeyChar == 'c' || (key.Key == ConsoleKey.C && key.Control))
        {
            return AppAction.Copy;
        }

        if (key.KeyChar == 'x' || (key.Key == ConsoleKey.X && key.Control))
        {
            return AppAction.Cut;
        }

        if (key.KeyChar is 'p' or 'v')
        {
            return AppAction.Paste;
        }

        if (key.KeyChar == 'N')
        {
            return AppAction.NewFile;
        }

        if (key.Key == ConsoleKey.F7)
        {
            return AppAction.NewDirectory;
        }

        if (key.Key == ConsoleKey.F2)
        {
            return AppAction.Rename;
        }

        if (key.Key is ConsoleKey.Delete)
        {
            return key.Shift ? AppAction.DeletePermanently : AppAction.Delete;
        }

        return key.Key switch
        {
            ConsoleKey.UpArrow or ConsoleKey.K => AppAction.NavigateUp,
            ConsoleKey.DownArrow or ConsoleKey.J => AppAction.NavigateDown,
            ConsoleKey.RightArrow or ConsoleKey.Enter or ConsoleKey.L => AppAction.Open,
            ConsoleKey.LeftArrow or ConsoleKey.Backspace or ConsoleKey.H => AppAction.Back,
            ConsoleKey.Escape => AppAction.Quit,
            ConsoleKey.PageUp => AppAction.PageUp,
            ConsoleKey.PageDown => AppAction.PageDown,
            ConsoleKey.Home => AppAction.Home,
            ConsoleKey.End => AppAction.End,
            _ => AppAction.None,
        };
    }
}
