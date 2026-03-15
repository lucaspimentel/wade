using Wade.FileSystem;
using Wade.Terminal;

namespace Wade;

internal sealed class GitStatusLoader
{
    private readonly InputPipeline _pipeline;
    private CancellationTokenSource? _cts;

#pragma warning disable CSLINT221 // Consider using a primary constructor
    public GitStatusLoader(InputPipeline pipeline)
#pragma warning restore CSLINT221
    {
        _pipeline = pipeline;
    }

    public void BeginLoad(string repoRoot)
    {
        _cts?.Cancel();
        _cts?.Dispose();
        _cts = new CancellationTokenSource();
        var token = _cts.Token;

        Task.Run(() => Load(repoRoot, token), token);
    }

    public void Cancel()
    {
        _cts?.Cancel();
        _cts?.Dispose();
        _cts = null;
    }

    private void Load(string repoRoot, CancellationToken ct)
    {
        try
        {
            string? branch = GitUtils.ReadBranchName(repoRoot);

            if (ct.IsCancellationRequested)
            {
                return;
            }

            var statuses = GitUtils.QueryStatus(repoRoot, ct);

            if (ct.IsCancellationRequested)
            {
                return;
            }

            var aheadBehind = GitUtils.GetAheadBehind(repoRoot, ct);
            int ahead = aheadBehind?.Ahead ?? 0;
            int behind = aheadBehind?.Behind ?? 0;

            if (ct.IsCancellationRequested)
            {
                return;
            }

            _pipeline.Inject(new GitStatusReadyEvent(repoRoot, branch, statuses, ahead, behind));
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
