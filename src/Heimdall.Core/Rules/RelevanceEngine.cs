using Heimdall.Core.Models;

namespace Heimdall.Core.Rules;

/// <summary>
/// Evaluates the configured relevance rules against a run: relevant if <em>any enabled</em> rule
/// matches. Disabled rules (per the supplied toggle set) never contribute.
/// </summary>
public sealed class RelevanceEngine
{
    private readonly IReadOnlyList<IRelevanceRule> _rules;

    public RelevanceEngine(IEnumerable<IRelevanceRule> rules) => _rules = rules.ToList();

    /// <summary>True if any enabled rule considers the run relevant to <paramref name="me"/>.</summary>
    public bool IsRelevant(RunRecord run, Identity me, RepoConfig repo, IReadOnlySet<string> enabledRuleIds) =>
        _rules.Any(rule => enabledRuleIds.Contains(rule.Id) && rule.IsRelevant(run, me, repo));

    /// <summary>The out-of-the-box toggle state for the configured rules, keyed by rule id.</summary>
    public IReadOnlyDictionary<string, bool> DefaultToggles() =>
        _rules.ToDictionary(rule => rule.Id, rule => rule.DefaultEnabled);
}
