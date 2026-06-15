namespace Heimdall.Core.Rules;

/// <summary>The MVP relevance rule set, for wiring the engine in composition and tests.</summary>
public static class StandardRules
{
    /// <summary>A fresh instance of each MVP rule. Rules are stateless, so new instances are cheap.</summary>
    public static IReadOnlyList<IRelevanceRule> All =>
    [
        new TriggeredByMeRule(),
        new MyPullRequestRule(),
        new DefaultBranchBreakingRule()
    ];
}
