using Heimdall.Core.Models;

namespace Heimdall;

/// <summary>One pipeline line inside a repo's submenu.</summary>
internal sealed record TrayMenuEntry(string Dot, string Label, string Url);

/// <summary>A repo's top-level menu line plus the pipelines revealed when it is expanded.</summary>
internal sealed record TrayMenuRepoGroup(string Header, IReadOnlyList<TrayMenuEntry> Pipelines);

/// <summary>The full tray menu model: health-grouped repos plus the recently-announced releases.</summary>
internal sealed record TrayMenu(IReadOnlyList<TrayMenuRepoGroup> Repos, IReadOnlyList<TrayMenuEntry> RecentlyAnnounced);

/// <summary>
/// Turns a pipeline snapshot into health-classified, ordered repo groups for the tray menu.
/// Health is failure-first to stay consistent with the tray icon (see <see cref="PipelineStateMachine"/>
/// aggregation): a repo is as unhealthy as its worst pipeline.
/// </summary>
internal static class TrayMenuModel
{
    // Ordered worst-first so the enum value doubles as the sort key and the repo aggregate is a Min().
    private enum Health { Failing, Running, Passing, Unknown }

    private const int RecentlyAnnouncedLimit = 10;

    public static TrayMenu Build(IReadOnlyList<PipelineState> pipelines)
    {
        var repos = pipelines
            .Where(p => p.CountsTowardTray)
            .GroupBy(p => $"{p.Key.Owner}/{p.Key.Repo}")
            .Select(group => (
                Slug: group.Key,
                Health: group.Select(Classify).Min(),
                Entries: group
                    .OrderBy(p => p.LastRun.WorkflowName, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(p => p.Key.HeadBranch, StringComparer.OrdinalIgnoreCase)
                    .Select(p => new TrayMenuEntry(
                        Dot(Classify(p)),
                        $"{p.LastRun.WorkflowName} · {p.Key.HeadBranch} — {Word(Classify(p))}",
                        p.LastRun.HtmlUrl))
                    .ToList()))
            .OrderBy(g => g.Health)
            .ThenBy(g => g.Slug, StringComparer.OrdinalIgnoreCase)
            .Select(g => new TrayMenuRepoGroup($"{Dot(g.Health)} {g.Slug}", g.Entries))
            .ToList();

        // Announce-only pipelines are tracked for release notifications but excluded from the tray
        // colour, so they live in their own recency-ordered list rather than skewing repo health.
        var recentlyAnnounced = pipelines
            .Where(p => !p.CountsTowardTray)
            .OrderByDescending(p => p.LastRun.CreatedAt)
            .ThenBy(p => p.LastRun.HtmlUrl, StringComparer.Ordinal)
            .Take(RecentlyAnnouncedLimit)
            .Select(p => new TrayMenuEntry(
                Dot(Classify(p)),
                $"{p.Key.Owner}/{p.Key.Repo} · {p.LastRun.WorkflowName} · {p.Key.HeadBranch} — {Word(Classify(p))}",
                p.LastRun.HtmlUrl))
            .ToList();

        return new TrayMenu(repos, recentlyAnnounced);
    }

    private static Health Classify(PipelineState pipeline) => pipeline switch
    {
        { LastSettledStatus: RunStatus.Failure } => Health.Failing,
        { InProgress: true } => Health.Running,
        { LastSettledStatus: RunStatus.Success } => Health.Passing,
        _ => Health.Unknown,
    };

    private static string Dot(Health health) => health switch
    {
        Health.Failing => "🔴",
        Health.Running => "🟡",
        Health.Passing => "🟢",
        _ => "⚪",
    };

    private static string Word(Health health) => health switch
    {
        Health.Failing => "failing",
        Health.Running => "running",
        Health.Passing => "passing",
        _ => "unknown",
    };
}
