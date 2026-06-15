using Heimdall.Core.Models;

namespace Heimdall.Core.Polling;

/// <summary>
/// Pure pipeline state tracking: folds the latest relevant run into prior state to detect
/// green↔red transitions, aggregates the tray indicator, and prunes stale pipeline lines.
/// No I/O — driven by the polling service.
/// </summary>
public static class PipelineStateMachine
{
    /// <summary>
    /// Folds the latest relevant run for a pipeline into its prior state, returning the new state
    /// and a notification iff a settled green↔red flip occurred.
    /// </summary>
    /// <remarks>
    /// A first sighting (<paramref name="prior"/> is null) seeds silently. In-progress and unknown
    /// runs preserve the settled anchor, so a re-run in flight or a cancelled run never fires a false
    /// transition. Only Success↔Failure flips notify.
    /// </remarks>
    public static (PipelineState State, NotificationPayload? Notification) Apply(PipelineState? prior, RunRecord latest)
    {
        var key = PipelineKey.For(latest);

        // In-progress and unknown runs carry no new settled pass/fail signal: keep the prior anchor.
        if (latest.Status is RunStatus.InProgress or RunStatus.Unknown)
        {
            var anchor = prior?.LastSettledStatus ?? RunStatus.Unknown;
            var inProgress = latest.Status == RunStatus.InProgress;
            return (new PipelineState(key, anchor, inProgress, latest.RunId, latest), null);
        }

        // Settled Success or Failure.
        var from = prior?.LastSettledStatus ?? RunStatus.Unknown;
        var to = latest.Status;
        var state = new PipelineState(key, to, InProgress: false, latest.RunId, latest);

        if (!IsNotableTransition(from, to))
            return (state, null); // silent seed, unchanged, or a transition touching Unknown

        var kind = to == RunStatus.Failure ? TransitionKind.Broke : TransitionKind.Recovered;
        return (state, new NotificationPayload(kind, latest));
    }

    /// <summary>Only settled green↔red flips are worth a notification.</summary>
    private static bool IsNotableTransition(RunStatus from, RunStatus to) =>
        (from, to) is (RunStatus.Success, RunStatus.Failure) or (RunStatus.Failure, RunStatus.Success);

    /// <summary>
    /// Aggregates the tray indicator across a developer's pipelines. Priority Grey ≻ Red ≻ Amber ≻ Green,
    /// so a known breakage stays visible even while another pipeline is re-running.
    /// </summary>
    public static TrayStatus Aggregate(IEnumerable<PipelineState> states, bool connected)
    {
        if (!connected)
            return TrayStatus.Grey;

        var snapshot = states as IReadOnlyCollection<PipelineState> ?? states.ToList();
        if (snapshot.Any(s => s.LastSettledStatus == RunStatus.Failure))
            return TrayStatus.Red;
        if (snapshot.Any(s => s.InProgress))
            return TrayStatus.Amber;
        return TrayStatus.Green;
    }

    /// <summary>
    /// Drops pipeline lines not seen this cycle, except failing lines, which are retained so a later
    /// recovery can still be reported even if the branch went quiet and its latest run aged off the
    /// recent-runs page. This pure helper has no notion of time, so failing lines are retained
    /// indefinitely here; age-based eviction (the "retained longer, not forever" intent) is layered on
    /// by the polling service, which owns the cycle clock.
    /// </summary>
    public static IReadOnlyDictionary<PipelineKey, PipelineState> Prune(
        IReadOnlyDictionary<PipelineKey, PipelineState> states,
        IReadOnlySet<PipelineKey> seenThisCycle) =>
        states
            .Where(kv => seenThisCycle.Contains(kv.Key) || kv.Value.LastSettledStatus == RunStatus.Failure)
            .ToDictionary(kv => kv.Key, kv => kv.Value);
}
