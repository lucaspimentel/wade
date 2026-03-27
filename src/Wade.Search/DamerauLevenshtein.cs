namespace Wade.Search;

internal static class DamerauLevenshtein
{
    /// <summary>
    /// Compute the Damerau-Levenshtein distance (optimal string alignment variant)
    /// between two strings. Supports insertion, deletion, substitution, and adjacent
    /// transposition. Case-insensitive comparison.
    /// Returns int.MaxValue if the distance exceeds <paramref name="maxDistance"/>.
    /// </summary>
    internal static int Distance(ReadOnlySpan<char> source, ReadOnlySpan<char> target, int maxDistance = int.MaxValue)
    {
        int sourceLen = source.Length;
        int targetLen = target.Length;

        // Early termination: length difference alone exceeds max distance.
        if (Math.Abs(sourceLen - targetLen) > maxDistance)
        {
            return int.MaxValue;
        }

        if (sourceLen == 0)
        {
            return targetLen <= maxDistance ? targetLen : int.MaxValue;
        }

        if (targetLen == 0)
        {
            return sourceLen <= maxDistance ? sourceLen : int.MaxValue;
        }

        // Use stackalloc for small inputs (typical file/folder names).
        int matrixRows = sourceLen + 1;
        int matrixCols = targetLen + 1;
        int matrixSize = matrixRows * matrixCols;

        // Threshold: 256 ints = 1KB on the stack.
        const int stackAllocThreshold = 256;
        Span<int> matrix = matrixSize <= stackAllocThreshold
            ? stackalloc int[matrixSize]
            : new int[matrixSize];

        // Initialize first column.
        for (int i = 0; i <= sourceLen; i++)
        {
            matrix[i * matrixCols] = i;
        }

        // Initialize first row.
        for (int j = 0; j <= targetLen; j++)
        {
            matrix[j] = j;
        }

        for (int i = 1; i <= sourceLen; i++)
        {
            char sc = char.ToLowerInvariant(source[i - 1]);
            int rowMin = int.MaxValue;

            for (int j = 1; j <= targetLen; j++)
            {
                char tc = char.ToLowerInvariant(target[j - 1]);
                int cost = sc == tc ? 0 : 1;

                int deletion = matrix[(i - 1) * matrixCols + j] + 1;
                int insertion = matrix[i * matrixCols + (j - 1)] + 1;
                int substitution = matrix[(i - 1) * matrixCols + (j - 1)] + cost;

                int distance = Math.Min(Math.Min(deletion, insertion), substitution);

                // Adjacent transposition.
                if (i > 1 && j > 1
                    && sc == char.ToLowerInvariant(target[j - 2])
                    && char.ToLowerInvariant(source[i - 2]) == tc)
                {
                    int transposition = matrix[(i - 2) * matrixCols + (j - 2)] + cost;
                    distance = Math.Min(distance, transposition);
                }

                matrix[i * matrixCols + j] = distance;

                if (distance < rowMin)
                {
                    rowMin = distance;
                }
            }

            // If the minimum value in this row exceeds maxDistance, no cell
            // in subsequent rows can possibly produce a result within the threshold.
            if (rowMin > maxDistance)
            {
                return int.MaxValue;
            }
        }

        int result = matrix[sourceLen * matrixCols + targetLen];
        return result <= maxDistance ? result : int.MaxValue;
    }
}
