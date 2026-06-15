namespace Heimdall.Core.Models;

/// <summary>
/// Normalised health of a workflow run. Insulates the rest of the app from Octokit's
/// status/conclusion strings. Only <see cref="Success"/> and <see cref="Failure"/> are
/// settled pass/fail signals that can drive notifications.
/// </summary>
public enum RunStatus
{
    /// <summary>Run completed successfully.</summary>
    Success,

    /// <summary>Run completed with a failure.</summary>
    Failure,

    /// <summary>Run is queued or executing — no settled pass/fail yet.</summary>
    InProgress,

    /// <summary>Run finished without an actionable pass/fail (cancelled, skipped, neutral, timed out, …).</summary>
    Unknown
}
