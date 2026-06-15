using Heimdall.Core.Models;

namespace Heimdall.Core.Rules;

/// <summary>Relevant when I triggered the run. On by default.</summary>
public sealed class TriggeredByMeRule : IRelevanceRule
{
    public const string RuleId = "TriggeredByMe";

    public string Id => RuleId;
    public bool DefaultEnabled => true;

    public bool IsRelevant(RunRecord run, Identity me, RepoConfig repo) =>
        string.Equals(run.TriggeringActorLogin, me.Login, StringComparison.OrdinalIgnoreCase);
}
