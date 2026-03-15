using System.Diagnostics;

namespace Wade.FileSystem;

[Flags]
internal enum GitFileStatus
{
    None      = 0,
    Untracked = 1 << 0,
    Modified  = 1 << 1,
    Staged    = 1 << 2,
    Ignored   = 1 << 3,
    Conflict  = 1 << 4,
}

internal static class GitUtils
{
    /// <summary>
    /// Walks up from <paramref name="path"/> looking for a directory containing a .git folder.
    /// Returns the repo root path, or null if not inside a git repository.
    /// </summary>
    public static string? FindRepoRoot(string path)
    {
        if (string.IsNullOrEmpty(path))
        {
            return null;
        }

        string? dirPath = Directory.Exists(path)
            ? path
            : Path.GetDirectoryName(path);

        if (string.IsNullOrEmpty(dirPath))
        {
            return null;
        }

        var dir = new DirectoryInfo(dirPath);

        while (dir is not null)
        {
            string gitPath = Path.Combine(dir.FullName, ".git");

            if (Directory.Exists(gitPath) || File.Exists(gitPath))
            {
                return dir.FullName;
            }

            dir = dir.Parent;
        }

        return null;
    }

    /// <summary>
    /// Reads the current branch name from the .git/HEAD file.
    /// Returns null for detached HEAD, missing files, or errors.
    /// </summary>
    public static string? ReadBranchName(string repoRoot)
    {
        try
        {
            string gitPath = Path.Combine(repoRoot, ".git");
            string headPath;

            if (File.Exists(gitPath) && !Directory.Exists(gitPath))
            {
                // Worktree: .git is a file containing "gitdir: <path>"
                string content = File.ReadAllText(gitPath).Trim();
                if (content.StartsWith("gitdir: "))
                {
                    string gitDir = content["gitdir: ".Length..].Trim();
                    if (!Path.IsPathRooted(gitDir))
                    {
                        gitDir = Path.GetFullPath(Path.Combine(repoRoot, gitDir));
                    }

                    headPath = Path.Combine(gitDir, "HEAD");
                }
                else
                {
                    return null;
                }
            }
            else
            {
                headPath = Path.Combine(gitPath, "HEAD");
            }

            if (!File.Exists(headPath))
            {
                return null;
            }

            string headContent = File.ReadAllText(headPath).Trim();

            const string refPrefix = "ref: refs/heads/";
            if (headContent.StartsWith(refPrefix))
            {
                return headContent[refPrefix.Length..];
            }

            // Detached HEAD (raw SHA or other)
            return null;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Runs a git command in the given repo root and returns success/failure with optional error message.
    /// </summary>
    public static (bool Success, string? Error) RunGitCommand(string repoRoot, string arguments, CancellationToken ct, int timeoutMs = 10_000)
    {
        try
        {
            if (ct.IsCancellationRequested)
            {
                return (false, "Cancelled");
            }

            var psi = new ProcessStartInfo
            {
                FileName = "git",
                Arguments = arguments,
                WorkingDirectory = repoRoot,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };

            using var process = Process.Start(psi);
            if (process is null)
            {
                return (false, "Failed to start git");
            }

            string stderr = process.StandardError.ReadToEnd();
            process.StandardOutput.ReadToEnd(); // drain stdout

            if (!process.WaitForExit(timeoutMs))
            {
                process.Kill();
                return (false, "Git command timed out");
            }

            if (ct.IsCancellationRequested)
            {
                return (false, "Cancelled");
            }

            if (process.ExitCode != 0)
            {
                string error = stderr.Trim();
                return (false, string.IsNullOrEmpty(error) ? $"git exited with code {process.ExitCode}" : error);
            }

            return (true, null);
        }
        catch (Exception ex)
        {
            return (false, ex.Message);
        }
    }

    /// <summary>
    /// Stages the specified file paths via <c>git add</c>.
    /// </summary>
    public static (bool Success, string? Error) Stage(string repoRoot, IReadOnlyList<string> paths, CancellationToken ct)
    {
        string args = BuildPathArgs("add", repoRoot, paths);
        return RunGitCommand(repoRoot, args, ct);
    }

    /// <summary>
    /// Unstages the specified file paths via <c>git restore --staged</c>.
    /// </summary>
    public static (bool Success, string? Error) Unstage(string repoRoot, IReadOnlyList<string> paths, CancellationToken ct)
    {
        string args = BuildPathArgs("restore --staged", repoRoot, paths);
        return RunGitCommand(repoRoot, args, ct);
    }

    /// <summary>
    /// Stages all changes via <c>git add -A</c>.
    /// </summary>
    public static (bool Success, string? Error) StageAll(string repoRoot, CancellationToken ct) =>
        RunGitCommand(repoRoot, "add -A", ct);

    /// <summary>
    /// Unstages all staged changes via <c>git reset HEAD</c>.
    /// </summary>
    public static (bool Success, string? Error) UnstageAll(string repoRoot, CancellationToken ct) =>
        RunGitCommand(repoRoot, "reset HEAD", ct);

    /// <summary>
    /// Commits staged changes with the given message via <c>git commit -m</c>.
    /// </summary>
    public static (bool Success, string? Error) Commit(string repoRoot, string message, CancellationToken ct) =>
        RunGitCommand(repoRoot, $"commit -m \"{EscapeCommitMessage(message)}\"", ct);

    /// <summary>
    /// Pushes to the remote via <c>git push</c>.
    /// </summary>
    public static (bool Success, string? Error) Push(string repoRoot, CancellationToken ct) =>
        RunGitCommand(repoRoot, "push", ct, timeoutMs: 30_000);

    /// <summary>
    /// Force-pushes to the remote via <c>git push --force-with-lease</c>.
    /// </summary>
    public static (bool Success, string? Error) PushForceWithLease(string repoRoot, CancellationToken ct) =>
        RunGitCommand(repoRoot, "push --force-with-lease", ct, timeoutMs: 30_000);

    /// <summary>
    /// Pulls from the remote via <c>git pull</c>.
    /// </summary>
    public static (bool Success, string? Error) Pull(string repoRoot, CancellationToken ct) =>
        RunGitCommand(repoRoot, "pull", ct, timeoutMs: 30_000);

    /// <summary>
    /// Pulls from the remote with rebase via <c>git pull --rebase</c>.
    /// </summary>
    public static (bool Success, string? Error) PullRebase(string repoRoot, CancellationToken ct) =>
        RunGitCommand(repoRoot, "pull --rebase", ct, timeoutMs: 30_000);

    /// <summary>
    /// Fetches from the remote via <c>git fetch</c>.
    /// </summary>
    public static (bool Success, string? Error) Fetch(string repoRoot, CancellationToken ct) =>
        RunGitCommand(repoRoot, "fetch", ct, timeoutMs: 30_000);

    /// <summary>
    /// Returns the number of commits ahead/behind the upstream branch.
    /// Returns null if there is no upstream or an error occurs.
    /// </summary>
    public static (int Ahead, int Behind)? GetAheadBehind(string repoRoot, CancellationToken ct)
    {
        try
        {
            if (ct.IsCancellationRequested)
            {
                return null;
            }

            var psi = new ProcessStartInfo
            {
                FileName = "git",
                Arguments = "rev-list --count --left-right @{upstream}...HEAD",
                WorkingDirectory = repoRoot,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };

            using var process = Process.Start(psi);

            if (process is null)
            {
                return null;
            }

            string stdout = process.StandardOutput.ReadToEnd();
            process.StandardError.ReadToEnd(); // drain

            if (!process.WaitForExit(10_000))
            {
                process.Kill();
                return null;
            }

            if (process.ExitCode != 0)
            {
                return null;
            }

            // Output format: "<behind>\t<ahead>\n"
            string[] parts = stdout.Trim().Split('\t');

            if (parts.Length == 2 &&
                int.TryParse(parts[0], out int behind) &&
                int.TryParse(parts[1], out int ahead))
            {
                return (ahead, behind);
            }

            return null;
        }
        catch
        {
            return null;
        }
    }

    private static string EscapeCommitMessage(string message) =>
        message.Replace("\\", "\\\\").Replace("\"", "\\\"");

    private static string BuildPathArgs(string command, string repoRoot, IReadOnlyList<string> paths)
    {
        var sb = new System.Text.StringBuilder(command);
        sb.Append(" --");
        foreach (string path in paths)
        {
            string relative = Path.GetRelativePath(repoRoot, path).Replace('\\', '/');
            sb.Append(" \"");
            sb.Append(relative);
            sb.Append('"');
        }

        return sb.ToString();
    }

    /// <summary>
    /// Runs <c>git diff</c> for a single file and returns the output lines.
    /// Returns null on error, cancellation, or empty diff.
    /// </summary>
    public static string[]? GetDiff(string repoRoot, string filePath, bool staged, CancellationToken ct)
    {
        try
        {
            string relativePath = Path.GetRelativePath(repoRoot, filePath).Replace('\\', '/');
            string arguments = staged
                ? $"diff --staged -- \"{relativePath}\""
                : $"diff -- \"{relativePath}\"";

            var psi = new ProcessStartInfo
            {
                FileName = "git",
                Arguments = arguments,
                WorkingDirectory = repoRoot,
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };

            using var process = Process.Start(psi);
            if (process is null)
            {
                return null;
            }

            string output = process.StandardOutput.ReadToEnd();

            if (!process.WaitForExit(10_000))
            {
                process.Kill();
                return null;
            }

            if (ct.IsCancellationRequested)
            {
                return null;
            }

            if (process.ExitCode != 0 || string.IsNullOrEmpty(output))
            {
                return null;
            }

            return output.Split('\n');
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Queries git status for the repository at <paramref name="repoRoot"/>.
    /// Returns a dictionary of full paths to their git status, including aggregated directory statuses.
    /// Returns null on error or cancellation.
    /// </summary>
    public static Dictionary<string, GitFileStatus>? QueryStatus(string repoRoot, CancellationToken ct)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "git",
                Arguments = "status --porcelain=v1",
                WorkingDirectory = repoRoot,
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };

            using var process = Process.Start(psi);
            if (process is null)
            {
                return null;
            }

            string output = process.StandardOutput.ReadToEnd();

            if (!process.WaitForExit(10_000))
            {
                process.Kill();
                return null;
            }

            if (ct.IsCancellationRequested)
            {
                return null;
            }

            if (process.ExitCode != 0)
            {
                return null;
            }

            return ParsePorcelainOutput(output, repoRoot);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Parses git status --porcelain=v1 output into a dictionary of file statuses,
    /// then aggregates statuses into parent directories.
    /// </summary>
    internal static Dictionary<string, GitFileStatus> ParsePorcelainOutput(string output, string repoRoot)
    {
        var statuses = new Dictionary<string, GitFileStatus>(StringComparer.OrdinalIgnoreCase);

        foreach (var line in output.AsSpan().EnumerateLines())
        {
            if (line.Length < 4)
            {
                continue;
            }

            char indexStatus = line[0];
            char workTreeStatus = line[1];
            // Path starts at index 3 (after "XY ")
            var relativePath = line[3..];

            // Handle renames: "R  old -> new" — use the new path
            int arrowIdx = relativePath.IndexOf(" -> ");
            if (arrowIdx >= 0)
            {
                relativePath = relativePath[(arrowIdx + 4)..];
            }

            // Strip surrounding quotes if present (git quotes paths with special chars)
            if (relativePath.Length >= 2 && relativePath[0] == '"' && relativePath[^1] == '"')
            {
                relativePath = relativePath[1..^1];
            }

            var status = GitFileStatus.None;

            // Conflict markers
            if (indexStatus == 'U' || workTreeStatus == 'U' ||
                (indexStatus == 'A' && workTreeStatus == 'A') ||
                (indexStatus == 'D' && workTreeStatus == 'D'))
            {
                status |= GitFileStatus.Conflict;
            }
            else
            {
                // Untracked
                if (indexStatus == '?' && workTreeStatus == '?')
                {
                    status |= GitFileStatus.Untracked;
                }
                // Ignored
                else if (indexStatus == '!' && workTreeStatus == '!')
                {
                    status |= GitFileStatus.Ignored;
                }
                else
                {
                    // Index (staged) status
                    if (indexStatus is 'A' or 'M' or 'D' or 'R' or 'C')
                    {
                        status |= GitFileStatus.Staged;
                    }

                    // Working tree (modified) status
                    if (workTreeStatus is 'M' or 'D')
                    {
                        status |= GitFileStatus.Modified;
                    }
                }
            }

            if (status == GitFileStatus.None)
            {
                continue;
            }

            // Convert relative path (using /) to full path with platform separators
            string fullPath = Path.GetFullPath(
                Path.Combine(repoRoot, relativePath.ToString().Replace('/', Path.DirectorySeparatorChar)));

            if (statuses.TryGetValue(fullPath, out var existing))
            {
                statuses[fullPath] = existing | status;
            }
            else
            {
                statuses[fullPath] = status;
            }
        }

        // Aggregate into parent directories (exclude Ignored from propagation)
        AggregateDirectoryStatuses(statuses, repoRoot);

        return statuses;
    }

    private static void AggregateDirectoryStatuses(Dictionary<string, GitFileStatus> statuses, string repoRoot)
    {
        // Snapshot keys to avoid modifying during enumeration
        var filePaths = statuses.Keys.ToArray();

        foreach (string filePath in filePaths)
        {
            var status = statuses[filePath] & ~GitFileStatus.Ignored;
            if (status == GitFileStatus.None)
            {
                continue;
            }

            string? parentDir = Path.GetDirectoryName(filePath);
            while (parentDir is not null &&
                   parentDir.Length >= repoRoot.Length &&
                   !string.Equals(parentDir, filePath, StringComparison.OrdinalIgnoreCase))
            {
                if (statuses.TryGetValue(parentDir, out var dirStatus))
                {
                    statuses[parentDir] = dirStatus | status;
                }
                else
                {
                    statuses[parentDir] = status;
                }

                if (string.Equals(parentDir, repoRoot, StringComparison.OrdinalIgnoreCase))
                {
                    break;
                }

                parentDir = Path.GetDirectoryName(parentDir);
            }
        }
    }
}
