using Heimdall.Core.GitHub;
using Heimdall.Core.Models;
using Heimdall.Core.Rules;
using Heimdall.Core.Settings;

namespace Heimdall.Core.Polling;

/// <summary>
/// The polling spine. Each cycle fetches recent runs per repo, filters to the developer's relevant
/// runs, folds the latest run per pipeline line through the state machine, and raises transition,
/// aggregate, and snapshot events. A single sequential loop owns the state map (no locking needed).
/// </summary>
public sealed class PollingService(IGitHubGateway gateway, RelevanceEngine engine, TimeProvider? timeProvider = null)
{
    // Pipelines whose latest run is older than this are dropped from tracking entirely — stale clutter
    // that should not colour the tray, notify, or appear in the menu.
    private static readonly TimeSpan StaleAfter = TimeSpan.FromDays(30);

    private readonly IGitHubGateway _gateway = gateway;
    private readonly RelevanceEngine _engine = engine;
    private readonly TimeProvider _time = timeProvider ?? TimeProvider.System;
    private Dictionary<PipelineKey, PipelineState> _states = new();

    /// <summary>Fired once per settled green↔red flip (suppressed when notifications are disabled).</summary>
    public event Action<NotificationPayload>? Transition;

    /// <summary>Fired each cycle with the aggregate tray status.</summary>
    public event Action<TrayStatus>? Aggregate;

    /// <summary>Fired each successful cycle with the current pipeline states (for the tray menu).</summary>
    public event Action<IReadOnlyList<PipelineState>>? Snapshot;

    /// <summary>Fired when a call fails authentication — the app should trigger re-auth.</summary>
    public event Action? AuthenticationFailed;

    /// <summary>Fired with the error when a cycle fails for a non-auth reason (for logging).</summary>
    public event Action<Exception>? PollFailed;

    /// <summary>
    /// Drives polling on a <see cref="PeriodicTimer"/> until cancelled, reading settings afresh each
    /// cycle (so repo/toggle/interval changes apply at the next boundary) and backing off when the
    /// rate limit runs low.
    /// </summary>
    public async Task RunAsync(ISettingsStore settingsStore, CancellationToken cancellationToken)
    {
        var settings = await settingsStore.LoadAsync(cancellationToken);
        using var timer = new PeriodicTimer(IntervalOf(settings));

        while (true)
        {
            await PollOnceAsync(settings, cancellationToken);

            // Re-read settings and set the period before waiting, so an interval change applies at the next boundary.
            settings = await settingsStore.LoadAsync(cancellationToken);
            timer.Period = IntervalOf(settings);

            // When quota is low, wait until it resets *instead of* the normal cadence (not in addition to it).
            if (RateLimitPolicy.ShouldBackOff(_gateway.LastRateLimit, settings.Repos.Count) && _gateway.LastRateLimit is { } limit)
                await DelayUntilAsync(limit.ResetsAt, cancellationToken);
            else if (!await timer.WaitForNextTickAsync(cancellationToken))
                break;
        }
    }

    /// <summary>Runs a single poll cycle against the given settings. The unit of testable behaviour.</summary>
    public async Task PollOnceAsync(AppSettings settings, CancellationToken cancellationToken)
    {
        try
        {
            var enabledRules = settings.RuleToggles.Where(toggle => toggle.Value).Select(toggle => toggle.Key).ToHashSet();
            var seen = new HashSet<PipelineKey>();

            foreach (var repo in settings.Repos)
            {
                var runs = await _gateway.GetRecentRunsAsync(repo, cancellationToken);
                foreach (var run in LatestTrackedPerKey(runs, settings.Identity, repo, enabledRules))
                {
                    var key = PipelineKey.For(run);
                    seen.Add(key);
                    _states.TryGetValue(key, out var prior);

                    var isAnnounce = repo.AnnounceWorkflows.Contains(run.WorkflowName, StringComparer.OrdinalIgnoreCase);
                    var isRelevant = _engine.IsRelevant(run, settings.Identity, repo, enabledRules);
                    var policy = isAnnounce ? NotifyPolicy.Announce : NotifyPolicy.Transitions;

                    var (state, notification) = PipelineStateMachine.Apply(prior, run, policy, repo.AnnounceFailures);
                    _states[key] = state with { CountsTowardTray = isRelevant };
                    if (notification is not null && settings.NotificationsEnabled)
                        Transition?.Invoke(notification);
                }
            }

            // Prune retains failing lines even when unseen (so a recovery can still notify); age-based
            // eviction is layered on here, where the cycle clock lives, to finally drop stale pipelines.
            var now = _time.GetUtcNow();
            _states = PipelineStateMachine.Prune(_states, seen)
                .Where(kv => now - kv.Value.LastRun.CreatedAt <= StaleAfter)
                .ToDictionary(kv => kv.Key, kv => kv.Value);
            Aggregate?.Invoke(PipelineStateMachine.Aggregate(_states.Values.Where(s => s.CountsTowardTray), connected: true));
            Snapshot?.Invoke(_states.Values.ToList());
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (GitHubAccessException ex) when (ex.Error == GitHubAccessError.Unauthorised)
        {
            Aggregate?.Invoke(TrayStatus.Grey);
            AuthenticationFailed?.Invoke();
        }
        catch (GitHubAccessException ex) when (ex.Error == GitHubAccessError.RateLimited)
        {
            // Expected when quota is exhausted: grey the tray and let RunAsync's backoff wait it out.
            Aggregate?.Invoke(TrayStatus.Grey);
        }
        catch (Exception ex)
        {
            // Grey tray is the user-visible error signal; PollFailed lets the app log the cause.
            Aggregate?.Invoke(TrayStatus.Grey);
            PollFailed?.Invoke(ex);
        }
    }

    // Track a run if it is relevant under the rules, or its workflow is an announce workflow for the repo.
    private IEnumerable<RunRecord> LatestTrackedPerKey(IReadOnlyList<RunRecord> runs, Identity me, RepoConfig repo, IReadOnlySet<string> enabledRules) =>
        runs
            .Where(run => _engine.IsRelevant(run, me, repo, enabledRules)
                || repo.AnnounceWorkflows.Contains(run.WorkflowName, StringComparer.OrdinalIgnoreCase))
            .GroupBy(PipelineKey.For)
            .Select(group => group.MaxBy(run => run.RunNumber)!);

    private static TimeSpan IntervalOf(AppSettings settings) => TimeSpan.FromSeconds(Math.Max(1, settings.PollIntervalSeconds));

    private static async Task DelayUntilAsync(DateTimeOffset until, CancellationToken cancellationToken)
    {
        var delay = until - DateTimeOffset.UtcNow;
        if (delay > TimeSpan.Zero)
            await Task.Delay(delay, cancellationToken);
    }
}
