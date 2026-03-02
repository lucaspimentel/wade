using System.Diagnostics;

namespace Wade.FileSystem;

internal static class FilePreview
{
    private const int MaxPreviewLines = 100;
    private const int BinaryCheckSize = 512;
    private const int FileCommandTimeoutMs = 2000;

    private static bool s_fileCommandAvailable;

    public static void Initialize()
    {
        try
        {
            using var process = new Process();
            process.StartInfo = new ProcessStartInfo
            {
                FileName = "file",
                Arguments = "--version",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            process.Start();
            s_fileCommandAvailable = process.WaitForExit(FileCommandTimeoutMs) && process.ExitCode == 0;
        }
        catch
        {
            s_fileCommandAvailable = false;
        }
    }

    public static string[] GetPreviewLines(string filePath)
    {
        try
        {
            if (IsBinary(filePath))
            {
                var description = GetFileTypeDescription(filePath);
                return description is not null ? [$"[binary: {description}]"] : ["[binary file]"];
            }

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

    private static string? GetFileTypeDescription(string filePath)
    {
        if (!s_fileCommandAvailable)
            return null;

        try
        {
            using var process = new Process();
            process.StartInfo = new ProcessStartInfo
            {
                FileName = "file",
                Arguments = $"--brief \"{filePath}\"",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            process.Start();
            string output = process.StandardOutput.ReadToEnd();

            if (process.WaitForExit(FileCommandTimeoutMs) && process.ExitCode == 0)
            {
                string trimmed = output.Trim();
                return trimmed.Length > 0 ? trimmed : null;
            }

            return null;
        }
        catch
        {
            return null;
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
