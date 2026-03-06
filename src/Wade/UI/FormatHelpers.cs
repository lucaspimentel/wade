using System.Globalization;

namespace Wade.UI;

internal static class FormatHelpers
{
    public static int FormatSize(Span<char> buf, long bytes)
    {
        if (bytes < 1024)
        {
            bytes.TryFormat(buf, out int n);
            " B".AsSpan().CopyTo(buf[n..]);
            return n + 2;
        }
        if (bytes < 1024 * 1024)
        {
            (bytes / 1024.0).TryFormat(buf, out int n, "F1");
            " KB".AsSpan().CopyTo(buf[n..]);
            return n + 3;
        }
        if (bytes < 1024L * 1024 * 1024)
        {
            (bytes / (1024.0 * 1024.0)).TryFormat(buf, out int n, "F1");
            " MB".AsSpan().CopyTo(buf[n..]);
            return n + 3;
        }
        {
            (bytes / (1024.0 * 1024.0 * 1024.0)).TryFormat(buf, out int n, "F1");
            " GB".AsSpan().CopyTo(buf[n..]);
            return n + 3;
        }
    }

    public static int FormatDate(Span<char> buf, DateTime dt, int maxWidth)
    {
        if (maxWidth >= 19)
        {
            // Full: "yyyy-MM-dd hh:mm tt"
            return dt.TryFormat(buf, out int n, "yyyy-MM-dd hh:mm tt", CultureInfo.InvariantCulture) ? n : 0;
        }
        if (maxWidth >= 10)
        {
            // Date only: "yyyy-MM-dd"
            return dt.TryFormat(buf, out int n, "yyyy-MM-dd", CultureInfo.InvariantCulture) ? n : 0;
        }
        if (maxWidth >= 6)
        {
            // Short: "MMM dd"
            return dt.TryFormat(buf, out int n, "MMM dd", CultureInfo.InvariantCulture) ? n : 0;
        }
        return 0;
    }
}
