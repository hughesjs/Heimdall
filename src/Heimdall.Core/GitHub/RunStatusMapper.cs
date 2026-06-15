using Heimdall.Core.Models;

namespace Heimdall.Core.GitHub;

/// <summary>
/// Maps GitHub Actions run status/conclusion strings to the domain <see cref="RunStatus"/>.
/// Works on the raw API strings (not Octokit's parsed enum) so an unrecognised value degrades to
/// <see cref="RunStatus.Unknown"/>/<see cref="RunStatus.InProgress"/> rather than throwing.
/// </summary>
public static class RunStatusMapper
{
    public static RunStatus FromApi(string? status, string? conclusion)
    {
        // Anything not yet completed (queued, in_progress, waiting, pending, requested) is in flight.
        if (!string.Equals(status, "completed", StringComparison.OrdinalIgnoreCase))
            return RunStatus.InProgress;

        // Only an outright failure is "red"; cancelled/timed_out/skipped/neutral/etc. are deliberately
        // Unknown so they never fire a false broke/recover (see DESIGN.md decision log).
        return conclusion?.ToLowerInvariant() switch
        {
            "success" => RunStatus.Success,
            "failure" or "startup_failure" => RunStatus.Failure,
            _ => RunStatus.Unknown
        };
    }
}
