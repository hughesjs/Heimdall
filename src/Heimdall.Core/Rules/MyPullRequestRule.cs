using Heimdall.Core.Models;

namespace Heimdall.Core.Rules;

/// <summary>
/// Relevant when the run is for a PR I authored. The PR author logins are resolved and enriched onto
/// the run by the GitHub gateway (the run payload does not carry them). On by default.
/// </summary>
public sealed class MyPullRequestRule : IRelevanceRule
{
    public const string RuleId = "MyPullRequest";

    public string Id => RuleId;
    public bool DefaultEnabled => true;

    public bool IsRelevant(RunRecord run, Identity me, RepoConfig repo) =>
        run.PullRequestAuthorLogins.Any(login => string.Equals(login, me.Login, StringComparison.OrdinalIgnoreCase));
}
