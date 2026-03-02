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

    public static string MoveCursor(int row, int col) => $"{Csi}{row + 1};{col + 1}H";

    public static string SetFg(byte r, byte g, byte b) => $"{Csi}38;2;{r};{g};{b}m";

    public static string SetBg(byte r, byte g, byte b) => $"{Csi}48;2;{r};{g};{b}m";

    public static string SetFg256(byte index) => $"{Csi}38;5;{index}m";

    public static string SetBg256(byte index) => $"{Csi}48;5;{index}m";
}
