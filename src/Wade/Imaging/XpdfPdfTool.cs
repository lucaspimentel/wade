using System.Diagnostics;

namespace Wade.Imaging;

internal sealed class XpdfPdfTool : IPdfTool
{
    private static readonly Lazy<bool> s_isAvailable = new(CheckAvailability);

    public bool IsAvailable => s_isAvailable.Value;

    public string? RenderPage(string pdfPath, int pageNumber, CancellationToken ct)
    {
        string tempDir = Path.Combine(Path.GetTempPath(), "wade-pdf-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        string tempRoot = Path.Combine(tempDir, "page");

        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "pdftopng",
                ArgumentList =
                {
                    "-f", pageNumber.ToString(),
                    "-l", pageNumber.ToString(),
                    "-r", "150",
                    "-q",
                    pdfPath,
                    tempRoot
                },
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = Process.Start(psi);
            if (process is null)
            {
                return null;
            }

            process.WaitForExit(TimeSpan.FromSeconds(10));
            ct.ThrowIfCancellationRequested();

            if (process.ExitCode != 0)
            {
                return null;
            }

            // pdftopng outputs <tempRoot>-NNNNNN.png
            string expectedPath = $"{tempRoot}-{pageNumber:D6}.png";
            if (File.Exists(expectedPath))
            {
                return expectedPath;
            }

            return null;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            // Clean up temp dir on failure
            try { Directory.Delete(tempDir, recursive: true); } catch { }
            return null;
        }
    }

    private static bool CheckAvailability()
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "pdftopng",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = Process.Start(psi);
            if (process is null)
            {
                return false;
            }

            process.WaitForExit(TimeSpan.FromSeconds(5));
            return true;
        }
        catch
        {
            return false;
        }
    }
}
