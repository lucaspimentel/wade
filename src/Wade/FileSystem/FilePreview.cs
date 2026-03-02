namespace Wade.FileSystem;

internal static class FilePreview
{
    private const int MaxPreviewLines = 100;
    private const int BinaryCheckSize = 512;

    public static string[] GetPreviewLines(string filePath)
    {
        try
        {
            if (IsBinary(filePath))
                return ["[binary file]"];

            var lines = new List<string>(MaxPreviewLines);
            using var reader = new StreamReader(filePath);

            while (lines.Count < MaxPreviewLines && reader.ReadLine() is { } line)
            {
                // Replace tabs with spaces for display
                lines.Add(line.Replace("\t", "    "));
            }

            return lines.Count > 0 ? [.. lines] : ["[empty file]"];
        }
        catch (UnauthorizedAccessException)
        {
            return ["[access denied]"];
        }
        catch (IOException ex)
        {
            return [$"[error: {ex.Message}]"];
        }
    }

    internal static bool IsBinary(string filePath)
    {
        try
        {
            var buffer = new byte[BinaryCheckSize];
            using var stream = File.OpenRead(filePath);
            int bytesRead = stream.Read(buffer, 0, BinaryCheckSize);

            for (int i = 0; i < bytesRead; i++)
            {
                if (buffer[i] == 0)
                    return true;
            }

            return false;
        }
        catch
        {
            return true;
        }
    }
}
