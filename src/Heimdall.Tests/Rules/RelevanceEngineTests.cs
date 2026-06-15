using Heimdall.Core.Models;
using Heimdall.Core.Rules;
using Shouldly;
using static Heimdall.Tests.TestSupport.RunBuilder;

namespace Heimdall.Tests.Rules;

public class RelevanceEngineTests
{
    private static readonly Identity Me = new("alice");
    private static readonly RepoConfig Repo = new("octo", "demo", "main");
    private readonly RelevanceEngine _engine = new(StandardRules.All);

    private static HashSet<string> Enabled(params string[] ids) => new(ids);

    [Fact]
    public void Relevant_when_an_enabled_rule_matches()
    {
        var run = Run(RunStatus.Failure, actor: "alice"); // matches TriggeredByMe
        _engine.IsRelevant(run, Me, Repo, Enabled(TriggeredByMeRule.RuleId)).ShouldBeTrue();
    }

    [Fact]
    public void Not_relevant_when_the_only_matching_rule_is_disabled()
    {
        // On the default branch, triggered by a stranger, no PR: only DefaultBranchBreaking would match.
        var run = Run(RunStatus.Failure, branch: "main", actor: "stranger");

        _engine.IsRelevant(run, Me, Repo, Enabled(TriggeredByMeRule.RuleId, MyPullRequestRule.RuleId)).ShouldBeFalse();
        _engine.IsRelevant(run, Me, Repo, Enabled(DefaultBranchBreakingRule.RuleId)).ShouldBeTrue();
    }

    [Fact]
    public void Rules_combine_with_or()
    {
        // Matches MyPullRequest only; TriggeredByMe enabled but doesn't match — OR still makes it relevant.
        var run = Run(RunStatus.Failure, branch: "feature/x", actor: "stranger", prAuthors: ["alice"]);

        _engine.IsRelevant(run, Me, Repo, Enabled(TriggeredByMeRule.RuleId, MyPullRequestRule.RuleId)).ShouldBeTrue();
    }

    [Fact]
    public void Never_relevant_when_no_rules_are_enabled()
    {
        var run = Run(RunStatus.Failure, branch: "main", actor: "alice", prAuthors: ["alice"]);
        _engine.IsRelevant(run, Me, Repo, Enabled()).ShouldBeFalse();
    }

    [Fact]
    public void Default_toggles_match_the_mvp_spec()
    {
        var toggles = _engine.DefaultToggles();

        toggles[TriggeredByMeRule.RuleId].ShouldBeTrue();
        toggles[MyPullRequestRule.RuleId].ShouldBeTrue();
        toggles[DefaultBranchBreakingRule.RuleId].ShouldBeFalse();
    }
}
