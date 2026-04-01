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

        if (bytes < 1024L * 1024 * 1024 * 1024)
        {
            if (!(bytes / (1024.0 * 1024.0 * 1024.0)).TryFormat(buf, out int n, "F1"))
            {
                return 0;
            }

            return TryAppend(buf, n, " GB");
        }

        {
            if (!(bytes / (1024.0 * 1024.0 * 1024.0 * 1024.0)).TryFormat(buf, out int n, "F1"))
            {
                return 0;
            }

            return TryAppend(buf, n, " TB");
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

    public static PercentBarResult FormatPercentBar(Span<char> buf, double fraction, int barWidth)
    {
        if (buf.Length < barWidth)
        {
            return new(0, 0, 0, 0);
        }

        int filledCount = (int)(fraction * barWidth + 0.5);
        filledCount = Math.Clamp(filledCount, 0, barWidth);

        for (int i = 0; i < filledCount; i++)
        {
            buf[i] = '\u2588'; // full block
        }

        for (int i = filledCount; i < barWidth; i++)
        {
            buf[i] = '\u2591'; // light shade
        }

        // Overlay percent text centered in the bar
        int percent = (int)(fraction * 100 + 0.5);
        percent = Math.Clamp(percent, 0, 100);
        Span<char> label = stackalloc char[5]; // "100%\0" max
        int labelLen = 0;
        if (percent.TryFormat(label, out labelLen))
        {
            label[labelLen++] = '%';
        }

        int labelStart = 0;
        if (labelLen > 0 && labelLen <= barWidth)
        {
            labelStart = (barWidth - labelLen) / 2;
            label[..labelLen].CopyTo(buf[labelStart..]);
        }
        else
        {
            labelLen = 0;
        }

        return new(barWidth, filledCount, labelStart, labelLen);
    }

    internal readonly record struct PercentBarResult(int Length, int FilledCount, int LabelStart, int LabelLength);

    private static int TryAppend(Span<char> buf, int offset, ReadOnlySpan<char> suffix)
    {
        if (suffix.TryCopyTo(buf[offset..]))
        {
            return offset + suffix.Length;
        }

        return 0;
    }
}
