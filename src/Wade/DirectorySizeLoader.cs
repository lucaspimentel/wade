using Wade.Terminal;

namespace Wade;

internal sealed class DirectorySizeLoader
{
    private readonly InputPipeline _pipeline;
    private CancellationTokenSource? _cts;

#pragma warning disable CSLINT221 // Consider using a primary constructor
    public DirectorySizeLoader(InputPipeline pipeline)
#pragma warning restore CSLINT221
    {
        _pipeline = pipeline;
    }

    public void BeginCalculation(string directoryPath)
    {
        _cts?.Cancel();
        _cts?.Dispose();
        _cts = new CancellationTokenSource();
        var token = _cts.Token;

        Task.Run(() => Calculate(directoryPath, token), token);
    }

    public void Cancel()
    {
        _cts?.Cancel();
        _cts?.Dispose();
        _cts = null;
    }

    private void Calculate(string directoryPath, CancellationToken ct)
    {
        try
        {
            long totalBytes = 0;
            int fileCount = 0;

            var options = new EnumerationOptions
            {
                RecurseSubdirectories = true,
                IgnoreInaccessible = true,
                AttributesToSkip = 0,
            };

            foreach (string file in Directory.EnumerateFiles(directoryPath, "*", options))
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

            _pipeline.Inject(new DirectorySizeReadyEvent(directoryPath, totalBytes));
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
