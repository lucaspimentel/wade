namespace Wade.Terminal;

internal static class AnsiCodes
{
    private const string Esc = "\x1b";
    private const string Csi = Esc + "[";

    // Screen
    public const string EnterAlternateScreen = Csi + "?1049h";
    public const string LeaveAlternateScreen = Csi + "?1049l";
    public const string ClearScreen = Csi + "2J";

    // Cursor
    public const string HideCursor = Csi + "?25l";
    public const string ShowCursor = Csi + "?25h";

    // Line
    public const string ClearLine = Csi + "2K";

    // Reset
    public const string ResetAttributes = Csi + "0m";

    // Mouse reporting (for future use)
    public const string EnableMouseReporting = Csi + "?1000h";
    public const string DisableMouseReporting = Csi + "?1000l";
    public const string EnableSgrMouseMode = Csi + "?1006h";
    public const string DisableSgrMouseMode = Csi + "?1006l";

    public static string MoveCursor(int row, int col) => $"{Csi}{row + 1};{col + 1}H";

    public static string SetFg(byte r, byte g, byte b) => $"{Csi}38;2;{r};{g};{b}m";

    public static string SetBg(byte r, byte g, byte b) => $"{Csi}48;2;{r};{g};{b}m";

    public static string SetFg256(byte index) => $"{Csi}38;5;{index}m";

    public static string SetBg256(byte index) => $"{Csi}48;5;{index}m";

    public static void AppendMoveCursor(System.Text.StringBuilder sb, int row, int col)
    {
        sb.Append('\x1b');
        sb.Append('[');
        sb.Append(row + 1);
        sb.Append(';');
        sb.Append(col + 1);
        sb.Append('H');
    }

    public static void AppendSetFg(System.Text.StringBuilder sb, byte r, byte g, byte b)
    {
        sb.Append('\x1b');
        sb.Append("[38;2;");
        sb.Append(r);
        sb.Append(';');
        sb.Append(g);
        sb.Append(';');
        sb.Append(b);
        sb.Append('m');
    }

    public static void AppendSetBg(System.Text.StringBuilder sb, byte r, byte g, byte b)
    {
        sb.Append('\x1b');
        sb.Append("[48;2;");
        sb.Append(r);
        sb.Append(';');
        sb.Append(g);
        sb.Append(';');
        sb.Append(b);
        sb.Append('m');
    }
}
