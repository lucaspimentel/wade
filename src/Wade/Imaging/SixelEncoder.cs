using System.Buffers;
using System.Runtime.CompilerServices;
using System.Text;

namespace Wade.Imaging;

internal static class SixelEncoder
{
    public static string Encode(byte[] rgba, int width, int height, int maxColors = 256)
    {
        if (width <= 0 || height <= 0)
        {
            return "";
        }

        int pixelCount = width * height;

        // Step 1: Pack all pixels as RGB ints
        int[] packedPixels = ArrayPool<int>.Shared.Rent(pixelCount);
        try
        {
            for (int i = 0; i < pixelCount; i++)
            {
                int off = i * 4;
                packedPixels[i] = (rgba[off] << 16) | (rgba[off + 1] << 8) | rgba[off + 2];
            }

            // Step 2: Deduplicate by sorting + compacting (avoids HashSet)
            int[] uniqueColors = ArrayPool<int>.Shared.Rent(pixelCount);
            try
            {
                Array.Copy(packedPixels, uniqueColors, pixelCount);
                Array.Sort(uniqueColors, 0, pixelCount);
                int uniqueCount = 1;
                for (int i = 1; i < pixelCount; i++)
                {
                    if (uniqueColors[i] != uniqueColors[i - 1])
                    {
                        uniqueColors[uniqueCount++] = uniqueColors[i];
                    }
                }

                // Step 3: In-place median cut
                int[] palette = MedianCutInPlace(uniqueColors, uniqueCount, maxColors);

                // Step 4: Map pixels to palette indices
                byte[] indexed = ArrayPool<byte>.Shared.Rent(pixelCount);
                try
                {
                    var colorMap = new Dictionary<int, byte>(uniqueCount);
                    for (int i = 0; i < pixelCount; i++)
                    {
                        int color = packedPixels[i];
                        if (!colorMap.TryGetValue(color, out byte palIdx))
                        {
                            palIdx = FindNearest(color, palette);
                            colorMap[color] = palIdx;
                        }

                        indexed[i] = palIdx;
                    }

                    // Step 5: Encode sixel
                    return EncodeSixel(indexed, palette, width, height);
                }
                finally
                {
                    ArrayPool<byte>.Shared.Return(indexed);
                }
            }
            finally
            {
                ArrayPool<int>.Shared.Return(uniqueColors);
            }
        }
        finally
        {
            ArrayPool<int>.Shared.Return(packedPixels);
        }
    }

