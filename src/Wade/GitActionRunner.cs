using Wade.FileSystem;
using Wade.Terminal;

namespace Wade;

internal sealed class GitActionRunner
{
    private readonly InputPipeline _pipeline;
    private CancellationTokenSource? _cts;

#pragma warning disable CSLINT221 // Consider using a primary constructor
    public GitActionRunner(InputPipeline pipeline)
#pragma warning restore CSLINT221
    {
        _pipeline = pipeline;
    }

    public void RunStage(string repoRoot, IReadOnlyList<string> paths)
    {
        StartAction(ct => GitUtils.Stage(repoRoot, paths, ct));
    }

    public void RunUnstage(string repoRoot, IReadOnlyList<string> paths)
    {
        StartAction(ct => GitUtils.Unstage(repoRoot, paths, ct));
    }

    public void RunStageAll(string repoRoot)
    {
        StartAction(ct => GitUtils.StageAll(repoRoot, ct));
    }

    public void RunUnstageAll(string repoRoot)
    {
        StartAction(ct => GitUtils.UnstageAll(repoRoot, ct));
    }

    public void RunCommit(string repoRoot, string message)
    {
        StartAction(ct => GitUtils.Commit(repoRoot, message, ct));
    }

    public void RunPush(string repoRoot)
    {
        StartAction(ct => GitUtils.Push(repoRoot, ct));
    }

    public void RunPushForceWithLease(string repoRoot)
    {
        StartAction(ct => GitUtils.PushForceWithLease(repoRoot, ct));
    }

    public void RunPull(string repoRoot)
    {
        StartAction(ct => GitUtils.Pull(repoRoot, ct));
    }

    public void RunPullRebase(string repoRoot)
    {
        StartAction(ct => GitUtils.PullRebase(repoRoot, ct));
    }

    public void RunFetch(string repoRoot)
    {
        StartAction(ct => GitUtils.Fetch(repoRoot, ct));
    }

    public void Cancel()
    {
        _cts?.Cancel();
        _cts?.Dispose();
        _cts = null;
    }

    private void StartAction(Func<CancellationToken, (bool Success, string? Error)> action)
    {
        _cts?.Cancel();
        _cts?.Dispose();
        _cts = new CancellationTokenSource();
        var token = _cts.Token;

        Task.Run(
            () =>
            {
                try
                {
                    var result = action(token);

                    if (token.IsCancellationRequested)
                    {
                        return;
                    }

                    _pipeline.Inject(new GitActionCompleteEvent(result.Success, result.Error));
                }
                catch (OperationCanceledException)
                {
                    // Normal cancellation
                }
                catch (InvalidOperationException)
                {
                    // Pipeline disposed / completed adding
                }
            },
            token);
    }
}
