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
    /// Folds the latest run for a pipeline into its prior state, returning the new state and an
    /// optional notification. Under <see cref="NotifyPolicy.Transitions"/> a notification fires iff a
    /// settled green↔red flip occurred; under <see cref="NotifyPolicy.Announce"/> each new settled run
    /// of the (announce) workflow notifies — success always, failure only when
    /// <paramref name="announceFailures"/> is set.
    /// </summary>
    /// <remarks>
    /// A first sighting (<paramref name="prior"/> is null) seeds silently. In-progress and unknown
    /// runs preserve the settled anchor, so a re-run in flight or a cancelled run never fires a false
    /// transition. Under the announce policy, announcements are deduped per run id so a settled run is
    /// announced at most once, and a pre-existing release seen on first sighting is suppressed.
    /// </remarks>
    public static (PipelineState State, NotificationPayload? Notification) Apply(
        PipelineState? prior,
        RunRecord latest,
        NotifyPolicy policy = NotifyPolicy.Transitions,
        bool announceFailures = false) =>
        policy == NotifyPolicy.Announce
            ? ApplyAnnounce(prior, latest, announceFailures)
            : ApplyTransitions(prior, latest);

    private static (PipelineState State, NotificationPayload? Notification) ApplyTransitions(PipelineState? prior, RunRecord latest)
    {
        var key = PipelineKey.For(latest);
        var lastAnnounced = prior?.LastAnnouncedRunId;

        // In-progress and unknown runs carry no new settled pass/fail signal: keep the prior anchor.
        if (latest.Status is RunStatus.InProgress or RunStatus.Unknown)
        {
            var anchor = prior?.LastSettledStatus ?? RunStatus.Unknown;
            var inProgress = latest.Status == RunStatus.InProgress;
            return (new PipelineState(key, anchor, inProgress, latest.RunId, latest) { LastAnnouncedRunId = lastAnnounced }, null);
        }

        // Settled Success or Failure.
        var from = prior?.LastSettledStatus ?? RunStatus.Unknown;
        var to = latest.Status;
        var state = new PipelineState(key, to, InProgress: false, latest.RunId, latest) { LastAnnouncedRunId = lastAnnounced };

        if (!IsNotableTransition(from, to))
            return (state, null); // silent seed, unchanged, or a transition touching Unknown

        var kind = to == RunStatus.Failure ? NotificationKind.Broke : NotificationKind.Recovered;
        return (state, new NotificationPayload(kind, latest));
    }

    private static (PipelineState State, NotificationPayload? Notification) ApplyAnnounce(PipelineState? prior, RunRecord latest, bool announceFailures)
    {
        var key = PipelineKey.For(latest);
        var anchor = latest.Status is RunStatus.Success or RunStatus.Failure ? latest.Status : prior?.LastSettledStatus ?? RunStatus.Unknown;
        var inProgress = latest.Status == RunStatus.InProgress;

        // In-progress and unknown runs carry no release signal: carry the prior announce marker, no notify.
        if (latest.Status is RunStatus.InProgress or RunStatus.Unknown)
            return (new PipelineState(key, anchor, inProgress, latest.RunId, latest) { LastAnnouncedRunId = prior?.LastAnnouncedRunId }, null);

        // Settled. First sighting seeds the announce marker silently (suppress a pre-existing release).
        if (prior is null)
            return (new PipelineState(key, anchor, InProgress: false, latest.RunId, latest) { LastAnnouncedRunId = latest.RunId }, null);

        var state = new PipelineState(key, anchor, InProgress: false, latest.RunId, latest) { LastAnnouncedRunId = latest.RunId };

        // Already announced this run id (e.g. re-seen across polls, or seen in-progress then settled): no repeat.
        if (prior.LastAnnouncedRunId == latest.RunId)
            return (state, null);

        // A new settled run: success always ships; failure only when configured.
        if (latest.Status == RunStatus.Success)
            return (state, new NotificationPayload(NotificationKind.Released, latest));
        if (announceFailures)
            return (state, new NotificationPayload(NotificationKind.Broke, latest));
        return (state, null);
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
