using System.Collections.Concurrent;
using System.Diagnostics;

namespace Wade;

internal static class CliTool
{
    private static readonly ConcurrentDictionary<string, bool> s_availabilityCache = new();

    /// <summary>
    /// Checks whether a CLI tool is available on the system PATH.
    /// Results are cached by <paramref name="fileName"/>.
    /// </summary>
    public static bool IsAvailable(string fileName, string? arguments = null, int timeoutMs = 3000, bool requireZeroExitCode = false)
    {
        return s_availabilityCache.GetOrAdd(fileName, _ =>
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = fileName,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                };

                if (arguments is not null)
                {
                    psi.Arguments = arguments;
                }

                using var process = Process.Start(psi);

                if (process is null)
                {
                    return false;
                }

                process.StandardOutput.ReadToEnd();
                process.StandardError.ReadToEnd();

                if (!process.WaitForExit(timeoutMs))
                {
                    try
                    {
                        process.Kill();
                    }
                    catch
                    {
                        /* best effort */
                    }

                    return false;
                }

                return !requireZeroExitCode || process.ExitCode == 0;
            }
            catch
            {
                return false;
            }
        });
    }

    /// <summary>
    /// Runs a CLI tool and returns its stdout on success, or null on failure/timeout.
    /// </summary>
    public static string? Run(string fileName, string[] args, int timeoutMs = 5000, CancellationToken ct = default)
    {
        var psi = new ProcessStartInfo
        {
            FileName = fileName,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        foreach (string arg in args)
        {
            psi.ArgumentList.Add(arg);
        }

        return Run(psi, timeoutMs, ct);
    }

    /// <summary>
    /// Runs a process with a custom <see cref="ProcessStartInfo"/> and returns its stdout on success, or null on failure/timeout.
    /// Enforces redirect flags on the provided PSI.
    /// </summary>
    public static string? Run(ProcessStartInfo psi, int timeoutMs = 5000, CancellationToken ct = default)
    {
        psi.RedirectStandardOutput = true;
        psi.RedirectStandardError = true;
        psi.UseShellExecute = false;
        psi.CreateNoWindow = true;

        try
        {
            using var process = Process.Start(psi);

            if (process is null)
            {
                return null;
            }

            using CancellationTokenRegistration reg = ct.Register(() =>
            {
                try
                {
                    process.Kill();
                }
                catch
                {
                    /* best effort */
                }
            });

            string output = process.StandardOutput.ReadToEnd();

            if (!process.WaitForExit(timeoutMs))
            {
                try
                {
                    process.Kill();
                }
                catch
                {
                    /* best effort */
                }

                return null;
            }

            ct.ThrowIfCancellationRequested();

            return process.ExitCode == 0 ? output : null;
        }
        catch (OperationCanceledException)
        {
            return null;
        }
        catch
        {
            return null;
        }
    }
}