    private static int[] MedianCutInPlace(int[] colors, int count, int maxColors)
    {
        var boxes = new (int start, int length, int range, int channel)[maxColors];
        boxes[0] = MakeBox(colors, 0, count);
        int boxCount = 1;

        while (boxCount < maxColors)
        {
            // Find splittable box with largest range
            int bestIdx = -1;
            int bestRange = -1;
            for (int i = 0; i < boxCount; i++)
            {
                if (boxes[i].length < 2)
                {
                    continue;
                }

                if (boxes[i].range > bestRange)
                {
                    bestRange = boxes[i].range;
                    bestIdx = i;
                }
            }

            if (bestIdx == -1)
            {
                break;
            }

            (int start, int length, _, int channel) = boxes[bestIdx];

            // Sort this range by dominant channel
            SortByChannel(colors, start, length, channel);

            // Split at median
            int mid = length / 2;
            boxes[bestIdx] = MakeBox(colors, start, mid);
            boxes[boxCount] = MakeBox(colors, start + mid, length - mid);
            boxCount++;
        }

        // Compute average color for each box
        int[] palette = new int[boxCount];
        for (int i = 0; i < boxCount; i++)
        {
            long rSum = 0, gSum = 0, bSum = 0;
            int end = boxes[i].start + boxes[i].length;
            for (int j = boxes[i].start; j < end; j++)
            {
                rSum += (colors[j] >> 16) & 0xFF;
                gSum += (colors[j] >> 8) & 0xFF;
                bSum += colors[j] & 0xFF;
            }

            int n = boxes[i].length;
            palette[i] = ((int)(rSum / n) << 16) | ((int)(gSum / n) << 8) | (int)(bSum / n);
        }

        return palette;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static (int start, int length, int range, int channel) MakeBox(int[] colors, int start, int length)
    {
        int rMin = 255, rMax = 0, gMin = 255, gMax = 0, bMin = 255, bMax = 0;
        int end = start + length;
        for (int i = start; i < end; i++)
        {
            int c = colors[i];
            int r = (c >> 16) & 0xFF;
            int g = (c >> 8) & 0xFF;
            int b = c & 0xFF;
            if (r < rMin)
            {
                rMin = r;
            }

            if (r > rMax)
            {
                rMax = r;
            }

            if (g < gMin)
            {
                gMin = g;
            }

            if (g > gMax)
            {
                gMax = g;
            }

            if (b < bMin)
            {
                bMin = b;
            }

            if (b > bMax)
            {
                bMax = b;
            }
        }

        int rRange = rMax - rMin;
        int gRange = gMax - gMin;
        int bRange = bMax - bMin;

        if (rRange >= gRange && rRange >= bRange)
        {
            return (start, length, rRange, 0);
        }

        if (gRange >= rRange && gRange >= bRange)
        {
            return (start, length, gRange, 1);
        }

        return (start, length, bRange, 2);
    }

    private static void SortByChannel(int[] colors, int start, int length, int channel)
    {
        Span<int> span = colors.AsSpan(start, length);
        switch (channel)
        {
            case 0: span.Sort((a, b) => ((a >> 16) & 0xFF) - ((b >> 16) & 0xFF)); break;
            case 1: span.Sort((a, b) => ((a >> 8) & 0xFF) - ((b >> 8) & 0xFF)); break;
            case 2: span.Sort((a, b) => (a & 0xFF) - (b & 0xFF)); break;
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static byte FindNearest(int color, int[] palette)
    {
        int cr = (color >> 16) & 0xFF;
        int cg = (color >> 8) & 0xFF;
        int cb = color & 0xFF;

        int bestIdx = 0;
        int bestDist = int.MaxValue;

        for (int i = 0; i < palette.Length; i++)
        {
            int dr = cr - ((palette[i] >> 16) & 0xFF);
            int dg = cg - ((palette[i] >> 8) & 0xFF);
            int db = cb - (palette[i] & 0xFF);
            int dist = dr * dr + dg * dg + db * db;

            if (dist < bestDist)
            {
                bestDist = dist;
                bestIdx = i;
                if (dist == 0)
                {
                    break;
                }
            }
        }

        return (byte)bestIdx;
    }

    private static string EncodeSixel(byte[] indexed, int[] palette, int width, int height)
    {
        int paletteCount = palette.Length;
        var sb = new StringBuilder(width * height);

        // DCS header with raster attributes
        sb.Append("\x1bPq");
        sb.Append("\"1;1;").Append(width).Append(';').Append(height);

        // Color registers
        for (int i = 0; i < paletteCount; i++)
        {
            int c = palette[i];
            sb.Append('#').Append(i).Append(";2;")
                .Append(((c >> 16) & 0xFF) * 100 / 255).Append(';')
                .Append(((c >> 8) & 0xFF) * 100 / 255).Append(';')
                .Append((c & 0xFF) * 100 / 255);
        }

        // Precompute which colors appear in each band to skip empty ones
        int bandCount = (height + 5) / 6;
        int[] sixelRow = new int[width];

        for (int band = 0; band < bandCount; band++)
        {
            int bandY = band * 6;
            int bandHeight = Math.Min(6, height - bandY);
            bool firstColorInBand = true;

            for (int colorIdx = 0; colorIdx < paletteCount; colorIdx++)
            {
                // Build sixel column data for this color
                bool hasAny = false;
                for (int x = 0; x < width; x++)
                {
                    int sixel = 0;
                    int baseOffset = bandY * width + x;
                    for (int bit = 0; bit < bandHeight; bit++)
                    {
                        if (indexed[baseOffset + bit * width] == colorIdx)
                        {
                            sixel |= 1 << bit;
                        }
                    }

                    sixelRow[x] = sixel;
                    if (sixel != 0)
                    {
                        hasAny = true;
                    }
                }

                if (!hasAny)
                {
                    continue;
                }

                if (!firstColorInBand)
                {
                    sb.Append('$');
                }

                firstColorInBand = false;

                sb.Append('#').Append(colorIdx);

                // RLE encode
                int runStart = 0;
                while (runStart < width)
                {
                    int val = sixelRow[runStart];
                    int runEnd = runStart + 1;
                    while (runEnd < width && sixelRow[runEnd] == val)
                    {
                        runEnd++;
                    }

                    int runLen = runEnd - runStart;
                    char sixelChar = (char)(val + 63);

                    if (runLen >= 4)
                    {
                        sb.Append('!').Append(runLen).Append(sixelChar);
                    }
                    else
                    {
                        for (int r = 0; r < runLen; r++)
                        {
                            sb.Append(sixelChar);
                        }
                    }

                    runStart = runEnd;
                }
            }

            if (band < bandCount - 1)
            {
                sb.Append('-');
            }
        }

        sb.Append("\x1b\\");
        return sb.ToString();
    }
}
