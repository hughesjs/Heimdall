using Heimdall.Core.Models;

namespace Heimdall.Core.Rules;

/// <summary>
/// Relevant when the run is on the repo's default branch, regardless of who triggered it — so a dev
/// learns when <c>main</c> breaks even if it wasn't their change. Off by default.
/// </summary>
public sealed class DefaultBranchBreakingRule : IRelevanceRule
{
    public const string RuleId = "DefaultBranchBreaking";

    public string Id => RuleId;
    public bool DefaultEnabled => false;

    public bool IsRelevant(RunRecord run, Identity me, RepoConfig repo) =>
        string.Equals(run.HeadBranch, repo.DefaultBranch, StringComparison.Ordinal);
}
