namespace Heimdall.Core.Models;

/// <summary>
/// Identity of a single tracked pipeline line: a workflow on a specific branch within a repo.
/// Per-branch by design, so <c>CI on main</c> and <c>CI on a feature branch</c> are distinct.
/// </summary>
public readonly record struct PipelineKey(string Owner, string Repo, long WorkflowId, string HeadBranch)
{
    /// <summary>Derives the key a run belongs to.</summary>
    public static PipelineKey For(RunRecord run) => new(run.RepoOwner, run.RepoName, run.WorkflowId, run.HeadBranch);
}
