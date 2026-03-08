namespace Wade.Terminal;

internal sealed record TerminalCapabilities(
    bool SixelSupported,
    int CellPixelWidth,
    int CellPixelHeight)
{
    public static readonly TerminalCapabilities Default = new(false, 8, 16);

    /// <summary>
    /// Parses raw terminal query responses (DA1 and cell size) from a byte buffer.
    /// DA1 response: ESC[?Pp;Pp;...c — Sixel supported if param 4 is present.
    /// Cell size response: ESC[6;HEIGHT;WIDTHt
    /// </summary>
    public static TerminalCapabilities ParseQueryResponses(ReadOnlySpan<byte> data)
    {
        bool sixelSupported = false;
        int cellPixelWidth = 8;
        int cellPixelHeight = 16;

        int i = 0;
        while (i < data.Length)
        {
            // Look for ESC [
            if (data[i] != 0x1B || i + 1 >= data.Length || data[i + 1] != '[')
            {
                i++;
                continue;
            }

            i += 2; // skip ESC [

            if (i < data.Length && data[i] == '?')
            {
                // DA1 response: ESC[?params c
                i++; // skip '?'
                int paramStart = i;

                // Find the terminating 'c'
                while (i < data.Length && data[i] != 'c')
                    i++;

                if (i < data.Length)
                {
                    // Parse semicolon-separated params between paramStart and i
                    var paramSpan = data[paramStart..i];
                    sixelSupported = ContainsParam(paramSpan, 4);
                    i++; // skip 'c'
                }
            }
            else
            {
                // Could be cell size response: ESC[6;HEIGHT;WIDTHt
                int paramStart = i;

                // Find the terminating letter
                while (i < data.Length && data[i] is not ((byte)'t' or (byte)'c' or >= 0x40 and <= 0x7E))
                    i++;

                if (i < data.Length && data[i] == 't')
                {
                    var paramSpan = data[paramStart..i];
                    ParseCellSizeParams(paramSpan, ref cellPixelHeight, ref cellPixelWidth);
                    i++; // skip 't'
                }
                else if (i < data.Length)
                {
                    i++; // skip terminator
                }
            }
        }

        return new TerminalCapabilities(sixelSupported, cellPixelWidth, cellPixelHeight);
    }

    private static bool ContainsParam(ReadOnlySpan<byte> paramSpan, int target)
    {
        int value = 0;
        bool hasValue = false;

        for (int i = 0; i <= paramSpan.Length; i++)
        {
            if (i == paramSpan.Length || paramSpan[i] == ';')
            {
                if (hasValue && value == target)
                    return true;

                value = 0;
                hasValue = false;
            }
            else if (paramSpan[i] is >= (byte)'0' and <= (byte)'9')
            {
                value = value * 10 + (paramSpan[i] - '0');
                hasValue = true;
            }
        }

        return false;
    }

    private static void ParseCellSizeParams(ReadOnlySpan<byte> paramSpan, ref int height, ref int width)
    {
        // Expected format: 6;HEIGHT;WIDTH
        int paramIndex = 0;
        int value = 0;
        bool hasValue = false;
        int firstParam = -1;

        for (int i = 0; i <= paramSpan.Length; i++)
        {
            if (i == paramSpan.Length || paramSpan[i] == ';')
            {
                if (paramIndex == 0)
                    firstParam = hasValue ? value : -1;
                else if (paramIndex == 1 && firstParam == 6 && hasValue)
                    height = value;
                else if (paramIndex == 2 && firstParam == 6 && hasValue)
                    width = value;

                paramIndex++;
                value = 0;
                hasValue = false;
            }
            else if (paramSpan[i] is >= (byte)'0' and <= (byte)'9')
            {
                value = value * 10 + (paramSpan[i] - '0');
                hasValue = true;
            }
        }
    }
}
