using System.Diagnostics;

namespace Wade.Imaging;

internal sealed class XpdfPdfTool : IPdfTool
{
    public bool IsAvailable => CliTool.IsAvailable("pdftopng");

    public string? RenderPage(string pdfPath, int pageNumber, CancellationToken ct)
    {
        string tempDir = Path.Combine(Path.GetTempPath(), "wade-pdf-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        string tempRoot = Path.Combine(tempDir, "page");
        bool success = false;

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

            using var reg = ct.Register(() => { try { process.Kill(); } catch { /* best effort */ } });

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
                success = true;
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
            return null;
        }
        finally
        {
            if (!success)
            {
                try { Directory.Delete(tempDir, recursive: true); } catch { }
            }
        }
    }
}
