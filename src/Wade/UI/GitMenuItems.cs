using Wade.FileSystem;
using Wade.Terminal;

namespace Wade.UI;

/// <summary>
/// Git-entry gating shared by the action palette and the context menu
/// (App.cs:2958-3018 and App.cs:3916-3927). Both the app and the renderer
/// fixture runners call this so fixtures exercise the same code as the app.
/// </summary>
internal static class GitMenuItems
{
    internal sealed class Context
    {
        public required string? RepoRoot { get; init; }

        public required Dictionary<string, GitFileStatus>? Statuses { get; init; }

        /// <summary>Corresponds to a valid selected entry's FullPath.</summary>
        public required string? SelectedPath { get; init; }

        public required ISet<string> MarkedPaths { get; init; }
    }

    /// <summary>
    /// Port of `HasStatusInSelection` (App.cs:1489): marked paths take
    /// precedence over the selected entry.
    /// </summary>
    internal static bool HasStatusInSelection(Context ctx, GitFileStatus statusMask)
    {
        if (ctx.Statuses is null)
        {
            return false;
        }

        if (ctx.MarkedPaths.Count > 0)
        {
            foreach (string path in ctx.MarkedPaths)
            {
                if (ctx.Statuses.TryGetValue(path, out GitFileStatus s) && (s & statusMask) != 0)
                {
                    return true;
                }
            }

            return false;
        }

        return ctx.SelectedPath is not null
            && ctx.Statuses.TryGetValue(ctx.SelectedPath, out GitFileStatus selectedStatus)
            && (selectedStatus & statusMask) != 0;
    }

    /// <summary>
    /// Port of the palette's git block (App.cs:2958-3018). "Git: Copy
    /// relative path" is omitted: it needs the OS clipboard, deferred to
    /// Phase 9.
    /// </summary>
    internal static List<ActionMenuItem> BuildPaletteItems(Context ctx)
    {
        if (ctx.RepoRoot is null)
        {
            return [];
        }

        var items = new List<ActionMenuItem>();

        if (ctx.Statuses is not null)
        {
            if (ctx.SelectedPath is not null)
            {
                if (HasStatusInSelection(ctx, GitFileStatus.Modified | GitFileStatus.Untracked))
                {
                    items.Add(new ActionMenuItem { Label = "Git: Stage", Action = AppAction.StageFile });
                }

                if (HasStatusInSelection(ctx, GitFileStatus.Staged))
                {
                    items.Add(new ActionMenuItem { Label = "Git: Unstage", Action = AppAction.UnstageFile });
                }
            }

            bool hasAnyChanges = false;
            foreach (KeyValuePair<string, GitFileStatus> kvp in ctx.Statuses)
            {
                if ((kvp.Value & (GitFileStatus.Modified | GitFileStatus.Untracked)) != 0)
                {
                    hasAnyChanges = true;
                    break;
                }
            }

            if (hasAnyChanges)
            {
                items.Add(new ActionMenuItem { Label = "Git: Stage all changes", Action = AppAction.StageAll });
            }

            bool hasAnyStagedChanges = false;
            foreach (KeyValuePair<string, GitFileStatus> kvp in ctx.Statuses)
            {
                if ((kvp.Value & GitFileStatus.Staged) != 0)
                {
                    hasAnyStagedChanges = true;
                    break;
                }
            }

            if (hasAnyStagedChanges)
            {
                items.Add(new ActionMenuItem { Label = "Git: Unstage all", Action = AppAction.UnstageAll });
                items.Add(new ActionMenuItem { Label = "Git: Commit", Action = AppAction.GitCommit });
            }
        }

        items.Add(new ActionMenuItem { Label = "Git: Push", Action = AppAction.GitPush });
        items.Add(new ActionMenuItem { Label = "Git: Push (force with lease)", Action = AppAction.GitPushForceWithLease });
        items.Add(new ActionMenuItem { Label = "Git: Pull", Action = AppAction.GitPull });
        items.Add(new ActionMenuItem { Label = "Git: Pull (rebase)", Action = AppAction.GitPullRebase });
        items.Add(new ActionMenuItem { Label = "Git: Fetch", Action = AppAction.GitFetch });

        return items;
    }

    /// <summary>
    /// Port of the context menu's git additions (App.cs:3916-3927):
    /// Stage/Unstage only, gated on repo + statuses + a selected entry.
    /// </summary>
    internal static List<ActionMenuItem> BuildContextMenuItems(Context ctx)
    {
        if (ctx.RepoRoot is null || ctx.Statuses is null || ctx.SelectedPath is null)
        {
            return [];
        }

        var items = new List<ActionMenuItem>();

        if (HasStatusInSelection(ctx, GitFileStatus.Modified | GitFileStatus.Untracked))
        {
            items.Add(new ActionMenuItem { Label = "Git: Stage", Action = AppAction.StageFile });
        }

        if (HasStatusInSelection(ctx, GitFileStatus.Staged))
        {
            items.Add(new ActionMenuItem { Label = "Git: Unstage", Action = AppAction.UnstageFile });
        }

        return items;
    }
}