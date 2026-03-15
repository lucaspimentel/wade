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
        DirectoryInfo? dir = Directory.Exists(path)
            ? new DirectoryInfo(path)
            : new DirectoryInfo(Path.GetDirectoryName(path) ?? path);

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
