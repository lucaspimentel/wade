using System.Globalization;

namespace Wade.UI;

internal static class FormatHelpers
{
    public static int FormatSize(Span<char> buf, long bytes)
    {
        if (bytes < 1024)
        {
            if (!bytes.TryFormat(buf, out int n))
            {
                return 0;
            }

            return TryAppend(buf, n, " B");
        }
        if (bytes < 1024 * 1024)
        {
            if (!(bytes / 1024.0).TryFormat(buf, out int n, "F1"))
            {
                return 0;
            }

            return TryAppend(buf, n, " KB");
        }
        if (bytes < 1024L * 1024 * 1024)
        {
            if (!(bytes / (1024.0 * 1024.0)).TryFormat(buf, out int n, "F1"))
            {
                return 0;
            }

            return TryAppend(buf, n, " MB");
        }
        {
            if (!(bytes / (1024.0 * 1024.0 * 1024.0)).TryFormat(buf, out int n, "F1"))
            {
                return 0;
            }

            return TryAppend(buf, n, " GB");
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

    private static int TryAppend(Span<char> buf, int offset, ReadOnlySpan<char> suffix)
    {
        if (suffix.TryCopyTo(buf[offset..]))
        {
            return offset + suffix.Length;
        }

        return 0;
    }
}
