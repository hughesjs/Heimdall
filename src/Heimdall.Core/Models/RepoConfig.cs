namespace Heimdall.Core.Models;

/// <summary>
/// A repository the developer watches, together with its default branch (resolved and cached when
/// the repo is added). Passed to relevance rules so the default-branch rule needs no extra lookup.
/// </summary>
public record RepoConfig(string Owner, string Name, string DefaultBranch)
{
    /// <summary>Workflow names (case-insensitive) whose new settled runs announce regardless of transitions.</summary>
    public IReadOnlyList<string> AnnounceWorkflows { get; init; } = [];

    /// <summary>When true, a failing announce-workflow run also notifies ("broke"); otherwise only successes ("shipped") do.</summary>
    public bool AnnounceFailures { get; init; }
}
