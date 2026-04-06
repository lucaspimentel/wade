using Wade.FileSystem;
using Wade.Terminal;

namespace Wade;

internal sealed class FileOperationRunner
{
    private readonly InputPipeline _pipeline;
    private CancellationTokenSource? _cts;

    public FileOperationRunner(InputPipeline pipeline)
    {
        _pipeline = pipeline;
    }

    public void BeginPaste(
        List<string> sourcePaths,
        string destination,
        bool isCut,
        bool overwrite,
        bool copySymlinksAsLinks)
    {
        Cancel();
        _cts = new CancellationTokenSource();
        CancellationToken ct = _cts.Token;
        string label = isCut ? "Moving" : "Copying";

        Task.Run(() => RunPaste(sourcePaths, destination, isCut, overwrite, copySymlinksAsLinks, label, ct), ct);
    }

    public void BeginDelete(List<string> paths, bool permanent)
    {
        Cancel();
        _cts = new CancellationTokenSource();
        CancellationToken ct = _cts.Token;

        Task.Run(() => RunDelete(paths, permanent, ct), ct);
    }

    public void Cancel()
    {
        _cts?.Cancel();
        _cts?.Dispose();
        _cts = null;
    }

    private void RunPaste(
        List<string> sourcePaths,
        string destination,
        bool isCut,
        bool overwrite,
        bool copySymlinksAsLinks,
        string label,
        CancellationToken ct)
    {
        int success = 0;
        int errors = 0;
        int totalFiles = sourcePaths.Count;
        long lastProgressTick = 0;

        for (int i = 0; i < sourcePaths.Count; i++)
        {
            if (ct.IsCancellationRequested)
            {
                break;
            }

            string sourcePath = sourcePaths[i];
            string destName = Path.GetFileName(sourcePath);
            string destPath = Path.Combine(destination, destName);

            // Throttle progress events to every 100ms
            long now = Environment.TickCount64;
            if (now - lastProgressTick >= 100 || i == 0)
            {
                _pipeline.Inject(new FileOperationProgressEvent(label, i, totalFiles, destName));
                lastProgressTick = now;
            }

            if (Path.Exists(destPath))
            {
                if (!overwrite)
                {
                    errors++;
                    continue;
                }

                try
                {
                    DeleteExisting(destPath);
                }
                catch
                {
                    errors++;
                    continue;
                }
            }

            try
            {
                if (isCut)
                {
                    if (Directory.Exists(sourcePath))
                    {
                        Directory.Move(sourcePath, destPath);
                    }
                    else
                    {
                        File.Move(sourcePath, destPath);
                    }
                }
                else
                {
                    var sourceInfo = new FileInfo(sourcePath);

                    if (copySymlinksAsLinks && sourceInfo.LinkTarget != null)
                    {
                        try
                        {
                            if (Directory.Exists(sourcePath))
                            {
                                Directory.CreateSymbolicLink(destPath, sourceInfo.LinkTarget);
                            }
                            else
                            {
                                File.CreateSymbolicLink(destPath, sourceInfo.LinkTarget);
                            }

                            success++;
                            continue;
                        }
                        catch (UnauthorizedAccessException)
                        {
                        }
                    }

                    if (Directory.Exists(sourcePath))
                    {
                        FileOperations.CopyDirectory(sourcePath, destPath, copySymlinksAsLinks);
                    }
                    else
                    {
                        File.Copy(sourcePath, destPath);
                    }
                }

                success++;
            }
            catch
            {
                errors++;
            }
        }

        _pipeline.Inject(new FileOperationCompleteEvent(success, errors, isCut));
    }

    private void RunDelete(List<string> paths, bool permanent, CancellationToken ct)
    {
        int success = 0;
        int errors = 0;
        long lastProgressTick = 0;

        for (int i = 0; i < paths.Count; i++)
        {
            if (ct.IsCancellationRequested)
            {
                break;
            }

            string path = paths[i];
            string name = Path.GetFileName(path);

            long now = Environment.TickCount64;
            if (now - lastProgressTick >= 100 || i == 0)
            {
                _pipeline.Inject(new FileOperationProgressEvent("Deleting", i, paths.Count, name));
                lastProgressTick = now;
            }

            try
            {
                if (!permanent && OperatingSystem.IsWindows())
                {
                    int err = FileOperations.Delete([path], permanent: false);
                    if (err > 0)
                    {
                        errors++;
                    }
                    else
                    {
                        success++;
                    }
                }
                else
                {
                    int err = FileOperations.Delete([path], permanent: true);
                    if (err > 0)
                    {
                        errors++;
                    }
                    else
                    {
                        success++;
                    }
                }
            }
            catch
            {
                errors++;
            }
        }

        _pipeline.Inject(new FileOperationCompleteEvent(success, errors, WasCut: false));
    }

    private static void DeleteExisting(string path)
    {
        var info = new FileInfo(path);

        if (info.LinkTarget != null)
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, false);
            }
            else
            {
                File.Delete(path);
            }
        }
        else if (Directory.Exists(path))
        {
            Directory.Delete(path, true);
        }
        else
        {
            File.Delete(path);
        }
    }
}
