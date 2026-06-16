namespace Heimdall.Core.Models;

/// <summary>
/// The tracked state of one pipeline line. <see cref="LastSettledStatus"/> is the comparison
/// anchor for transition detection and is only ever Success/Failure/Unknown — an in-progress run
/// sets <see cref="InProgress"/> without disturbing the anchor.
/// </summary>
public record PipelineState(
    PipelineKey Key,
    RunStatus LastSettledStatus,
    bool InProgress,
    long LastRunId,
    RunRecord LastRun)
{
    /// <summary>Last run id announced for this pipeline under the announce policy (null = none seen).</summary>
    public long? LastAnnouncedRunId { get; init; }

    /// <summary>False for announce-only pipelines, which are tracked for notifications but must not recolour the tray.</summary>
    public bool CountsTowardTray { get; init; } = true;
}
