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
    RunRecord LastRun);
