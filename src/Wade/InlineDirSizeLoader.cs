using Wade.Terminal;

namespace Wade;

internal sealed class InlineDirSizeLoader
{
    private readonly InputPipeline _pipeline;
    private CancellationTokenSource? _cts;

#pragma warning disable CSLINT221 // Consider using a primary constructor
    public InlineDirSizeLoader(InputPipeline pipeline)
#pragma warning restore CSLINT221
    {
        _pipeline = pipeline;
    }

    public void BeginLoad(string parentPath, List<string> directoryPaths)
    {
        _cts?.Cancel();
        _cts?.Dispose();
        _cts = new CancellationTokenSource();
        CancellationToken token = _cts.Token;

        Task.Run(() => Load(parentPath, directoryPaths, token), token);
    }

    public void Cancel()
    {
        _cts?.Cancel();
        _cts?.Dispose();
        _cts = null;
    }

    private void Load(string parentPath, List<string> directoryPaths, CancellationToken ct)
    {
        try
        {
            var options = new EnumerationOptions
            {
                RecurseSubdirectories = true,
                IgnoreInaccessible = true,
                AttributesToSkip = 0,
            };

            foreach (string dirPath in directoryPaths)
            {
                if (ct.IsCancellationRequested)
                {
                    return;
                }

                long totalBytes = 0;
                int fileCount = 0;

                foreach (string file in Directory.EnumerateFiles(dirPath, "*", options))
                {
                    if (++fileCount % 500 == 0 && ct.IsCancellationRequested)
                    {
                        return;
                    }

                    totalBytes += new FileInfo(file).Length;
                }

                if (ct.IsCancellationRequested)
                {
                    return;
                }

                _pipeline.Inject(new InlineDirSizeReadyEvent(parentPath, dirPath, totalBytes));
            }

            if (!ct.IsCancellationRequested)
            {
                _pipeline.Inject(new InlineDirSizeCompleteEvent(parentPath));
            }
        }
        catch (OperationCanceledException)
        {
            // Normal cancellation
        }
        catch (InvalidOperationException)
        {
            // Pipeline disposed / completed adding
        }
    }
}
